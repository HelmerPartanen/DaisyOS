using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Directory = global::System.IO.Directory;
using File = global::System.IO.File;
using Path = global::System.IO.Path;
using DaisyOS.Shell.Apps.Files.Services;

namespace DaisyOS.Shell.Apps.Files;

public sealed class FilesViewModel : INotifyPropertyChanged
{
    private FilesTabViewModel? _activeTab;
    private bool _isPreviewPaneVisible;
    private FilesPreferences _currentPreferences;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FilesTabViewModel> Tabs { get; } = [];
    public ObservableCollection<SidebarLocation> PinnedLocations { get; } = [];
    public ObservableCollection<SidebarLocation> StandardLocations { get; } = [];
    public ObservableCollection<VolumeInfo> Volumes { get; } = [];

    public bool IsPreviewPaneVisible
    {
        get => _isPreviewPaneVisible;
        set
        {
            if (SetProperty(ref _isPreviewPaneVisible, value))
            {
                SavePreferences();
            }
        }
    }

    public bool HasPinnedLocations => PinnedLocations.Count > 0;
    public bool HasVolumes => Volumes.Count > 0;

    public FilesTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                foreach (var tab in Tabs)
                {
                    tab.IsActive = (tab == value);
                }
            }
        }
    }

    internal FilesViewModel(FilesPreferences preferences)
    {
        PinnedLocations.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPinnedLocations));
            SavePreferences();
        };
        Volumes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasVolumes));
        
        _currentPreferences = preferences;
        _isPreviewPaneVisible = _currentPreferences.IsPreviewPaneVisible;
    }

    public FilesViewModel(string initialPath, string? itemToReveal = null, bool selectItem = false)
        : this(FilesPreferencesService.Load())
    {
        LoadStandardLocations();
        LoadPinnedLocations(_currentPreferences.StarredPaths ?? []);
        RefreshVolumes();

        var tab = NewTab(initialPath);
        if (selectItem && !string.IsNullOrWhiteSpace(itemToReveal))
        {
            tab.Reveal(itemToReveal, selectItem: true);
        }
    }

    public FilesTabViewModel NewTab(string? path = null)
    {
        path ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tab = new FilesTabViewModel(path, _currentPreferences);
        tab.PreferencesChanged += () =>
        {
            UpdatePreferencesFromTab(tab);
            SavePreferences();
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public void CloseTab(FilesTabViewModel tab)
    {
        if (Tabs.Contains(tab))
        {
            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            tab.Dispose();

            if (Tabs.Count == 0)
            {
                NewTab();
            }
            else if (ReferenceEquals(ActiveTab, tab))
            {
                ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
            }
        }
    }

    public void Reveal(string path, bool selectItem)
    {
        if (ActiveTab is null)
        {
            ActiveTab = NewTab();
        }

        ActiveTab.Reveal(path, selectItem);
    }

    public void TogglePreviewPane() => IsPreviewPaneVisible = !IsPreviewPaneVisible;

    public void AddPinnedLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || PinnedLocations.Any(location =>
                string.Equals(location.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var presentation = FileLocationPresentation.Resolve(path);
        PinnedLocations.Add(new SidebarLocation(
            Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), path,
            presentation.Icon, PinnedColor(presentation)));
    }

    public bool RemovePinnedLocation(string path)
    {
        var location = PinnedLocations.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        return location is not null && PinnedLocations.Remove(location);
    }

    public void RefreshVolumes()
    {
        Volumes.Clear();
        foreach (var volume in VolumeService.GetVolumes()
                     .OrderBy(volume => volume.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Volumes.Add(volume);
        }
    }

    private void LoadPinnedLocations(string[] pinnedPaths)
    {
        foreach (var path in pinnedPaths)
        {
            if (Directory.Exists(path))
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                var presentation = FileLocationPresentation.Resolve(path);
                PinnedLocations.Add(new SidebarLocation(
                    string.IsNullOrEmpty(name) ? "System" : name, path,
                    presentation.Icon, PinnedColor(presentation)));
            }
        }
    }

    private static string PinnedColor(FileLocationPresentation presentation) =>
        presentation.Kind == FileLocationKind.Generic
            ? "OrangeSwatchBrush"
            : presentation.ColorResourceKey;

    private void LoadStandardLocations()
    {
        foreach (var name in new[] { "Home", "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music" })
            AddStandardLocation(name, UserDirectoryPaths.Current[name]);
        AddStandardLocation("Trash", UserTrashPaths.Files);
    }

    private void AddStandardLocation(string name, string path)
    {
        var presentation = FileLocationPresentation.Resolve(path);
        StandardLocations.Add(new SidebarLocation(Loc.Get(name), path, presentation.Icon, presentation.ColorResourceKey, name));
    }

    public void RefreshStandardLocationLabels()
    {
        for (var index = 0; index < StandardLocations.Count; index++)
        {
            var location = StandardLocations[index];
            if (location.TranslationSource is not { } source) continue;
            var name = Loc.Get(source);
            if (location.Name != name) StandardLocations[index] = location with { Name = name };
        }
    }

    private void UpdatePreferencesFromTab(FilesTabViewModel tab)
    {
        _currentPreferences = _currentPreferences with
        {
            IsGridView = tab.IsGridView,
            SortColumn = tab.SortColumn,
            SortAscending = tab.SortAscending,
            ShowHiddenFiles = tab.ShowHiddenFiles,
            ShowDateColumn = tab.ShowDateColumn,
            ShowTypeColumn = tab.ShowTypeColumn,
            ShowSizeColumn = tab.ShowSizeColumn
        };
    }

    private void SavePreferences()
    {
        var tab = ActiveTab ?? Tabs.FirstOrDefault();
        if (tab is not null)
        {
            UpdatePreferencesFromTab(tab);
        }

        _currentPreferences = _currentPreferences with
        {
            IsPreviewPaneVisible = IsPreviewPaneVisible,
            StarredPaths = PinnedLocations.Select(l => l.Path).ToArray()
        };

        FilesPreferencesService.Save(_currentPreferences);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record SidebarLocation(string Name, string Path, string Icon, string ColorResourceKey, string? TranslationSource = null);
