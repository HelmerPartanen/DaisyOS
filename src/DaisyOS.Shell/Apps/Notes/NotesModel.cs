using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DaisyOS.Shell.Apps.Notes.Documents;

namespace DaisyOS.Shell.Apps.Notes;

public class NoteItem : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isSelected;
    private string? _filePath;
    private bool _isDirty;
    private bool _isPinned;
    private DateTime _lastModified = DateTime.Now;
    private NotePackage? _package;
    private Documents.DocumentFormat _format = Documents.FormatDetector.RichNote;
    private Documents.TextFileContent? _fileContent;

    public Documents.DocumentFormat Format
    {
        get => _format;
        set
        {
            if (_format != value)
            {
                _format = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRichNote));
                OnPropertyChanged(nameof(CanConvertToRichNote));
                OnPropertyChanged(nameof(FormatLabel));
            }
        }
    }

    public NotePackage? Package
    {
        get => _package;
        set
        {
            if (_package != value)
            {
                _package = value;
                _isPinned = value?.Document.Pinned ?? _isPinned;
                OnPropertyChanged(nameof(IsPinned));
                _cachedTitle = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(IsRichNote));
                OnPropertyChanged(nameof(CanConvertToRichNote));
                OnPropertyChanged(nameof(FormatLabel));
            }
        }
    }

    public Documents.TextFileContent? FileContent
    {
        get => _fileContent;
        set
        {
            if (_fileContent != value)
            {
                _fileContent = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanConvertToRichNote => Format.Kind == Documents.DocumentKind.PlainText;
    public string FormatLabel => Format.DisplayName;

    public bool IsRichNote => Format.Kind == Documents.DocumentKind.RichNote;

    internal string PreferredExtension { get; set; } = ".txt";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                if (!string.IsNullOrEmpty(value))
                {
                    _format = Documents.FormatDetector.FromPath(value);
                    OnPropertyChanged(nameof(Format));
                    OnPropertyChanged(nameof(IsRichNote));
                    OnPropertyChanged(nameof(CanConvertToRichNote));
                    OnPropertyChanged(nameof(FormatLabel));
                }
                OnPropertyChanged();
            }
        }
    }

    private bool _showDivider;
    public bool ShowDivider
    {
        get => _showDivider;
        set
        {
            if (_showDivider != value)
            {
                _showDivider = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontStyle));
            }
        }
    }

    public string FontStyle => IsDirty ? "Italic" : "Normal";

    public bool IsPinned
    {
        get => _package?.Document.Pinned ?? _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                if (_package != null) _package.Document.Pinned = value;
                IsDirty = true;
                OnPropertyChanged();
            }
        }
    }

    public DateTime LastModified
    {
        get => _lastModified;
        set
        {
            if (_lastModified != value)
            {
                _lastModified = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastModifiedFormatted));
            }
        }
    }

    public string LastModifiedFormatted => LastModified.ToString("MMM d, yyyy  HH:mm");

    private string? _cachedTitle;

    public string Content
    {
        get
        {
            if (IsRichNote && _package != null)
            {
                return _package.Document.ToPlainText();
            }
            return _content;
        }
        set
        {
            if (_content != value || (IsRichNote && _package != null && _package.Document.ToPlainText() != value))
            {
                _content = value;
                if (IsRichNote)
                {
                    _package ??= new NotePackage();
                    _package.Document = NoteDocument.FromPlainText(value);
                }
                _cachedTitle = null; // Invalidate cached title
                IsDirty = true;
                LastModified = DateTime.Now;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public void NotifyDocumentChanged()
    {
        _cachedTitle = null;
        IsDirty = true;
        LastModified = DateTime.Now;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsPinned));
    }

    public string Title
    {
        get
        {
            if (_cachedTitle != null) return _cachedTitle;

            if (IsRichNote && _package != null)
            {
                var pkgTitle = _package.Document.DisplayTitle;
                if (!string.IsNullOrWhiteSpace(pkgTitle))
                {
                    _cachedTitle = pkgTitle;
                    return _cachedTitle;
                }
            }

            if (!string.IsNullOrEmpty(_filePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(_filePath);
                if (!string.IsNullOrWhiteSpace(fileName) && !fileName.StartsWith("202"))
                {
                    _cachedTitle = fileName;
                    return _cachedTitle;
                }
            }

            var text = Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                _cachedTitle = string.Empty;
                return _cachedTitle;
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                _cachedTitle = string.Empty;
                return _cachedTitle;
            }

            var firstLine = lines[0].Trim();
            _cachedTitle = firstLine.Length > 40 ? firstLine.Substring(0, 40) + "..." : firstLine;
            return _cachedTitle;
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Icon { get; set; } = "description";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
