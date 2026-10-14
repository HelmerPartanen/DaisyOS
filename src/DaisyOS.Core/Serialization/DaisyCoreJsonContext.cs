using System.Text.Json.Serialization;
using DaisyOS.Core.Models;
using DaisyOS.Core.Models.Gaming;

namespace DaisyOS.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ControllerKeybindingsConfig))]
[JsonSerializable(typeof(GameArtworkMetadata))]
[JsonSerializable(typeof(GameArtworkAssets))]
[JsonSerializable(typeof(GameStoreSource))]
public sealed partial class DaisyCoreJsonContext : JsonSerializerContext
{
}

