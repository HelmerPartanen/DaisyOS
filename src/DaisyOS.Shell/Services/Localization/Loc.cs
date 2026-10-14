using System.Collections;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell.Services.Localization;

/// <summary>Standard .NET resources, published as live Avalonia resource bindings.</summary>
public static class Loc
{
    private static readonly ResourceManager Messages = new("DaisyOS.Shell.Resources.Messages", typeof(Loc).Assembly);
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(SystemLanguages.DefaultTag);
    public static event EventHandler? Changed;
    private static readonly Dictionary<string, string> Originals = new(StringComparer.Ordinal);
    public static string Original(string text) => Originals.GetValueOrDefault(text, text);

    // Fluent supplies these labels separately from app XAML (text editing,
    // scrollbars and date/time pickers). Keep them on the same live catalog.
    private static readonly IReadOnlyDictionary<string, string> FrameworkLabels = new Dictionary<string, string>
    {
        ["StringDatePickerDayText"] = "day",
        ["StringDatePickerMonthText"] = "month",
        ["StringDatePickerYearText"] = "year",
        ["StringTimePickerHourText"] = "hour",
        ["StringTimePickerMinuteText"] = "minute",
        ["StringTimePickerSecondText"] = "second",
        ["StringTextFlyoutCutText"] = "Cut",
        ["StringTextFlyoutCopyText"] = "Copy",
        ["StringTextFlyoutPasteText"] = "Paste",
        ["StringManagedFileChooserFileNamePlaceholder"] = "File name",
        ["StringManagedFileChooserShowHiddenFilesText"] = "Show hidden files",
        ["StringManagedFileChooserOkText"] = "OK",
        ["StringManagedFileChooserCancelText"] = "Cancel",
        ["StringManagedFileChooserNameColumn"] = "Name",
        ["StringManagedFileChooserDateModifiedColumn"] = "Date Modified",
        ["StringManagedFileChooserTypeColumn"] = "Type",
        ["StringManagedFileChooserSizeColumn"] = "Size",
        ["StringManagedFileChooserOverwritePromptFileAlreadyExistsText"] = "{0} already exists. Do you want to replace it?",
        ["StringManagedFileChooserOverwritePromptConfirmText"] = "Yes",
        ["StringManagedFileChooserOverwritePromptCancelText"] = "No",
        ["StringScrollBarScrollHere"] = "Scroll Here",
        ["StringScrollBarTop"] = "Top",
        ["StringScrollBarBottom"] = "Bottom",
        ["StringScrollBarPageUp"] = "Page Up",
        ["StringScrollBarPageDown"] = "Page Down",
        ["StringScrollBarScrollUp"] = "Scroll Up",
        ["StringScrollBarScrollDown"] = "Scroll Down",
        ["StringScrollBarLeftEdge"] = "Left Edge",
        ["StringScrollBarRightEdge"] = "Right Edge",
        ["StringScrollBarPageLeft"] = "Page Left",
        ["StringScrollBarPageRight"] = "Page Right",
        ["StringScrollBarScrollLeft"] = "Scroll Left",
        ["StringScrollBarScrollRight"] = "Scroll Right",
    };

    public static string Key(string source) => "Loc_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
    public static string Get(string source) => Messages.GetString(Key(source), Culture) ?? source;
    public static string Format(string source, params object?[] arguments) => string.Format(Culture, Get(source), arguments);

    public static void Apply(IResourceDictionary resources, string tag)
    {
        var culture = CultureInfo.GetCultureInfo(SystemLanguages.Resolve(tag).Tag);
        var changed = Culture.Name != culture.Name;
        if (!changed && resources.TryGetValue("LocalizationTag", out var current) && Equals(current, culture.Name)) return;
        resources["LocalizationTag"] = culture.Name;
        Culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        Originals.Clear();
        var neutral = Messages.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        foreach (DictionaryEntry resource in neutral)
        {
            var key = (string)resource.Key;
            var translated = Messages.GetString(key, culture) ?? resource.Value;
            resources[key] = translated;
            if (translated is string value && resource.Value is string original) Originals.TryAdd(value, original);
        }
        foreach (var label in FrameworkLabels) resources[label.Key] = Get(label.Value);
        resources["SystemFlowDirection"] = culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (changed) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void RefreshOnChange(Control control, Action refresh)
    {
        EventHandler handler = (_, _) => refresh();
        if (control is Window window)
        {
            // A Window is a visual root; it does not attach like its child controls.
            window.Opened += (_, _) => { Changed += handler; refresh(); };
            window.Closed += (_, _) => Changed -= handler;
        }
        else
        {
            control.AttachedToVisualTree += (_, _) => { Changed += handler; refresh(); };
            control.DetachedFromVisualTree += (_, _) => Changed -= handler;
        }
    }

    public static T Bind<T>(T control, AvaloniaProperty property, string source, params object?[] arguments) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(Key(source), value =>
            arguments.Length == 0 ? value as string ?? Get(source) : string.Format(Culture, value as string ?? Get(source), arguments)));
        return control;
    }
}
