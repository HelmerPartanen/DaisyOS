using System;
using System.IO;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Coalesces shared preference writes onto the application UI thread.</summary>
internal sealed class AppearanceFileObserver : IDisposable
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private FileSystemWatcher? _watcher;
    private volatile bool _disposed;

    public AppearanceFileObserver(string path, Action refresh)
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (!_disposed) refresh();
        };
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += Changed;
            _watcher.Created += Changed;
            _watcher.Renamed += Changed;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
            // The normal one-second appearance poll remains the fallback.
            Console.Error.WriteLine($"[appearance] Preference watcher unavailable: {exception.Message}");
        }
    }

    private void Changed(object sender, FileSystemEventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        _debounce.Stop();
        _debounce.Start();
    });

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _debounce.Stop();
    }
}
