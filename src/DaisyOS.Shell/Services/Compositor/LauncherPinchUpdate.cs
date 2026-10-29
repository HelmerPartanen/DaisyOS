using System.Text.Json;

namespace DaisyOS.Shell.Services.Compositor;

public sealed record LauncherPinchUpdate(bool Opening, double Progress, bool Finished, bool Commit)
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
            update = new(root.GetProperty("opening").GetBoolean(), Math.Clamp(progress, 0, 1),
                root.GetProperty("finished").GetBoolean(), root.GetProperty("commit").GetBoolean());
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }
}
