using System;
using System.IO;

namespace DaisyOS.System.Processes;

/// <summary>
/// Discovers and launches an available terminal emulator with the requested working directory.
/// </summary>
public static class TerminalLauncher
{
    private static readonly (string Binary, Func<string, string[]> Args)[] SupportedTerminals =
    [
        ("kgx", dir => [$"--working-directory={dir}"]),
        ("ptyxis", dir => [$"--working-directory={dir}"]),
        ("konsole", dir => ["--workdir", dir]),
        ("alacritty", dir => ["--working-directory", dir]),
        ("kitty", dir => ["--directory", dir]),
        ("foot", dir => [$"--working-directory={dir}"]),
        ("ghostty", dir => [$"--working-directory={dir}"]),
        ("gnome-terminal", dir => [$"--working-directory={dir}"]),
        ("wezterm", dir => ["start", "--cwd", dir]),
        ("xfce4-terminal", dir => [$"--working-directory={dir}"]),
        ("xterm", _ => [])
    ];

    public static bool OpenTerminal(string? directoryPath = null, SafeProcessLauncher? launcher = null)
    {
        launcher ??= new SafeProcessLauncher();
        var dir = !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath)
            ? directoryPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var (binary, getArgs) in SupportedTerminals)
        {
            var res = launcher.Launch(binary, getArgs(dir));
            if (res.Succeeded) return true;

            var fallback = launcher.Launch(binary);
            if (fallback.Succeeded) return true;
        }

        return false;
    }
}
