namespace DaisyOS.Core.Models;

public sealed record MediaSession(
    string Title,
    string Artist,
    string? AlbumArtPath,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    string? SourceIdentity = null,
    string? SourceUrl = null);
