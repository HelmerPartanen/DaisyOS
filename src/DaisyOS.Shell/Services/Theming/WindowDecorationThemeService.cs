using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>
/// Exports DaisyOS's resolved semantic window-chrome roles for KWin's native
/// KDecoration plugin. The plugin never parses Avalonia XAML; this bridge is
/// the single runtime contract between the shell and compositor process.
/// </summary>
public sealed class WindowDecorationThemeService
{
    private const string ConfigFileName = "daisyos-window-decorationrc";
    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private string? _lastExportedConfig;
    private bool _frameEffectChecked;

    public void Export(Application application) => _ = ExportAsync(application);

    public async Task ExportAsync(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        await _exportGate.WaitAsync();
        try
        {
            // Snapshot both palettes together after previous writes finish, so an
            // older startup/theme export cannot overwrite a newer preference.
            (string, IReadOnlyList<KeyValuePair<string, string>>, IReadOnlyList<KeyValuePair<string, string>>) CaptureTheme() =>
                (BuildConfig(application), BuildOverviewConfig(application), BuildFrameEffectConfig(application));
            var (content, overviewValues, frameValues) = Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
                ? CaptureTheme()
                : await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(CaptureTheme);

            if (string.Equals(_lastExportedConfig, content, StringComparison.Ordinal))
            {
                return;
            }

            var directory = GetConfigDirectory();
            // File I/O and qdbus6 process spawn run on the thread pool to avoid
            // blocking the UI thread during startup or theme changes.
            await Task.Run(() =>
            {
                // Existing development launchers may predate effect provisioning.
                // Repair those accounts when the updated shell starts, too.
                if (!_frameEffectChecked)
                {
                    try { _frameEffectChecked = EnsureDevelopmentFrameEffectAsync().GetAwaiter().GetResult(); }
                    catch (Exception ex) { Console.Error.WriteLine($"[daisy-decoration] Frame effect repair failed: {ex.Message}"); }
                }
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, ConfigFileName);
                var temporary = Path.Combine(directory, $".{ConfigFileName}.{Environment.ProcessId}.tmp");
                File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporary, destination, overwrite: true);
                ExportKWinConfig(overviewValues, "Effect-daisyos-task-overview-v16");
                ExportKWinConfig(overviewValues.Where(pair => pair.Key == "ReduceMotion").ToArray(), "Effect-daisyos-window-motion");
                ExportKWinConfig(frameValues, "Round-Corners");
                ExportKWinConfig([new("BlurStrength", "4")], "Effect-blur");
                ExportKWinConfig([new("blurEnabled", "true"), new("daisyframeEnabled", "true"), new("kwin4_effect_shapecornersEnabled", "false")], "Plugins");
                RequestKWinReconfigureAsync().GetAwaiter().GetResult();
            }).ConfigureAwait(false);
            _lastExportedConfig = _frameEffectChecked ? content : null;
        }
        catch (Exception ex)
        {
            // Decoration export must not be able to degrade shell startup or a
            // theme change. KWin uses its most recent valid palette instead.
            Console.Error.WriteLine($"[daisy-decoration] Failed to export the KWin decoration theme: {ex.Message}");
        }
        finally
        {
            _exportGate.Release();
        }
    }

    internal static string BuildConfig(Application application)
    {
        var theme = application.ActualThemeVariant;
        var values = new (string Key, string Value)[]
        {
            ("SurfaceBase", ToArgb(Opaque(ResolveColor(application, theme, "SurfaceBaseSolidBrush", "SurfaceBaseColorSolid",
                ToArgb(ResolveColor(application, theme, "SurfaceBaseBrush", "SurfaceBaseColor", "#FF121214")))))),
            ("SurfaceRaised", ToArgb(Opaque(ResolveColor(application, theme, "SurfaceRaisedBrush", "SurfaceRaisedColor", "#FF1A1A1D")))),
            ("WindowBorder", ToArgb(ResolveColor(application, theme, "StrokeDefaultBrush", "StrokeDefaultColor", "#26FFFFFF"))),
            ("ShellFrameOuterEdge", ToArgb(ResolveColor(application, theme, "ShellFrameOuterEdgeBrush", "ShellFrameOuterEdgeColor", "#99000000"))),
            ("ShellFrameInnerHighlight", ToArgb(ResolveColor(application, theme, "ShellFrameInnerHighlightBrush", "ShellFrameInnerHighlightColor", "#26FFFFFF"))),
            ("ContentPrimary", ToArgb(ResolveColor(application, theme, "ContentPrimaryBrush", "ContentPrimaryColor", "#FFF5F5F7"))),
            ("ContentDisabled", ToArgb(ResolveColor(application, theme, "ContentDisabledBrush", "ContentDisabledColor", "#FF98989F"))),
            ("StateHover", ToArgb(ResolveColor(application, theme, "StateHoverBrush", "StateHoverColor", "#14FFFFFF"))),
            ("StatePressed", ToArgb(ResolveColor(application, theme, "StatePressedBrush", "StatePressedColor", "#24FFFFFF"))),
            ("StatusDanger", ToArgb(ResolveColor(application, theme, "ActionDangerHoverBrush", "ActionDangerHoverColor", "#FFB8212C"))),
            ("ContentOnAction", ToArgb(ResolveColor(application, theme, "ContentOnActionBrush", "ContentOnActionColor", "#FFFFFFFF"))),
            ("TitleBarHeight", ResolveDouble(application, theme, "KWinDecorationTitleBarHeight", 32).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("CaptionButtonWidth", ResolveDouble(application, theme, "KWinDecorationCaptionButtonWidth", 46).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("CaptionButtonHeight", ResolveDouble(application, theme, "KWinDecorationCaptionButtonHeight", 32).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("CaptionGlyphSize", ResolveDouble(application, theme, "KWinDecorationCaptionGlyphSize", 10).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("TitleFontFamily", ResolveString(application, theme, "SystemFontFamilyName", TypographyPolicy.DefaultFamilyName)),
            ("TitleFontPixels", ResolveDouble(application, theme, "KWinDecorationTitleFontPixels", 13).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("ActionPrimary", ToArgb(ResolveColor(application, theme, "ActionPrimaryBrush", "ActionPrimaryColor", theme == ThemeVariant.Light ? "#FF1598FF" : "#FF148BE8"))),
            ("ReduceMotion", application is App app && app.ReduceMotionEnabled ? "true" : "false"),
            ("CornerRadius", ResolveDouble(application, theme, "AppWindowCornerRadiusValue", 16).ToString(global::System.Globalization.CultureInfo.InvariantCulture))
        };

        var builder = new StringBuilder("[Decoration]\n");
        foreach (var (key, value) in values)
        {
            builder.Append(key).Append('=').Append(value).Append('\n');
        }

        // KWin's native QMenus use the same semantic menu contract as Avalonia.
        // Preserve the semantic brush opacity; KWin blurs behind native menu windows.
        var menuValues = new (string Key, string Value)[]
        {
            ("Surface", ToArgb(ResolveMenuSurface(application, theme))),
            ("FrameOuter", ToArgb(ResolveColor(application, theme, "ShellFrameOuterEdgeBrush", "ShellFrameOuterEdgeColor", "#99000000"))),
            ("FrameInner", ToArgb(ResolveColor(application, theme, "ShellFrameInnerHighlightBrush", "ShellFrameInnerHighlightColor", "#14FFFFFF"))),
            ("ContentPrimary", ToArgb(ResolveColor(application, theme, "ContentPrimaryBrush", "ContentPrimaryColor", "#FFFFFFFF"))),
            ("ContentSecondary", ToArgb(ResolveColor(application, theme, "ContentSecondaryBrush", "ContentSecondaryColor", "#CCFFFFFF"))),
            ("ContentDisabled", ToArgb(ResolveColor(application, theme, "ContentDisabledBrush", "ContentDisabledColor", "#66FFFFFF"))),
            ("StateHover", ToArgb(ResolveColor(application, theme, "StateHoverBrush", "StateHoverColor", "#14FFFFFF"))),
            ("StatePressed", ToArgb(ResolveColor(application, theme, "StatePressedBrush", "StatePressedColor", "#24FFFFFF"))),
            ("Separator", ToArgb(ResolveColor(application, theme, "StrokeDefaultBrush", "StrokeDefaultColor", "#26FFFFFF"))),
            ("FontFamily", ResolveString(application, theme, "SystemFontFamilyName", TypographyPolicy.DefaultFamilyName)),
            ("FontPixels", ResolveDouble(application, theme, "TypeSizeBodySmall", 13).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("ItemHeight", ResolveDouble(application, theme, "HitTargetCompact", 32).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("IconSize", ResolveDouble(application, theme, "IconSizeStandard", 18).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("MinWidth", ResolveDouble(application, theme, "MenuMinWidth", 184).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("CornerRadius", ResolveDouble(application, theme, "RoundedLGValue", 14).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            ("ItemCornerRadius", (application.TryGetResource("RadiusControlInner", theme, out var radius) && radius is CornerRadius corner ? corner.TopLeft : 8).ToString(global::System.Globalization.CultureInfo.InvariantCulture))
        };
        builder.Append("\n[Menus]\n");
        foreach (var (key, value) in menuValues) builder.Append(key).Append('=').Append(value).Append('\n');
        return builder.ToString();
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> BuildOverviewConfig(Application application)
    {
        var theme = application.ActualThemeVariant;
        return
        [
            new("SurfaceBase", ToArgb(Opaque(ResolveColor(application, theme, "SurfaceBaseSolidBrush", "SurfaceBaseColorSolid",
                ToArgb(ResolveColor(application, theme, "SurfaceBaseBrush", "SurfaceBaseColor", "#FF121214")))))),
            new("SurfaceRaised", ToArgb(Opaque(ResolveColor(application, theme, "SurfaceRaisedBrush", "SurfaceRaisedColor", "#FF1A1A1D")))),
            new("ContentPrimary", ToArgb(ResolveColor(application, theme, "ContentPrimaryBrush", "ContentPrimaryColor", "#FFF5F5F7"))),
            new("ContentDisabled", ToArgb(ResolveColor(application, theme, "ContentDisabledBrush", "ContentDisabledColor", "#FF98989F"))),
            new("ContentOnAction", ToArgb(ResolveColor(application, theme, "ContentOnActionBrush", "ContentOnActionColor", "#FFFFFFFF"))),
            new("ContentSecondary", ToArgb(ResolveColor(application, theme, "ContentSecondaryBrush", "ContentSecondaryColor", "#FFB3B3BA"))),
            new("StatusDanger", ToArgb(ResolveColor(application, theme, "ActionDangerHoverBrush", "ActionDangerHoverColor", "#FFB8212C"))),
            new("FrameOuter", ToArgb(ResolveColor(application, theme, "ShellFrameOuterEdgeBrush", "ShellFrameOuterEdgeColor", "#99000000"))),
            new("FrameInner", ToArgb(ResolveColor(application, theme, "ShellFrameInnerHighlightBrush", "ShellFrameInnerHighlightColor", "#26FFFFFF"))),
            new("CornerRadius", ResolveDouble(application, theme, "AppWindowCornerRadiusValue", 16).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("TitleBarHeight", ResolveDouble(application, theme, "KWinDecorationTitleBarHeight", 32).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("CaptionButtonWidth", ResolveDouble(application, theme, "KWinDecorationCaptionButtonWidth", 46).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("CaptionGlyphSize", ResolveDouble(application, theme, "KWinDecorationCaptionGlyphSize", 10).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("TitleFontFamily", ResolveString(application, theme, "SystemFontFamilyName", TypographyPolicy.DefaultFamilyName)),
            new("TitleFontPixels", ResolveDouble(application, theme, "KWinDecorationTitleFontPixels", 13).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("TileSpacing", "20"),
            new("OuterPadding", "48"),
            new("MaxPreviewScale", "1"),
            new("HoverScale", "1.012"),
            new("AnimationDuration", "280"),
            new("ReduceMotion", application is App app && app.ReduceMotionEnabled ? "true" : "false")
        ];
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> BuildFrameEffectConfig(Application application)
    {
        var theme = application.ActualThemeVariant;
        var outer = ResolveColor(application, theme, "StrokeDefaultBrush", "StrokeDefaultColor", "#26FFFFFF");
        var inner = Colors.Transparent;
        static string Rgb(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        return
        [
            new("Size", ResolveDouble(application, theme, "AppWindowCornerRadiusValue", 16).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("InactiveCornerRadius", ResolveDouble(application, theme, "AppWindowCornerRadiusValue", 16).ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("OutlineThickness", "1"),
            new("InactiveOutlineThickness", "1"),
            new("OutlineColor", Rgb(outer)),
            new("InactiveOutlineColor", Rgb(outer)),
            new("ActiveOutlineAlpha", outer.A.ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("InactiveOutlineAlpha", outer.A.ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("SecondOutlineColor", Rgb(inner)),
            new("InactiveSecondOutlineColor", Rgb(inner)),
            new("ActiveSecondOutlineAlpha", inner.A.ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("InactiveSecondOutlineAlpha", inner.A.ToString(global::System.Globalization.CultureInfo.InvariantCulture)),
            new("SecondOutlineThickness", "0"),
            new("InactiveSecondOutlineThickness", "0"),
            new("OuterOutlineThickness", "0"),
            new("InactiveOuterOutlineThickness", "0"),
            // Repair legacy process-wide exclusions on upgrades as well as fresh installs.
            // The effect excludes layer-shell surfaces by type; app windows need the frame.
            new("Exclusions", "DaisyOS Lock Screen,DaisyOS Lock Wallpaper"),
            new("IncludeNormalWindows", "true"),
            new("IncludeDialogs", "true"),
            new("DisableOutlineTile", "false"),
            new("DisableOutlineMaximize", "false"),
            new("DisableOutlineFullScreen", "false")
        ];
    }

    private static Color ResolveMenuSurface(Application application, ThemeVariant? theme)
    {
        var color = ResolveColor(application, theme, "ContextMenuBackgroundBrush", "ContextMenuBackgroundColor", "#FF212121");
        var opacity = application.TryGetResource("ContextMenuBackgroundBrush", theme, out var value) && value is ISolidColorBrush brush
            ? brush.Opacity : 0.6;
        return Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);
    }

    private static Color ResolveColor(Application application, ThemeVariant? theme, string brushKey, string colorKey, string fallback)
    {
        if (application.TryGetResource(brushKey, theme, out var brushValue) && brushValue is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        if (application.TryGetResource(colorKey, theme, out var colorValue) && colorValue is Color color)
        {
            return color;
        }

        return Color.Parse(fallback);
    }

    private static double ResolveDouble(Application application, ThemeVariant? theme, string key, double fallback) =>
        application.TryGetResource(key, theme, out var value) && value is double number ? number : fallback;

    private static string ResolveString(Application application, ThemeVariant? theme, string key, string fallback) =>
        application.TryGetResource(key, theme, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Replace('\n', ' ').Replace('\r', ' ').Trim()
            : fallback;

    private static string ToArgb(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    private static Color Opaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);

    private static string GetConfigDirectory() =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    private static void ExportKWinConfig(IReadOnlyList<KeyValuePair<string, string>> values, string? group = null)
    {
        if (!OperatingSystem.IsLinux() || !string.Equals(Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION"), "1", StringComparison.Ordinal)) return;

        foreach (var (key, value) in values)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "kwriteconfig6",
                    ArgumentList =
                    {
                        "--file", "kwinrc",
                        "--group", group ?? (key == "ReduceMotion" ? "Effect-daisyos-window-motion" : "Effect-daisyos-task-overview-v16"),
                        "--key", key,
                        value
                    },
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                process?.WaitForExit(2000);
            }
            catch
            {
                // The package defaults remain usable in stripped-down sessions.
            }
        }
    }

    private static async Task<bool> EnsureDevelopmentFrameEffectAsync()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION") != "1") return true;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var installer = Path.Combine(directory.FullName, "scripts", "install-kwin-frame-effect.sh");
            if (File.Exists(installer))
            {
                async Task<int> RunAsync(bool check)
                {
                    var start = new ProcessStartInfo(installer)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    if (check) start.ArgumentList.Add("--check");
                    using var process = Process.Start(start) ?? throw new IOException("Could not check the KWin frame effect.");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    using var timeout = new CancellationTokenSource(check ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5));
                    try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
                    if (!check && process.ExitCode != 0)
                        Console.Error.WriteLine($"[daisy-decoration] Frame effect repair failed: {await output}\n{await error}");
                    else { await output; await error; }
                    return process.ExitCode;
                }
                return await RunAsync(true).ConfigureAwait(false) == 0 || await RunAsync(false).ConfigureAwait(false) == 0;
            }
            directory = directory.Parent;
        }
        return true;
    }

    private static async Task RequestKWinReconfigureAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION"), "1", StringComparison.Ordinal)) return;

        // Loading and reconfiguring must finish in order. Independent processes
        // could reconfigure before the effect was loaded and leave its old palette.
        foreach (var args in new[]
        {
            new[] { "/KWin", "org.kde.KWin.reconfigure" },
            new[] { "/Effects", "org.kde.kwin.Effects.loadEffect", "daisyframe" },
            new[] { "/Effects", "org.kde.kwin.Effects.reconfigureEffect", "daisyframe" },
            new[] { "/Effects", "org.kde.kwin.Effects.reconfigureEffect", "daisyos-window-motion" },
            new[] { "/Effects", "org.kde.kwin.Effects.reconfigureEffect", "daisyos-task-overview-v16" },
            new[] { "/Effects", "org.kde.kwin.Effects.isEffectLoaded", "daisyframe" }
        })
        {
            var start = new ProcessStartInfo("qdbus6") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            start.ArgumentList.Add("org.kde.KWin");
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) throw new IOException("Could not start KWin theme reconfiguration.");
            var output = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { process.Kill(); throw; }
            var result = await output.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"KWin theme reconfiguration failed: {args[1]}");
            if (args[1].EndsWith(".isEffectLoaded", StringComparison.Ordinal) && result.Trim() != "true")
                throw new IOException("The KWin frame effect could not be loaded. Restart the DaisyOS session after updating its launcher.");
        }
    }
}
