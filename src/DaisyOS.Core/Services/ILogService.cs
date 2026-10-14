using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface ILogService
{
    void Log(LogLevel level, string message, Exception? exception = null);
}
