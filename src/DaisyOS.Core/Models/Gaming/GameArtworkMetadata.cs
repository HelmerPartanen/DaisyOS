using System.Text.Json.Serialization;

namespace DaisyOS.Core.Models.Gaming;

public sealed record GameArtworkMetadataAssets(
    [property: JsonPropertyName("hero")] string? Hero,
    [property: JsonPropertyName("cover")] string? Cover,
    [property: JsonPropertyName("logo")] string? Logo,
    [property: JsonPropertyName("icon")] string? Icon);

public sealed record GameArtworkMetadata(
    [property: JsonPropertyName("game")] string Game,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("artwork_source")] string ArtworkSource,
    [property: JsonPropertyName("last_checked")] string LastChecked,
    [property: JsonPropertyName("assets")] GameArtworkMetadataAssets Assets);
