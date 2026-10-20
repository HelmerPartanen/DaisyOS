using System;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using DaisyOS.Shell.Apps.Notes.Documents;
using TextMateSharp.Grammars;

namespace DaisyOS.Shell.Apps.Notes.Editor.Code;

public partial class CodeEditorControl : UserControl
{
    private RegistryOptions? _registryOptions;
    private TextMate.Installation? _textMateInstallation;
    private TextFileContent? _fileContent;
    private DocumentFormat _format = FormatDetector.PlainText;
    private bool _isUpdatingTextInternally;

    public static readonly StyledProperty<bool> IsBinaryProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(IsBinary));

    public static readonly StyledProperty<bool> IsMarkdownPreviewActiveProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(IsMarkdownPreviewActive));

    public bool IsBinary
    {
        get => GetValue(IsBinaryProperty);
        set => SetValue(IsBinaryProperty, value);
    }

    public bool IsMarkdownPreviewActive
    {
        get => GetValue(IsMarkdownPreviewActiveProperty);
        set => SetValue(IsMarkdownPreviewActiveProperty, value);
    }

    public TextFileContent? FileContent => _fileContent;
    public DocumentFormat Format => _format;

    public event EventHandler? TextContentChanged;

    public CodeEditorControl()
    {
        InitializeComponent();
        Loc.RefreshOnChange(this, UpdateStatusBar);

        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = true;
        Editor.Options.ShowBoxForControlCharacters = true;

        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged += OnTextChanged;

        InitializeTextMate();
        ActualThemeVariantChanged += (_, _) => UpdateEditorTheme();
    }

    private void InitializeTextMate()
    {
        try
        {
            var isDark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
            _registryOptions = new RegistryOptions(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            _textMateInstallation = Editor.InstallTextMate(_registryOptions);
            _textMateInstallation.AppliedTheme += (_, _) => ApplyEditorSurface();
            ApplyEditorSurface();
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[CodeEditor] Failed to initialize TextMate: {ex.Message}");
        }
    }

    private void ApplyEditorSurface()
    {
        Editor.Background = Brushes.Transparent;
        Editor.Bind(TextEditor.ForegroundProperty, Editor.GetResourceObservable("ContentPrimaryBrush"));
    }

    private void UpdateEditorTheme()
    {
        if (_textMateInstallation == null || _registryOptions == null) return;
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus));
        ApplyEditorSurface();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textMateInstallation?.Dispose();
        _textMateInstallation = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_textMateInstallation == null) InitializeTextMate();
        UpdateEditorTheme();
        ConfigureForFormat(_format);
    }

    public void OpenFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var format = FormatDetector.Detect(filePath, bytes);

            if (format.Kind == DocumentKind.Binary)
            {
                IsBinary = true;
                _fileContent = null;
                _format = format;
                return;
            }

            IsBinary = false;
            _format = format;
            _fileContent = TextFileCodec.Decode(bytes);

            SetTextInternal(_fileContent.Text);
            ConfigureForFormat(format);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"[CodeEditor] Failed to open {filePath}: {ex.Message}");
        }
    }

    public void LoadText(string text, DocumentFormat format, TextFileContent? fileContent = null)
    {
        IsBinary = format.Kind == DocumentKind.Binary;
        IsMarkdownPreviewActive = false;
        EditorLayout.ColumnDefinitions = new ColumnDefinitions("*,Auto,0");
        _format = format;
        _fileContent = fileContent;
        SetTextInternal(text);
        ConfigureForFormat(format);
        UpdateStatusBar();
    }

    public void FocusEditor() => Editor.TextArea.Focus();

    public string GetText() => Editor.Text ?? string.Empty;

    public void ConfigureForFormat(DocumentFormat format)
    {
        _format = format;
        Editor.ShowLineNumbers = format.UsesMonospace;
        Editor.Options.HighlightCurrentLine = format.UsesMonospace;
        Editor.Options.IndentationSize = _fileContent?.IndentSize > 0 ? _fileContent.IndentSize : 4;
        Editor.Options.ConvertTabsToSpaces = _fileContent?.IndentStyle != IndentStyle.Tabs;
        Editor.TextArea.IndentationStrategy = new AvaloniaEdit.Indentation.DefaultIndentationStrategy();
        Editor.WordWrap = !format.UsesMonospace;

        if (format.UsesMonospace)
        {
            Editor.ClearValue(FontFamilyProperty);
            Editor.FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas, monospace");
        }
        else
        {
            Editor.Bind(FontFamilyProperty, this.GetResourceObservable("ShellFontFamily"));
        }

        if (_textMateInstallation != null && _registryOptions != null)
        {
            try
            {
                var scope = _registryOptions.GetScopeByLanguageId(format.LanguageId);
                _textMateInstallation.SetGrammar(scope);
            }
            catch { }
        }

        UpdateStatusBar();
        UpdateMarkdownPreview();
    }

    public void ToggleLineNumbers() => Editor.ShowLineNumbers = !Editor.ShowLineNumbers;

    public void ToggleWordWrap() => Editor.WordWrap = !Editor.WordWrap;

    public void ToggleMarkdownPreview()
    {
        if (_format.Kind != DocumentKind.Markdown) return;
        IsMarkdownPreviewActive = !IsMarkdownPreviewActive;
        EditorLayout.ColumnDefinitions = new ColumnDefinitions(IsMarkdownPreviewActive ? "*,Auto,*" : "*,Auto,0");
        if (IsMarkdownPreviewActive) UpdateMarkdownPreview();
    }

    public void SetFontSize(double size)
    {
        if (size >= 8 && size <= 72)
        {
            Editor.FontSize = size;
        }
    }

    private void SetTextInternal(string text)
    {
        _isUpdatingTextInternally = true;
        try
        {
            Editor.Text = text;
        }
        finally
        {
            _isUpdatingTextInternally = false;
        }
        UpdateMarkdownPreview();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingTextInternally) return;

        UpdateStatusBar();
        UpdateMarkdownPreview();
        TextContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        var line = Editor.TextArea.Caret.Line;
        var col = Editor.TextArea.Caret.Column;
        var selection = Editor.SelectionLength;

        CursorPositionText.Text = selection > 0
            ? Loc.Format("Ln {0}, Col {1} ({2} selected)", line, col, selection)
            : Loc.Format("Ln {0}, Col {1}", line, col);
    }

    private void UpdateStatusBar()
    {
        var text = Editor.Text ?? string.Empty;
        var words = CountWords(text);
        WordCountText.Text = words == 1 ? Loc.Get("1 word") : Loc.Format("{0:N0} words", words);

        LanguageText.Text = _format.DisplayName;
        EncodingText.Text = _fileContent?.EncodingDisplayName ?? "UTF-8";
        LineEndingText.Text = _fileContent?.LineEndingDisplayName ?? "LF";
    }

    private void UpdateMarkdownPreview()
    {
        if (!IsMarkdownPreviewActive) return;
        MarkdownPreviewText.Text = Editor.Text ?? string.Empty;
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }
}
