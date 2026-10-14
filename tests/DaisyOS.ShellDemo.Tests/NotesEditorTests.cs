using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.Notes.Documents;
using DaisyOS.Shell.Apps.Notes.Editor.Code;
using DaisyOS.Shell.Apps.Notes.Editor.Rich;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class NotesEditorTests
{
    [AvaloniaFact]
    public void FocusingAndSelectingFormattedBlocksDoesNotMutateDocument()
    {
        var block = NoteBlock.Heading(2, "Heading");
        block.Runs[0].Style = new RunStyle { FontWeight = 600, FontFamily = "Roboto" };
        var package = new NotePackage { Document = new NoteDocument { Blocks = [block, NoteBlock.Paragraph("Body")] } };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var changes = 0;
            view.DocumentChanged += (_, _) => changes++;
            var editor = Editors(view).First();
            editor.Focus();
            editor.SelectAll();
            Assert.Equal(0, changes);
            Assert.Equal(BlockKind.Heading, block.Kind);
            Assert.Equal(2, block.Level);
            Assert.Equal("Heading2", ((ComboBoxItem)view.FindControl<ComboBox>("BlockTypeCombo")!.SelectedItem!).Tag);
            Assert.Equal(600, block.Runs.Single().Style.FontWeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FormattingWithoutSelectionAppliesToTypedTextAndUndoRestoresContent()
    {
        var package = new NotePackage();
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var editor = Editors(view).Single();
            editor.Focus();
            PressKey(editor, Key.B, KeyModifiers.Control);
            editor.InsertText("Bold");
            Assert.Equal(700, package.Document.Blocks.Single().Runs.Single().Style.FontWeight);
            PressKey(editor, Key.Z, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", package.Document.Blocks.Single().PlainText);
            PressKey(Editors(view).Single(), Key.Y, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Bold", package.Document.Blocks.Single().PlainText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SplittingSelectionPreservesRunStylesAndBackspaceCannotEraseTextAfterImage()
    {
        var block = NoteBlock.Paragraph();
        block.Runs = [new StyledRun("Bold", new RunStyle { FontWeight = 700 }), new StyledRun(" plain", new RunStyle { Italic = true })];
        var package = new NotePackage { Document = new NoteDocument { Blocks = [block] } };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var editor = Editors(view).Single();
            editor.Focus();
            editor.SelectRange(4, 1);
            PressKey(editor, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Bold", package.Document.Blocks[0].PlainText);
            Assert.Equal(700, package.Document.Blocks[0].Runs.Single().Style.FontWeight);
            Assert.Equal("plain", package.Document.Blocks[1].PlainText);
            Assert.True(package.Document.Blocks[1].Runs.Single().Style.Italic);
            package.Document.Blocks.Insert(0, new NoteBlock { Kind = BlockKind.Image });
            view.RebuildBlocks();
            editor = Editors(view).First();
            editor.Focus();
            editor.SetCaret(0);
            PressKey(editor, Key.Back);
            Assert.Equal("Bold", package.Document.Blocks[1].PlainText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ShiftSelectionCanReverseAndDeletingEmojiRemovesWholeCharacter()
    {
        var editor = new RichTextBlockEditor();
        var block = NoteBlock.Paragraph("A😀B");
        editor.LoadBlock(block);
        var window = Show(editor);
        try
        {
            editor.Focus();
            editor.SetCaret(3);
            PressKey(editor, Key.Left, KeyModifiers.Shift);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(2, editor.SelectionLength);
            PressKey(editor, Key.Right, KeyModifiers.Shift);
            Assert.False(editor.HasSelection);
            Assert.Equal(3, editor.CaretIndex);
            PressKey(editor, Key.Back);
            Assert.Equal("AB", block.PlainText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectAllReplacesEntireDocumentAndUndoRestoresAllBlocks()
    {
        var package = new NotePackage { Document = NoteDocument.FromPlainText("First\nSecond") };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var editor = Editors(view).First();
            editor.Focus();
            PressKey(editor, Key.A, KeyModifiers.Control);
            editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Replacement" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Replacement", package.Document.ToPlainText());
            PressKey(Editors(view).Single(), Key.Z, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("First\nSecond", package.Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 720)]
    [InlineData(true, 720)]
    [InlineData(false, 960)]
    [InlineData(true, 960)]
    [InlineData(false, 1280)]
    [InlineData(true, 1280)]
    [InlineData(false, 2560)]
    [InlineData(true, 2560)]
    public void FloatingToolbarAndRichDocumentFitWindow(bool dark, int width)
    {
        using var folder = new TemporaryFolder();
        using var model = new NotesViewModel(folder.Path);
        model.AddNote();
        model.SelectedNote!.Package!.Document = new NoteDocument { Blocks = [NoteBlock.Heading(1, "Writing should feel simple"), NoteBlock.Paragraph("A readable note with formatting and a floating toolbar."), new NoteBlock { Kind = BlockKind.BulletedList, Runs = [new StyledRun(string.Join(" ", Enumerable.Repeat("Long list content wraps inside the document", 8)))] }] };
        var view = new NotesView { DataContext = model };
        var window = Show(view, width, dark);
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var rich = view.FindControl<RichEditorView>("RichEditor")!;
            var toolbar = rich.FindControl<Border>("FloatingToolbar")!;
            var blocks = rich.FindControl<StackPanel>("BlocksContainer")!;
            Assert.True(toolbar.Bounds.Width <= rich.Bounds.Width - 24);
            Assert.True(blocks.Bounds.Width <= rich.Bounds.Width - 40);
            Assert.Equal(24, blocks.TranslatePoint(default, rich)!.Value.X);
            Assert.InRange(blocks.Bounds.Width, rich.Bounds.Width - 70, rich.Bounds.Width - 48);
            Assert.Equal(40, toolbar.Bounds.Height);
            Assert.InRange(Math.Abs(toolbar.Bounds.Center.X - rich.Bounds.Width / 2), 0, 1);
            if (width == 1280) Assert.True(toolbar.Bounds.Width < rich.Bounds.Width - 100);
            Assert.Equal(0, blocks.Spacing);
            Assert.Equal(toolbar.Bounds.Height + 24, blocks.Margin.Top);
            Assert.Equal(view.FindControl<Border>("NavigationSidebar")!.Background, toolbar.Background);
            Assert.True(rich.FindControl<ComboBox>("FontFamilyCombo")!.IsVisible);
            Assert.Equal(92, rich.FindControl<ComboBox>("FontFamilyCombo")!.Bounds.Width);
            foreach (var name in new[] { "BlockTypeCombo", "FontFamilyCombo", "FontWeightCombo" })
            {
                var picker = rich.FindControl<ComboBox>(name)!;
                picker.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                using var focusedFrame = window.CaptureRenderedFrame();
                var background = picker.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background");
                Assert.Equal(32, background.Bounds.Height);
                Assert.DoesNotContain(picker.GetVisualDescendants().OfType<Border>(), b => b.Name == "HighlightBackground");
                Assert.Equal(32, picker.Bounds.Height);
                var origin = picker.TranslatePoint(default, toolbar)!.Value;
                Assert.Equal(4, origin.Y);
                Assert.Equal(4, toolbar.Bounds.Height - origin.Y - picker.Bounds.Height);
            }
            Assert.True(rich.FindControl<Button>("StrikeBtn")!.IsVisible);
            foreach (var editor in Editors(rich)) Assert.True(editor.Bounds.Width <= blocks.Bounds.Width);
            var artifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts)) { Directory.CreateDirectory(artifacts); frame.Save(Path.Combine(artifacts, $"notes-rich-{(dark ? "dark" : "light")}-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            var focusArtifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrEmpty(focusArtifacts))
            {
                rich.FindControl<ComboBox>("BlockTypeCombo")!.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                using var focusedFrame = window.CaptureRenderedFrame();
                focusedFrame!.Save(Path.Combine(focusArtifacts, $"notes-focused-{(dark ? "dark" : "light")}-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            window.Width = 720;
            Dispatcher.UIThread.RunJobs();
            using var narrowFrame = window.CaptureRenderedFrame();
            Assert.True(toolbar.Bounds.Width <= rich.Bounds.Width - 24);
            Assert.InRange(Math.Abs(toolbar.Bounds.Center.X - rich.Bounds.Width / 2), 0, 1);
            window.Width = 1280;
            Dispatcher.UIThread.RunJobs();
            using var wideFrame = window.CaptureRenderedFrame();
            Assert.True(toolbar.Bounds.Width < rich.Bounds.Width - 100);
            Assert.InRange(Math.Abs(toolbar.Bounds.Center.X - rich.Bounds.Width / 2), 0, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CodeAndPlainTextUseFullWidthAndFormatSpecificWrapping()
    {
        var view = new CodeEditorControl();
        var window = Show(view, 600);
        try
        {
            view.LoadText("class Example {}", new DocumentFormat(DocumentKind.Code, "csharp", "C#"));
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            Assert.True(editor.Bounds.Width > view.Bounds.Width - 50);
            Assert.NotNull(editor.TextArea);
            Assert.NotEmpty(editor.GetVisualDescendants().OfType<AvaloniaEdit.Rendering.TextView>());
            Assert.False(editor.WordWrap);
            Assert.True(editor.ShowLineNumbers);
            view.LoadText("Plain text", FormatDetector.PlainText);
            Assert.True(editor.WordWrap);
            Assert.False(editor.ShowLineNumbers);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RichEditsUpdateTitleAndDuplicatePreservesIndependentFormattingAndImages()
    {
        using var folder = new TemporaryFolder();
        using var model = new NotesViewModel(folder.Path);
        model.AddNote();
        var original = model.SelectedNote!;
        Assert.Equal("", original.Title);
        original.Package!.Document.Blocks = [NoteBlock.Heading(2, "New title"), new NoteBlock { Kind = BlockKind.Image, Image = new NoteImage { Asset = "image.png", Caption = "Photo" } }];
        original.Package.Assets["image.png"] = [1, 2, 3];
        original.NotifyDocumentChanged();
        Assert.Equal("New title", original.Title);
        model.DuplicateNoteCommand.Execute(original);
        var copy = model.SelectedNote!;
        Assert.Equal(BlockKind.Heading, copy.Package!.Document.Blocks[0].Kind);
        Assert.Equal("Photo", copy.Package.Document.Blocks[1].Image!.Caption);
        copy.Package.Document.Blocks[0].Runs[0].Text = "Independent";
        Assert.Equal("New title", original.Package.Document.Blocks[0].PlainText);
    }

    [AvaloniaFact]
    public void ConversionKeepsOriginalFileAndFailedConversionKeepsSelection()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "original.txt");
        File.WriteAllText(source, "Keep my text");
        using var model = new NotesViewModel(folder.Path);
        model.InsertExternalNote(source);
        var original = model.SelectedNote;
        model.ConvertCurrentNoteToRichNote();
        Assert.True(File.Exists(source));
        Assert.True(model.SelectedNote!.IsRichNote);
        Assert.Equal("Keep my text", File.ReadAllText(source));
        var blocked = Path.Combine(folder.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");
        using var failed = new NotesViewModel(blocked);
        failed.InsertExternalNote(source);
        var selected = failed.SelectedNote;
        failed.ConvertCurrentNoteToRichNote();
        Assert.Same(selected, failed.SelectedNote);
        Assert.False(failed.SelectedNote!.IsRichNote);
        Assert.True(failed.HasError);
        Assert.True(File.Exists(source));
    }

    [AvaloniaFact]
    public void BinaryAndDamagedFilesReportErrorsWithoutChangingSelectedNote()
    {
        using var folder = new TemporaryFolder();
        using var model = new NotesViewModel(folder.Path);
        model.AddNote();
        var selected = model.SelectedNote;
        var binary = Path.Combine(folder.Path, "binary.txt");
        File.WriteAllBytes(binary, [0, 1, 2, 0, 4]);
        model.InsertExternalNote(binary);
        Assert.Same(selected, model.SelectedNote);
        Assert.True(model.HasError);
        var broken = Path.Combine(folder.Path, "broken.dnote");
        File.WriteAllText(broken, "broken package");
        model.InsertExternalNote(broken);
        Assert.Same(selected, model.SelectedNote);
        Assert.True(model.HasError);
    }

    [AvaloniaFact]
    public async Task ClipboardCopyCutAndPasteWorkForSelectionsAndWholeNotes()
    {
        var package = new NotePackage { Document = NoteDocument.FromPlainText("First\nSecond") };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var editor = Editors(view).First();
            editor.Focus();
            editor.SelectAll();
            PressKey(editor, Key.C, KeyModifiers.Control);
            Assert.Equal("First", await window.Clipboard!.TryGetTextAsync());
            PressKey(editor, Key.X, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", package.Document.Blocks[0].PlainText);
            PressKey(editor, Key.V, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("First", package.Document.Blocks[0].PlainText);
            PressKey(editor, Key.A, KeyModifiers.Control);
            PressKey(editor, Key.C, KeyModifiers.Control);
            Assert.Equal("First\nSecond", await window.Clipboard!.TryGetTextAsync());
            await window.Clipboard!.SetTextAsync("Pasted\nDocument");
            PressKey(editor, Key.V, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Pasted\nDocument", package.Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NotesWindowPointerFocusAndToolbarKeepTypingInEditor()
    {
        using var folder = new TemporaryFolder();
        var model = new NotesViewModel(folder.Path);
        model.AddNote();
        var window = new NotesWindow(model);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var rich = window.GetVisualDescendants().OfType<RichEditorView>().Single();
            var editor = Editors(rich).Single();
            var point = editor.TranslatePoint(new Point(4, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(editor.IsFocused);
            window.KeyTextInput("Write");
            var button = rich.FindControl<Button>("BoldBtn")!;
            button.Focus();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(editor.IsFocused);
            window.KeyTextInput(" bold");
            Assert.Equal("Write bold", model.SelectedNote!.Content);
            Assert.Equal(700, model.SelectedNote.Package!.Document.Blocks.Single().Runs.Last().Style.FontWeight);
            Assert.True(model.SaveAll());
            Assert.Equal("Write bold", NotePackageFormat.Read(model.SelectedNote.FilePath!).Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SwitchingEditorsDoesNotWriteIntoOtherNoteAndReopeningKeepsRichUndo()
    {
        using var folder = new TemporaryFolder();
        using var model = new NotesViewModel(folder.Path);
        model.AddNote();
        var richNote = model.SelectedNote!;
        var path = Path.Combine(folder.Path, "example.py");
        File.WriteAllText(path, "print('original')\n");
        var view = new NotesView { DataContext = model };
        var window = Show(view);
        try
        {
            var rich = view.FindControl<RichEditorView>("RichEditor")!;
            Editors(rich).Single().InsertText("Rich content");
            model.InsertExternalNote(path);
            var textNote = model.SelectedNote!;
            var editor = view.FindControl<CodeEditorControl>("CodeEditor")!.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            editor.Text = "print('changed')\n";
            Assert.Equal("Rich content", richNote.Content);
            Assert.Equal(editor.Text, textNote.Content);
            model.SelectedNote = richNote;
            var active = Editors(rich).Single();
            active.Focus();
            active.InsertText("More ");
            model.SaveNote(richNote);
            model.InsertExternalNote(richNote.FilePath!);
            PressKey(active, Key.Z, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Rich content", richNote.Content);
            Assert.True(model.SaveAll());
            Assert.Equal("print('changed')\n", File.ReadAllText(path));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DuplicatingCodePreservesFileExtensionAndPinSurvivesReload()
    {
        using var folder = new TemporaryFolder();
        using var model = new NotesViewModel(folder.Path);
        var path = Path.Combine(folder.Path, "example.py");
        File.WriteAllText(path, "print('hello')");
        model.InsertExternalNote(path);
        model.DuplicateNoteCommand.Execute(model.SelectedNote);
        Assert.True(model.SaveAll());
        Assert.EndsWith(".py", model.SelectedNote!.FilePath);
        Assert.Equal(DocumentKind.Code, model.SelectedNote.Format.Kind);
        model.AddNote();
        model.PinNoteCommand.Execute(model.SelectedNote);
        model.SaveAll();
        Assert.True(NotePackageFormat.Read(model.SelectedNote!.FilePath!).Document.Pinned);
        using var reloaded = new NotesViewModel(folder.Path);
        await reloaded.LoadNotesAsync();
        Assert.Contains(reloaded.Notes, note => note.Format.Kind == DocumentKind.Code && note.FilePath != path);
        Assert.Contains(reloaded.Notes, note => note.IsPinned);
    }

    [AvaloniaFact]
    public async Task DraggingAcrossParagraphsCopiesAndReplacesOnlySelectedText()
    {
        var package = new NotePackage { Document = NoteDocument.FromPlainText("First paragraph\nSecond paragraph") };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = Show(view);
        try
        {
            var editors = Editors(view).ToArray();
            var start = editors[0].TranslatePoint(new Point(30, 12), window)!.Value;
            var end = editors[1].TranslatePoint(new Point(60, 12), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            var prefix = editors[0].Block.PlainText[..editors[0].SelectionStart];
            var suffix = editors[1].Block.PlainText[(editors[1].SelectionStart + editors[1].SelectionLength)..];
            var selected = editors[0].Block.PlainText.Substring(editors[0].SelectionStart, editors[0].SelectionLength) + "\n" + editors[1].Block.PlainText.Substring(editors[1].SelectionStart, editors[1].SelectionLength);
            Assert.True(editors[0].HasSelection);
            Assert.True(editors[1].HasSelection);
            PressKey(editors[1], Key.C, KeyModifiers.Control);
            Assert.Equal(selected, await window.Clipboard!.TryGetTextAsync());
            window.KeyTextInput("Replacement");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(prefix + "Replacement" + suffix, package.Document.ToPlainText());
            PressKey(Editors(view).Single(), Key.Z, KeyModifiers.Control);
            Assert.Equal("First paragraph\nSecond paragraph", package.Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BlankNoteCanRegainFocusAnywhereOnWritingSurface()
    {
        var view = new RichEditorView();
        view.LoadPackage(new NotePackage());
        var window = Show(view);
        try
        {
            var editor = Editors(view).Single();
            editor.Focus();
            view.FindControl<ComboBox>("BlockTypeCombo")!.Focus();
            window.MouseDown(new Point(300, 200), MouseButton.Left);
            window.MouseUp(new Point(300, 200), MouseButton.Left);
            Assert.True(editor.IsFocused);
            window.KeyTextInput("Recovered");
            Assert.Equal("Recovered", view.Package.Document.ToPlainText());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("BoldBtn", "bold")]
    [InlineData("ItalicBtn", "italic")]
    [InlineData("UnderlineBtn", "underline")]
    [InlineData("StrikeBtn", "strike")]
    public void FormattingButtonsToggleOnAndOffWithRealPointerClicks(string buttonName, string style)
    {
        var view = new RichEditorView();
        view.LoadPackage(new NotePackage());
        var window = Show(view, 960);
        try
        {
            var editor = Editors(view).Single();
            editor.Focus();
            PressKey(editor, Key.A, KeyModifiers.Control); // Blank Select All must not swallow formatting.
            var button = view.FindControl<Button>(buttonName)!;
            Click(window, button);
            Assert.Contains("Active", button.Classes);
            window.KeyTextInput("On");
            Click(window, button);
            Assert.DoesNotContain("Active", button.Classes);
            // Returning to the same caret must retain the explicitly disabled style.
            var caretPoint = editor.TranslatePoint(new Point(500, 12), window)!.Value;
            window.MouseDown(caretPoint, MouseButton.Left);
            window.MouseUp(caretPoint, MouseButton.Left);
            window.KeyTextInput("Off");
            var runs = view.Package.Document.Blocks.Single().Runs;
            Assert.Equal("OnOff", view.Package.Document.ToPlainText());
            Assert.True(Enabled(runs[0].Style));
            Assert.False(Enabled(runs[^1].Style));
            bool Enabled(RunStyle value) => style switch { "bold" => value.FontWeight >= 700, "italic" => value.Italic, "underline" => value.Underline, _ => value.Strikethrough };
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task StandaloneImagePickerOpensAndImportsAnImage()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "photo.png");
        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(32, 32)))
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var view = new RichEditorView();
        view.LoadPackage(new NotePackage());
        var window = Show(view, 960);
        try
        {
            view.OnInsertImageClicked(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            var picker = Assert.IsType<DaisyOS.Shell.Apps.Files.Picker.DaisyFilePickerWindow>(Assert.Single(window.OwnedWindows));
            var entries = picker.FindControl<ItemsControl>("EntriesView")!;
            // Give the real picker a fixture file without changing the user's Pictures folder.
            entries.ItemsSource = new[] { new DaisyOS.Shell.Apps.Files.Picker.PickerEntry(path, false) };
            picker.UpdateLayout();
            var tile = entries.GetVisualDescendants().OfType<Button>().First(button => button.DataContext is DaisyOS.Shell.Apps.Files.Picker.PickerEntry entry && entry.Path == path);
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var confirm = picker.FindControl<Button>("ConfirmButton")!;
            Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 100 && !view.Package.Document.Blocks.Any(block => block.Kind == BlockKind.Image); attempt++) await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.WasConfirmed);
            var imageView = view.GetVisualDescendants().OfType<RichImageBlockView>().Single();
            Assert.True(imageView.FindControl<Border>("ImageBorder")!.Bounds.Width > 0);
            Assert.Single(view.Package.Document.Blocks, block => block.Kind == BlockKind.Image);
            Assert.Single(view.Package.Assets);
            var saved = Path.Combine(folder.Path, "saved.dnote");
            NotePackageFormat.Write(saved, view.Package.Document, view.Package.Assets);
            Assert.Single(NotePackageFormat.Read(saved).Assets);
            await view.ImportImageAsync(Path.Combine(folder.Path, "missing.png"));
            Assert.True(view.FindControl<Border>("ImageImportError")!.IsVisible);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static IEnumerable<RichTextBlockEditor> Editors(Control view) => view.GetVisualDescendants().OfType<RichTextBlockEditor>();
    private static void PressKey(Control editor, Key key, KeyModifiers modifiers = KeyModifiers.None) => editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static Window Show(Control view, int width = 700, bool dark = false)
    {
        var window = new Window { Width = width, Height = 480, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "daisy-notes-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }
}
