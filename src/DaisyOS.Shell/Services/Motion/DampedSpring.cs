namespace DaisyOS.Shell.Services.Motion;

/// <summary>Exact damped spring step, independent of refresh rate.</summary>
internal static class DampedSpring
{
    internal static (double Position, double Velocity) Step(double position, double velocity, double target, double frequency, double seconds, double dampingRatio = 1)
    {
        var displacement = position - target;
        if (dampingRatio < 1)
        {
            var attenuation = dampingRatio * frequency;
            var angular = frequency * Math.Sqrt(1 - dampingRatio * dampingRatio);
            var sine = Math.Sin(angular * seconds);
            var cosine = Math.Cos(angular * seconds);
            var envelope = Math.Exp(-attenuation * seconds);
            var oscillation = (velocity + attenuation * displacement) / angular;
            return (target + envelope * (displacement * cosine + oscillation * sine),
                envelope * (velocity * cosine - (attenuation * velocity + frequency * frequency * displacement) / angular * sine));
        }
        var coefficient = velocity + frequency * displacement;
        var decay = Math.Exp(-frequency * seconds);
        return (target + (displacement + coefficient * seconds) * decay,
            (velocity - frequency * coefficient * seconds) * decay);
    }
}
