using System.Globalization;
using System.Text;

namespace DaisyOS.Shell.Views.Components.Taskbar;

public static class TaskbarAppLabel
{
    public static string Format(string appId, string label)
    {
        var id = appId.Replace("org.daisyos.", "", StringComparison.OrdinalIgnoreCase).Replace(".desktop", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        var native = id switch
        {
            "taskmanager" or "task-manager" or "systemmonitor" or "system-monitor" or "diagnostics" => "System Monitor",
            "appstore" => "App Store",
            "diskmanager" => "Disk Manager",
            "files" => "Files", "settings" => "Settings", "notes" => "Notes",
            "calendar" => "Calendar", "calculator" => "Calculator", _ => null
        };
        if (native is not null) return native;
        // Uppercase word initials without destroying intentional casing such as LibreOffice.
        var result = new StringBuilder(label.Length);
        var initial = true;
        foreach (var c in label.Trim())
        {
            result.Append(initial ? char.ToUpper(c, CultureInfo.CurrentCulture) : c);
            initial = char.IsWhiteSpace(c);
        }
        return result.ToString();
    }
}
