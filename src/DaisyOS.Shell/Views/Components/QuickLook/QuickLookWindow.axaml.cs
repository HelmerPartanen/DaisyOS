using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Views.Components.QuickLook;

public partial class QuickLookWindow : Window
{
    private static QuickLookWindow? _currentInstance;
    private string? _currentFilePath;

    public QuickLookWindow()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => Close();
        OpenWithButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && (File.Exists(_currentFilePath) || Directory.Exists(_currentFilePath)))
            {
                _ = new SafeProcessLauncher().Launch("gio", ["open", _currentFilePath]);
                Close();
            }
        };

        AddHandler(KeyDownEvent, (s, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Space)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentInstance, this))
            {
                _currentInstance = null;
            }
        };
    }

    public static void ShowOrToggle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        if (_currentInstance != null)
        {
            if (string.Equals(_currentInstance._currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentInstance.Close();
                return;
            }

            _currentInstance.LoadFile(filePath);
            _currentInstance.Activate();
            return;
        }

        var window = new QuickLookWindow();
        _currentInstance = window;
        window.LoadFile(filePath);
        window.Show();
    }

    public void LoadFile(string filePath)
    {
        _currentFilePath = filePath;
        FilePathText.Text = filePath;
        FileNameText.Text = Path.GetFileName(filePath);

        ImagePreview.IsVisible = false;
        TextScroll.IsVisible = false;
        GenericPreview.IsVisible = false;

        if (Directory.Exists(filePath))
        {
            HeaderIcon.Icon = "folder";
            var dirInfo = new DirectoryInfo(filePath);
            try
            {
                var children = dirInfo.GetFileSystemInfos();
                FileMetaText.Text = Loc.Format("Folder • {0} items • Modified {1:g}", children.Length, dirInfo.LastWriteTime);
                GenericIcon.Icon = "folder";
                GenericTitle.Text = dirInfo.Name;
                GenericDetail.Text = Loc.Format("{0} items inside.\n", children.Length) + string.Join("\n", children.Take(5).Select(c => $"• {c.Name}"));
                GenericPreview.IsVisible = true;
            }
            catch
            {
                FileMetaText.Text = Loc.Get("Folder");
                GenericTitle.Text = dirInfo.Name;
                GenericDetail.Text = Loc.Get("Folder contents could not be enumerated.");
                GenericPreview.IsVisible = true;
            }
            return;
        }

        if (!File.Exists(filePath))
        {
            GenericTitle.Text = Loc.Get("File not found");
            GenericDetail.Text = filePath;
            GenericPreview.IsVisible = true;
            return;
        }

        var fileInfo = new FileInfo(filePath);
        var ext = fileInfo.Extension.ToLowerInvariant();
        var sizeStr = FormatFileSize(fileInfo.Length);
        FileMetaText.Text = Loc.Format("{0} • {1} • Modified {2:g}", ext.TrimStart('.').ToUpperInvariant(), sizeStr, fileInfo.LastWriteTime);

        // 1. Image preview
        var imageExts = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
        if (imageExts.Contains(ext))
        {
            HeaderIcon.Icon = "image";
            try
            {
                (ImagePreview.Source as IDisposable)?.Dispose();
                var bitmap = new Bitmap(filePath);
                ImagePreview.Source = bitmap;
                ImagePreview.IsVisible = true;
                FileMetaText.Text = $"{ext.TrimStart('.').ToUpperInvariant()} • {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} • {sizeStr}";
                return;
            }
            catch
            {
                // Fall back to generic preview if decode fails
            }
        }

        // 2. Text / Code preview
        var textExts = new[]
        {
            ".txt", ".md", ".cs", ".json", ".xml", ".yaml", ".yml",
            ".sh", ".bash", ".py", ".rs", ".go", ".c", ".h", ".cpp",
            ".html", ".css", ".js", ".ts", ".jsx", ".tsx", ".log", ".conf", ".ini"
        };
        if (textExts.Contains(ext) || fileInfo.Length < 100_000 && IsLikelyTextFile(filePath))
        {
            HeaderIcon.Icon = "file-text";
            try
            {
                using var reader = new StreamReader(filePath);
                var charBuffer = new char[30_000];
                var charsRead = reader.Read(charBuffer, 0, charBuffer.Length);
                var text = new string(charBuffer, 0, charsRead);
                if (charsRead == charBuffer.Length)
                {
                    text += Loc.Get("\n\n[... Preview truncated ...]");
                }

                TextPreview.Text = text;
                TextScroll.IsVisible = true;
                return;
            }
            catch
            {
                // Fall back to generic preview
            }
        }

        // 3. Audio preview
        var audioExts = new[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };
        if (audioExts.Contains(ext))
        {
            HeaderIcon.Icon = "music-notes";
            GenericIcon.Icon = "music-notes";
            GenericTitle.Text = fileInfo.Name;
            GenericDetail.Text = Loc.Format("Audio Track • {0}", sizeStr);
            GenericPreview.IsVisible = true;
            return;
        }

        // 4. Video preview
        var videoExts = new[] { ".mp4", ".mkv", ".webm", ".avi", ".mov" };
        if (videoExts.Contains(ext))
        {
            HeaderIcon.Icon = "film-strip";
            GenericIcon.Icon = "film-strip";
            GenericTitle.Text = fileInfo.Name;
            GenericDetail.Text = Loc.Format("Video File • {0}", sizeStr);
            GenericPreview.IsVisible = true;
            return;
        }

        // 5. Generic File preview
        HeaderIcon.Icon = "file";
        GenericIcon.Icon = "file";
        GenericTitle.Text = fileInfo.Name;
        GenericDetail.Text = Loc.Format("{0} • Click 'Open' to view in default application.", sizeStr);
        GenericPreview.IsVisible = true;
    }

    private static bool IsLikelyTextFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[512];
            var read = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return false; // NUL byte indicates binary
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }
}

