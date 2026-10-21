using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DaisyOS.Core.Models;

namespace DaisyOS.System.Media;

/// <summary>
/// Resolves and caches looping Spotify Canvas MP4 videos for active media tracks.
/// Supports direct local video paths, Spotify track IDs, and artist-based Canvas lookup.
/// </summary>
public static class SpotifyCanvasResolver
{
    private const int MaxHtmlBytes = 512 * 1024;
    private const int MaxVideoBytes = 16 * 1024 * 1024;
    private const int MaxCacheEntries = 32;

    private static readonly string CacheDirectory = Path.Combine(Path.GetTempPath(), "daisyos-canvas-cache");

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly Regex SpotifyTrackIdRegex = new(
        @"(?:open\.spotify\.com/(?:intl-[a-zA-Z-]+/)?track/|spotify:track:|/com/spotify/track/)(?<id>[A-Za-z0-9]{22})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CanvasMp4Regex = new(
        @"https://canvaz\.scdn\.co/[^""'\s>#]+\.mp4",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArtistCanvasBlockRegex = new(
        @"data-umami-event-canvas=""spotify:track:(?<id>[A-Za-z0-9]{22})""[^>]*onclick='saveFile\(""(?<url>https://canvaz\.scdn\.co/[^""'\s>#]+\.mp4)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> CanvasCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> EvictionQueue = new();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) DaisyOS/1.0");
        return client;
    }

    public static Task<string?> ResolveAsync(MediaSession? session, CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            return Task.FromResult<string?>(null);
        }

        return ResolveAsync(session.SourceUrl, session.Title, session.Artist, cancellationToken);
    }

    public static async Task<string?> ResolveAsync(
        string? sourceUrl,
        string? title = null,
        string? artist = null,
        CancellationToken cancellationToken = default)
    {
        if (TryResolveLocalVideoPath(sourceUrl, out var localVideoPath))
        {
            return localVideoPath;
        }

        if (string.IsNullOrWhiteSpace(sourceUrl) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var cacheKey = $"{title?.Trim()}\u001f{artist?.Trim()}\u001f{sourceUrl?.Trim()}";
        if (CanvasCache.TryGetValue(cacheKey, out var existingEntry) && existingEntry.IsValueCreated)
        {
            try
            {
                var previousPath = await existingEntry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(previousPath) && File.Exists(previousPath))
                {
                    return previousPath;
                }

                CanvasCache.TryRemove(cacheKey, out _);
            }
            catch
            {
                CanvasCache.TryRemove(cacheKey, out _);
            }
        }

        var resolveTask = CanvasCache.GetOrAdd(
            cacheKey,
            key =>
            {
                EvictionQueue.Enqueue(key);
                return new Lazy<Task<string?>>(
                    () => ResolveAndDownloadCoreAsync(sourceUrl, title, artist),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }).Value;

        while (CanvasCache.Count > MaxCacheEntries && EvictionQueue.TryDequeue(out var oldestKey))
        {
            CanvasCache.TryRemove(oldestKey, out _);
        }

        try
        {
            return await resolveTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public static bool TryExtractSpotifyTrackId(string? sourceUrl, out string trackId)
    {
        trackId = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return false;
        }

        var match = SpotifyTrackIdRegex.Match(sourceUrl);
        if (!match.Success)
        {
            return false;
        }

        trackId = match.Groups["id"].Value;
        return !string.IsNullOrWhiteSpace(trackId);
    }

    public static string? ExtractCanvasUrlFromHtml(string html, string? preferredTrackId = null, string? titleForSelection = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredTrackId))
        {
            foreach (Match blockMatch in ArtistCanvasBlockRegex.Matches(html))
            {
                if (string.Equals(blockMatch.Groups["id"].Value, preferredTrackId, StringComparison.Ordinal))
                {
                    return blockMatch.Groups["url"].Value;
                }
            }
        }

        var matches = CanvasMp4Regex.Matches(html)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 1 || string.IsNullOrWhiteSpace(titleForSelection))
        {
            return matches[0];
        }

        int hash = ComputeStableHash(titleForSelection);
        return matches[hash % matches.Length];
    }

    private static int ComputeStableHash(string text)
    {
        uint hash = 2166136261u;
        foreach (char c in text.Trim())
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

    private static bool TryResolveLocalVideoPath(string? sourceUrl, out string localPath)
    {
        localPath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return false;
        }

        string candidate = sourceUrl.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            candidate = uri.LocalPath;
        }

        var ext = Path.GetExtension(candidate);
        if ((ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            && File.Exists(candidate))
        {
            localPath = Path.GetFullPath(candidate);
            return true;
        }

        return false;
    }

    private static async Task<string?> ResolveAndDownloadCoreAsync(string? sourceUrl, string? title, string? artist)
    {
        var remoteMp4Url = await ResolveRemoteCanvasUrlAsync(sourceUrl, title, artist).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(remoteMp4Url))
        {
            return null;
        }

        return await DownloadCanvasVideoAsync(remoteMp4Url).ConfigureAwait(false);
    }

    private static async Task<string?> ResolveRemoteCanvasUrlAsync(string? sourceUrl, string? title, string? artist)
    {
        TryExtractSpotifyTrackId(sourceUrl, out var trackId);

        // 1. Direct lookup by Spotify track ID if available.
        if (!string.IsNullOrWhiteSpace(trackId))
        {
            var directUrl = await TryGetCanvasByTrackIdAsync(trackId).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                return directUrl;
            }
        }

        // 2. Artist search and artist page lookup.
        foreach (var artistCandidate in GetArtistSearchCandidates(artist, title))
        {
            var slug = await TrySearchArtistSlugAsync(artistCandidate).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var artistCanvasUrl = await TryGetCanvasFromArtistSlugAsync(slug, trackId, title).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(artistCanvasUrl))
            {
                return artistCanvasUrl;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetArtistSearchCandidates(string? artist, string? title)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(artist))
        {
            var trimmed = artist.Trim();
            if (yielded.Add(trimmed))
            {
                yield return trimmed;
            }

            var primary = trimmed
                .Split([',', ';', '&', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(primary))
            {
                int featIndex = primary.IndexOf(" feat", StringComparison.OrdinalIgnoreCase);
                if (featIndex > 0)
                {
                    primary = primary[..featIndex].Trim();
                }

                if (!string.IsNullOrWhiteSpace(primary) && yielded.Add(primary))
                {
                    yield return primary;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            int dashIndex = title.IndexOf('-');
            if (dashIndex > 1)
            {
                var prefixArtist = title[..dashIndex].Trim();
                if (!string.IsNullOrWhiteSpace(prefixArtist) && yielded.Add(prefixArtist))
                {
                    yield return prefixArtist;
                }
            }
        }
    }

    private static async Task<string?> TryGetCanvasByTrackIdAsync(string trackId)
    {
        try
        {
            var trackUri = $"https://open.spotify.com/track/{Uri.EscapeDataString(trackId)}";
            var endpoint = new Uri($"https://www.canvasdownloader.com/canvas?link={Uri.EscapeDataString(trackUri)}");
            using var response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var html = await ReadLimitedUtf8Async(stream, MaxHtmlBytes).ConfigureAwait(false);
            return ExtractCanvasUrlFromHtml(html, preferredTrackId: trackId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> TrySearchArtistSlugAsync(string artistQuery)
    {
        try
        {
            var endpoint = new Uri($"https://www.canvasdownloader.com/api/search?q={Uri.EscapeDataString(artistQuery)}");
            using var response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("artists", out var artists)
                || artists.ValueKind != JsonValueKind.Array
                || artists.GetArrayLength() == 0)
            {
                return null;
            }

            string? fallbackSlug = null;
            foreach (var element in artists.EnumerateArray())
            {
                if (!element.TryGetProperty("slug", out var slugProp)
                    || slugProp.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(slugProp.GetString()))
                {
                    continue;
                }

                var slug = slugProp.GetString()!;
                fallbackSlug ??= slug;

                if (element.TryGetProperty("name", out var nameProp)
                    && nameProp.ValueKind == JsonValueKind.String
                    && string.Equals(nameProp.GetString()?.Trim(), artistQuery.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return slug;
                }
            }

            return fallbackSlug;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> TryGetCanvasFromArtistSlugAsync(string slug, string? preferredTrackId, string? title)
    {
        try
        {
            var endpoint = new Uri($"https://www.canvasdownloader.com/artists/{Uri.EscapeDataString(slug)}");
            using var response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var html = await ReadLimitedUtf8Async(stream, MaxHtmlBytes).ConfigureAwait(false);
            return ExtractCanvasUrlFromHtml(html, preferredTrackId, title);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> DownloadCanvasVideoAsync(string mp4Url)
    {
        if (!Uri.TryCreate(mp4Url, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var hashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..24].ToLowerInvariant();
            var finalPath = Path.Combine(CacheDirectory, $"canvas-{hashHex}.mp4");

            if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 1024)
            {
                return finalPath;
            }

            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxVideoBytes)
            {
                return null;
            }

            var tempPath = Path.Combine(CacheDirectory, $"canvas-{hashHex}-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
                {
                    var buffer = new byte[16 * 1024];
                    int totalRead = 0;
                    while (true)
                    {
                        int read = await sourceStream.ReadAsync(buffer).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        totalRead += read;
                        if (totalRead > MaxVideoBytes)
                        {
                            return null;
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    }
                }

                if (new FileInfo(tempPath).Length <= 1024)
                {
                    return null;
                }

                File.Move(tempPath, finalPath, overwrite: true);
                return finalPath;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> ReadLimitedUtf8Async(Stream stream, int maxBytes)
    {
        var buffer = new byte[16 * 1024];
        await using var destination = new MemoryStream();
        while (destination.Length < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - destination.Length))).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, (int)destination.Length);
    }
}
