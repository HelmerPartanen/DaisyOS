using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Processes;

public sealed class LinuxFileAssociationService : IFileAssociationService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    private const int MaxHandlers = 100;

    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<CommandResult>> _runCommand;
    private readonly Func<string, IReadOnlyList<string>, AppLaunchResult> _launchProcess;
    private readonly IReadOnlyList<string> _applicationDirectories;
    private readonly ILogService _log;

    public LinuxFileAssociationService(ICommandRunner runner, ILogService? log = null)
        : this(
            runner.RunAsync,
            (fileName, arguments) => new SafeProcessLauncher(log).Launch(fileName, arguments),
            GetApplicationDirectories().ToArray(),
            log)
    {
    }

    internal LinuxFileAssociationService(
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<CommandResult>> runCommand,
        Func<string, IReadOnlyList<string>, AppLaunchResult> launchProcess,
        IReadOnlyList<string> applicationDirectories,
        ILogService? log = null)
    {
        _runCommand = runCommand;
        _launchProcess = launchProcess;
        _applicationDirectories = applicationDirectories;
        _log = log ?? NullLogService.Instance;
    }

    public async Task<FileHandlerQueryResult> GetHandlersAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeFilePath(filePath, out var normalizedPath, out var pathError))
        {
            return Unavailable(pathError);
        }

        var result = await _runCommand(
            "gio",
            ["info", "--attributes=standard::content-type", normalizedPath],
            QueryTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            return Unavailable(result.ExitCode == 127
                ? "Open With requires gio, but it is not installed."
                : "The file type could not be determined.");
        }

        var mimeType = ParseContentType(result.StandardOutput);
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return Unavailable("The file type could not be determined.");
        }

        var handlers = DiscoverHandlers(mimeType, _applicationDirectories, _log);
        return new FileHandlerQueryResult(
            IsAvailable: true,
            mimeType,
            handlers,
            ErrorMessage: string.Empty);
    }

    public Task<AppLaunchResult> OpenDefaultAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppLaunchResult>(cancellationToken);
        }

        if (!TryNormalizeFilePath(filePath, out var normalizedPath, out var pathError))
        {
            return Task.FromResult(new AppLaunchResult(false, pathError));
        }

        return Task.FromResult(_launchProcess("gio", ["open", normalizedPath]));
    }

    public Task<AppLaunchResult> OpenWithAsync(
        FileHandler handler,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppLaunchResult>(cancellationToken);
        }

        if (!TryNormalizeFilePath(filePath, out var normalizedPath, out var pathError))
        {
            return Task.FromResult(new AppLaunchResult(false, pathError));
        }

        if (handler is null
            || string.IsNullOrWhiteSpace(handler.DesktopFilePath)
            || !Path.IsPathFullyQualified(handler.DesktopFilePath)
            || !File.Exists(handler.DesktopFilePath)
            || !string.Equals(Path.GetExtension(handler.DesktopFilePath), ".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AppLaunchResult(false, "The selected application is no longer available."));
        }

        return Task.FromResult(_launchProcess("gio", ["launch", handler.DesktopFilePath, normalizedPath]));
    }

    internal static string? ParseContentType(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string prefix = "standard::content-type:";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = line[prefix.Length..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    internal static IReadOnlyList<FileHandler> DiscoverHandlers(
        string mimeType,
        IReadOnlyList<string> applicationDirectories,
        ILogService? log = null)
    {
        var handlers = new List<FileHandler>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logger = log ?? NullLogService.Instance;

        foreach (var directory in applicationDirectories)
        {
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> desktopFiles;
            try
            {
                desktopFiles = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Log(LogLevel.Warning, $"Could not enumerate desktop applications in {directory}.", ex);
                continue;
            }

            foreach (var desktopFile in desktopFiles)
            {
                if (handlers.Count >= MaxHandlers)
                {
                    return handlers.OrderBy(handler => handler.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                }

                var handler = TryReadHandler(desktopFile, mimeType, logger);
                if (handler is not null && seenIds.Add(handler.Id))
                {
                    handlers.Add(handler);
                }
            }
        }

        return handlers.OrderBy(handler => handler.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static FileHandler? TryReadHandler(string desktopFile, string mimeType, ILogService log)
    {
        Dictionary<string, string>? entry = null;
        var inDesktopEntry = false;

        try
        {
            foreach (var rawLine in File.ReadLines(desktopFile))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                    if (inDesktopEntry)
                    {
                        entry ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                if (!inDesktopEntry || entry is null) continue;

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator];
                if (key is "Name" or "Icon" or "MimeType" or "Type" or "NoDisplay" or "Hidden" or "Terminal")
                {
                    entry[key] = line[(separator + 1)..].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Log(LogLevel.Warning, $"Could not read desktop handler: {desktopFile}", ex);
            return null;
        }

        if (entry is null
            || !TryGet(entry, "Name", out var name)
            || !TryGet(entry, "MimeType", out var mimeTypes)
            || !TryGet(entry, "Type", out var type)
            || !string.Equals(type, "Application", StringComparison.OrdinalIgnoreCase)
            || IsTruthy(entry, "NoDisplay")
            || IsTruthy(entry, "Hidden")
            || IsTruthy(entry, "Terminal")
            || !mimeTypes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(mimeType, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var icon = TryGet(entry, "Icon", out var iconValue) ? iconValue : string.Empty;
        return new FileHandler(Path.GetFileName(desktopFile), name, icon, Path.GetFullPath(desktopFile));
    }

    private static IEnumerable<string> GetApplicationDirectories()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        yield return Path.Combine(dataHome, "applications");

        var dataDirectories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrWhiteSpace(dataDirectories))
        {
            dataDirectories = "/usr/local/share:/usr/share";
        }

        foreach (var directory in dataDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, "applications");
        }
    }

    private static bool TryNormalizeFilePath(string filePath, out string normalizedPath, out string error)
    {
        normalizedPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "No file was selected.";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The selected file path is invalid.";
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            error = "The selected file no longer exists.";
            return false;
        }

        return true;
    }

    private static FileHandlerQueryResult Unavailable(string error)
    {
        return new FileHandlerQueryResult(false, string.Empty, [], error);
    }

    private static bool TryGet(Dictionary<string, string> entry, string key, out string value)
    {
        return entry.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsTruthy(Dictionary<string, string> entry, string key)
    {
        return TryGet(entry, key, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
