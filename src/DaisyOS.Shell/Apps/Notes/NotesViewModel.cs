using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using DaisyOS.Core.Helpers;

namespace DaisyOS.Shell.Apps.Notes;

public class NotesViewModel : INotifyPropertyChanged, IDisposable
{
    private NoteItem? _selectedNote;
    private string _searchQuery = string.Empty;
    private readonly string _notesDir;
    private DispatcherTimer _autosaveTimer;
    private readonly HashSet<NoteItem> _observedNotes = [];
    private readonly HashSet<NoteItem> _failedSaves = [];
    private string _operationError = string.Empty;
    private bool _disposed;

    public string ErrorMessage => _failedSaves.Count > 0
        ? "Some notes could not be saved. Your text is still here. Check available space and folder permissions, then try again."
        : _operationError;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasSaveError => _failedSaves.Count > 0;
    internal bool IsAutosaveScheduled => _autosaveTimer.IsEnabled;

    public ObservableCollection<NoteItem> Notes { get; } = new();
    
    public ObservableCollection<NoteItem> FilteredNotes { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterNotes();
            }
        }
    }

    public NoteItem? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (_selectedNote != value)
            {
                if (_selectedNote != null) _selectedNote.IsSelected = false;
                _selectedNote = value;
                if (_selectedNote != null) _selectedNote.IsSelected = true;

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedNote));
                FilterNotes(); // Resort list based on new selection
            }
        }
    }

    public bool HasSelectedNote => _selectedNote != null;
    public bool HasFilteredNotes => FilteredNotes.Count > 0;
    public string EmptyListMessage => Loc.Get(Notes.Count == 0 ? "No notes yet" : "No notes match your search");

    public ICommand AddNoteCommand { get; }
    public ICommand SelectNoteCommand { get; }
    public ICommand PinNoteCommand { get; }
    public ICommand DuplicateNoteCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand RetrySaveCommand { get; }

    public NotesViewModel() : this(Services.Diagnostics.ApplicationPerformanceProbe.Enabled
        ? Path.Combine(Services.Diagnostics.ApplicationPerformanceProbe.DataDirectory!, "notes")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Notes"), true) { }

    internal NotesViewModel(string notesDirectory, bool loadOnStart = false)
    {
        _notesDir = notesDirectory;
        Loc.Changed += OnLanguageChanged;


        AddNoteCommand = new RelayCommand(_ => AddNote());
        SelectNoteCommand = new RelayCommand(param =>
        {
            if (param is NoteItem note) SelectNote(note);
        });
        PinNoteCommand = new RelayCommand(param =>
        {
            if (param is NoteItem note) 
            {
                note.IsPinned = !note.IsPinned;
                FilterNotes();
            }
        });
        DuplicateNoteCommand = new RelayCommand(param =>
        {
            if (param is NoteItem note) 
            {
                var copy = new NoteItem
                {
                    Format = note.Format,
                    PreferredExtension = Path.GetExtension(note.FilePath) is { Length: > 0 } extension ? extension : note.PreferredExtension,
                    Icon = note.Icon,
                    IsDirty = true
                };
                if (note.IsRichNote)
                {
                    copy.Package = new Documents.NotePackage
                    {
                        Document = global::System.Text.Json.JsonSerializer.Deserialize(global::System.Text.Json.JsonSerializer.Serialize(note.Package?.Document ?? Documents.NoteDocument.FromPlainText(note.Content), Documents.NotesJsonContext.Default.NoteDocument), Documents.NotesJsonContext.Default.NoteDocument)!,
                        Assets = note.Package != null ? new Dictionary<string, byte[]>(note.Package.Assets) : new()
                    };
                }
                else
                {
                    copy.Content = note.Content;
                    copy.FileContent = note.FileContent;
                }
                Notes.Add(copy);
                SearchQuery = string.Empty;
                SelectedNote = copy;
                FilterNotes();
                NoteCreated?.Invoke(this, EventArgs.Empty);
            }
        });
        SaveNoteCommand = new RelayCommand(param =>
        {
            if (param is NoteItem note) SaveNote(note);
            else if (SelectedNote != null) SaveNote(SelectedNote);
        });
        RetrySaveCommand = new RelayCommand(_ => SaveAll());

        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _autosaveTimer.Tick += OnAutosaveTick;
        Notes.CollectionChanged += OnNotesChanged;
        // Timer stays stopped by default and is only started when a note becomes dirty.

        if (loadOnStart)
            Dispatcher.UIThread.Post(() => { if (!_disposed) _ = LoadNotesAsync(); }, DispatcherPriority.Background);
    }

    private void OnNotesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var removedNotes = e.Action == NotifyCollectionChangedAction.Reset
            ? _observedNotes.ToArray() : e.OldItems?.Cast<NoteItem>().ToArray() ?? [];
        foreach (var removed in removedNotes)
        {
            removed.PropertyChanged -= OnNoteChanged;
            _observedNotes.Remove(removed);
            _failedSaves.Remove(removed);
        }
        foreach (var note in e.NewItems?.Cast<NoteItem>() ?? [])
        {
            if (_observedNotes.Add(note)) note.PropertyChanged += OnNoteChanged;
            if (note.IsDirty) NotifyNoteDirty();
        }
        PublishError();
    }

    private void OnNoteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteItem.IsDirty) && sender is NoteItem { IsDirty: true })
            NotifyNoteDirty();
    }

    private void PublishError()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasSaveError));
    }

    public void NotifyNoteDirty()
    {
        if (!_disposed && !_autosaveTimer.IsEnabled)
        {
            _autosaveTimer.Start();
        }
    }

    private void OnAutosaveTick(object? sender, EventArgs e)
    {
        foreach (var note in Notes.Where(n => n.IsDirty).ToList())
        {
            SaveNote(note);
        }

        if (!Notes.Any(n => n.IsDirty))
        {
            _autosaveTimer.Stop();
        }
    }

    private readonly CancellationTokenSource _loadLifetime = new();
    public async Task LoadNotesAsync()
    {
        if (_disposed) return;
        var token = _loadLifetime.Token;
        try
        {
            var notes = await Task.Run(() =>
            {
                Directory.CreateDirectory(_notesDir);
                var result = new List<NoteItem>();
                var unreadable = 0;
                foreach (var file in Directory.EnumerateFiles(_notesDir).Where(file => file.EndsWith(Documents.FormatDetector.RichNoteExtension, StringComparison.OrdinalIgnoreCase)))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var package = Documents.NotePackageFormat.Read(file);
                        result.Add(new NoteItem
                        {
                            Package = package,
                            Format = Documents.FormatDetector.RichNote,
                            FilePath = file,
                            LastModified = File.GetLastWriteTime(file),
                            Icon = "file-text",
                            IsDirty = false
                        });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Documents.NotePackageException) { unreadable++; }
                }

                foreach (var file in Directory.EnumerateFiles(_notesDir).Where(file => !file.EndsWith(Documents.FormatDetector.RichNoteExtension, StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(file).StartsWith('.')))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        var format = Documents.FormatDetector.Detect(file, bytes);
                        if (!format.IsText) continue;
                        var fileContent = Documents.TextFileCodec.Decode(bytes);
                        result.Add(new NoteItem
                        {
                            Content = fileContent.Text,
                            FileContent = fileContent,
                            Format = format,
                            FilePath = file,
                            LastModified = File.GetLastWriteTime(file),
                            Icon = "file-text",
                            IsDirty = false
                        });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; }
                }
                return (Items: result, Unreadable: unreadable);
            }, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                var existing = Notes.Where(note => !string.IsNullOrEmpty(note.FilePath)).Select(note => note.FilePath).ToHashSet(StringComparer.Ordinal);
                foreach (var note in notes.Items) if (existing.Add(note.FilePath)) Notes.Add(note);
                _operationError = notes.Unreadable > 0 ? $"{notes.Unreadable} note(s) could not be opened. Their files are still in your notes folder." : string.Empty;
                PublishError();
                FilterNotes();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _operationError = "Your notes folder could not be opened. Check that it is available and that you have permission to read it.";
            PublishError();
            global::System.Console.Error.WriteLine($"[notes] Load failed: {ex.Message}");
        }
    }

    public void SaveNote(NoteItem note)
    {
        try
        {
            if (string.IsNullOrEmpty(note.FilePath)) Directory.CreateDirectory(_notesDir);
            note.FilePath = NoteStorage.Save(_notesDir, note);
            note.IsDirty = false;
            _failedSaves.Remove(note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note.IsDirty = true;
            _failedSaves.Add(note);
            NotifyNoteDirty();
        }
        PublishError();
    }

    public bool SaveAll()
    {
        foreach (var note in Notes.Where(note => note.IsDirty).ToArray()) SaveNote(note);
        return !Notes.Any(note => note.IsDirty);
    }

    private void SelectNote(NoteItem? note)
    {
        SelectedNote = note;
    }

    private void FilterNotes()
    {
        var sorted = Notes.Where(n => string.IsNullOrEmpty(SearchQuery) || 
                                     n.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                     n.Content.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(n => n.IsPinned)
                          .ThenByDescending(n => n.LastModified)
                          .ToList();

        // Check if order is identical to prevent UI thrashing
        bool identical = FilteredNotes.Count == sorted.Count;
        if (identical)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                if (FilteredNotes[i] != sorted[i])
                {
                    identical = false;
                    break;
                }
            }
        }

        if (!identical)
        {
            // Keep surviving rows and their containers alive. Resetting the
            // collection drops scroll anchors and keyboard focus on pin/reorder.
            var retained = sorted.ToHashSet();
            for (var i = FilteredNotes.Count - 1; i >= 0; i--)
                if (!retained.Contains(FilteredNotes[i])) FilteredNotes.RemoveAt(i);
            for (int i = 0; i < sorted.Count; i++)
            {
                var note = sorted[i];
                note.ShowDivider = i < sorted.Count - 1;
                if (i < FilteredNotes.Count && ReferenceEquals(FilteredNotes[i], note)) continue;
                var previous = FilteredNotes.IndexOf(note);
                if (previous >= 0) FilteredNotes.Move(previous, i);
                else FilteredNotes.Insert(i, note);
            }
        }
        else
        {
            for (int i = 0; i < FilteredNotes.Count; i++)
            {
                FilteredNotes[i].ShowDivider = i < FilteredNotes.Count - 1;
            }
        }
        OnPropertyChanged(nameof(HasFilteredNotes));
        OnPropertyChanged(nameof(EmptyListMessage));
    }

    public event EventHandler? NoteCreated;

    public void AddNote()
    {
        SearchQuery = string.Empty;
        var package = new Documents.NotePackage
        {
            Document = Documents.NoteDocument.CreateEmpty()
        };
        var note = new NoteItem
        {
            Package = package,
            Format = Documents.FormatDetector.RichNote,
            Icon = "file-text",
            IsDirty = true
        };
        Notes.Add(note);
        SelectedNote = note;
        FilterNotes();
        NoteCreated?.Invoke(this, EventArgs.Empty);
    }

    public void ConvertCurrentNoteToRichNote()
    {
        if (SelectedNote == null || SelectedNote.IsRichNote) return;

        if (!SelectedNote.CanConvertToRichNote) return;
        var note = SelectedNote;
        var package = new Documents.NotePackage { Document = Documents.NoteDocument.FromPlainText(note.Content) };
        // Save a new rich copy first. The source file and selected note survive a failed conversion.
        var converted = new NoteItem { Package = package, Format = Documents.FormatDetector.RichNote, IsDirty = true };
        SaveNote(converted);
        if (converted.IsDirty)
        {
            _failedSaves.Remove(converted);
            _operationError = "The rich note could not be created. Your original text file is still available.";
            PublishError();
            return;
        }
        Notes.Add(converted);
        SelectedNote = converted;
        _operationError = string.Empty;
        PublishError();

        FilterNotes();
        OnPropertyChanged(nameof(SelectedNote));
    }

    public void DeleteNote(NoteItem note)
    {
        if (note != null)
        {
            if (!string.IsNullOrEmpty(note.FilePath))
            {
                try
                {
                    File.Delete(note.FilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _operationError = "This note could not be deleted. It is still in your notes.";
                    PublishError();
                    return;
                }
            }
            
            var index = FilteredNotes.IndexOf(note);
            _operationError = string.Empty;
            Notes.Remove(note);
            FilterNotes(); // Refresh filter
            if (SelectedNote == note)
            {
                SelectedNote = FilteredNotes.Count > 0
                    ? FilteredNotes[Math.Clamp(index, 0, FilteredNotes.Count - 1)] : null;
            }
        }
    }

    private static readonly SemaphoreSlim ExternalReaders = new(2);
    private CancellationTokenSource? _externalOpen;
    private int _externalOpenGeneration;

    public void InsertExternalNote(string filePath)
    {
        ++_externalOpenGeneration;
        _externalOpen?.Cancel();
        if (_disposed) return;
        var existing = Notes.FirstOrDefault(note => string.Equals(note.FilePath, filePath, StringComparison.Ordinal));
        ApplyExternalNote(filePath, existing is null ? ReadExternalNote(filePath) : (existing, string.Empty));
    }

    internal async Task InsertExternalNoteAsync(string filePath)
    {
        if (_disposed) return;
        var generation = ++_externalOpenGeneration;
        _externalOpen?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_loadLifetime.Token);
        _externalOpen = request;
        var selection = SelectedNote;
        try
        {
            var existing = Notes.FirstOrDefault(note => string.Equals(note.FilePath, filePath, StringComparison.Ordinal));
            if (existing is not null)
            {
                ApplyExternalNote(filePath, (existing, string.Empty));
                return;
            }
            await ExternalReaders.WaitAsync(request.Token);
            (NoteItem? Note, string Error) result;
            try
            {
                result = await Task.Run(() => ReadExternalNote(filePath), request.Token);
            }
            finally { ExternalReaders.Release(); }
            // A late file read must not steal selection after another open or click.
            if (!request.IsCancellationRequested && generation == _externalOpenGeneration &&
                ReferenceEquals(selection, SelectedNote)) ApplyExternalNote(filePath, result);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_externalOpen, request)) _externalOpen = null; }
    }

    private void ApplyExternalNote(string filePath, (NoteItem? Note, string Error) result)
    {
        if (result.Note is { } loaded)
        {
            var note = Notes.FirstOrDefault(note => string.Equals(note.FilePath, filePath, StringComparison.Ordinal)) ?? loaded;
            if (!Notes.Contains(note)) Notes.Add(note);
            SearchQuery = string.Empty;
            SelectedNote = note;
            FilterNotes();
        }
        _operationError = result.Error;
        PublishError();
    }

    private static (NoteItem? Note, string Error) ReadExternalNote(string filePath)
    {
        if (!File.Exists(filePath))
            return (null, "This note is no longer available. Choose another file.");
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var format = Documents.FormatDetector.Detect(filePath, bytes);
            if (format.Kind == Documents.DocumentKind.Binary)
            {
                return (null, "This file cannot be edited as text. Choose a note or text file.");
            }
            var fileInfo = new FileInfo(filePath);

            NoteItem note;
            if (format.Kind == Documents.DocumentKind.RichNote)
            {
                var package = Documents.NotePackageFormat.Read(filePath);
                note = new NoteItem
                {
                    Package = package,
                    Format = format,
                    FilePath = filePath,
                    LastModified = fileInfo.LastWriteTime,
                    Icon = "file-text",
                    IsDirty = false
                };
            }
            else
            {
                var fileContent = Documents.TextFileCodec.Decode(bytes);
                note = new NoteItem
                {
                    Content = fileContent.Text,
                    FileContent = fileContent,
                    Format = format,
                    FilePath = filePath,
                    LastModified = fileInfo.LastWriteTime,
                    Icon = format.UsesMonospace ? "code" : "file-text",
                    IsDirty = false
                };
            }

            return (note, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Documents.NotePackageException)
        {
            return (null, ex is Documents.NotePackageException ? ex.Message : "This note could not be opened. Check that you have permission to read it.");
        }
    }

    /// <summary>
    /// Flushes any unsaved notes and stops the autosave timer.
    /// Called by <see cref="NotesWindow"/> when the window is closed.
    /// </summary>
    public void Dispose() => Dispose(saveChanges: true);

    internal void Dispose(bool saveChanges)
    {
        if (_disposed) return;
        _disposed = true;
        Loc.Changed -= OnLanguageChanged;
        _loadLifetime.Cancel();
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= OnAutosaveTick;

        // Final save pass so no work is lost on close.
        foreach (var note in Notes.Where(n => saveChanges && n.IsDirty))
        {
            SaveNote(note);
        }
        Notes.CollectionChanged -= OnNotesChanged;
        foreach (var note in _observedNotes) note.PropertyChanged -= OnNoteChanged;
        _observedNotes.Clear();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(EmptyListMessage));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
