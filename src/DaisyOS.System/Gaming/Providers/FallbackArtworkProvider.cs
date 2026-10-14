using System.IO;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;

namespace DaisyOS.System.Gaming.Providers;

public sealed class FallbackArtworkProvider : IGameArtworkProvider
{
    public string Name => "DaisyOS Fallback Card Provider";
    public int Priority => 5;

    public bool CanResolve(GameIdentity identity) => true;

    public Task<GameArtworkAssets?> ResolveAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(identity.IconNameOrPath) && File.Exists(identity.IconNameOrPath))
        {
            iconPath = identity.IconNameOrPath;
        }

        var assets = new GameArtworkAssets(
            HeroPath: null,
            CoverPath: null,
            LogoPath: null,
            IconPath: iconPath,
            IsFallback: true,
            ArtworkSource: "fallback",
            LastChecked: DateTimeOffset.UtcNow);

        return Task.FromResult<GameArtworkAssets?>(assets);
    }
}
