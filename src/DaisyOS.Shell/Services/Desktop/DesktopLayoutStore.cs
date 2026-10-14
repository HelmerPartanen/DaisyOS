using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DaisyOS.Shell.Services.Desktop;

public sealed class DesktopWidgetConfig
{
    public string Id { get; set; } = string.Empty;
    public int Column { get; set; } = -1;
    public int Row { get; set; } = -1;
    public int ColumnSpan { get; set; } = 4;
    public int RowSpan { get; set; } = 2;
    public bool IsRemoved { get; set; }
    public List<string>? ImagePaths { get; set; }
}

public sealed class DesktopLayoutConfig
{
    public bool BottomMediaWidgetVisible { get; set; } = true;
    public List<DesktopWidgetConfig> Widgets { get; set; } = new();
    public Dictionary<string, int> ItemSortOrders { get; set; } = new();
    public Dictionary<string, int> ItemColumns { get; set; } = new();
    public Dictionary<string, int> ItemRows { get; set; } = new();
}

public sealed class DesktopLayoutStore
{
    private readonly string _filePath;

    public DesktopLayoutStore(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultPath();
    }

    public static string GetDefaultPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            configRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(configRoot, "DaisyOS", "desktop_layout.json");
    }

    public DesktopLayoutConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new DesktopLayoutConfig();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.DesktopLayoutConfig) ?? new DesktopLayoutConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load desktop layout config: {ex.Message}");
            return new DesktopLayoutConfig();
        }
    }

    public void Save(DesktopLayoutConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.DesktopLayoutConfig);
            global::DaisyOS.Core.AtomicTextFile.Write(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save desktop layout config: {ex.Message}");
        }
    }
}
