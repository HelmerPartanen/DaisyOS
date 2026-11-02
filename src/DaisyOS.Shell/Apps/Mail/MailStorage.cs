using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DaisyOS.Shell.Apps.Mail;

public sealed class MailStorage
{
    public string Root { get; }
    public MailStorage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "daisyos", "mail");
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public T Read<T>(string key, JsonTypeInfo<T> type, T fallback)
    {
        var path = Path.Combine(Root, key + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) ?? fallback : fallback;
    }
    public void Write<T>(string key, T value, JsonTypeInfo<T> type)
    {
        var path = Path.Combine(Root, key + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, type));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, true);
    }
    public static string CacheKey(string account, string folder) => "cache-" + account + "-" +
        Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(folder)));
}

// Credentials never enter account metadata, command arguments, logs, or the mail cache.
internal static class MailSecrets
{
    public static async Task<string> RunAsync(string operation, string id, string? value, CancellationToken ct)
    {
        var info = new ProcessStartInfo("secret-tool") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(operation);
        if (operation == "store") info.ArgumentList.Add("--label=DaisyOS Mail");
        info.ArgumentList.Add("application"); info.ArgumentList.Add("daisyos-mail");
        info.ArgumentList.Add("account"); info.ArgumentList.Add(id);
        try
        {
            using var process = Process.Start(info) ?? throw new IOException("Could not open the system keyring.");
            if (value is not null) await process.StandardInput.WriteAsync(value.AsMemory(), ct);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct); }
            catch { if (!process.HasExited) process.Kill(); throw; }
            await error;
            if (process.ExitCode != 0 && operation != "lookup") throw new IOException("The system keyring is locked or unavailable.");
            return (await output).TrimEnd('\r', '\n');
        }
        catch (global::System.ComponentModel.Win32Exception) { throw new IOException("Install libsecret (secret-tool) and unlock your system keyring to use Mail."); }
    }
}
