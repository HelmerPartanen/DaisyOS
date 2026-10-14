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
