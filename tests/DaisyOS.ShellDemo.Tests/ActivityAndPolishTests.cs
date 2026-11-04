using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Shell.Services.Motion;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ActivityAndPolishTests
{
    [AvaloniaFact]
    public void RuntimeNotificationAndSliderGlyphsArePackaged()
    {
        foreach (var category in Enum.GetValues<DaisyOS.Core.Models.NotificationCategory>())
        {
            var item = new DaisyOS.Core.Models.NotificationItem("Fixture", "", DateTimeOffset.Now, Category: category);
            Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri(
                $"avares://DaisyOS.Shell/Assets/Icons/HugeIcons/{item.EffectiveSystemIconGlyph}.svg")));
        }
        var slider = new DaisyOS.Shell.Controls.QuickSettingsSlider();
        Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri(
            $"avares://DaisyOS.Shell/Assets/Icons/HugeIcons/{slider.Icon}.svg")));
    }

    [AvaloniaFact]
    public void ActivityGateIgnoresFadeFramesButStopsOnHiddenAncestorAndDetach()
    {
        var child = new Border();
        var parent = new Border { Child = child };
        var activity = new List<bool>();
        _ = new VisualActivityGate(child, activity.Add);
        var window = new Window { Content = parent };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(activity.Last());
            activity.Clear();
            parent.Opacity = .9;
            parent.Opacity = .5;
            parent.Opacity = .1;
            Assert.Empty(activity);
            parent.Opacity = 0;
            Assert.Equal(new[] { false }, activity);
            parent.Opacity = .1;
            parent.Opacity = .8;
            Assert.Equal(new[] { false, true }, activity);
            parent.IsVisible = false;
            parent.IsVisible = true;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(activity.Last());
            var count = activity.Count;
            parent.Opacity = 0;
            parent.Opacity = 1;
            Assert.Equal(count, activity.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PinningNotesMovesExistingRowsWithoutResettingCollection()
    {
        var model = new NotesViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        try
        {
            model.AddNote();
            var first = model.SelectedNote!;
            model.AddNote();
            var second = model.SelectedNote!;
            var actions = new List<NotifyCollectionChangedAction>();
            model.FilteredNotes.CollectionChanged += (_, change) => actions.Add(change.Action);
            model.PinNoteCommand.Execute(first);
            Assert.Same(first, model.FilteredNotes[0]);
            Assert.Same(second, model.SelectedNote);
            Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
            Assert.DoesNotContain(NotifyCollectionChangedAction.Remove, actions);
            Assert.Contains(NotifyCollectionChangedAction.Move, actions);
        }
        finally { model.Dispose(saveChanges: false); }
    }

    [AvaloniaFact]
    public async Task ExternalNoteReadDoesNotBlockAndLateCompletionCannotStealSelection()
    {
        var directory = Directory.CreateTempSubdirectory("daisy-note-polish-");
        var model = new NotesViewModel(directory.FullName);
        try
        {
            var path = Path.Combine(directory.FullName, "external.txt");
            await File.WriteAllTextAsync(path, "External text");
            model.AddNote();
            var open = model.InsertExternalNoteAsync(path);
            // IO completion must yield to input even for a small local file.
            Assert.False(open.IsCompleted);
            model.AddNote();
            var latestSelection = model.SelectedNote;
            await open;
            Assert.Same(latestSelection, model.SelectedNote);
            Assert.DoesNotContain(model.Notes, note => note.FilePath == path);
            await model.InsertExternalNoteAsync(path);
            Assert.Equal(path, model.SelectedNote!.FilePath);
            Assert.Equal("External text", model.SelectedNote.Content);
            await model.InsertExternalNoteAsync(path);
            Assert.Single(model.Notes, note => note.FilePath == path);
        }
        finally { model.Dispose(saveChanges: false); directory.Delete(true); }
    }

    [Theory]
    [InlineData(220, 0, 0)]
    [InlineData(220, .5, 110)]
    [InlineData(220, .01, 40)]
    [InlineData(20, .5, 20)]
    [InlineData(0, .5, 0)]
    public void ReversalsUseRemainingDistanceWithoutExceedingFullDuration(double full, double fraction, double expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected), GuiMotion.RemainingDuration(TimeSpan.FromMilliseconds(full), fraction));
    }
}
