using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FileAttributes = global::System.IO.FileAttributes;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files;

public sealed class FileEntryViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isRenaming;
    private string _name = string.Empty;
    private string _displayName = string.Empty;
    private string _presentationName = string.Empty;
    private string _lastWriteDisplay = string.Empty;
    private string _sizeDisplay = string.Empty;
    private long _visualVersion;
    private VisualLease? _visualLease;
    public FileEntrySnapshot Snapshot { get; private set; }
    public string NameSortKey { get; private set; } = string.Empty;
    public string TypeSortKey { get; private set; } = string.Empty;
    private IImage? _thumbnail;
    private IImage? _applicationIcon;
    private string _imageDimensions = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; private set; } = string.Empty;
    
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _presentationName = GetPresentationName(value);
                _displayName = AbbreviateFileName(_presentationName);
                RaisePropertyChanged(nameof(PresentationName));
                RaisePropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>
    /// A presentation-only name keeps desktop launchers readable without
    /// changing the on-disk file name or the rename workflow.
    /// </summary>
    public string DisplayName => _displayName;
    public string PresentationName => _presentationName;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string Extension { get; private set; } = string.Empty;
    public bool IsDirectory { get; private set; }
    public DateTime LastWriteTime { get; private set; }
    public long Length { get; private set; }

    public string LastWriteTimeDisplay => _lastWriteDisplay;
    public string DisplaySize => _sizeDisplay;
    public string DisplayType { get; private set; } = string.Empty;
    public string Icon { get; private set; } = string.Empty;
    public string IconColor { get; private set; } = string.Empty;
    public bool IsHidden => Snapshot.Name.StartsWith(".", StringComparison.Ordinal) || (Snapshot.Attributes & FileAttributes.Hidden) != 0;
    public IImage? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                RaisePropertyChanged(nameof(HasThumbnail));
                RaisePropertyChanged(nameof(HasVisualImage));
            }
        }
    }

    public IImage? ApplicationIcon
    {
        get => _applicationIcon;
        private set
        {
            if (SetProperty(ref _applicationIcon, value))
            {
                RaisePropertyChanged(nameof(HasApplicationIcon));
                RaisePropertyChanged(nameof(HasVisualImage));
            }
        }
    }

    public bool HasThumbnail => Thumbnail is not null;
    public bool HasApplicationIcon => ApplicationIcon is not null;
    public bool HasVisualImage => HasThumbnail || HasApplicationIcon;
    public string ImageDimensions
    {
        get => _imageDimensions;
        private set
        {
            if (SetProperty(ref _imageDimensions, value))
            {
                RaisePropertyChanged(nameof(HasImageDimensions));
            }
        }
    }

    public bool HasImageDimensions => !string.IsNullOrWhiteSpace(ImageDimensions);

    public FileEntryViewModel(FileEntrySnapshot snapshot) => ApplySnapshot(snapshot);

    public void ApplySnapshot(FileEntrySnapshot snapshot)
    {
        if (Snapshot == snapshot) return;
        var old = Snapshot;
        var initial = old.FullPath == null;
        var typeChanged = initial || old.Extension != snapshot.Extension || old.IsDirectory != snapshot.IsDirectory;
        var visualChanged = !initial && (old.FullPath != snapshot.FullPath || typeChanged ||
            old.Length != snapshot.Length || old.LastWriteUtcTicks != snapshot.LastWriteUtcTicks);
        Snapshot = snapshot;
        if (initial || old.FullPath != snapshot.FullPath)
        {
            Path = snapshot.FullPath;
            RaisePropertyChanged(nameof(Path));
        }
        if (initial || old.Name != snapshot.Name)
        {
            if (!IsRenaming) Name = snapshot.Name;
            NameSortKey = snapshot.Name;
        }
        if (initial || old.Extension != snapshot.Extension)
        {
            Extension = snapshot.Extension;
            RaisePropertyChanged(nameof(Extension));
        }
        if (initial || old.IsDirectory != snapshot.IsDirectory)
        {
            IsDirectory = snapshot.IsDirectory;
            RaisePropertyChanged(nameof(IsDirectory));
        }
        if (initial || old.Length != snapshot.Length)
        {
            Length = snapshot.Length;
            RaisePropertyChanged(nameof(Length));
        }
        if (initial || old.LastWriteUtcTicks != snapshot.LastWriteUtcTicks)
        {
            LastWriteTime = snapshot.LastWriteUtcTicks > 0 ? new DateTime(snapshot.LastWriteUtcTicks, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue;
            _lastWriteDisplay = LastWriteTime == DateTime.MinValue ? string.Empty : LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            RaisePropertyChanged(nameof(LastWriteTime));
            RaisePropertyChanged(nameof(LastWriteTimeDisplay));
        }
        if (initial || old.Length != snapshot.Length || old.IsDirectory != snapshot.IsDirectory)
        {
            _sizeDisplay = IsDirectory ? string.Empty : FormatFileSize(Length);
            RaisePropertyChanged(nameof(DisplaySize));
        }
        if (typeChanged)
        {
            DisplayType = IsDirectory ? "File folder" : GetDisplayType(Extension);
            (Icon, IconColor) = IsDirectory ? ("folder", "#F59E0B") : GetIconAndColor(Extension);
            TypeSortKey = DisplayType;
            RaisePropertyChanged(nameof(DisplayType));
            RaisePropertyChanged(nameof(Icon));
            RaisePropertyChanged(nameof(IconColor));
        }
        if (initial || old.Name != snapshot.Name || old.Attributes != snapshot.Attributes) RaisePropertyChanged(nameof(IsHidden));
        if (visualChanged) ClearVisual();
    }

    public void UpdatePathAfterRename(FileEntrySnapshot snapshot) => ApplySnapshot(snapshot);

    internal void ClearVisual()
    {
        Interlocked.Increment(ref _visualVersion);
        Thumbnail = null;
        ApplicationIcon = null;
        ImageDimensions = string.Empty;
        Interlocked.Exchange(ref _visualLease, null)?.Dispose();
    }

    internal void ApplyVisualLease(VisualLease lease)
    {
        var previous = _visualLease;
        _visualLease = lease;
        ApplyVisual(lease.Visual);
        previous?.Dispose();
    }

    internal void ApplyVisual(FileVisual visual)
    {
        Thumbnail = visual.Thumbnail;
        ApplicationIcon = visual.ApplicationIcon;
        ImageDimensions = visual.ImageDimensions;
    }

    // Explicit requests only; constructors and renames never start visual work.
    public async Task LoadVisualAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot;
        var version = Interlocked.Read(ref _visualVersion);
        try
        {
            var lease = await FileVisualResolver.ResolveLeaseAsync(snapshot, 96, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && version == _visualVersion && snapshot == Snapshot)
                    ApplyVisualLease(lease);
                else lease.Dispose();
            });
        }
        catch (OperationCanceledException) { }
        catch { /* Optional previews never prevent directory display. */ }
    }

    private static string GetDisplayType(string ext) => ext switch
    {
        ".txt" => "Text Document",
        ".md" => "Markdown Document",
        ".cs" => "C# Source File",
        ".json" => "JSON Document",
        ".xml" => "XML Document",
        ".html" or ".htm" => "HTML Web Page",
        ".css" => "Cascading Style Sheet",
        ".js" or ".ts" => "JavaScript / TypeScript File",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => "Image File",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" => "Audio File",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "Video File",
        ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "Archive File",
        ".pdf" => "PDF Document",
        ".sh" or ".bash" => "Shell Script",
        ".exe" or ".dll" or ".so" => "Executable / Library",
        _ when string.IsNullOrEmpty(ext) => "File",
        _ => $"{ext.TrimStart('.').ToUpperInvariant()} File"
    };

    private static (string icon, string color) GetIconAndColor(string ext) => ext switch
    {
        ".txt" or ".md" => ("file-text", "#9CA3AF"),
        ".cs" or ".json" or ".xml" or ".html" or ".css" or ".js" or ".ts" => ("code", "#10B981"),
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => ("image", "#3B82F6"),
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" => ("music-note", "#EC4899"),
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => ("film-strip", "#8B5CF6"),
        ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => ("file-zip", "#F59E0B"),
        ".pdf" => ("file-text", "#EF4444"),
        ".sh" or ".bash" => ("terminal-window", "#10B981"),
        ".appimage" => ("package", "#38BDF8"),
        _ => ("file", "#9CA3AF")
    };

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    private static string AbbreviateFileName(string name)
    {
        const int maximumLength = 28;
        if (name.Length <= maximumLength)
        {
            return name;
        }

        var extension = global::System.IO.Path.GetExtension(name);
        var suffixLength = Math.Min(12, Math.Max(extension.Length, maximumLength / 2));
        var prefixLength = maximumLength - suffixLength - 3;
        if (prefixLength < 6 || suffixLength >= name.Length)
        {
            return name[..(maximumLength - 3)] + "...";
        }

        return name[..prefixLength] + "..." + name[^suffixLength..];
    }

    private static string GetPresentationName(string name) =>
        name.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? name[..^".desktop".Length]
            : name;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
