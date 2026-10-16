namespace DaisyOS.Shell.Services.Motion;

/// <summary>Exact critically damped spring step; independent of refresh rate and safe after a missed frame.</summary>
internal static class DampedSpring
{
    internal static (double Position, double Velocity) Step(double position, double velocity, double target, double frequency, double seconds)
    {
        var displacement = position - target;
        var coefficient = velocity + frequency * displacement;
        var decay = Math.Exp(-frequency * seconds);
        return (target + (displacement + coefficient * seconds) * decay,
            (velocity - frequency * coefficient * seconds) * decay);
    }
}
