using System.IO;
using System.Text.RegularExpressions;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Gaming;

public sealed class LinuxGameDiscoveryService : IGameDiscoveryService
{
    private static readonly Regex SteamManifestAppIdRegex = new(@"\b""appid""\s+""(\d+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamManifestNameRegex = new(@"\b""name""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamManifestExecRegex = new(@"\b""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamRungameidRegex = new(@"steam://rungameid/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> GameCategoryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Game", "ActionGame", "AdventureGame", "ArcadeGame", "BlocksGame", "BoardGame",
        "CardGame", "CasinoGame", "KidsGame", "LogicGame", "RolePlaying", "Simulation",
        "SportsGame", "StrategyGame", "Emulator"
    };

    public async Task<IReadOnlyList<GameIdentity>> DiscoverGamesAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<GameIdentity>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Steam AppManifest files
        foreach (var steamDir in GetSteamAppsDirectories())
        {
            if (!Directory.Exists(steamDir)) continue;

            foreach (var manifestFile in Directory.EnumerateFiles(steamDir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var text = await File.ReadAllTextAsync(manifestFile, cancellationToken).ConfigureAwait(false);
                    var appIdMatch = SteamManifestAppIdRegex.Match(text);
                    var nameMatch = SteamManifestNameRegex.Match(text);
                    var dirMatch = SteamManifestExecRegex.Match(text);

                    if (appIdMatch.Success && nameMatch.Success)
                    {
                        var appId = appIdMatch.Groups[1].Value;
                        var rawName = nameMatch.Groups[1].Value;

                        // Exclude Steamworks common redistributables or Proton runtimes
                        if (rawName.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
                            rawName.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
                            rawName.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var normalized = GameTitleNormalizer.Normalize(rawName);
                        var key = $"steam-{appId}";

                        if (seenKeys.Add(key))
                        {
                            var execPath = dirMatch.Success ? Path.Combine(steamDir, "common", dirMatch.Groups[1].Value) : null;
                            games.Add(new GameIdentity(
                                Title: rawName,
                                NormalizedTitle: normalized,
                                Source: GameStoreSource.Steam,
                                SourceId: appId,
                                ExecutablePath: execPath,
                                DesktopFilePath: null,
                                IconNameOrPath: "steam",
                                LocalArtworkPath: null,
                                Categories: ["Game", "Steam"]));
                        }
                    }
                }
                catch
                {
                    // Best effort manifest parsing
                }
            }
        }

        // 2. Scan Desktop Entries for Games
        var appLauncher = new LinuxAppLauncherService();
        var desktopApps = appLauncher.GetAvailableApps();

        foreach (var app in desktopApps)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var isGameCategory = app.Categories?.Any(cat => GameCategoryKeywords.Contains(cat)) ?? false;
            var steamMatch = app.LaunchTarget != null ? SteamRungameidRegex.Match(app.LaunchTarget) : Match.Empty;
            if (!steamMatch.Success && app.LaunchArguments != null)
            {
                foreach (var arg in app.LaunchArguments)
                {
                    var m = SteamRungameidRegex.Match(arg);
                    if (m.Success) { steamMatch = m; break; }
                }
            }

            if (steamMatch.Success)
            {
                var appId = steamMatch.Groups[1].Value;
                var key = $"steam-{appId}";
                if (seenKeys.Add(key))
                {
                    var normalized = GameTitleNormalizer.Normalize(app.Name);
                    games.Add(new GameIdentity(
                        Title: app.Name,
                        NormalizedTitle: normalized,
                        Source: GameStoreSource.Steam,
                        SourceId: appId,
                        ExecutablePath: app.LaunchTarget,
                        DesktopFilePath: app.Id,
                        IconNameOrPath: app.Icon,
                        LocalArtworkPath: null,
                        Categories: app.Categories));
                }
            }
            else if (isGameCategory)
            {
                var key = $"desktop-{app.Id}";
                if (seenKeys.Add(key))
                {
                    var normalized = GameTitleNormalizer.Normalize(app.Name);
                    var source = app.Name.Contains("Heroic", StringComparison.OrdinalIgnoreCase) ? GameStoreSource.Heroic
                               : app.Name.Contains("Lutris", StringComparison.OrdinalIgnoreCase) ? GameStoreSource.Lutris
                               : GameStoreSource.Desktop;

                    games.Add(new GameIdentity(
                        Title: app.Name,
                        NormalizedTitle: normalized,
                        Source: source,
                        SourceId: app.Id,
                        ExecutablePath: app.LaunchTarget,
                        DesktopFilePath: app.Id,
                        IconNameOrPath: app.Icon,
                        LocalArtworkPath: null,
                        Categories: app.Categories));
                }
            }
        }

        return games;
    }

    private static IEnumerable<string> GetSteamAppsDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam", "steamapps");
        yield return Path.Combine(home, ".local", "share", "Steam", "steamapps");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps");
    }
}
