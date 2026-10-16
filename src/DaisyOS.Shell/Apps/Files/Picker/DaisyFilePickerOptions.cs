namespace DaisyOS.Shell.Apps.Files.Picker;

public sealed record DaisyFilePickerOptions(
    string Title,
    bool AllowMultiple,
    bool AllowFolderContents,
    IReadOnlyCollection<string> AllowedExtensions,
    IReadOnlyList<string>? InitialSelection = null,
    string? InitialFolder = null,
    string ItemNameSingular = "file",
    string ItemNamePlural = "files",
    bool SelectDirectory = false,
    bool SaveFile = false,
    string? SuggestedName = null);
