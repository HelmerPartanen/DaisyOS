using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Apps.Notes;

public partial class NotesView : UserControl
{
    private NotesViewModel? ViewModel => DataContext as NotesViewModel;
    private NotesViewModel? _subscribedViewModel;
    private NoteItem? _currentNote;
    private Documents.NotePackage? _loadedPackage;

    public NotesView()
    {
        InitializeComponent();

        RichEditor.DocumentChanged += OnRichDocumentChanged;
        CodeEditor.TextContentChanged += OnCodeEditorTextChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.NoteCreated -= OnNoteCreated;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = ViewModel;
        if (ViewModel != null)
        {
            ViewModel.NoteCreated -= OnNoteCreated;
            ViewModel.NoteCreated += OnNoteCreated;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            SyncActiveEditor();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotesViewModel.SelectedNote))
        {
            SyncActiveEditor();
            if (_currentNote is not null)
                _ = GuiMotion.RevealAsync(_currentNote.IsRichNote ? RichEditor : CodeEditor, 0);
        }
    }

    private void SyncActiveEditor()
    {
        if (ViewModel == null) return;
        var note = ViewModel.SelectedNote;
        if (ReferenceEquals(_currentNote, note) && ReferenceEquals(_loadedPackage, note?.Package)) return;
        _currentNote = note;
        _loadedPackage = note?.Package;

        if (note == null) return;

        if (note.IsRichNote && note.Package != null)
        {
            RichEditor.LoadPackage(note.Package);
        }
        else
        {
            CodeEditor.LoadText(note.Content, note.Format, note.FileContent);
        }
    }

    private void OnRichDocumentChanged(object? sender, EventArgs e)
    {
        if (_currentNote != null && _currentNote.IsRichNote)
        {
            _currentNote.NotifyDocumentChanged();
            ViewModel?.NotifyNoteDirty();
        }
    }

    private void OnCodeEditorTextChanged(object? sender, EventArgs e)
    {
        if (_currentNote != null && !_currentNote.IsRichNote)
        {
            _currentNote.Content = CodeEditor.GetText();
            _currentNote.IsDirty = true;
            ViewModel?.NotifyNoteDirty();
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            NotesSearch.FocusInput();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && NotesSearch.IsKeyboardFocusWithin && !string.IsNullOrEmpty(NotesSearch.Text))
        {
            NotesSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void OnNoteCreated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_currentNote?.IsRichNote == true) RichEditor.FocusDocument();
            else CodeEditor.FocusEditor();
        });
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || ViewModel == null) return;

        var options = new DaisyOS.Shell.Apps.Files.Picker.DaisyFilePickerOptions(
            "Open a note or code file", false, false,
            [".dnote", ".txt", ".md", ".cs", ".py", ".js", ".ts", ".json", ".xml", ".axaml", ".yaml", ".yml", ".sh", ".html", ".css", ".cpp", ".c", ".h", ".rs", ".go"]);
        var files = Avalonia.Application.Current is App app
            ? await app.PickFilesAsync(topLevel, options)
            : topLevel is Window owner
                ? await new DaisyOS.Shell.Apps.Files.Picker.DaisyFilePickerWindow(options).ShowDialog<IReadOnlyList<string>?>(owner)
                : null;
        if (files is { Count: > 0 })
        {
            ViewModel.InsertExternalNote(files[0]);
            SyncActiveEditor();
        }
    }

    private void OnConvertNoteClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ConvertCurrentNoteToRichNote();
        SyncActiveEditor();
    }

    private async void OnRenameNoteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: NoteItem { IsRichNote: true } note } ||
            ViewModel is not { } model || TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new NoteTitleDialog(note.Package!.Document.Title ?? note.Title);
        var title = await dialog.ShowDialog<string?>(owner);
        if (title == null || !model.Notes.Contains(note)) return;
        model.SelectedNote = note;
        RichEditor.SetDocumentTitle(title);
    }

    private async void OnDeleteNoteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: NoteItem note } || ViewModel is not { } viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        var title = string.IsNullOrWhiteSpace(note.Title) ? Loc.Get("Untitled note") : note.Title;
        var dialog = new Views.Dialogs.ConfirmationDialog(Loc.Get("Delete note"), Loc.Format("Delete “{0}”?", title),
            Loc.Get("This permanently deletes the note and its file. This action cannot be undone."), Loc.Get("Delete note"));
        if (await dialog.ShowDialog<bool>(owner)) viewModel.DeleteNote(note);
    }
}
