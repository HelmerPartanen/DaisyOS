using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Notes.Documents;

namespace DaisyOS.Shell.Apps.Notes.Editor.Rich;

public class RichTextBlockEditor : Control
{
    private NoteBlock _block = NoteBlock.Paragraph();
    private TextLayout? _textLayout;
    private int _caretIndex;
    private int _selectionStart;
    private int _selectionLength;
    private bool _isCaretVisible = true;
    private readonly DispatcherTimer _caretBlinkTimer;
    private bool _isSelectingWithPointer;
    private int _pointerDragStartIndex;
    private int _selectionAnchor;
    private RunStyle? _typingStyle;

    public NoteBlock Block => _block;
    public int CaretIndex => _caretIndex;
    public int SelectionStart => _selectionStart;
    public int SelectionLength => _selectionLength;
    public bool HasSelection => _selectionLength > 0;

    public event EventHandler<PointerPressedEventArgs>? SelectionDragStarted;
    public event EventHandler<PointerEventArgs>? SelectionDragMoved;
    public event EventHandler? SelectionDragEnded;
    public int HitTestCaret(Point point) => Math.Clamp(_textLayout?.HitTestPoint(point).TextPosition ?? 0, 0, _block.TextLength);

    public event EventHandler? ContentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? SplitBlockRequested;
    public event EventHandler? MergeBlockRequested;
    public event EventHandler? MergeNextBlockRequested;
    public event EventHandler<Key>? NavigateBlockRequested;
    public event EventHandler<BlockKind>? ConvertBlockRequested;

    public RichTextBlockEditor()
    {
        ActualThemeVariantChanged += (_, _) => { RebuildLayout(); InvalidateVisual(); };
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ContextMenu = new ContextMenu
        {
            ItemsSource = new Control[] { CreateEditingMenuItem("Cut", Key.X), CreateEditingMenuItem("Copy", Key.C), CreateEditingMenuItem("Paste", Key.V), new Separator(), CreateEditingMenuItem("Select all", Key.A) }
        };
        var inputClient = new BlockInputMethodClient(this);
        TextInputMethodClientRequested += (_, e) => e.Client = inputClient;
        SelectionChanged += (_, _) => inputClient.NotifySelection();
        ContentChanged += (_, _) => inputClient.NotifyContent();

        _caretBlinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _caretBlinkTimer.Tick += (_, _) =>
        {
            if (IsFocused)
            {
                _isCaretVisible = !_isCaretVisible;
                InvalidateVisual();
            }
        };
    }

    private MenuItem CreateEditingMenuItem(string label, Key key)
    {
        var item = new MenuItem { Header = label, InputGesture = new KeyGesture(key, KeyModifiers.Control) };
        Loc.Bind(item, MenuItem.HeaderProperty, label);
        item.Click += (_, _) =>
        {
            Focus();
            RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control });
        };
        return item;
    }

    public void LoadBlock(NoteBlock block)
    {
        _block = block;
        _caretIndex = Math.Clamp(_caretIndex, 0, _block.TextLength);
        _selectionLength = 0;
        RebuildLayout();
        InvalidateVisual();
    }

    public void SetCaret(int index)
    {
        var position = Math.Clamp(index, 0, _block.TextLength);
        if (position != _caretIndex || _selectionLength != 0) _typingStyle = null;
        _caretIndex = position;
        _selectionLength = 0;
        _selectionStart = _caretIndex;
        _selectionAnchor = _caretIndex;
        _isCaretVisible = true;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectRange(int start, int length)
    {
        _selectionStart = Math.Clamp(start, 0, _block.TextLength);
        _selectionLength = Math.Clamp(length, 0, _block.TextLength - _selectionStart);
        _caretIndex = _selectionStart + _selectionLength;
        _selectionAnchor = _selectionStart;
        _typingStyle = null;
        _isCaretVisible = true;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll() => SelectRange(0, _block.TextLength);

    public RunStyle GetCurrentSelectionStyle()
    {
        if (_selectionLength == 0 && _typingStyle != null) return _typingStyle;
        if (_block.Runs.Count == 0) return RunStyle.Default;
        var targetIndex = _selectionLength > 0 ? _selectionStart : Math.Max(0, _caretIndex - 1);
        var currentOffset = 0;
        foreach (var run in _block.Runs)
        {
            if (targetIndex >= currentOffset && targetIndex < currentOffset + run.Text.Length)
            {
                return run.Style;
            }
            currentOffset += run.Text.Length;
        }
        return _block.Runs[^1].Style;
    }

    public void ApplyStyleToSelection(Func<RunStyle, RunStyle> updateStyle)
    {
        if (_selectionLength == 0)
        {
            _typingStyle = updateStyle(GetCurrentSelectionStyle());
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var start = _selectionStart;
        var end = _selectionStart + _selectionLength;
        var newRuns = new List<StyledRun>();
        var currentOffset = 0;

        foreach (var run in _block.Runs)
        {
            var runStart = currentOffset;
            var runEnd = currentOffset + run.Text.Length;
            currentOffset = runEnd;

            if (runEnd <= start || runStart >= end)
            {
                newRuns.Add(run);
                continue;
            }

            // Slice before selection
            if (runStart < start)
            {
                var beforeText = run.Text.Substring(0, start - runStart);
                newRuns.Add(new StyledRun(beforeText, run.Style));
            }

            // Slice inside selection
            var insideStart = Math.Max(runStart, start) - runStart;
            var insideEnd = Math.Min(runEnd, end) - runStart;
            var insideText = run.Text.Substring(insideStart, insideEnd - insideStart);
            newRuns.Add(new StyledRun(insideText, updateStyle(run.Style)));

            // Slice after selection
            if (runEnd > end)
            {
                var afterText = run.Text.Substring(end - runStart);
                newRuns.Add(new StyledRun(afterText, run.Style));
            }
        }

        _block.Runs = newRuns;
        _block.Normalize();
        RebuildLayout();
        InvalidateMeasure();
        InvalidateVisual();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearFormattingOnSelection()
    {
        ApplyStyleToSelection(_ => RunStyle.Default);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _isCaretVisible = true;
        _caretBlinkTimer.Start();
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _caretBlinkTimer.Stop();
        _isCaretVisible = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        if (_textLayout == null) return;

        var point = e.GetPosition(this);
        var hit = _textLayout.HitTestPoint(point);
        _pointerDragStartIndex = Math.Clamp(hit.TextPosition, 0, _block.TextLength);
        if (e.ClickCount >= 3) { SelectAll(); e.Handled = true; return; }
        if (e.ClickCount == 2)
        {
            var text = _block.PlainText;
            var start = _pointerDragStartIndex;
            var end = start;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            SelectRange(start, end - start);
            e.Handled = true;
            return;
        }
        MoveCaret(_pointerDragStartIndex, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _isSelectingWithPointer = true;
        e.Pointer.Capture(this);
        SelectionDragStarted?.Invoke(this, e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isSelectingWithPointer || _textLayout == null) return;
        SelectionDragMoved?.Invoke(this, e);
        if (e.Handled) return;

        var point = e.GetPosition(this);
        var hit = _textLayout.HitTestPoint(point);
        MoveCaret(Math.Clamp(hit.TextPosition, 0, _block.TextLength), true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isSelectingWithPointer)
        {
            _isSelectingWithPointer = false;
            e.Pointer.Capture(null);
            SelectionDragEnded?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;

        InsertText(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key is Key.C or Key.X or Key.V)
        {
            _ = HandleClipboardAsync(e.Key);
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Back:
                if (control && !HasSelection && _caretIndex > 0) SelectRange(PreviousPosition(_caretIndex, true), _caretIndex - PreviousPosition(_caretIndex, true));
                HandleBackspace();
                break;
            case Key.Delete:
                if (control && !HasSelection && _caretIndex < _block.TextLength) SelectRange(_caretIndex, NextPosition(_caretIndex, true) - _caretIndex);
                HandleDelete();
                break;
            case Key.Enter:
                if (shift || _block.Kind == BlockKind.Code) InsertText("\n");
                else SplitBlockRequested?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Left:
                if (!shift && !HasSelection && _caretIndex == 0) { NavigateBlockRequested?.Invoke(this, Key.Left); break; }
                MoveCaret(!shift && HasSelection ? _selectionStart : PreviousPosition(_caretIndex, control), shift);
                break;
            case Key.Right:
                if (!shift && !HasSelection && _caretIndex == _block.TextLength) { NavigateBlockRequested?.Invoke(this, Key.Right); break; }
                MoveCaret(!shift && HasSelection ? _selectionStart + _selectionLength : NextPosition(_caretIndex, control), shift);
                break;
            case Key.Up:
            case Key.Down:
                if (_textLayout == null) return;
                var rect = _textLayout.HitTestTextPosition(_caretIndex);
                var y = e.Key == Key.Up ? rect.Top - rect.Height / 2 : rect.Bottom + rect.Height / 2;
                if (y < 0 || y >= _textLayout.Height) NavigateBlockRequested?.Invoke(this, e.Key);
                else MoveCaret(_textLayout.HitTestPoint(new Point(rect.Left, y)).TextPosition, shift);
                break;
            case Key.Home:
            case Key.End:
                if (control || _textLayout == null) MoveCaret(e.Key == Key.Home ? 0 : _block.TextLength, shift);
                else
                {
                    var line = _textLayout.HitTestTextPosition(_caretIndex);
                    MoveCaret(_textLayout.HitTestPoint(new Point(e.Key == Key.Home ? 0 : Bounds.Width, line.Top + line.Height / 2)).TextPosition, shift);
                }
                break;
            case Key.A when control: SelectAll(); break;
            case Key.B when control:
                var bold = (GetCurrentSelectionStyle().FontWeight ?? (_block.Kind == BlockKind.Heading ? 700 : 400)) >= 700;
                ApplyStyleToSelection(style => style with { FontWeight = bold ? 400 : 700 });
                break;
            case Key.I when control: ApplyStyleToSelection(style => style with { Italic = !style.Italic }); break;
            case Key.U when control: ApplyStyleToSelection(style => style with { Underline = !style.Underline }); break;
            default: return;
        }
        e.Handled = true;
    }

    private void MoveCaret(int position, bool extend)
    {
        position = Math.Clamp(position, 0, _block.TextLength);
        if (!extend) { SetCaret(position); return; }
        if (_selectionLength == 0) _selectionAnchor = _caretIndex;
        _caretIndex = position;
        _selectionStart = Math.Min(_selectionAnchor, position);
        _selectionLength = Math.Abs(_selectionAnchor - position);
        _typingStyle = null;
        _isCaretVisible = true;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private int PreviousPosition(int position, bool word = false)
    {
        var text = _block.PlainText;
        if (word)
        {
            while (position > 0 && char.IsWhiteSpace(text[position - 1])) position--;
            while (position > 0 && !char.IsWhiteSpace(text[position - 1])) position--;
            return position;
        }
        var previous = 0;
        foreach (var index in StringInfo.ParseCombiningCharacters(text))
        {
            if (index >= position) break;
            previous = index;
        }
        return previous;
    }

    private int NextPosition(int position, bool word = false)
    {
        var text = _block.PlainText;
        if (word)
        {
            while (position < text.Length && !char.IsWhiteSpace(text[position])) position++;
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            return position;
        }
        foreach (var index in StringInfo.ParseCombiningCharacters(text)) if (index > position) return index;
        return text.Length;
    }

    private async global::System.Threading.Tasks.Task HandleClipboardAsync(Key key)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var block = _block;
        var start = _selectionStart;
        var length = _selectionLength;
        var caret = _caretIndex;
        var originalText = block.PlainText;
        try
        {
            if (key == Key.V)
            {
                var text = await clipboard.TryGetTextAsync();
                if (block == _block && block.PlainText == originalText && TopLevel.GetTopLevel(this) != null && IsFocused && caret == _caretIndex && start == _selectionStart && length == _selectionLength && !string.IsNullOrEmpty(text))
                    InsertText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
            }
            else if (length > 0)
            {
                await clipboard.SetTextAsync(block.PlainText.Substring(start, length));
                if (key == Key.X && block == _block && block.PlainText == originalText && TopLevel.GetTopLevel(this) != null && start == _selectionStart && length == _selectionLength) DeleteSelection();
            }
        }
        catch (Exception ex) { global::System.Console.Error.WriteLine($"[notes] Clipboard failed: {ex.Message}"); }
    }

    internal void InsertText(string text)
    {
        var style = GetCurrentSelectionStyle();
        var start = HasSelection ? _selectionStart : _caretIndex;
        var end = HasSelection ? _selectionStart + _selectionLength : _caretIndex;
        var runs = RichEditorView.SliceRuns(_block.Runs, 0, start);
        runs.Add(new StyledRun(text, style));
        runs.AddRange(RichEditorView.SliceRuns(_block.Runs, end, _block.TextLength - end));
        _block.Runs = runs;
        _caretIndex = start + text.Length;
        _selectionStart = _selectionAnchor = _caretIndex;
        _selectionLength = 0;
        _selectionStart = _selectionAnchor = _caretIndex;
        _block.Normalize();
        RebuildLayout();
        InvalidateMeasure();
        InvalidateVisual();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (_block.Kind == BlockKind.Paragraph && text == " ") CheckMarkdownShortcuts();
    }

    private void CheckMarkdownShortcuts()
    {
        var text = _block.PlainText;
        if (text.StartsWith("# "))
        {
            RemovePrefix(2);
            _block.Level = 1;
            ConvertBlockRequested?.Invoke(this, BlockKind.Heading);
        }
        else if (text.StartsWith("## "))
        {
            RemovePrefix(3);
            _block.Level = 2;
            ConvertBlockRequested?.Invoke(this, BlockKind.Heading);
        }
        else if (text.StartsWith("### "))
        {
            RemovePrefix(4);
            _block.Level = 3;
            ConvertBlockRequested?.Invoke(this, BlockKind.Heading);
        }
        else if (text.StartsWith("- ") || text.StartsWith("* "))
        {
            RemovePrefix(2);
            ConvertBlockRequested?.Invoke(this, BlockKind.BulletedList);
        }
        else if (text.StartsWith("1. "))
        {
            RemovePrefix(3);
            ConvertBlockRequested?.Invoke(this, BlockKind.NumberedList);
        }
        else if (text.StartsWith("[] ") || text.StartsWith("[ ] "))
        {
            RemovePrefix(text.StartsWith("[] ") ? 3 : 4);
            ConvertBlockRequested?.Invoke(this, BlockKind.CheckList);
        }
        else if (text.StartsWith("> "))
        {
            RemovePrefix(2);
            ConvertBlockRequested?.Invoke(this, BlockKind.Quote);
        }
        else if (text.StartsWith("---"))
        {
            ConvertBlockRequested?.Invoke(this, BlockKind.Divider);
        }
    }

    private void RemovePrefix(int length)
    {
        var remaining = length;
        while (_block.Runs.Count > 0 && remaining > 0)
        {
            var run = _block.Runs[0];
            if (run.Text.Length <= remaining)
            {
                remaining -= run.Text.Length;
                _block.Runs.RemoveAt(0);
            }
            else
            {
                run.Text = run.Text.Substring(remaining);
                remaining = 0;
            }
        }
        _caretIndex = Math.Max(0, _caretIndex - length);
    }

    private void HandleBackspace()
    {
        if (_selectionLength > 0)
        {
            DeleteSelection();
            return;
        }

        if (_caretIndex == 0)
        {
            if (_block.Kind != BlockKind.Paragraph)
            {
                ConvertBlockRequested?.Invoke(this, BlockKind.Paragraph);
            }
            else
            {
                MergeBlockRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        SelectRange(PreviousPosition(_caretIndex), _caretIndex - PreviousPosition(_caretIndex));
        DeleteSelection();
    }

    private void HandleDelete()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (_caretIndex >= _block.TextLength) { MergeNextBlockRequested?.Invoke(this, EventArgs.Empty); return; }
        SelectRange(_caretIndex, NextPosition(_caretIndex) - _caretIndex);
        DeleteSelection();
    }

    private void DeleteSelection()
    {
        var start = _selectionStart;
        var end = _selectionStart + _selectionLength;
        var newRuns = new List<StyledRun>();
        var currentOffset = 0;

        foreach (var run in _block.Runs)
        {
            var runStart = currentOffset;
            var runEnd = currentOffset + run.Text.Length;
            currentOffset = runEnd;

            if (runEnd <= start || runStart >= end)
            {
                newRuns.Add(run);
                continue;
            }

            if (runStart < start)
            {
                newRuns.Add(new StyledRun(run.Text.Substring(0, start - runStart), run.Style));
            }
            if (runEnd > end)
            {
                newRuns.Add(new StyledRun(run.Text.Substring(end - runStart), run.Style));
            }
        }

        _block.Runs = newRuns;
        _caretIndex = start;
        _selectionLength = 0;
        _selectionStart = _selectionAnchor = _caretIndex;
        _block.Normalize();
        RebuildLayout();
        InvalidateMeasure();
        InvalidateVisual();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        RebuildLayout(availableSize.Width);
        if (_textLayout == null) return new Size(0, GetDefaultLineHeight());
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : _textLayout.Width, Math.Max(GetDefaultLineHeight(), _textLayout.Height));
    }

    private double GetDefaultLineHeight() => _block.Kind switch
    {
        BlockKind.Heading when _block.Level == 1 => 40,
        BlockKind.Heading when _block.Level == 2 => 32,
        BlockKind.Heading => 26,
        _ => 24
    };

    private void RebuildLayout(double maxWidth = double.PositiveInfinity)
    {
        var defaultFontSize = _block.Kind switch
        {
            BlockKind.Heading when _block.Level == 1 => 28.0,
            BlockKind.Heading when _block.Level == 2 => 22.0,
            BlockKind.Heading => 18.0,
            BlockKind.Quote => 16.0,
            _ => 16.0
        };

        var defaultWeight = _block.Kind == BlockKind.Heading ? FontWeight.Bold : FontWeight.Normal;
        var defaultStyle = FontStyle.Normal;
        var defaultFamily = _block.Kind == BlockKind.Code ? "JetBrains Mono, monospace" : (TryGetResource<FontFamily>("ShellFontFamily")?.ToString() ?? DaisyOS.Shell.Services.Theming.TypographyPolicy.BundledDefaultFamilyName);
        var defaultTypeface = new Typeface(new FontFamily(defaultFamily), defaultStyle, defaultWeight);
        var defaultForeground = TryGetResource<IBrush>("ContentPrimaryBrush") ?? Brushes.White;

        var fullText = _block.PlainText;
        if (string.IsNullOrEmpty(fullText)) fullText = " "; // Maintain height for empty line

        var overrides = new List<ValueSpan<TextRunProperties>>();
        var currentOffset = 0;

        foreach (var run in _block.Runs)
        {
            if (run.Text.Length == 0) continue;

            var runTypeface = new Typeface(
                new FontFamily(run.Style.FontFamily ?? defaultFamily),
                run.Style.Italic ? FontStyle.Italic : defaultStyle,
                run.Style.FontWeight.HasValue ? (FontWeight)run.Style.FontWeight.Value : defaultWeight);

            var fontSize = run.Style.FontSize ?? defaultFontSize;

            var decorations = new TextDecorationCollection();
            if (run.Style.Underline) decorations.Add(TextDecorations.Underline[0]);
            if (run.Style.Strikethrough) decorations.Add(TextDecorations.Strikethrough[0]);

            IBrush runForeground = defaultForeground;
            if (!string.IsNullOrEmpty(run.Style.Foreground))
            {
                if (run.Style.Foreground == "accent") runForeground = TryGetResource<IBrush>("ActionPrimaryBrush") ?? defaultForeground;
                else if (Color.TryParse(run.Style.Foreground, out var color))
                    runForeground = new SolidColorBrush(color);
            }

            IBrush? runHighlight = null;
            if (!string.IsNullOrEmpty(run.Style.Highlight))
            {
                if (Color.TryParse(run.Style.Highlight, out var highlightColor))
                    runHighlight = new SolidColorBrush(highlightColor);
            }

            var baseline = run.Style.Baseline switch
            {
                BaselineShift.Superscript => BaselineAlignment.Superscript,
                BaselineShift.Subscript => BaselineAlignment.Subscript,
                _ => BaselineAlignment.Baseline
            };

            var props = new GenericTextRunProperties(
                runTypeface,
                fontSize,
                decorations.Count > 0 ? decorations : null,
                runForeground,
                runHighlight,
                baseline,
                CultureInfo.CurrentCulture);

            overrides.Add(new ValueSpan<TextRunProperties>(currentOffset, run.Text.Length, props));
            currentOffset += run.Text.Length;
        }

        var width = double.IsPositiveInfinity(maxWidth) ? (Bounds.Width > 0 ? Bounds.Width : 600) : maxWidth;
        _textLayout?.Dispose();
        _textLayout = new TextLayout(
            fullText,
            defaultTypeface,
            defaultFontSize,
            defaultForeground,
            textAlignment: TextAlignment.Left,
            textWrapping: TextWrapping.Wrap,
            maxWidth: width > 0 ? width : 600,
            textStyleOverrides: overrides.Count > 0 ? overrides : null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Empty text still needs a hit-testable writing surface.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_textLayout == null) return;

        // Draw Selection highlight
        if (_selectionLength > 0)
        {
            var selectionBrush = TryGetResource<IBrush>("ActionPrimarySubtleBrush")
                ?? new SolidColorBrush(Color.FromArgb(80, 89, 199, 232));
            foreach (var rect in _textLayout.HitTestTextRange(_selectionStart, _selectionLength))
            {
                context.FillRectangle(selectionBrush, rect);
            }
        }

        // Draw Formatted Text
        _textLayout.Draw(context, new Point(0, 0));

        // Draw Caret
        if (IsFocused && _isCaretVisible && _selectionLength == 0)
        {
            var caretRect = _textLayout.HitTestTextPosition(_caretIndex);
            var caretBrush = TryGetResource<IBrush>("ContentPrimaryBrush") ?? Brushes.Black;
            var pen = new Pen(caretBrush, 1);
            var x = Math.Floor(caretRect.X) + 0.5;
            var top = Math.Floor(caretRect.Y) + 0.5;
            var bottom = Math.Ceiling(caretRect.Bottom) - 0.5;
            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _caretBlinkTimer.Stop();
        _textLayout?.Dispose();
        _textLayout = null;
        base.OnDetachedFromVisualTree(e);
    }

    private sealed class BlockInputMethodClient(RichTextBlockEditor editor) : TextInputMethodClient
    {
        public override Visual TextViewVisual => editor;
        public override bool SupportsPreedit => false;
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => editor.Block.PlainText;
        public override Rect CursorRectangle => editor._textLayout?.HitTestTextPosition(editor.CaretIndex) ?? new Rect(0, 0, 2, 24);
        public override TextSelection Selection
        {
            get => editor.HasSelection ? new TextSelection(editor.SelectionStart, editor.SelectionStart + editor.SelectionLength) : new TextSelection(editor.CaretIndex, editor.CaretIndex);
            set => editor.SelectRange(Math.Min(value.Start, value.End), Math.Abs(value.End - value.Start));
        }
        public void NotifySelection() { RaiseSelectionChanged(); RaiseCursorRectangleChanged(); }
        public void NotifyContent() { RaiseSurroundingTextChanged(); NotifySelection(); }
        public override void ExecuteContextMenuAction(ContextMenuAction action)
        {
            if (action == ContextMenuAction.SelectAll) editor.SelectAll();
            else _ = editor.HandleClipboardAsync(action switch { ContextMenuAction.Copy => Key.C, ContextMenuAction.Cut => Key.X, _ => Key.V });
        }
    }

    private T? TryGetResource<T>(string key) where T : class
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var res) && res is T val)
            return val;
        return null;
    }
}
