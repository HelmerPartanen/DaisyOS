using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.Notes.Documents;
using DaisyOS.Shell.Apps.Notes.Editor.Rich;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ContinuityHandoffTests
{
    [AvaloniaFact]
    public void RestoredActivityImmediatelyLoadsTheEditorAndSavesAnIndependentDocument()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-handoff-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var model = new NotesViewModel(folder);
            var window = new NotesWindow(model);
            window.Show();
            try
            {
                Assert.True(window.RestoreContinuityActivity(new ContinuityActivity("org.daisyos.notes", "note.text", 1, "Meeting", "Continue here")));
                Dispatcher.UIThread.RunJobs();
                var note = Assert.Single(model.Notes);
                Assert.Same(note, model.SelectedNote);
                var editor = window.GetVisualDescendants().OfType<RichEditorView>().Single();
                Assert.Same(note.Package, editor.Package);
                Assert.Equal("Continue here", editor.Package.Document.ToPlainText());
                Assert.Equal("Meeting", editor.Package.Document.Title);
                Assert.Equal("Continue here", NotePackageFormat.Read(note.FilePath!).Document.ToPlainText());
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }
}
