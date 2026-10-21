namespace DaisyOS.Shell.Services.Media;

internal static class MediaPlaybackTime
{
    internal static string Format(double seconds, bool negative = false)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var totalSeconds = (int)Math.Round(seconds);
        var minutes = totalSeconds / 60;
        var secs = totalSeconds % 60;
        return negative ? $"- {minutes}.{secs:D2}" : $"{minutes}.{secs:D2}";
    }

}
