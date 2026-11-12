using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.Notes.Documents;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class UsabilityTests
{
    [AvaloniaFact]
    public void LauncherHasNoFavouritesAndOnlyDisplaysTheCurrentPage()
    {
        using var fixture = new TemporaryFolder();
        var apps = Enumerable.Range(0, 200)
            .Select(index => new AppEntry($"app-{index}", $"App {index:D3}", "", "notes", false))
            .ToArray();
        var model = new LauncherViewModel(new ShellSessionState(Path.Combine(fixture.Path, "state.json")), apps);
        var launcher = new DaisyOS.Shell.Views.Components.Launcher.LauncherView(model);
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
            Assert.DoesNotContain(launcher.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Favourites");
            var firstPage = model.PageApps.ToArray();
            Assert.True(model.PageCount > 1);
            Assert.DoesNotContain(launcher.FindControl<Grid>("LauncherPageViewport")!.GetVisualDescendants(), visual => visual is ScrollViewer);

            model.GoToPage(1);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.DoesNotContain(model.PageApps, app => firstPage.Contains(app));
            Assert.Equal(model.PageApps.Count, launcher.CurrentPageGrid.ItemCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SearchResultTracksQueryEvenWhenTheMatchingListDoesNotChange()
    {
        using var fixture = new TemporaryFolder();
        var launcher = new LauncherViewModel(new ShellSessionState(Path.Combine(fixture.Path, "state.json")),
            [new AppEntry("notes", "Notes", "Write text", "file-text", false)]);
        Assert.Null(launcher.TopSearchResult);
        launcher.SearchText = "Notes";
        Assert.Equal("notes", launcher.ResolveSearchResult()?.App.Id);
        Assert.True(launcher.AllApps[0].IsTopSearchResult);
        launcher.SearchText = " ";
        Assert.Null(launcher.ResolveSearchResult());
        Assert.False(launcher.AllApps[0].IsTopSearchResult);
        launcher.SearchText = "missing";
        launcher.ResolveSearchResult();
        Assert.True(launcher.ShowNoResults);
        launcher.SearchText = "";
        launcher.ResolveSearchResult();
        Assert.False(launcher.ShowNoResults);
    }

    [AvaloniaFact]
    public void FastEnterUsesTheLatestQueryBeforeDispatcherProcessesSearch()
    {
        using var fixture = new TemporaryFolder();
        var launcher = new LauncherViewModel(new ShellSessionState(Path.Combine(fixture.Path, "state.json")),
        [new AppEntry("files", "Files", "Browse", "folder", false), new AppEntry("notes", "Notes", "Write", "file-text", false)]);
        launcher.SearchText = "Files";
        Assert.Equal("files", launcher.ResolveSearchResult()?.App.Id);
        launcher.SearchText = "Notes";
        Assert.Equal("notes", launcher.ResolveSearchResult()?.App.Id);
    }

    [AvaloniaFact]
    public void SystemMonitorIsSearchableByCurrentAndFormerNames()
    {
        using var fixture = new TemporaryFolder();
        var app = new DaisyOS.System.Processes.LinuxAppLauncherService().GetAvailableApps()
            .Single(candidate => candidate.Id == "org.daisyos.taskmanager.desktop");
        var launcher = new LauncherViewModel(
            new ShellSessionState(Path.Combine(fixture.Path, "state.json")), [app]);

        foreach (var query in new[] { "System Monitor", "Task Manager", "performance", "processes" })
        {
            launcher.SearchText = query;
            Assert.Equal(app.Id, launcher.ResolveSearchResult()?.App.Id);
        }
    }

    [AvaloniaFact]
    public void ClosingBackgroundTabPreservesActiveTabAndClosingActiveTabSelectsItsNeighbor()
    {
        using var fixture = new TemporaryFolder();
        using var first = new FilesTabViewModel(fixture.Path, navigateImmediately: false);
        using var middle = new FilesTabViewModel(fixture.Path, navigateImmediately: false);
        using var last = new FilesTabViewModel(fixture.Path, navigateImmediately: false);
        var files = new FilesViewModel(new FilesPreferences());
        files.Tabs.Add(first);
        files.Tabs.Add(middle);
        files.Tabs.Add(last);
        files.ActiveTab = last;
        files.CloseTab(first);
        Assert.Same(last, files.ActiveTab);
        Assert.True(last.IsActive);
        files.CloseTab(last);
        Assert.Same(middle, files.ActiveTab);
    }

    [AvaloniaFact]
    public void EditingAndDuplicatingNotesSchedulesAutosaveAndSameTitlesRemainSeparate()
    {
        using var fixture = new TemporaryFolder();
        using var notes = new NotesViewModel(fixture.Path);
        notes.AddNote();
        var first = notes.SelectedNote!;
        first.Content = "Same title\nFirst content";
        Assert.True(notes.IsAutosaveScheduled);
        notes.DuplicateNoteCommand.Execute(first);
        var second = notes.SelectedNote!;
        second.Content = "Same title\nSecond content";
        Assert.True(notes.SaveAll());
        Assert.NotEqual(first.FilePath, second.FilePath);
        var firstText = first.FilePath!.EndsWith(FormatDetector.RichNoteExtension, StringComparison.OrdinalIgnoreCase)
            ? NotePackageFormat.ReadPreview(first.FilePath!)
            : File.ReadAllText(first.FilePath!);
        var secondText = second.FilePath!.EndsWith(FormatDetector.RichNoteExtension, StringComparison.OrdinalIgnoreCase)
            ? NotePackageFormat.ReadPreview(second.FilePath!)
            : File.ReadAllText(second.FilePath!);
        Assert.Equal(first.Content, firstText);
        Assert.Equal(second.Content, secondText);
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.tmp"));
        Assert.False(notes.HasError);
    }

    [AvaloniaFact]
    public void SaveFailureKeepsTextDirtyAndRetryRecoversWithoutLosingOtherNotes()
    {
        using var fixture = new TemporaryFolder();
        using var notes = new NotesViewModel(fixture.Path);
        var blocked = Path.Combine(fixture.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var first = new NoteItem { FilePath = Path.Combine(blocked, "note.txt"), Content = "Keep this text" };
        var second = new NoteItem { Content = "This note can be saved" };
        notes.Notes.Add(first);
        notes.Notes.Add(second);
        Assert.False(notes.SaveAll());
        Assert.True(first.IsDirty);
        Assert.False(second.IsDirty);
        Assert.True(notes.HasError);
        Assert.Equal("Keep this text", first.Content);
        File.Delete(blocked);
        Directory.CreateDirectory(blocked);
        Assert.True(notes.SaveAll());
        Assert.False(notes.HasError);
        Assert.Equal(first.Content, File.ReadAllText(first.FilePath));
    }

    [AvaloniaFact]
    public void FailedDeletionKeepsTheNoteVisibleAndSelected()
    {
        using var fixture = new TemporaryFolder();
        using var notes = new NotesViewModel(fixture.Path);
        var note = new NoteItem { FilePath = fixture.Path, Content = "Still here", IsDirty = false };
        notes.Notes.Add(note);
        notes.SelectedNote = note;
        notes.DeleteNote(note);
        Assert.Contains(note, notes.Notes);
        Assert.Same(note, notes.SelectedNote);
        Assert.True(notes.HasError);
    }

    [AvaloniaFact]
    public void FailedCloseOffersSafeDefaultAndCanRecoverAfterSaveDestinationReturns()
    {
        using var fixture = new TemporaryFolder();
        var blocked = Path.Combine(fixture.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var notes = new NotesViewModel(fixture.Path);
        var note = new NoteItem { FilePath = Path.Combine(blocked, "note.txt"), Content = "Do not lose this" };
        notes.Notes.Add(note);
        notes.SelectedNote = note;
        var window = new NotesWindow(notes);
        window.Show();
        try
        {
            window.Close();
            Assert.True(window.IsVisible);
            var dialog = Assert.Single(window.OwnedWindows);
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.True(note.IsDirty);
            File.Delete(blocked);
            Directory.CreateDirectory(blocked);
            window.Close();
            Assert.False(window.IsVisible);
            Assert.Equal(note.Content, File.ReadAllText(note.FilePath!));
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            if (File.Exists(blocked)) File.Delete(blocked);
            Directory.CreateDirectory(blocked);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OpeningTheSameNotePreservesUnsavedEditsAndNewNotesClearSearch()
    {
        using var fixture = new TemporaryFolder();
        var path = Path.Combine(fixture.Path, "note.txt");
        File.WriteAllText(path, "original");
        using var notes = new NotesViewModel(fixture.Path);
        notes.InsertExternalNote(path);
        notes.SelectedNote!.Content = "unsaved";
        notes.InsertExternalNote(path);
        Assert.Single(notes.Notes);
        Assert.Equal("unsaved", notes.SelectedNote.Content);
        notes.SearchQuery = "no match";
        notes.AddNote();
        Assert.Empty(notes.SearchQuery);
        Assert.Contains(notes.SelectedNote!, notes.FilteredNotes);
    }

    [AvaloniaFact]
    public void SearchPopupRetainsActiveFilterAndEscapeRestoresFocus()
    {
        var search = new ToolbarSearchPopover();
        var window = new Window { Width = 480, Height = 200, Content = search };
        window.Show();
        try
        {
            search.Open();
            Dispatcher.UIThread.RunJobs();
            search.Text = "report";
            search.Close(clear: false);
            var button = search.FindControl<Button>("OpenButton")!;
            Assert.Contains("Active", button.Classes);
            search.Open();
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(search.IsOpen);
            Assert.Empty(search.Text);
            Assert.True(button.IsFocused);
            Assert.DoesNotContain("Active", button.Classes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClosingSearchBeforeItsQueuedFocusDoesNotStealFocus()
    {
        var search = new ToolbarSearchPopover();
        var other = new Button { Content = "Another action" };
        var window = new Window { Content = new StackPanel { Children = { search, other } } };
        window.Show();
        try
        {
            search.Open();
            search.Close(restoreFocus: false);
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PickerHonorsSingleSelectionAndRejectsFilesRemovedBeforeConfirmation()
    {
        using var fixture = new TemporaryFolder();
        var first = Path.Combine(fixture.Path, "one.txt");
        var second = Path.Combine(fixture.Path, "two.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose", false, false, [".txt"],
            InitialFolder: fixture.Path, InitialSelection: [first, second]));
        picker.Show();
        try
        {
            Assert.Single(picker.SelectedPaths);
            File.Delete(first);
            picker.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(picker.WasConfirmed);
            Assert.True(picker.IsVisible);
            Assert.Contains("no longer available", picker.FindControl<TextBlock>("SelectionHint")!.Text);
        }
        finally { picker.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 720, 480)]
    [InlineData(true, 720, 480)]
    [InlineData(false, 960, 640)]
    [InlineData(true, 960, 640)]
    public void NotesErrorStateRendersAtMinimumAndStandardSizes(bool dark, int width, int height)
    {
        using var fixture = new TemporaryFolder();
        using var notes = new NotesViewModel(fixture.Path);
        var blocked = Path.Combine(fixture.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var note = new NoteItem { FilePath = Path.Combine(blocked, "note.txt"), Content = "A note worth keeping\n\nYour writing stays available when a save fails." };
        notes.Notes.Add(note);
        notes.SelectedNote = note;
        notes.SaveAll();
        var view = new NotesView { DataContext = notes };
        var window = new Window { Width = width, Height = height, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var editor = (view.FindControl<Control>("CodeEditor")?.IsVisible == true
                ? view.FindControl<Control>("CodeEditor")
                : view.FindControl<Control>("RichEditor")) ?? (Control?)view.FindControl<TextBox>("ContentTextBox");
            Assert.NotNull(editor);
            Assert.True(editor.Bounds.Width > 200);
            SaveFrame(frame, $"notes-{(dark ? "dark" : "light")}-{width}.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerSupportsKeyboardFolderNavigationAndSearchRecovery(bool dark)
    {
        using var fixture = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(fixture.Path, "Documents"));
        File.WriteAllText(Path.Combine(fixture.Path, "A document with a longer descriptive name.txt"), "sample");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose a document", true, true, [".txt"], InitialFolder: fixture.Path))
        {
            Width = 760,
            Height = 540,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        picker.Show();
        try
        {
            var entries = picker.FindControl<ListBox>("EntriesGridView")!;
            if (!entries.IsVisible) entries = picker.FindControl<ListBox>("EntriesListView")!;
            for (var attempt = 0; attempt < 100 && entries.ItemCount < 2; attempt++) await Task.Delay(10);
            Assert.Equal(2, entries.ItemCount);
            picker.UpdateLayout();
            foreach (var icon in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(picker).OfType<DaisyIcon>())
                Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri(icon.Path!)), $"Missing picker icon: {icon.Icon}");
            var folderButton = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(entries)
                .OfType<Button>().First(button => button.DataContext is PickerEntry { IsDirectory: true });
            ((ListBox)entries).SelectedItem = folderButton.DataContext;
            entries.Focus();
            picker.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            picker.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("Documents", picker.FindControl<TextBlock>("FolderTitle")!.Text);
            Assert.False(picker.WasConfirmed);
            picker.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
            picker.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
            for (var attempt = 0; attempt < 100 && entries.ItemCount < 2; attempt++) await Task.Delay(10);
            Assert.Equal(2, entries.ItemCount);
            var search = picker.FindControl<SearchBar>("SearchBox")!;
            search.Text = "missing";
            search.FocusInput();
            Assert.True(picker.FindControl<StackPanel>("EmptyState")!.IsVisible);
            picker.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            picker.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Empty(search.Text);
            Assert.True(picker.IsVisible);
            Assert.False(picker.FindControl<StackPanel>("EmptyState")!.IsVisible);
            using var frame = picker.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveFrame(frame, $"picker-{(dark ? "dark" : "light")}-760.png");
        }
        finally { picker.Close(); }
    }

    private static void SaveFrame(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(), $"daisy-ux-{Guid.NewGuid():N}")).FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
