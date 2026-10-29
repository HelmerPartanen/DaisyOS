namespace DaisyOS.Shell.Services;

public static class LauncherSwipePhysics
{
    // Offset is in logical pixels and velocity in logical pixels per millisecond.
    public static bool ShouldCommit(double offset, double velocity, double width)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(velocity) || !double.IsFinite(width) || width <= 0)
            return false;
        var threshold = Math.Clamp(width * 0.18, 96, 180);
        // A deliberate reversal wins over distance already travelled.
        if (Math.Sign(offset) != Math.Sign(velocity) && Math.Abs(velocity) > 0.35) return false;
        return Math.Abs(offset) >= threshold ||
            Math.Abs(offset) >= 24 && Math.Abs(velocity) >= 0.45 &&
            Math.Sign(offset) == Math.Sign(velocity) && Math.Abs(offset + velocity * 160) >= threshold;
    }
}
