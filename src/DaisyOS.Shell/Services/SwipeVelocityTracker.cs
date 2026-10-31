namespace DaisyOS.Shell.Services;

/// <summary>Recent finger velocity, independent of dispatcher batching and event frequency.</summary>
internal sealed class SwipeVelocityTracker
{
    private readonly (long Time, double Position)[] _samples = new (long, double)[24];
    private int _start;
    private int _count;
    private double _position;
    private double _lastDelta;
    public long LastMotionTime { get; private set; }

    public void Add(double delta, long time)
    {
        if (!double.IsFinite(delta) || delta == 0) return;
        if (_count == 0 || time < LastMotionTime || time - LastMotionTime > 100)
        {
            _count = 1;
            _start = 0;
            _samples[0] = (time - 16, _position);
        }
        else if (Math.Sign(delta) != Math.Sign(_lastDelta) && Math.Abs(delta) >= 2)
        {
            // A deliberate reversal must not inherit the outward flick's momentum.
            _count = 1;
            _start = 0;
            _samples[0] = (LastMotionTime, _position);
        }
        _position += delta;
        _lastDelta = delta;
        LastMotionTime = time;
        var last = (_start + _count - 1) % _samples.Length;
        if (_count > 1 && _samples[last].Time == time)
            _samples[last] = (time, _position);
        else
        {
            if (_count == _samples.Length) { _start = (_start + 1) % _samples.Length; _count--; }
            _samples[(_start + _count++) % _samples.Length] = (time, _position);
        }
        while (_count > 2 && time - _samples[_start].Time > 80)
        {
            _start = (_start + 1) % _samples.Length;
            _count--;
        }
    }

    public double VelocityAt(long time) => _count == 0 || time - LastMotionTime > 100 || time < LastMotionTime
        ? 0 : (_position - _samples[_start].Position) / Math.Max(8, LastMotionTime - _samples[_start].Time);

    public void Reset()
    {
        _start = _count = 0;
        _position = _lastDelta = 0;
        LastMotionTime = 0;
    }
}
