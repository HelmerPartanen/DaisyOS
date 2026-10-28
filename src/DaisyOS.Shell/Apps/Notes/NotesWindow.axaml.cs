using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using Avalonia.Threading;
using DaisyOS.Shell.Apps;

namespace DaisyOS.Shell.Apps.Notes;

public partial class NotesWindow : Window
{
    private readonly SystemAppWindowChrome _fullscreenController;
    private bool _discardUnsavedChanges;
    private bool _confirmingClose;

    public NotesWindow() : this(new NotesViewModel()) { }

    internal NotesWindow(NotesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _fullscreenController = new SystemAppWindowChrome(this,
            NotesContent.FindControl<Controls.AppWindowTitleBar>("AppTitleBar")!,
            NotesContent.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
    }

    public void ImportContinuityActivity(string path)
    {
        try
        {
            var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrEmpty(state) || !global::System.IO.Path.IsPathFullyQualified(state))
                state = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
            var root = global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(state, "daisyos", "continuity", "activities"));
            path = global::System.IO.Path.GetFullPath(path);
            if (global::System.IO.Path.GetDirectoryName(path) != root || !Guid.TryParseExact(global::System.IO.Path.GetFileNameWithoutExtension(path), "N", out _) ||
                new global::System.IO.FileInfo(path).Length > 400_000 || new global::System.IO.FileInfo(path).LinkTarget is not null) return;
            var activity = global::System.Text.Json.JsonSerializer.Deserialize(global::System.IO.File.ReadAllBytes(path),
                DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ContinuityActivity) ?? throw new global::System.IO.InvalidDataException();
            if (RestoreContinuityActivity(activity)) global::System.IO.File.Delete(path);
        }
        catch { (Avalonia.Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t restore the handed-off note.")); }
    }

    internal bool RestoreContinuityActivity(DaisyOS.Core.Continuity.ContinuityActivity activity)
    {
        activity.Validate();
        if (DataContext is not NotesViewModel model) return false;
        var package = new Documents.NotePackage { Document = Documents.NoteDocument.FromPlainText(activity.Text) };
        package.Document.Title = activity.Title;
        var note = new NoteItem { Package = package, IsDirty = true, Icon = "file-text" };
        model.Notes.Add(note);
        model.SearchQuery = string.Empty;
        // Select only after the package is complete so the editor loads the snapshot.
        model.SelectedNote = note;
        return model.SaveAll();
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullscreenController.Dispose();
        base.OnClosed(e);
        (DataContext as NotesViewModel)?.Dispose(saveChanges: !_discardUnsavedChanges);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _discardUnsavedChanges) return;
        if (_confirmingClose)
        {
            e.Cancel = true;
            return;
        }
        if (DataContext is NotesViewModel viewModel && !viewModel.SaveAll())
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        }
    }

    private async Task ConfirmCloseAsync()
    {
        _confirmingClose = true;
        try
        {
            var dialog = new Views.Dialogs.ConfirmationDialog(Loc.Get("Unsaved notes"), Loc.Get("Your changes have not been saved"),
                Loc.Get("Keep Notes open to retry saving or copy your text somewhere safe. Closing now will lose the unsaved changes."),
                Loc.Get("Close without saving"), Loc.Get("Keep editing"));
            if (await dialog.ShowDialog<bool>(this))
            {
                _discardUnsavedChanges = true;
                Close();
            }
        }
        finally { _confirmingClose = false; }
    }

    public void CreateNewNote()
    {
        if (DataContext is NotesViewModel viewModel)
        {
            viewModel.AddNote();
        }
    }
}
