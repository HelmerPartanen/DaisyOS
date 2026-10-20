using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Notes.Documents;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Services.Wallpaper;

namespace DaisyOS.Shell.Apps.Notes.Editor.Rich;

public partial class RichEditorView : UserControl
{
    private NotePackage _package = new();
    private RichTextBlockEditor? _activeEditor;
    private double _currentFontSize = 16;
    private bool _syncingToolbar;
    private readonly List<string> _history = [];
    private int _historyIndex;
    private bool _restoringHistory;
    private bool _allSelected;
    private bool _formattingBatch;
    private int _rangeStartBlock, _rangeStartOffset, _rangeEndBlock, _rangeEndOffset;
    private int _dragAnchorBlock, _dragAnchorOffset;
    private RichTextBlockEditor? _dragTarget;

    public NotePackage Package => _package;

    public event EventHandler? DocumentChanged;

    public RichEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnHistoryKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnDocumentTextInput, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_allSelected && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is Visual source && (source == DocumentScroll || source.GetVisualAncestors().Contains(DocumentScroll))) ClearDocumentSelection();
        }, RoutingStrategies.Tunnel);
        FormattingScroll.ScrollChanged += (_, _) =>
        {
            ToolbarScrollLeft.IsVisible = FormattingScroll.Offset.X > 0.5;
            ToolbarScrollRight.IsVisible = FormattingScroll.Offset.X + FormattingScroll.Viewport.Width < FormattingScroll.Extent.Width - 0.5;
        };
        SizeChanged += (_, _) => FloatingToolbar.MaxWidth = Math.Max(0, Bounds.Width - 24);
        DocumentScroll.AddHandler(PointerPressedEvent, OnDocumentBackgroundPressed, RoutingStrategies.Bubble);
        FloatingToolbar.SizeChanged += (_, _) =>
            BlocksContainer.Margin = new Thickness(24, FloatingToolbar.Bounds.Height + 24, 24, 24);
    }

    public void LoadPackage(NotePackage package)
    {
        BlocksContainer.Classes.Remove("DocumentEntering");
        _package = package;
        _history.Clear();
        _historyIndex = 0;
        _history.Add(JsonSerializer.Serialize(_package.Document, NotesJsonContext.Default.NoteDocument));
        RebuildBlocks();
        DocumentScroll.Offset = default;
        if (Application.Current is not App { ReduceMotionEnabled: true })
            BlocksContainer.Classes.Add("DocumentEntering");
    }

    public void RebuildBlocks()
    {
        _allSelected = false;
        _activeEditor = null;
        BlocksContainer.Children.Clear();

        if (_package.Document.Blocks.Count == 0)
        {
            _package.Document.Blocks.Add(NoteBlock.Paragraph());
        }

        var numberIndex = 1;
        for (int i = 0; i < _package.Document.Blocks.Count; i++)
        {
            var block = _package.Document.Blocks[i];
            var blockIndex = i;

            if (block.Kind != BlockKind.NumberedList) numberIndex = 1;

            var control = CreateBlockControl(block, blockIndex, ref numberIndex);
            BlocksContainer.Children.Add(control);
        }
        var first = TextEditors().FirstOrDefault();
        if (first != null) SetActiveEditor(first);
    }

    private Control CreateBlockControl(NoteBlock block, int index, ref int numberIndex)
    {
        if (block.Kind == BlockKind.Divider)
        {
            return new Border
            {
                Height = 1,
                Background = TryGetBrush("StrokeDefaultBrush") ?? Brushes.Gray,
                Margin = new Thickness(0, 16)
            };
        }

        if (block.Kind == BlockKind.Image)
        {
            var imageView = new RichImageBlockView();
            byte[]? assetBytes = null;
            if (block.Image != null && _package.Assets.TryGetValue(block.Image.Asset, out var bytes))
            {
                assetBytes = bytes;
            }
            imageView.LoadBlock(block, assetBytes);

            imageView.MoveUpRequested += (_, _) => MoveBlock(index, index - 1);
            imageView.MoveDownRequested += (_, _) => MoveBlock(index, index + 1);
            imageView.DeleteRequested += (_, _) => DeleteBlock(index);
            imageView.Changed += (_, _) => PublishChange();

            return imageView;
        }

        // Text-based blocks
        var editor = new RichTextBlockEditor();
        editor.LoadBlock(block);

        editor.SelectionDragStarted += (_, _) =>
        {
            _dragAnchorBlock = index;
            _dragAnchorOffset = editor.CaretIndex;
            _dragTarget = editor;
        };
        editor.SelectionDragMoved += (_, e) => SelectAcrossBlocks(e);
        editor.SelectionDragEnded += (_, _) => _dragTarget?.Focus();
        editor.GotFocus += (_, _) => SetActiveEditor(editor);
        editor.ContentChanged += (_, _) => PublishChange();
        editor.SelectionChanged += (_, _) => OnEditorSelectionChanged(editor);
        editor.SplitBlockRequested += (_, _) => SplitBlockAt(index, editor);
        editor.MergeBlockRequested += (_, _) => MergeBlockAt(index);
        editor.MergeNextBlockRequested += (_, _) => MergeBlockAt(index + 1);
        editor.NavigateBlockRequested += (_, key) => NavigateBlock(index, key);
        editor.ConvertBlockRequested += (_, kind) => ConvertBlock(index, kind);

        switch (block.Kind)
        {
            case BlockKind.BulletedList:
            {
                var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                var bullet = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = "•",
                    FontSize = 18,
                    Foreground = TryGetBrush("ContentSecondaryBrush") ?? Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, 0, 0, 0)
                }, "Title");
                panel.Children.Add(bullet);
                Grid.SetColumn(editor, 1);
                panel.Children.Add(editor);
                return panel;
            }
            case BlockKind.NumberedList:
            {
                var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                var num = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
                {
                    Text = $"{numberIndex++}.",
                    FontSize = 15,
                    FontWeight = FontWeight.Medium,
                    Foreground = TryGetBrush("ContentSecondaryBrush") ?? Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Top,
                    Width = 24
                }, "BodyLarge");
                panel.Children.Add(num);
                Grid.SetColumn(editor, 1);
                panel.Children.Add(editor);
                return panel;
            }
            case BlockKind.CheckList:
            {
                var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                var check = new CheckBox
                {
                    IsChecked = block.Checked,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 0, 0)
                };
                check.IsCheckedChanged += (_, _) =>
                {
                    block.Checked = check.IsChecked == true;
                    PublishChange();
                };
                panel.Children.Add(check);
                Grid.SetColumn(editor, 1);
                panel.Children.Add(editor);
                return panel;
            }
            case BlockKind.Quote:
            {
                return new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = TryGetBrush("ActionPrimaryBrush") ?? Brushes.Cyan,
                    Padding = new Thickness(16, 4, 0, 4),
                    Child = editor
                };
            }
            case BlockKind.Code:
            {
                return new Border
                {
                    Background = TryGetBrush("SurfaceRaisedSubtleBrush") ?? Brushes.Black,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12),
                    Child = editor
                };
            }
            default:
                return editor;
        }
    }

    private void SetActiveEditor(RichTextBlockEditor editor)
    {
        _activeEditor = editor;
        UpdateToolbarForStyle(editor.GetCurrentSelectionStyle(), editor.Block.Kind);
    }

    private void OnEditorSelectionChanged(RichTextBlockEditor editor)
    {
        if (editor != _activeEditor) SetActiveEditor(editor);
        UpdateToolbarForStyle(editor.GetCurrentSelectionStyle(), editor.Block.Kind);

    }

    private void UpdateToolbarForStyle(RunStyle style, BlockKind blockKind)
    {
        _syncingToolbar = true;
        try
        {
        BoldBtn.Classes.Set("Active", (style.FontWeight ?? (blockKind == BlockKind.Heading ? 700 : 400)) >= 700);
        ItalicBtn.Classes.Set("Active", style.Italic);
        UnderlineBtn.Classes.Set("Active", style.Underline);
        StrikeBtn.Classes.Set("Active", style.Strikethrough);

        _currentFontSize = style.FontSize ?? (blockKind == BlockKind.Heading ? (_activeEditor?.Block.Level == 1 ? 28 : _activeEditor?.Block.Level == 2 ? 22 : 18) : 16);
        FontSizeLabel.Text = Math.Round(_currentFontSize).ToString();

        // Sync block type combo
        for (int i = 0; i < BlockTypeCombo.Items.Count; i++)
        {
            if (BlockTypeCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), blockKind == BlockKind.Heading ? $"Heading{_activeEditor?.Block.Level ?? 1}" : blockKind.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                BlockTypeCombo.SelectedIndex = i;
                break;
            }
        }
        FontFamilyCombo.SelectedItem = FontFamilyCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => style.FontFamily is null ? Equals(item.Tag, "SystemFont") : string.Equals(item.Content?.ToString(), style.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontWeightCombo.SelectedItem = FontWeightCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == (style.FontWeight ?? (blockKind == BlockKind.Heading ? 700 : 400)).ToString());
        }
        finally { _syncingToolbar = false; }
    }

    private void SplitBlockAt(int index, RichTextBlockEditor editor)
    {
        var block = _package.Document.Blocks[index];
        var text = block.PlainText;
        var caret = editor.CaretIndex;

        var nextKind = block.Kind is BlockKind.BulletedList or BlockKind.NumberedList or BlockKind.CheckList
            ? block.Kind
            : BlockKind.Paragraph;

        // If in a list and the item is empty, pressing Enter turns it into a paragraph
        if (string.IsNullOrWhiteSpace(text) && block.Kind is BlockKind.BulletedList or BlockKind.NumberedList or BlockKind.CheckList)
        {
            block.Kind = BlockKind.Paragraph;
            RebuildBlocks();
            FocusBlock(index);
            PublishChange();
            return;
        }

        var before = SliceRuns(block.Runs, 0, editor.HasSelection ? editor.SelectionStart : caret);
        var afterStart = editor.HasSelection ? editor.SelectionStart + editor.SelectionLength : caret;
        var after = SliceRuns(block.Runs, afterStart, text.Length - afterStart);
        block.Runs = before;
        block.Normalize();
        var newBlock = new NoteBlock { Kind = nextKind, Runs = after };

        _package.Document.Blocks.Insert(index + 1, newBlock);
        RebuildBlocks();
        FocusBlock(index + 1);
        PublishChange();
    }

    private void MergeBlockAt(int index)
    {
        if (index <= 0 || index >= _package.Document.Blocks.Count || !_package.Document.Blocks[index].IsTextBlock) return;

        var prevBlock = _package.Document.Blocks[index - 1];
        var currBlock = _package.Document.Blocks[index];

        if (prevBlock.IsTextBlock)
        {
            var prevLength = prevBlock.TextLength;
            prevBlock.Runs.AddRange(currBlock.Runs);
            prevBlock.Normalize();

            _package.Document.Blocks.RemoveAt(index);
            RebuildBlocks();
            FocusBlock(index - 1, prevLength);
        }
        else
        {
            // A non-text block is not a valid merge destination. Keep the text intact.
            return;
        }

        PublishChange();
    }

    private void NavigateBlock(int index, Key key)
    {
        var backwards = key is Key.Up or Key.Left;
        var target = backwards ? index - 1 : index + 1;
        while (target >= 0 && target < _package.Document.Blocks.Count && !_package.Document.Blocks[target].IsTextBlock)
            target += backwards ? -1 : 1;
        if (target >= 0 && target < _package.Document.Blocks.Count)
        {
            FocusBlock(target, backwards ? _package.Document.Blocks[target].TextLength : 0);
        }
    }

    private void ConvertBlock(int index, BlockKind kind)
    {
        var block = _package.Document.Blocks[index];
        var caret = _activeEditor?.CaretIndex ?? 0;
        block.Kind = kind;
        RebuildBlocks();
        FocusBlock(index, caret);
        PublishChange();
    }

    private void MoveBlock(int from, int to)
    {
        if (to < 0 || to >= _package.Document.Blocks.Count) return;
        var block = _package.Document.Blocks[from];
        _package.Document.Blocks.RemoveAt(from);
        _package.Document.Blocks.Insert(to, block);
        RebuildBlocks();
        PublishChange();
    }

    private void DeleteBlock(int index)
    {
        if (index >= 0 && index < _package.Document.Blocks.Count)
        {
            _package.Document.Blocks.RemoveAt(index);
            if (_package.Document.Blocks.Count == 0)
                _package.Document.Blocks.Add(NoteBlock.Paragraph());
            RebuildBlocks();
            PublishChange();
        }
    }

    private void FocusBlock(int index, int caret = 0)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (index >= 0 && index < BlocksContainer.Children.Count)
            {
                var child = BlocksContainer.Children[index];
                var editor = FindEditorIn(child);
                if (editor != null)
                {
                    editor.Focus();
                    editor.SetCaret(caret);
                    editor.BringIntoView();
                }
            }
        });
    }

    private RichTextBlockEditor? FindEditorIn(Control control) => control switch
    {
        RichTextBlockEditor r => r,
        Panel p => FindEditorInPanel(p),
        Decorator d when d.Child is Control c => FindEditorIn(c),
        _ => null
    };

    private RichTextBlockEditor? FindEditorInPanel(Panel p)
    {
        foreach (var c in p.Children)
        {
            var found = FindEditorIn(c);
            if (found != null) return found;
        }
        return null;
    }

    // Formatting Toolbar Actions
    public void OnBoldClicked(object? sender, RoutedEventArgs e)
    {
        var bold = (_activeEditor?.GetCurrentSelectionStyle().FontWeight ?? (_activeEditor?.Block.Kind == BlockKind.Heading ? 700 : 400)) >= 700;
        ApplyFormatting(s => s with { FontWeight = bold ? 400 : 700 });
    }

    public void OnItalicClicked(object? sender, RoutedEventArgs e)
    {
        var italic = _activeEditor?.GetCurrentSelectionStyle().Italic == true;
        ApplyFormatting(s => s with { Italic = !italic });
    }

    public void OnUnderlineClicked(object? sender, RoutedEventArgs e)
    {
        var underline = _activeEditor?.GetCurrentSelectionStyle().Underline == true;
        ApplyFormatting(s => s with { Underline = !underline });
    }

    public void OnStrikeClicked(object? sender, RoutedEventArgs e)
    {
        var strike = _activeEditor?.GetCurrentSelectionStyle().Strikethrough == true;
        ApplyFormatting(s => s with { Strikethrough = !strike });
    }

    public void OnIncreaseFontSizeClicked(object? sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Min(96, _currentFontSize + 2);
        FontSizeLabel.Text = _currentFontSize.ToString();
        ApplyFormatting(s => s with { FontSize = _currentFontSize });
    }

    public void OnDecreaseFontSizeClicked(object? sender, RoutedEventArgs e)
    {
        _currentFontSize = Math.Max(8, _currentFontSize - 2);
        FontSizeLabel.Text = _currentFontSize.ToString();
        ApplyFormatting(s => s with { FontSize = _currentFontSize });
    }

    public void OnFontFamilySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_syncingToolbar && FontFamilyCombo.SelectedItem is ComboBoxItem item && item.Content is string family)
        {
            ApplyFormatting(s => s with { FontFamily = Equals(item.Tag, "SystemFont") ? null : family });
            _activeEditor?.Focus();
        }
    }

    public void OnFontWeightSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_syncingToolbar && FontWeightCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var weight))
        {
            ApplyFormatting(s => s with { FontWeight = weight });
            _activeEditor?.Focus();
        }
    }

    public void OnColorSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
        {
            ApplyFormatting(s => s with { Foreground = string.IsNullOrEmpty(color) ? null : color });
        }
    }

    public void OnHighlightSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string highlight })
        {
            ApplyFormatting(s => s with { Highlight = string.IsNullOrEmpty(highlight) ? null : highlight });
        }
    }

    public void OnClearFormattingClicked(object? sender, RoutedEventArgs e)
    {
        ApplyFormatting(_ => RunStyle.Default);
    }

    public void OnBlockTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingToolbar || _activeEditor == null || BlockTypeCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();

        var (kind, level) = tag switch
        {
            "Heading1" => (BlockKind.Heading, 1),
            "Heading2" => (BlockKind.Heading, 2),
            "Heading3" => (BlockKind.Heading, 3),
            "BulletedList" => (BlockKind.BulletedList, 0),
            "NumberedList" => (BlockKind.NumberedList, 0),
            "CheckList" => (BlockKind.CheckList, 0),
            "Quote" => (BlockKind.Quote, 0),
            "Code" => (BlockKind.Code, 0),
            _ => (BlockKind.Paragraph, 0)
        };

        var caret = _activeEditor.CaretIndex;
        _activeEditor.Block.Kind = kind;
        _activeEditor.Block.Level = level;
        var idx = _package.Document.Blocks.IndexOf(_activeEditor.Block);
        RebuildBlocks();
        if (idx >= 0) FocusBlock(idx, caret);
        PublishChange();
    }

    public void OnInsertDividerClicked(object? sender, RoutedEventArgs e)
    {
        var index = _activeEditor != null ? _package.Document.Blocks.IndexOf(_activeEditor.Block) + 1 : _package.Document.Blocks.Count;
        _package.Document.Blocks.Insert(index, new NoteBlock { Kind = BlockKind.Divider });
        _package.Document.Blocks.Insert(index + 1, NoteBlock.Paragraph());
        RebuildBlocks();
        FocusBlock(index + 1);
        PublishChange();
    }

    public async void OnInsertImageClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var package = _package;
        var anchor = _activeEditor?.Block;
        ImageImportError.IsVisible = false;
        try
        {
            var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Insert image", false, false,
                WallpaperFileTypes.ImagePatterns.Select(pattern => pattern.TrimStart('*')).ToArray(),
                ItemNameSingular: "image", ItemNamePlural: "images"));
            var files = await picker.ShowDialog<IReadOnlyList<string>?>(owner);
            if (package != _package || files == null || files.Count == 0) return;
            await ImportImageAsync(files[0], package, anchor);
        }
        catch (Exception ex)
        {
            ShowImageImportError(ex);
        }
    }

    internal async global::System.Threading.Tasks.Task ImportImageAsync(string filePath, NotePackage? target = null, NoteBlock? anchor = null)
    {
        var package = target ?? _package;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            using var stream = new MemoryStream(bytes);
            using var bitmap = new Bitmap(stream);
            if (package != _package) return;
            var assetName = NotePackageFormat.CreateAssetName(filePath);
            var block = new NoteBlock
            {
                Kind = BlockKind.Image,
                Image = new NoteImage
                {
                    Asset = assetName, PixelWidth = bitmap.PixelSize.Width, PixelHeight = bitmap.PixelSize.Height,
                    Width = Math.Min(560, bitmap.Size.Width), CornerRadius = 12,
                    Alignment = ImageAlignment.Center, Wrap = ImageWrap.None
                }
            };
            package.Assets[assetName] = bytes;
            var index = anchor == null ? package.Document.Blocks.Count : package.Document.Blocks.IndexOf(anchor) + 1;
            package.Document.Blocks.Insert(index, block);
            package.Document.Blocks.Insert(index + 1, NoteBlock.Paragraph());
            RebuildBlocks();
            FocusBlock(index + 1);
            ImageImportError.IsVisible = false;
            PublishChange();
        }
        catch (Exception ex) { if (package == _package) ShowImageImportError(ex); }
    }

    private void ShowImageImportError(Exception error)
    {
        ImageImportErrorText.Text = Loc.Get("This image could not be inserted. Choose a readable PNG, JPEG, WebP, BMP, or GIF file.");
        ImageImportError.IsVisible = true;
        global::System.Console.Error.WriteLine($"[notes] Image import failed: {error.Message}");
    }

    private void OnDocumentBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.GetVisualAncestors().OfType<RichImageBlockView>().Any()) return;
        var editors = TextEditors().ToArray();
        if (editors.Length == 0)
        {
            _package.Document.Blocks.Add(NoteBlock.Paragraph());
            RebuildBlocks();
            PublishChange();
            editors = TextEditors().ToArray();
        }
        var closest = editors.OrderBy(editor => Math.Abs(e.GetPosition(editor).Y - editor.Bounds.Height / 2)).First();
        closest.Focus();
        var point = e.GetPosition(closest);
        closest.SetCaret(point.Y < 0 ? 0 : point.Y > closest.Bounds.Height ? closest.Block.TextLength : closest.HitTestCaret(point));
        e.Handled = true;
    }

    private void OnToolbarScrollLeft(object? sender, RoutedEventArgs e) =>
        FormattingScroll.Offset = new Vector(Math.Max(0, FormattingScroll.Offset.X - 160), 0);

    private void OnToolbarScrollRight(object? sender, RoutedEventArgs e) =>
        FormattingScroll.Offset = new Vector(Math.Min(Math.Max(0, FormattingScroll.Extent.Width - FormattingScroll.Viewport.Width), FormattingScroll.Offset.X + 160), 0);

    public void SetDocumentTitle(string title)
    {
        _package.Document.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        PublishChange();
    }

    public void FocusDocument() => FocusBlock(0);

    internal static List<StyledRun> SliceRuns(List<StyledRun> runs, int start, int length)
    {
        var result = new List<StyledRun>();
        var offset = 0;
        foreach (var run in runs)
        {
            var from = Math.Max(start, offset);
            var to = Math.Min(start + length, offset + run.Text.Length);
            if (to > from) result.Add(new StyledRun(run.Text.Substring(from - offset, to - from), run.Style));
            offset += run.Text.Length;
        }
        return result;
    }

    private void PublishChange()
    {
        if (_formattingBatch) return;
        if (!_restoringHistory)
        {
            var state = JsonSerializer.Serialize(_package.Document, NotesJsonContext.Default.NoteDocument);
            if (_history.Count == 0 || state != _history[_historyIndex])
            {
                if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(state);
                if (_history.Count > 100) _history.RemoveAt(0);
                _historyIndex = _history.Count - 1;
            }
        }
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is RichTextBlockEditor && e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var active = _activeEditor;
            foreach (var editor in TextEditors()) editor.SelectAll();
            _activeEditor = active;
            _rangeStartBlock = 0;
            _rangeStartOffset = 0;
            _rangeEndBlock = _package.Document.Blocks.Count - 1;
            _rangeEndOffset = _package.Document.Blocks[_rangeEndBlock].TextLength;
            _allSelected = true;
            e.Handled = true;
            return;
        }
        if (_allSelected && e.Source is RichTextBlockEditor)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.C or Key.X or Key.V)
            {
                _ = HandleDocumentClipboardAsync(e.Key);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Back or Key.Delete or Key.Enter)
            {
                ReplaceDocumentText(string.Empty);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            {
                ClearDocumentSelection();
                FocusBlock(e.Key is Key.Right or Key.End ? _package.Document.Blocks.Count - 1 : 0,
                    e.Key is Key.Right or Key.End ? _package.Document.Blocks.Last().TextLength : 0);
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.B or Key.I or Key.U)
            {
                ApplyFormatting(style => e.Key switch
                {
                    Key.B => style with { FontWeight = style.FontWeight is >= 700 ? 400 : 700 },
                    Key.I => style with { Italic = !style.Italic },
                    _ => style with { Underline = !style.Underline }
                });
                e.Handled = true;
                return;
            }
        }
        if (e.Source is TextBox || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key != Key.Z && e.Key != Key.Y) return;
        var redo = e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var target = _historyIndex + (redo ? 1 : -1);
        e.Handled = true;
        if (target < 0 || target >= _history.Count) return;
        var blockIndex = _activeEditor == null ? 0 : _package.Document.Blocks.IndexOf(_activeEditor.Block);
        var caret = _activeEditor?.CaretIndex ?? 0;
        _restoringHistory = true;
        try
        {
            _historyIndex = target;
            _package.Document = JsonSerializer.Deserialize(_history[target], NotesJsonContext.Default.NoteDocument)!;
            RebuildBlocks();
            FocusBlock(Math.Clamp(blockIndex, 0, _package.Document.Blocks.Count - 1), caret);
            PublishChange();
        }
        finally { _restoringHistory = false; }
    }

    private IEnumerable<RichTextBlockEditor> TextEditors() => BlocksContainer.Children
        .Select(FindEditorIn).OfType<RichTextBlockEditor>();

    private void ApplyFormatting(Func<RunStyle, RunStyle> update)
    {
        var active = _activeEditor;
        if (_allSelected && TextEditors().Any(editor => editor.HasSelection))
        {
            _formattingBatch = true;
            try { foreach (var editor in TextEditors().Where(editor => editor.HasSelection)) editor.ApplyStyleToSelection(update); }
            finally { _formattingBatch = false; }
            PublishChange();
        }
        else
        {
            _allSelected = false;
            active?.ApplyStyleToSelection(update);
        }
        _activeEditor = active;
        active?.Focus();
    }

    private void ClearDocumentSelection()
    {
        _allSelected = false;
        foreach (var editor in TextEditors()) editor.SetCaret(editor.CaretIndex);
    }

    private void OnDocumentTextInput(object? sender, TextInputEventArgs e)
    {
        if (!_allSelected || e.Source is not RichTextBlockEditor || string.IsNullOrEmpty(e.Text)) return;
        ReplaceDocumentText(e.Text);
        e.Handled = true;
    }

    private void SelectAcrossBlocks(PointerEventArgs e)
    {
        var candidates = TextEditors().ToArray();
        if (candidates.Length == 0) return;
        var target = candidates.Last();
        foreach (var editor in candidates)
        {
            if (e.GetPosition(editor).Y <= editor.Bounds.Height) { target = editor; break; }
        }
        _dragTarget = target;
        var targetIndex = _package.Document.Blocks.IndexOf(target.Block);
        var point = e.GetPosition(target);
        var offset = point.Y < 0 ? 0 : point.Y > target.Bounds.Height ? target.Block.TextLength : target.HitTestCaret(point);
        var backwards = targetIndex < _dragAnchorBlock || targetIndex == _dragAnchorBlock && offset < _dragAnchorOffset;
        _rangeStartBlock = backwards ? targetIndex : _dragAnchorBlock;
        _rangeStartOffset = backwards ? offset : _dragAnchorOffset;
        _rangeEndBlock = backwards ? _dragAnchorBlock : targetIndex;
        _rangeEndOffset = backwards ? _dragAnchorOffset : offset;
        foreach (var editor in candidates)
        {
            var index = _package.Document.Blocks.IndexOf(editor.Block);
            if (index < _rangeStartBlock || index > _rangeEndBlock) editor.SetCaret(editor.CaretIndex);
            else
            {
                var from = index == _rangeStartBlock ? _rangeStartOffset : 0;
                var to = index == _rangeEndBlock ? _rangeEndOffset : editor.Block.TextLength;
                editor.SelectRange(from, to - from);
            }
        }
        _allSelected = _rangeStartBlock != _rangeEndBlock;
        SetActiveEditor(target);
        e.Handled = true;
    }

    private string SelectedDocumentText()
    {
        var blocks = _package.Document.Blocks.GetRange(_rangeStartBlock, _rangeEndBlock - _rangeStartBlock + 1).Select(block => block.Clone()).ToList();
        var last = blocks[^1];
        last.Runs = SliceRuns(last.Runs, 0, _rangeEndOffset);
        var first = blocks[0];
        first.Runs = SliceRuns(first.Runs, _rangeStartOffset, first.TextLength - _rangeStartOffset);
        return new NoteDocument { Blocks = blocks }.ToPlainText();
    }

    private void ReplaceDocumentText(string text)
    {
        var blocks = _package.Document.Blocks;
        var first = blocks[_rangeStartBlock];
        var last = blocks[_rangeEndBlock];
        var before = SliceRuns(first.Runs, 0, _rangeStartOffset);
        var after = SliceRuns(last.Runs, _rangeEndOffset, last.TextLength - _rangeEndOffset);
        var inserted = NoteDocument.FromPlainText(text).Blocks;
        inserted[0].Kind = first.IsTextBlock ? first.Kind : BlockKind.Paragraph;
        inserted[0].Level = first.Level;
        inserted[0].Runs.InsertRange(0, before);
        var caret = inserted[^1].TextLength;
        inserted[^1].Runs.AddRange(after);
        foreach (var block in inserted) block.Normalize();
        blocks.RemoveRange(_rangeStartBlock, _rangeEndBlock - _rangeStartBlock + 1);
        blocks.InsertRange(_rangeStartBlock, inserted);
        var target = _rangeStartBlock + inserted.Count - 1;
        RebuildBlocks();
        FocusBlock(target, caret);
        PublishChange();
    }

    private async global::System.Threading.Tasks.Task HandleDocumentClipboardAsync(Key key)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var package = _package;
        var state = _history[_historyIndex];
        try
        {
            if (key == Key.V)
            {
                var text = await clipboard.TryGetTextAsync();
                if (_allSelected && package == _package && state == _history[_historyIndex] && text != null) ReplaceDocumentText(text);
            }
            else
            {
                await clipboard.SetTextAsync(SelectedDocumentText());
                if (key == Key.X && _allSelected && package == _package && state == _history[_historyIndex]) ReplaceDocumentText(string.Empty);
            }
        }
        catch (Exception ex) { global::System.Console.Error.WriteLine($"[notes] Clipboard failed: {ex.Message}"); }
    }

    private IBrush? TryGetBrush(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var res) && res is IBrush brush)
            return brush;
        return null;
    }
}
