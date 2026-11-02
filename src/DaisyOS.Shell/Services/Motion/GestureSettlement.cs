namespace DaisyOS.Shell.Services.Motion;

/// <summary>Finite gesture settlement with a release tangent and zero endpoint velocity.</summary>
public static class GestureSettlement
{
    // Velocity is expressed in the same units as position, per millisecond.
    public static double Duration(double distance, double velocity, double minimum = 80, double maximum = 220)
    {
        if (distance == 0) return 0;
        if (!double.IsFinite(distance) || !double.IsFinite(velocity)) return maximum;
        return Math.Clamp(Math.Abs(distance) / Math.Max(Math.Abs(velocity), Math.Abs(distance) / maximum), minimum, maximum);
    }

    public static double Position(double from, double to, double velocity, double elapsedMs, double durationMs)
    {
        if (durationMs <= 0 || elapsedMs >= durationMs) return to;
        if (elapsedMs <= 0 || from == to) return from;
        var t = Math.Clamp(elapsedMs / durationMs, 0, 1);
        var distance = to - from;
        // Limit extreme sensor samples. Normal release velocity is preserved;
        // an opposing tangent may move briefly away before returning.
        var tangent = distance * Math.Clamp(velocity * durationMs / distance, -1, 3);
        return from + distance * t * t * (3 - 2 * t) + tangent * t * (1 - t) * (1 - t);
    }
}
