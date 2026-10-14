using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public sealed class NullLogService : ILogService
{
    public static NullLogService Instance { get; } = new();

    private NullLogService()
    {
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }
}
