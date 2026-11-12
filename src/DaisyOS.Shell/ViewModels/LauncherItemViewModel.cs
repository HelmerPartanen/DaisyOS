using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Avalonia.Media;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell.ViewModels;

public class LauncherItemViewModel : INotifyPropertyChanged
{
    private bool _isTopSearchResult;
    private bool _isKeyboardSelected;

    public AppEntry App { get; private set; }
    private bool IsDaisyApp => App.LaunchKind == AppLaunchKind.Internal || App.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase);
    public string Name => IsDaisyApp ? Loc.Get(App.Name) : App.Name;
    public string Initial => Name is { Length: > 0 } value ? char.ToUpperInvariant(value[0]).ToString() : "•";
    public string Description => string.IsNullOrWhiteSpace(App.Description) ? Loc.Get("Application") : IsDaisyApp ? Loc.Get(App.Description) : App.Description;
    public string IconName => App.Icon;
    public bool IsBitmapIconVisible => IconBitmap is not null;
    public bool IsFallbackIconVisible => IconBitmap is null;
    public bool CanRequestUninstall => true;

    public string SearchKey { get; private set; } = string.Empty;
    internal string UpperName { get; private set; } = string.Empty;
    internal string[] NameWords { get; private set; } = Array.Empty<string>();
    internal string NameAcronym { get; private set; } = string.Empty;
    internal string CamelAcronym { get; private set; } = string.Empty;
    internal string[] UpperSearchTerms { get; private set; } = Array.Empty<string>();

    private void CacheSearchKey()
    {
        var rawName = Name ?? string.Empty;
        UpperName = rawName.ToUpperInvariant();
        NameWords = ExtractUpperWords(UpperName);
        NameAcronym = BuildInitials(NameWords);
        CamelAcronym = BuildInitials(ExtractCamelWords(rawName));

        var terms = (App.SearchTerms ?? Array.Empty<string>())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim().ToUpperInvariant())
            .Append((App.Id ?? string.Empty).Replace(".desktop", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        UpperSearchTerms = terms;

        SearchKey = string.Join("\n", new[] { rawName, Description }
            .Concat(App.Categories ?? Array.Empty<string>())
            .Concat(App.SearchTerms ?? Array.Empty<string>()))
            .ToUpperInvariant();
    }

    private static string[] ExtractUpperWords(string upperText)
    {
        var words = new List<string>(4);
        var start = -1;
        for (var i = 0; i < upperText.Length; i++)
        {
            if (char.IsLetterOrDigit(upperText[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                words.Add(upperText[start..i]);
                start = -1;
            }
        }
        if (start >= 0) words.Add(upperText[start..]);
        return words.Count == 0 ? Array.Empty<string>() : words.ToArray();
    }

    private static string[] ExtractCamelWords(string text)
    {
        var words = new List<string>(6);
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString().ToUpperInvariant());
                    current.Clear();
                }
                continue;
            }

            if (current.Length > 0 && char.IsUpper(ch) && char.IsLower(text[i - 1]))
            {
                words.Add(current.ToString().ToUpperInvariant());
                current.Clear();
            }
            current.Append(ch);
        }
        if (current.Length > 0) words.Add(current.ToString().ToUpperInvariant());
        return words.Count == 0 ? Array.Empty<string>() : words.ToArray();
    }

    private static string BuildInitials(string[] words)
    {
        if (words.Length == 0) return string.Empty;
        Span<char> chars = words.Length <= 16 ? stackalloc char[words.Length] : new char[words.Length];
        for (var i = 0; i < words.Length; i++) chars[i] = words[i][0];
        return new string(chars);
    }

    private IImage? _iconBitmap;
    public IImage? IconBitmap
    {
        get => _iconBitmap;
        set
        {
            if (ReferenceEquals(_iconBitmap, value)) return;
            _iconBitmap = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBitmap)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBitmapIconVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFallbackIconVisible)));
        }
    }

    public void UpdateApp(AppEntry app)
    {
        App = app;
        CacheSearchKey();
        foreach (var name in new[] { nameof(Name), nameof(Initial), nameof(Description), nameof(IconName), nameof(IsBitmapIconVisible), nameof(IsFallbackIconVisible), nameof(CanRequestUninstall) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Identifies the active matching application while the launcher is searching. The view uses
    /// this to provide an unambiguous visual target for the Enter key.
    /// </summary>
    public bool IsTopSearchResult
    {
        get => _isTopSearchResult;
        set
        {
            if (_isTopSearchResult == value)
            {
                return;
            }

            _isTopSearchResult = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTopSearchResult)));
        }
    }

    /// <summary>True when directional keyboard navigation has highlighted this tile.</summary>
    public bool IsKeyboardSelected
    {
        get => _isKeyboardSelected;
        set
        {
            if (_isKeyboardSelected == value)
            {
                return;
            }

            _isKeyboardSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKeyboardSelected)));
        }
    }

    public LauncherItemViewModel(AppEntry app, IImage? iconBitmap = null)
    {
        App = app;
        CacheSearchKey();
        IconBitmap = iconBitmap;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
