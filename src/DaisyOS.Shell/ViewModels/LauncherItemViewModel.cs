using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using DaisyOS.Core.Models;

namespace DaisyOS.Shell.ViewModels;

public class LauncherItemViewModel : INotifyPropertyChanged
{
    private bool _isTopSearchResult;

    public AppEntry App { get; private set; }
    private bool IsDaisyApp => App.LaunchKind == AppLaunchKind.Internal || App.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase);
    public string Name => IsDaisyApp ? Loc.Get(App.Name) : App.Name;
    public string Description => string.IsNullOrWhiteSpace(App.Description) ? Loc.Get("Application") : IsDaisyApp ? Loc.Get(App.Description) : App.Description;
    public string IconName => App.Icon;
    public bool CanRequestUninstall => true;
    public string SearchKey { get; private set; } = string.Empty;
    private void CacheSearchKey() => SearchKey = string.Join("\n", new[] { Name, Description }.Concat(App.Categories ?? Array.Empty<string>()).Concat(App.SearchTerms ?? Array.Empty<string>())).ToUpperInvariant();
    private IImage? _iconBitmap;
    public IImage? IconBitmap
    {
        get => _iconBitmap;
        set { if (ReferenceEquals(_iconBitmap, value)) return; _iconBitmap = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconBitmap))); }
    }
    public void UpdateApp(AppEntry app)
    {
        App = app;
        CacheSearchKey();
        foreach (var name in new[] { nameof(Name), nameof(Description), nameof(IconName), nameof(CanRequestUninstall) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Identifies the first matching application while the launcher is searching. The view uses
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

    public LauncherItemViewModel(AppEntry app, IImage? iconBitmap = null)
    {
        App = app;
        CacheSearchKey();
        IconBitmap = iconBitmap;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
