using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Picker;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class PrintPortal
{
    private sealed record Job(string App, string Printer, int Copies, string? File, DateTime Expires);
    private readonly Dictionary<uint, Job> _jobs = [];
    public async Task<(uint Code, Dictionary<string, VariantValue> Results)> PrepareAsync(List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop)
    {
        foreach (var expired in _jobs.Where(pair => pair.Value.Expires < DateTime.UtcNow).Select(pair => pair.Key).ToArray()) _jobs.Remove(expired);
        var printers = new List<string> { "Save as PDF" };
        try
        {
            using var list = Process.Start(new ProcessStartInfo("lpstat") { ArgumentList = { "-a" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (list is not null)
            {
                using var cancelList = stop.Register(() => { try { if (!list.HasExited) list.Kill(); } catch (InvalidOperationException) { } });
                var output = await list.StandardOutput.ReadToEndAsync(stop); await list.WaitForExitAsync(stop);
                if (list.ExitCode == 0) printers.AddRange(output.Split('\n').Select(line => line.Split(' ', 2)[0]).Where(printer => printer.Length > 0));
            }
        }
        catch (global::System.ComponentModel.Win32Exception) { }
        var dialog = PortalDialog.Create(() => new PortalDialog(args[3], $"{(args[1].Length > 0 ? args[1] : "This application")} wants to print a document.", "Continue"));
        var printer = new ComboBox { ItemsSource = printers, SelectedIndex = 0 };
        var copies = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1 };
        dialog.Fields.Children.Add(new TextBlock { Text = Loc.Get("Printer") }); dialog.Fields.Children.Add(printer);
        dialog.Fields.Children.Add(new TextBlock { Text = Loc.Get("Copies") }); dialog.Fields.Children.Add(copies);
        using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
        string? file = null;
        if (printer.SelectedIndex == 0)
        {
            var picker = PortalDialog.Create(() => new DaisyFilePickerWindow(new DaisyFilePickerOptions("Save PDF", false, false, [".pdf"], SaveFile: true, SuggestedName: "Document.pdf")));
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.Closed += (_, _) => closed.TrySetResult();
            using var cancelPicker = stop.Register(() => Dispatcher.UIThread.Post(picker.Close));
            picker.Show(); await closed.Task;
            if (!picker.WasConfirmed || stop.IsCancellationRequested) return (1, []);
            file = picker.SelectedPaths[0];
            if (File.Exists(file))
            {
                var replace = PortalDialog.Create(() => new PortalDialog("Replace file?", "The selected PDF already exists. Replace it?", "Replace"));
                using var cancelReplace = stop.Register(() => Dispatcher.UIThread.Post(replace.Close));
                if (!await replace.Present() || stop.IsCancellationRequested) return (1, []);
            }
        }
        var token = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var job = new Job(args[1], printer.SelectedIndex == 0 ? "" : printers[printer.SelectedIndex], (int)(copies.Value ?? 1), file, DateTime.UtcNow.AddMinutes(10));
        _jobs[token] = job;
        return (0, new()
        {
            ["token"] = token,
            ["settings"] = new Dict<string, VariantValue> { ["printer"] = job.Printer, ["n-copies"] = job.Copies.ToString() },
            ["page-setup"] = new Dict<string, VariantValue>()
        });
    }

    public async Task<(uint Code, Dictionary<string, VariantValue> Results)> PrintAsync(List<string> args, Dictionary<string, VariantValue> options, SafeFileHandle file, CancellationToken stop)
    {
        uint token;
        if (options.TryGetValue("token", out var value)) token = value.GetUInt32();
        else
        {
            var prepared = await PrepareAsync(args, options, stop);
            if (prepared.Code != 0) return prepared;
            token = prepared.Results["token"].GetUInt32();
        }
        if (!_jobs.Remove(token, out var job) || job.App != args[1] || job.Expires < DateTime.UtcNow) return (2, []);
        using var input = new FileStream(file, FileAccess.Read);
        if (job.File is not null)
        {
            var staging = job.File + ".daisy-" + Guid.NewGuid().ToString("N");
            try
            {
                var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var output = new FileStream(staging, fileOptions))
                    await input.CopyToAsync(output, stop);
                stop.ThrowIfCancellationRequested(); File.Move(staging, job.File, overwrite: true);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        else
        {
            var start = new ProcessStartInfo("lp") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in new[] { "-d", job.Printer, "-n", job.Copies.ToString(), "-t", args[3] }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Printing is unavailable.");
            using var cancelPrint = stop.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
            var error = process.StandardError.ReadToEndAsync(stop); var output = process.StandardOutput.ReadToEndAsync(stop);
            await input.CopyToAsync(process.StandardInput.BaseStream, stop); process.StandardInput.Close();
            await process.WaitForExitAsync(stop); await output;
            if (process.ExitCode != 0) throw new IOException(await error);
        }
        return (0, []);
    }
}
