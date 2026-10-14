using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DaisyOS.System.Processes;
using DaisyOS.Core.Models;

namespace DaisyOS.System.Appearance;

public sealed record CursorThemeOption(string Id, string DisplayName, string DirectoryPath);
public sealed record FontFamilyOption(string FamilyName);
public sealed record PersonalizationApplyResult(bool Succeeded, string? Error = null)
{
    public static PersonalizationApplyResult Success { get; } = new(true);
    public static PersonalizationApplyResult Failure(string message) => new(false, message);
}
public sealed record FontInstallResult(bool Succeeded, IReadOnlyList<string> Families, string? InstalledPath = null, string? Error = null)
{
    public static FontInstallResult Failure(string message) => new(false, [], null, message);
}

/// <summary>Applies per-user cursor and typography preferences through KDE, GTK and Fontconfig.</summary>
public sealed class LinuxPersonalizationService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);
    private static readonly HashSet<int> SupportedCursorSizes = [24, 32, 48, 64];
    private static readonly HashSet<int> SupportedTextScales = [90, 100, 110, 125, 150];
    private static readonly HashSet<string> SupportedFontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };
    private const long MaximumFontBytes = 64L * 1024 * 1024;

    private readonly ICommandRunner _runner;
    private readonly string _configHome;
    private readonly string _dataHome;
    private readonly IReadOnlyList<string> _dataDirectories;

    public LinuxPersonalizationService(
        ICommandRunner commandRunner,
        string? configHome = null,
        string? dataHome = null,
        IReadOnlyList<string>? dataDirectories = null)
    {
        _runner = commandRunner;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configHome = configHome ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        _dataHome = dataHome ?? Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share");
        _dataDirectories = dataDirectories ?? ParseDataDirectories(Environment.GetEnvironmentVariable("XDG_DATA_DIRS"));
    }

    public Task<IReadOnlyList<CursorThemeOption>> GetCursorThemesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<CursorThemeOption>>(() => DiscoverCursorThemes(), cancellationToken);

    public async Task<IReadOnlyList<FontFamilyOption>> GetFontFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("fc-list", [":scalable=true", "--format=%{family}\\n"], CommandTimeout, cancellationToken);
        if (!result.Succeeded) return [];
        return ParseFontFamilies(result.StandardOutput).Select(name => new FontFamilyOption(name)).ToArray();
    }

    public async Task<PersonalizationApplyResult> ApplyCursorAsync(string themeId, int size, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(themeId) || themeId.IndexOfAny(['/', '\\', '\r', '\n']) >= 0)
            return PersonalizationApplyResult.Failure("That cursor theme is not valid.");
        if (!SupportedCursorSizes.Contains(size))
            return PersonalizationApplyResult.Failure("Choose a supported cursor size.");

        var apply = await _runner.RunAsync("plasma-apply-cursortheme", ["--size", size.ToString(CultureInfo.InvariantCulture), themeId], CommandTimeout, cancellationToken);
        if (!apply.Succeeded)
            return PersonalizationApplyResult.Failure("The cursor theme could not be applied.");

        // plasma-apply-cursortheme updates the active theme, but on Plasma 6 it does
        // not reliably persist cursorSize. Keep KWin's source of truth complete and
        // notify the cursor subsystem to reload both values immediately. The
        // generic KWin reconfigure method does not reload cursor themes.
        var kdeTheme = await WriteKdeCursorAsync("cursorTheme", themeId, cancellationToken);
        var kdeSize = await WriteKdeCursorAsync("cursorSize", size.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!kdeTheme.Succeeded || !kdeSize.Succeeded)
            return PersonalizationApplyResult.Failure("The cursor changed, but KWin could not save the new pointer settings.");

        var reconfigure = await _runner.RunAsync(
            "dbus-send",
            ["--session", "--type=signal", "/KGlobalSettings", "org.kde.KGlobalSettings.notifyChange", "int32:5", "int32:0"],
            CommandTimeout,
            cancellationToken);
        if (!reconfigure.Succeeded)
            return PersonalizationApplyResult.Failure("The cursor was saved, but KWin could not apply it immediately.");

        // Check the compositor's live value, rather than treating a saved config
        // or the helper's "already set" response as proof of a size change.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var state = await _runner.RunAsync("qdbus6", ["org.kde.KWin", "/KWin", "supportInformation"], CommandTimeout, cancellationToken);
            var match = global::System.Text.RegularExpressions.Regex.Match(state.StandardOutput, @"(?m)^themeSize:\s*(\d+)");
            if (!state.Succeeded || !match.Success) break; // Non-KWin sessions have no such diagnostic.
            if (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) == size) break;
            if (attempt == 3) return PersonalizationApplyResult.Failure("The cursor was saved, but KWin has not applied the selected size.");
            await Task.Delay(80, cancellationToken);
        }

        var themeSetting = _runner.RunAsync("gsettings", ["set", "org.gnome.desktop.interface", "cursor-theme", themeId], CommandTimeout, cancellationToken);
        var sizeSetting = _runner.RunAsync("gsettings", ["set", "org.gnome.desktop.interface", "cursor-size", size.ToString(CultureInfo.InvariantCulture)], CommandTimeout, cancellationToken);
        try
        {
            UpsertGtkSetting("gtk-cursor-theme-name", themeId);
            UpsertGtkSetting("gtk-cursor-theme-size", size.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // KWin already accepted and persisted the selection. GTK files are a
            // compatibility mirror and must not turn that successful change into
            // a rejected selection in Settings.
        }

        // KWin is the DaisyOS compositor and therefore the authoritative success
        // condition. GTK settings are synchronized for compatible applications,
        // but their absence must not roll the working desktop change back in UI.
        await Task.WhenAll(themeSetting, sizeSetting);
        // D-Bus/systemd-activated applications do not inherit the shell's updated
        // environment. Keep their future launches on the same native cursor theme.
        await _runner.RunAsync("dbus-update-activation-environment",
            ["--systemd", "XCURSOR_THEME=" + themeId, "XCURSOR_SIZE=" + size.ToString(CultureInfo.InvariantCulture)],
            CommandTimeout, cancellationToken);
        return PersonalizationApplyResult.Success;
    }

    public async Task<PersonalizationApplyResult> ApplyTypographyAsync(string family, int textScalePercent, CancellationToken cancellationToken = default)
    {
        family = family.Trim();
        if (family.Length == 0 || family.IndexOfAny(['\r', '\n']) >= 0)
            return PersonalizationApplyResult.Failure("Choose a valid font family.");
        if (!SupportedTextScales.Contains(textScalePercent))
            return PersonalizationApplyResult.Failure("Choose a supported text size.");

        var scale = textScalePercent / 100d;
        var basePoints = Math.Round(10 * scale, 1, MidpointRounding.AwayFromZero);
        var smallPoints = Math.Round(8 * scale, 1, MidpointRounding.AwayFromZero);
        var kdeFamily = family.Replace(",", "\\,", StringComparison.Ordinal);
        var normalFont = SerializeKdeFont(kdeFamily, basePoints);
        var smallFont = SerializeKdeFont(kdeFamily, smallPoints);
        var kdeWrites = new[]
        {
            WriteKdeFontAsync("font", normalFont, cancellationToken),
            WriteKdeFontAsync("menuFont", normalFont, cancellationToken),
            WriteKdeFontAsync("toolBarFont", normalFont, cancellationToken),
            WriteKdeFontAsync("activeFont", normalFont, cancellationToken),
            WriteKdeFontAsync("smallestReadableFont", smallFont, cancellationToken)
        };
        var gtkFontName = $"{family} {basePoints.ToString("0.#", CultureInfo.InvariantCulture)}";
        var gsettingsFont = _runner.RunAsync("gsettings", ["set", "org.gnome.desktop.interface", "font-name", gtkFontName], CommandTimeout, cancellationToken);
        var gsettingsScale = _runner.RunAsync("gsettings", ["set", "org.gnome.desktop.interface", "text-scaling-factor", scale.ToString("0.##", CultureInfo.InvariantCulture)], CommandTimeout, cancellationToken);

        try { UpsertGtkSetting("gtk-font-name", gtkFontName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return PersonalizationApplyResult.Failure("The font could not be saved for GTK applications."); }

        var results = await Task.WhenAll(kdeWrites.Append(gsettingsFont).Append(gsettingsScale));
        return results.All(result => result.Succeeded)
            ? PersonalizationApplyResult.Success
            : PersonalizationApplyResult.Failure("The font changed in DaisyOS, but some applications could not be synchronized.");
    }

    public async Task<FontInstallResult> InstallFontAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) return FontInstallResult.Failure("The selected font file no longer exists.");
        var info = new FileInfo(sourcePath);
        if (!SupportedFontExtensions.Contains(info.Extension)) return FontInstallResult.Failure("Choose a TTF, OTF, or TTC font file.");
        if (info.Length <= 0 || info.Length > MaximumFontBytes) return FontInstallResult.Failure("The font file must be smaller than 64 MB.");

        var scan = await _runner.RunAsync("fc-scan", ["--format=%{family}\\n", sourcePath], CommandTimeout, cancellationToken);
        var families = scan.Succeeded ? ParseFontFamilies(scan.StandardOutput) : [];
        if (families.Count == 0) return FontInstallResult.Failure("This file does not contain a usable font family.");

        var destinationDirectory = Path.Combine(_dataHome, "fonts", "DaisyOS");
        Directory.CreateDirectory(destinationDirectory);
        string hash;
        await using (var stream = File.OpenRead(sourcePath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))[..12].ToLowerInvariant();
        var safeName = string.Concat(Path.GetFileNameWithoutExtension(sourcePath).Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')).Trim('-');
        if (safeName.Length == 0) safeName = "font";
        var destination = Path.Combine(destinationDirectory, $"{safeName}-{hash}{info.Extension.ToLowerInvariant()}");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(sourcePath))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(temporary, destination, true);
            var cache = await _runner.RunAsync("fc-cache", ["-f", destinationDirectory], CommandTimeout, cancellationToken);
            if (!cache.Succeeded)
            {
                File.Delete(destination);
                return FontInstallResult.Failure("The font was valid, but Fontconfig could not activate it.");
            }
            return new FontInstallResult(true, families, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch { }
            return FontInstallResult.Failure("The font could not be installed for this user.");
        }
    }

    internal static IReadOnlyList<string> ParseFontFamilies(string output) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(line => line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(name => name.Length > 0)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private IReadOnlyList<CursorThemeOption> DiscoverCursorThemes()
    {
        var roots = new[] { Path.Combine(_dataHome, "icons") }
            .Concat(_dataDirectories.Select(path => Path.Combine(path, "icons")));
        var themes = new Dictionary<string, CursorThemeOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(directory);
                var index = Path.Combine(directory, "index.theme");
                if (themes.ContainsKey(id) || !File.Exists(index) || !Directory.Exists(Path.Combine(directory, "cursors"))) continue;
                themes[id] = new CursorThemeOption(id, ReadThemeName(index) ?? id.Replace('_', ' '), directory);
            }
        }
        return themes.Values.OrderBy(theme => theme.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string? ReadThemeName(string path)
    {
        try
        {
            var inIconTheme = false;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('[')) { inIconTheme = line.Equals("[Icon Theme]", StringComparison.OrdinalIgnoreCase); continue; }
                if (inIconTheme && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase)) return line[5..].Trim();
            }
        }
        catch (IOException) { }
        return null;
    }

    private Task<CommandResult> WriteKdeFontAsync(string key, string value, CancellationToken cancellationToken) =>
        _runner.RunAsync("kwriteconfig6", ["--file", "kdeglobals", "--group", "General", "--key", key, value, "--notify"], CommandTimeout, cancellationToken);

    private Task<CommandResult> WriteKdeCursorAsync(string key, string value, CancellationToken cancellationToken) =>
        _runner.RunAsync("kwriteconfig6", ["--file", "kcminputrc", "--group", "Mouse", "--key", key, value, "--notify"], CommandTimeout, cancellationToken);

    private static string SerializeKdeFont(string family, double points) =>
        $"{family},{points.ToString("0.#", CultureInfo.InvariantCulture)},-1,5,50,0,0,0,0,0";

    private void UpsertGtkSetting(string key, string value)
    {
        UpsertIni(Path.Combine(_configHome, "gtk-3.0", "settings.ini"), key, value);
        UpsertIni(Path.Combine(_configHome, "gtk-4.0", "settings.ini"), key, value);
    }

    private static void UpsertIni(string path, string key, string value)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var section = lines.FindIndex(line => line.Trim().Equals("[Settings]", StringComparison.OrdinalIgnoreCase));
        if (section < 0) { if (lines.Count > 0) lines.Add(string.Empty); lines.Add("[Settings]"); section = lines.Count - 1; }
        var end = lines.FindIndex(section + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        var entry = lines.FindIndex(section + 1, end - section - 1, line => line.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        if (entry >= 0) lines[entry] = $"{key}={value}"; else lines.Insert(end, $"{key}={value}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        global::DaisyOS.Core.AtomicTextFile.Write(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static IReadOnlyList<string> ParseDataDirectories(string? value) =>
        (string.IsNullOrWhiteSpace(value) ? "/usr/local/share:/usr/share" : value)
        .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
