namespace DaisyOS.Shell.Apps.Notes.Documents;

public enum DocumentKind
{
    /// <summary>A DaisyOS rich note package (.dnote).</summary>
    RichNote,
    /// <summary>Prose text: shown with the interface font and soft wrapping.</summary>
    PlainText,
    /// <summary>Markdown source: prose font, Markdown highlighting, optional preview.</summary>
    Markdown,
    /// <summary>Source code or structured data: monospace, line numbers, highlighting.</summary>
    Code,
    /// <summary>Not text. Never decoded or saved by Notes.</summary>
    Binary
}

/// <summary>The detected kind of a document and the language used for highlighting.</summary>
public sealed record DocumentFormat(DocumentKind Kind, string LanguageId, string DisplayName)
{
    public bool IsText => Kind is DocumentKind.PlainText or DocumentKind.Markdown or DocumentKind.Code;
    public bool UsesMonospace => Kind == DocumentKind.Code;
}

public static class FormatDetector
{
    public const string RichNoteExtension = ".dnote";

    public static readonly DocumentFormat RichNote = new(DocumentKind.RichNote, "dnote", "Note");
    public static readonly DocumentFormat PlainText = new(DocumentKind.PlainText, "plaintext", "Plain Text");
    public static readonly DocumentFormat Markdown = new(DocumentKind.Markdown, "markdown", "Markdown");
    public static readonly DocumentFormat Binary = new(DocumentKind.Binary, "binary", "Binary");

    // Extension -> (TextMate-compatible language id, display name).
    private static readonly Dictionary<string, (string Id, string Name)> CodeLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = ("csharp", "C#"), [".csx"] = ("csharp", "C#"),
        [".fs"] = ("fsharp", "F#"), [".fsx"] = ("fsharp", "F#"),
        [".vb"] = ("vb", "Visual Basic"),
        [".c"] = ("c", "C"), [".h"] = ("c", "C"),
        [".cpp"] = ("cpp", "C++"), [".cc"] = ("cpp", "C++"), [".cxx"] = ("cpp", "C++"),
        [".hpp"] = ("cpp", "C++"), [".hh"] = ("cpp", "C++"), [".hxx"] = ("cpp", "C++"),
        [".m"] = ("objective-c", "Objective-C"), [".mm"] = ("objective-cpp", "Objective-C++"),
        [".rs"] = ("rust", "Rust"), [".go"] = ("go", "Go"), [".zig"] = ("zig", "Zig"),
        [".java"] = ("java", "Java"), [".kt"] = ("kotlin", "Kotlin"), [".kts"] = ("kotlin", "Kotlin"),
        [".scala"] = ("scala", "Scala"), [".groovy"] = ("groovy", "Groovy"), [".gradle"] = ("groovy", "Gradle"),
        [".swift"] = ("swift", "Swift"), [".dart"] = ("dart", "Dart"),
        [".py"] = ("python", "Python"), [".pyw"] = ("python", "Python"), [".pyi"] = ("python", "Python"),
        [".rb"] = ("ruby", "Ruby"), [".php"] = ("php", "PHP"), [".pl"] = ("perl", "Perl"), [".pm"] = ("perl", "Perl"),
        [".lua"] = ("lua", "Lua"), [".r"] = ("r", "R"), [".jl"] = ("julia", "Julia"),
        [".js"] = ("javascript", "JavaScript"), [".mjs"] = ("javascript", "JavaScript"), [".cjs"] = ("javascript", "JavaScript"),
        [".jsx"] = ("javascriptreact", "JavaScript React"),
        [".ts"] = ("typescript", "TypeScript"), [".mts"] = ("typescript", "TypeScript"), [".cts"] = ("typescript", "TypeScript"),
        [".tsx"] = ("typescriptreact", "TypeScript React"),
        [".html"] = ("html", "HTML"), [".htm"] = ("html", "HTML"), [".xhtml"] = ("html", "HTML"),
        [".vue"] = ("vue", "Vue"), [".svelte"] = ("svelte", "Svelte"),
        [".css"] = ("css", "CSS"), [".scss"] = ("scss", "SCSS"), [".sass"] = ("sass", "Sass"), [".less"] = ("less", "Less"),
        [".json"] = ("json", "JSON"), [".jsonc"] = ("jsonc", "JSON with Comments"), [".json5"] = ("jsonc", "JSON5"),
        [".xml"] = ("xml", "XML"), [".xaml"] = ("xml", "XAML"), [".axaml"] = ("xml", "XAML"), [".svg"] = ("xml", "SVG"),
        [".csproj"] = ("xml", "MSBuild"), [".props"] = ("xml", "MSBuild"), [".targets"] = ("xml", "MSBuild"),
        [".plist"] = ("xml", "Property List"),
        [".yaml"] = ("yaml", "YAML"), [".yml"] = ("yaml", "YAML"), [".toml"] = ("toml", "TOML"),
        [".ini"] = ("ini", "INI"), [".cfg"] = ("ini", "Config"), [".conf"] = ("ini", "Config"),
        [".desktop"] = ("ini", "Desktop Entry"), [".service"] = ("ini", "systemd Unit"), [".editorconfig"] = ("ini", "EditorConfig"),
        [".sh"] = ("shellscript", "Shell Script"), [".bash"] = ("shellscript", "Bash"), [".zsh"] = ("shellscript", "Zsh"),
        [".fish"] = ("fish", "Fish"), [".ps1"] = ("powershell", "PowerShell"), [".psm1"] = ("powershell", "PowerShell"),
        [".bat"] = ("bat", "Batch"), [".cmd"] = ("bat", "Batch"),
        [".sql"] = ("sql", "SQL"), [".graphql"] = ("graphql", "GraphQL"), [".gql"] = ("graphql", "GraphQL"),
        [".proto"] = ("proto", "Protocol Buffers"),
        [".hs"] = ("haskell", "Haskell"), [".ml"] = ("ocaml", "OCaml"), [".ex"] = ("elixir", "Elixir"), [".exs"] = ("elixir", "Elixir"),
        [".erl"] = ("erlang", "Erlang"), [".clj"] = ("clojure", "Clojure"), [".lisp"] = ("lisp", "Lisp"), [".el"] = ("lisp", "Emacs Lisp"),
        [".tex"] = ("latex", "LaTeX"), [".bib"] = ("bibtex", "BibTeX"),
        [".cmake"] = ("cmake", "CMake"), [".mk"] = ("makefile", "Makefile"),
        [".diff"] = ("diff", "Diff"), [".patch"] = ("diff", "Diff"),
        [".glsl"] = ("glsl", "GLSL"), [".vert"] = ("glsl", "GLSL"), [".frag"] = ("glsl", "GLSL"), [".hlsl"] = ("hlsl", "HLSL"),
        [".csv"] = ("csv", "CSV"), [".tsv"] = ("csv", "TSV"),
        [".dockerfile"] = ("dockerfile", "Dockerfile"),
        [".gitignore"] = ("ignore", "Ignore File"), [".dockerignore"] = ("ignore", "Ignore File"),
        [".env"] = ("dotenv", "Environment"),
        [".sln"] = ("plaintext", "Solution"),
    };

    private static readonly Dictionary<string, (string Id, string Name)> WellKnownFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Makefile"] = ("makefile", "Makefile"), ["GNUmakefile"] = ("makefile", "Makefile"),
        ["Dockerfile"] = ("dockerfile", "Dockerfile"), ["Containerfile"] = ("dockerfile", "Containerfile"),
        ["CMakeLists.txt"] = ("cmake", "CMake"), ["PKGBUILD"] = ("shellscript", "PKGBUILD"),
        [".bashrc"] = ("shellscript", "Bash"), [".bash_profile"] = ("shellscript", "Bash"), [".profile"] = ("shellscript", "Shell Script"),
        [".zshrc"] = ("shellscript", "Zsh"), [".gitconfig"] = ("ini", "Git Config"), [".gitattributes"] = ("ignore", "Git Attributes"),
        ["Gemfile"] = ("ruby", "Ruby"), ["Rakefile"] = ("ruby", "Ruby"), ["Jenkinsfile"] = ("groovy", "Groovy"),
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".mdx"
    };

    private static readonly HashSet<string> ProseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".log", ".rst", ".adoc", ".org", ".nfo", ".srt", ".vtt", ".asc"
    };

    /// <summary>Classifies a file from its name alone; content is checked by <see cref="Detect(string, ReadOnlySpan{byte})"/>.</summary>
    public static DocumentFormat FromPath(string path)
    {
        var fileName = Path.GetFileName(path);
        if (WellKnownFileNames.TryGetValue(fileName, out var known))
            return new DocumentFormat(DocumentKind.Code, known.Id, known.Name);

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) && fileName.StartsWith('.'))
            extension = fileName; // ".gitignore" style names
        if (extension.Equals(RichNoteExtension, StringComparison.OrdinalIgnoreCase)) return RichNote;
        if (MarkdownExtensions.Contains(extension)) return Markdown;
        if (ProseExtensions.Contains(extension)) return PlainText;
        if (CodeLanguages.TryGetValue(extension, out var language))
            return new DocumentFormat(DocumentKind.Code, language.Id, language.Name);
        return PlainText;
    }

    /// <summary>Classifies a file using its name and leading bytes (shebangs, binary sniffing).</summary>
    public static DocumentFormat Detect(string path, ReadOnlySpan<byte> leadingBytes)
    {
        var byName = FromPath(path);
        if (byName.Kind == DocumentKind.RichNote) return byName;
        if (TextFileCodec.IsProbablyBinary(leadingBytes)) return Binary;
        if (byName.Kind == DocumentKind.PlainText && string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            var shebang = DetectShebang(leadingBytes);
            if (shebang is not null) return shebang;
        }
        return byName;
    }

    /// <summary>All languages known to the code editor, for the language picker.</summary>
    public static IReadOnlyList<DocumentFormat> KnownLanguages { get; } = CodeLanguages.Values
        .Concat(WellKnownFileNames.Values)
        .DistinctBy(language => language.Id)
        .OrderBy(language => language.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(language => new DocumentFormat(DocumentKind.Code, language.Id, language.Name))
        .Prepend(Markdown)
        .Prepend(PlainText)
        .ToArray();

    private static DocumentFormat? DetectShebang(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 3 || bytes[0] != (byte)'#' || bytes[1] != (byte)'!') return null;
        var end = bytes.IndexOf((byte)'\n');
        var line = global::System.Text.Encoding.ASCII.GetString(end < 0 ? bytes[..Math.Min(bytes.Length, 128)] : bytes[..end]);
        (string Id, string Name)? language =
            line.Contains("python") ? ("python", "Python") :
            line.Contains("node") || line.Contains("deno") || line.Contains("bun") ? ("javascript", "JavaScript") :
            line.Contains("ruby") ? ("ruby", "Ruby") :
            line.Contains("perl") ? ("perl", "Perl") :
            line.Contains("php") ? ("php", "PHP") :
            line.Contains("lua") ? ("lua", "Lua") :
            line.Contains("pwsh") ? ("powershell", "PowerShell") :
            line.Contains("fish") ? ("fish", "Fish") :
            line.Contains("sh") ? ("shellscript", "Shell Script") : null;
        return language is { } found ? new DocumentFormat(DocumentKind.Code, found.Id, found.Name) : null;
    }
}
