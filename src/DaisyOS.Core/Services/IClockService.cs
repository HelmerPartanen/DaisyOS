namespace DaisyOS.Core.Services;

public interface IClockService
{
    DateTimeOffset Now { get; }
}

