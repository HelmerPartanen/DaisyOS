using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisyOS.System.Media;

/// <summary>
/// Supplies artwork when a media source omits MPRIS artwork.
/// Resolves directly from YouTube thumbnails, Spotify oEmbed, iTunes Search API,
/// Deezer API, or OpenGraph meta tags. Requests are anonymous, bounded, and fast.
/// </summary>
internal static class BrowserMediaArtworkResolver
{
    private const int MaxMetadataBytes = 384 * 1024;
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        """(?<name>[\w:-]+)\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)'|(?<bare>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static async Task<string?> ResolveAsync(string? sourceUrl, string? title = null, string? artist = null)
    {
        // 1. Check for deterministic direct media URLs (e.g. YouTube, direct Spotify track links).
        if (TryGetPublicHttpsUri(sourceUrl, out var pageUri))
        {
            var youtubeArtwork = TryGetYouTubeArtworkUri(pageUri);
            if (youtubeArtwork is not null)
            {
                return youtubeArtwork;
            }

            if (IsSpotifyUri(pageUri) && IsSpotifyTrackOrEpisodeUri(pageUri))
            {
                var spotifyArt = await TryGetSpotifyArtworkAsync(pageUri).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(spotifyArt))
                {
                    return spotifyArt;
                }
            }
        }

        // 2. Query iTunes Search API and Deezer API by track title & artist.
        // This flawlessly handles Spotify Web, Apple Music web, YouTube Music, Deezer, etc.
        if (!string.IsNullOrWhiteSpace(title))
        {
            var itunesArt = await TryGetItunesArtworkAsync(title, artist).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(itunesArt))
            {
                return itunesArt;
            }

            var deezerArt = await TryGetDeezerArtworkAsync(title, artist).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(deezerArt))
            {
                return deezerArt;
            }
        }

        // 3. Fallback to OpenGraph meta tags if we have a public web page URI.
        if (TryGetPublicHttpsUri(sourceUrl, out var ogUri))
        {
            return await TryGetOpenGraphArtworkAsync(ogUri).ConfigureAwait(false);
        }

        return null;
    }

    internal static string? TryGetYouTubeArtworkUri(Uri sourceUri)
    {
        var host = sourceUri.Host;
        string? videoId = null;

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            videoId = sourceUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        else if (host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceUri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                videoId = ParseQueryValue(sourceUri.Query, "v");
            }
            else if (sourceUri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)
                     || sourceUri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase)
                     || sourceUri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
            {
                videoId = sourceUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
            }
        }

        return videoId is { Length: 11 } && videoId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg"
            : null;
    }

    private static async Task<string?> TryGetSpotifyArtworkAsync(Uri sourceUri)
    {
        try
        {
            var endpoint = new UriBuilder("https://open.spotify.com/oembed")
            {
                Query = $"url={Uri.EscapeDataString(sourceUri.AbsoluteUri)}"
            }.Uri;
            using var response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("thumbnail_url", out var thumbnail)
                   && thumbnail.ValueKind == JsonValueKind.String
                   && TryGetPublicHttpsUri(thumbnail.GetString(), out var imageUri)
                ? imageUri.AbsoluteUri
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> TryGetItunesArtworkAsync(string title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            var searchTerm = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var uri = new UriBuilder("https://itunes.apple.com/search")
            {
                Query = $"term={Uri.EscapeDataString(searchTerm)}&entity=song&limit=1"
            }.Uri;

            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array
                && results.GetArrayLength() > 0)
            {
                var first = results[0];
                if (first.TryGetProperty("artworkUrl100", out var artworkProp)
                    && artworkProp.ValueKind == JsonValueKind.String
                    && artworkProp.GetString() is { } artworkUrl)
                {
                    // Request high-resolution artwork (512x512)
                    return artworkUrl.Replace("100x100bb", "512x512bb", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
        }

        return null;
    }

    private static async Task<string?> TryGetDeezerArtworkAsync(string title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            var query = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var uri = new UriBuilder("https://api.deezer.com/search")
            {
                Query = $"q={Uri.EscapeDataString(query)}&limit=1"
            }.Uri;

            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.TryGetProperty("album", out var album)
                    && album.TryGetProperty("cover_big", out var cover)
                    && cover.ValueKind == JsonValueKind.String
                    && cover.GetString() is { } coverUrl)
                {
                    return coverUrl;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
        }

        return null;
    }

    private static async Task<string?> TryGetOpenGraphArtworkAsync(Uri pageUri)
    {
        try
        {
            using var response = await HttpClient.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType is not { } contentType
                || !contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentLength > MaxMetadataBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var markup = await ReadLimitedUtf8Async(stream).ConfigureAwait(false);
            foreach (Match metaTag in MetaTagRegex.Matches(markup))
            {
                var attributes = ReadAttributes(metaTag.Value);
                if (!attributes.TryGetValue("content", out var imageSource)
                    || (!attributes.TryGetValue("property", out var property)
                        && !attributes.TryGetValue("name", out property))
                    || (!property.Equals("og:image", StringComparison.OrdinalIgnoreCase)
                        && !property.Equals("twitter:image", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var decoded = WebUtility.HtmlDecode(imageSource);
                if (Uri.TryCreate(pageUri, decoded, out var imageUri)
                    && imageUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return imageUri.AbsoluteUri;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // The media session remains usable when a provider blocks anonymous metadata requests.
        }

        return null;
    }

    private static async Task<string> ReadLimitedUtf8Async(Stream stream)
    {
        var buffer = new byte[16 * 1024];
        await using var destination = new MemoryStream();
        while (destination.Length < MaxMetadataBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaxMetadataBytes - destination.Length))).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, (int)destination.Length);
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(tag))
        {
            attributes[match.Groups["name"].Value] = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
        }

        return attributes;
    }

    internal static bool IsSpotifyTrackOrEpisodeUri(Uri sourceUri) =>
        IsSpotifyUri(sourceUri)
        && sourceUri.Segments.Length >= 3
        && (sourceUri.Segments[1].TrimEnd('/').Equals("track", StringComparison.OrdinalIgnoreCase)
            || sourceUri.Segments[1].TrimEnd('/').Equals("episode", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(sourceUri.Segments[2].Trim('/'));

    private static bool IsSpotifyUri(Uri sourceUri) =>
        sourceUri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase)
        || sourceUri.Host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase);

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var candidateKey = separator < 0 ? pair : pair[..separator];
            if (candidateKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            }
        }

        return null;
    }

    private static bool TryGetPublicHttpsUri(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(uri.Host);
}
