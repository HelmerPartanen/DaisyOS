namespace DaisyOS.Shell.Apps.Files.Picker;

public sealed record DaisyFilePickerFilter(
    string Name,
    IReadOnlyList<string> Extensions);

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
    string? SuggestedName = null,
    string? AcceptLabel = null,
    IReadOnlyList<DaisyFilePickerFilter>? Filters = null,
    int SelectedFilterIndex = 0);
