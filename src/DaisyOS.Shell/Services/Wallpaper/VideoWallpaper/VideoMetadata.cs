using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

public sealed record VideoMetadata(int Width, int Height, double Fps, double Duration,
    string Primaries, string Transfer, string Matrix, string Range, int Rotation,
    int PeakNits, int MaxCll, int MaxFall, int MinimumLuminance, int[] MasteringPrimaries)
{
    public bool IsHdr => Transfer is "smpte2084" or "arib-std-b67";

    public static async Task<VideoMetadata> ProbeAsync(string path, CancellationToken token = default)
    {
        var json = await VideoProcess.RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0",
            "-read_intervals", "%+#1", "-show_streams", "-show_frames", "-of", "json", Path.GetFullPath(path)], token);
        return Parse(json);
    }

    public static VideoMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var stream = document.RootElement.GetProperty("streams")[0];
        var width = Number(stream, "width");
        var height = Number(stream, "height");
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("Unsupported video dimensions.");
        var fps = Rational(Text(stream, "avg_frame_rate"));
        if (fps <= 0) fps = Rational(Text(stream, "r_frame_rate"));
        if (fps <= 0 || !double.IsFinite(fps)) fps = 30;
        var duration = Rational(Text(stream, "duration"));
        var rotation = 0;
        var peak = 1000;
        var cll = 0;
        var fall = 0;
        var minimum = 0;
        var primaries = new int[8];
        void ReadSideData(JsonElement owner)
        {
            if (!owner.TryGetProperty("side_data_list", out var sides)) return;
            foreach (var side in sides.EnumerateArray())
            {
                if (side.TryGetProperty("rotation", out var r)) rotation = r.GetInt32();
                if (Text(side, "side_data_type") == "Mastering display metadata")
                {
                    peak = Math.Clamp((int)Math.Round(Rational(Text(side, "max_luminance"))), 1, 10000);
                    minimum = (int)Math.Round(Rational(Text(side, "min_luminance")) * 10000);
                    var names = new[] { "red_x", "red_y", "green_x", "green_y", "blue_x", "blue_y", "white_point_x", "white_point_y" };
                    for (var i = 0; i < names.Length; i++) primaries[i] = (int)Math.Round(Rational(Text(side, names[i])) * 1_000_000);
                }
                if (Text(side, "side_data_type") == "Content light level metadata")
                {
                    cll = Number(side, "max_content");
                    fall = Number(side, "max_average");
                }
            }
        }
        ReadSideData(stream);
        if (document.RootElement.TryGetProperty("frames", out var frames) && frames.GetArrayLength() > 0)
            ReadSideData(frames[0]);
        return new(width, height, fps, duration, Text(stream, "color_primaries"), Text(stream, "color_transfer"),
            Text(stream, "color_space"), Text(stream, "color_range"), rotation, peak, cll, fall, minimum, primaries);
    }

    internal static string Text(JsonElement owner, string name) => owner.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";
    private static int Number(JsonElement owner, string name) => int.TryParse(Text(owner, name), out var value) ? value : 0;
    internal static double Rational(string text)
    {
        var parts = text.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        if (parts.Length == 1) return value;
        return double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator != 0
            ? value / denominator : 0;
    }
}

internal static class VideoProcess
{
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Cannot start {executable}.");
        using var registration = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var result = await output;
        var errors = await error;
        if (process.ExitCode != 0) throw new IOException($"{executable} failed: {errors[..Math.Min(errors.Length, 1500)]}");
        return result;
    }
}
