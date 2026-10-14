namespace DaisyOS.Core.Models.Gaming;

public sealed record GameIdentity(
    string Title,
    string NormalizedTitle,
    GameStoreSource Source,
    string? SourceId = null,
    string? ExecutablePath = null,
    string? DesktopFilePath = null,
    string? IconNameOrPath = null,
    string? LocalArtworkPath = null,
    IReadOnlyList<string>? Categories = null);
