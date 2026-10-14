using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DaisyOS.Shell.Apps.Calendar;

internal sealed class CalendarEventStore
{
    private readonly string _path;

    public CalendarEventStore()
    {
        var configRoot = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configRoot))
        {
            configRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        _path = Path.Combine(configRoot, "DaisyOS", "calendar-events.json");
    }

    public IReadOnlyList<CalendarEventRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<CalendarEventRecord>();
            return JsonSerializer.Deserialize(File.ReadAllText(_path), DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.ListCalendarEventRecord)
                   ?? new List<CalendarEventRecord>();
        }
        catch (JsonException) { return Array.Empty<CalendarEventRecord>(); }
        catch (IOException) { return Array.Empty<CalendarEventRecord>(); }
    }

    public void Save(IReadOnlyCollection<CalendarEventRecord> events)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var list = events is List<CalendarEventRecord> l ? l : new List<CalendarEventRecord>(events);
        global::DaisyOS.Core.AtomicTextFile.Write(_path, JsonSerializer.Serialize(list, DaisyOS.Shell.Services.Serialization.DaisyShellJsonContext.Default.ListCalendarEventRecord));
    }
}

public sealed record CalendarEventRecord(Guid Id, string Title, DateTime StartTime, DateTime EndTime);
