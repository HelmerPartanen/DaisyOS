using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Appearance;

/// <summary>
/// Applies the desktop colour preference through the Linux mechanisms consumed
/// by GTK, Qt/KDE, and the XDG settings portal. DaisyOS remains responsible
/// for its own palette; this service communicates the matching system intent
/// to applications outside the shell.
/// </summary>
public sealed class LinuxSystemThemeService
{
    // Match the three caption actions exposed by the native DaisyOS decoration.
    public const string CaptionButtonLayout = ":minimize,maximize,close";
    private static readonly TimeSpan ChangeTimeout = TimeSpan.FromSeconds(5);

    private readonly ICommandRunner _commandRunner;
    private readonly string _configHome;

    public LinuxSystemThemeService(ICommandRunner commandRunner, string? configHome = null)
    {
        _commandRunner = commandRunner;
        _configHome = configHome ?? GetConfigHome();
    }

    public async Task<SystemThemeApplyResult> ApplyAsync(
        ThemeMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode == ThemeMode.Automatic)
            throw new ArgumentException("Automatic mode must be resolved to Light or Dark before applying the system theme.", nameof(mode));

        var dark = mode == ThemeMode.Dark;
        var gtkTheme = dark ? "Breeze-Dark" : "Breeze";
        var colorScheme = dark ? "prefer-dark" : "prefer-light";
        var kdeColorScheme = dark ? "BreezeDark" : "BreezeLight";

        // GTK 3 reads these files when a desktop-wide XSettings provider is
        // absent. GTK 4 also uses them as a documented fallback when no
        // settings portal is available, so keep both toolkits in sync.
        var gtkFallbackUpdated = UpdateGtkFallbackSettings(gtkTheme, dark);

        var gsettingsColorScheme = _commandRunner.RunAsync(
            "gsettings",
            ["set", "org.gnome.desktop.interface", "color-scheme", colorScheme],
            ChangeTimeout,
            cancellationToken);
        var gsettingsGtkTheme = _commandRunner.RunAsync(
            "gsettings",
            ["set", "org.gnome.desktop.interface", "gtk-theme", gtkTheme],
            ChangeTimeout,
            cancellationToken);
        var gsettingsButtons = _commandRunner.RunAsync(
            "gsettings",
            ["set", "org.gnome.desktop.wm.preferences", "button-layout", CaptionButtonLayout],
            ChangeTimeout,
            cancellationToken);
        // The KDE portal publishes its appearance preference from kdeglobals.
        // --notify gives already-running KDE/Qt clients a configuration change
        // signal; clients that do not support live theme updates use this on
        // their next launch.
        var kdeTheme = _commandRunner.RunAsync(
            "kwriteconfig6",
            ["--file", "kdeglobals", "--group", "General", "--key", "ColorScheme", kdeColorScheme, "--notify"],
            ChangeTimeout,
            cancellationToken);

        await Task.WhenAll(gsettingsColorScheme, gsettingsGtkTheme, gsettingsButtons, kdeTheme);
        return new SystemThemeApplyResult(
            GtkPreferenceUpdated: (await gsettingsColorScheme).Succeeded && (await gsettingsGtkTheme).Succeeded && (await gsettingsButtons).Succeeded,
            KdePreferenceUpdated: (await kdeTheme).Succeeded,
            GtkFallbackFilesUpdated: gtkFallbackUpdated);
    }

    private bool UpdateGtkFallbackSettings(string gtkTheme, bool dark)
    {
        try
        {
            UpsertGtkSettings(Path.Combine(_configHome, "gtk-3.0", "settings.ini"), gtkTheme, dark);
            UpsertGtkSettings(Path.Combine(_configHome, "gtk-4.0", "settings.ini"), gtkTheme, dark);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only home directory must not prevent the shell itself
            // from changing theme. gsettings and the portal can still succeed.
            return false;
        }
    }

    private static void UpsertGtkSettings(string path, string gtkTheme, bool dark)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var settingsStart = FindSettingsSection(lines);
        if (settingsStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add("[Settings]");
            settingsStart = lines.Count - 1;
        }

        UpsertKey(lines, settingsStart, "gtk-theme-name", gtkTheme);
        UpsertKey(lines, settingsStart, "gtk-application-prefer-dark-theme", dark ? "1" : "0");
        UpsertKey(lines, settingsStart, "gtk-decoration-layout", CaptionButtonLayout);

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        global::DaisyOS.Core.AtomicTextFile.Write(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static int FindSettingsSection(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index].Trim(), "[Settings]", StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static void UpsertKey(List<string> lines, int settingsStart, string key, string value)
    {
        var sectionEnd = lines.Count;
        for (var index = settingsStart + 1; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                sectionEnd = index;
                break;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && string.Equals(trimmed[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = $"{key}={value}";
                return;
            }
        }

        lines.Insert(sectionEnd, $"{key}={value}");
    }

    private static string GetConfigHome()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(configHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : configHome;
    }
}

public sealed record SystemThemeApplyResult(
    bool GtkPreferenceUpdated,
    bool KdePreferenceUpdated,
    bool GtkFallbackFilesUpdated)
{
    public bool Succeeded => GtkPreferenceUpdated || KdePreferenceUpdated || GtkFallbackFilesUpdated;
}
