using System.IO;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;

namespace DaisyOS.System.Gaming.Providers;

public sealed class MetadataArtworkProvider : IGameArtworkProvider
{
    private const double MinConfidenceThreshold = 0.85;

    public string Name => "Metadata Artwork API Provider";
    public int Priority => 3;

    public bool CanResolve(GameIdentity identity) => !string.IsNullOrWhiteSpace(identity.Title);

    public Task<GameArtworkAssets?> ResolveAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        var normalized = GameTitleNormalizer.Normalize(identity.Title);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult<GameArtworkAssets?>(null);
        }

        // Verify confidence score against query title
        var confidence = GameTitleNormalizer.CalculateConfidence(identity.Title, normalized);
        if (confidence < MinConfidenceThreshold)
        {
            // Low confidence match: do NOT silently choose artwork when multiple games are plausible.
            return Task.FromResult<GameArtworkAssets?>(null);
        }

        // Modular provider interface allows registering metadata web APIs or local database indexes.
        return Task.FromResult<GameArtworkAssets?>(null);
    }
}
