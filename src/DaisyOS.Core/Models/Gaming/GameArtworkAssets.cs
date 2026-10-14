namespace DaisyOS.Core.Models.Gaming;

public sealed record GameArtworkAssets(
    string? HeroPath,
    string? CoverPath,
    string? LogoPath,
    string? IconPath,
    bool IsFallback,
    string ArtworkSource,
    DateTimeOffset LastChecked)
{
    public bool HasHero => !string.IsNullOrWhiteSpace(HeroPath);
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);
}
