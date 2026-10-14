using System.Text.Json;
using DaisyOS.Shell.Services.Serialization;

namespace DaisyOS.Shell.Services.Compositor;

// Only the UI thread publishes; the session-bus thread reads an immutable snapshot.
internal static class TaskbarPreviewState
{
    private static string _snapshot = "{}";
    private static long _lastRead;
    private static int _showing;
    internal static bool IsAvailable => Interlocked.Read(ref _lastRead) > 0 && Environment.TickCount64 - Interlocked.Read(ref _lastRead) < 1500;
    internal static bool IsShowing => IsAvailable && Volatile.Read(ref _showing) != 0;
    internal static string Read(bool showing)
    {
        Volatile.Write(ref _showing, showing ? 1 : 0);
        Interlocked.Exchange(ref _lastRead, Environment.TickCount64);
        return Volatile.Read(ref _snapshot);
    }
    internal static void Show(TaskbarPreviewRequest request) =>
        Volatile.Write(ref _snapshot, JsonSerializer.Serialize(request, DaisyShellJsonContext.Default.TaskbarPreviewRequest));
    internal static void Hide() => Volatile.Write(ref _snapshot, "{}");
    internal static void Dismiss() => Volatile.Write(ref _snapshot, "{\"dismiss\":true}");
}

internal sealed record TaskbarPreviewRequest(string Token, string[] WindowIds, double AnchorX, double AnchorY,
    double AnchorWidth, double AnchorHeight, string Surface, string Raised, string Foreground,
    string Stroke, string Hover, string Danger, bool ReduceMotion, double PanelHeight = 0, string OnAction = "#ffffffff");
