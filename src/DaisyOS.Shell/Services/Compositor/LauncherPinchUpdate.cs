using System.Text.Json;

namespace DaisyOS.Shell.Services.Compositor;

public sealed record LauncherPinchUpdate(bool Opening, double Progress, bool Finished, bool Commit, double Velocity = 0)
{
    public static bool TryParse(string payload, out LauncherPinchUpdate? update)
    {
        update = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var progress = root.GetProperty("progress").GetDouble();
            if (!double.IsFinite(progress)) return false;
            var velocity = root.TryGetProperty("velocity", out var value) ? value.GetDouble() : 0;
            if (!double.IsFinite(velocity)) return false;
            update = new(root.GetProperty("opening").GetBoolean(), Math.Clamp(progress, 0, 1),
                root.GetProperty("finished").GetBoolean(), root.GetProperty("commit").GetBoolean(), velocity);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }
}
