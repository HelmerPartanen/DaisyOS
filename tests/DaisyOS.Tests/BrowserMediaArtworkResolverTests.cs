using DaisyOS.System.Media;
using Xunit;

namespace DaisyOS.Tests;

public sealed class BrowserMediaArtworkResolverTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=43", "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg")]
    public void TryGetYouTubeArtworkUriBuildsTheVideoThumbnailUrl(string source, string expected)
    {
        var artwork = BrowserMediaArtworkResolver.TryGetYouTubeArtworkUri(new Uri(source));

        Assert.Equal(expected, artwork);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=not-a-video-id")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    public void TryGetYouTubeArtworkUriRejectsUnknownOrMalformedSources(string source)
    {
        var artwork = BrowserMediaArtworkResolver.TryGetYouTubeArtworkUri(new Uri(source));

        Assert.Null(artwork);
    }

    [Theory]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC", true)]
    [InlineData("https://open.spotify.com/episode/7makk4oTQel546B0PZlDM5", true)]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", false)]
    [InlineData("https://open.spotify.com/album/1ATL5GLyefJaxhQzSPVrLX", false)]
    public void IsSpotifyTrackOrEpisodeUriAvoidsCollectionArtwork(string source, bool expected)
    {
        Assert.Equal(expected, BrowserMediaArtworkResolver.IsSpotifyTrackOrEpisodeUri(new Uri(source)));
    }
}
