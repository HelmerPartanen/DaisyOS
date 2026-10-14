using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Processes;

if (args.Length != 1) throw new ArgumentException("Usage: DisplaySettingsAcceptance <artifact-directory>");
var artifact = Path.GetFullPath(args[0]);
Directory.CreateDirectory(artifact);
var exitCode = 1;
Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
DaisyOS.Shell.Program.BuildAvaloniaApp(() => new App((app, desktop) =>
{
    app.UseDesktopSettings();
    var window = new SettingsWindow { Width = 860, Height = 720 };
    desktop.MainWindow = window;
    window.Opened += async (_, _) =>
    {
        DetectedDisplay? original = null;
        var results = new List<object>();
        try
        {
            window.SelectCategory("Displays");
            original = await DisplaySettingsHelper.DetectPrimaryDisplayAsync() ?? throw new Exception("No display");
            var target = Math.Abs(original.Scale - 1.25) < .01 ? 1.0 : 1.25;
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            var method = typeof(SettingsView).GetMethod("ApplyKScreenSettingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var buildVersion = typeof(SettingsView).GetField("_categoryBuildVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var action in new[] { "timeout", "keep", "revert" })
            {
                var started = Stopwatch.StartNew();
                var change = (Task)method.Invoke(view, [original.Name, "display scale",
                    new Func<DetectedDisplay, string>(_ => $"output.{original.Name}.scale.{target.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                    (int)buildVersion.GetValue(view)!])!;
                Window? panel = null;
                for (var attempt = 0; attempt < 100 && panel is null; attempt++)
                { await Task.Delay(100); panel = window.OwnedWindows.FirstOrDefault(w => w.IsVisible); }
                if (panel is null) throw new Exception("Confirmation panel missing");
                var applied = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).Single(d => d.Name == original.Name);
                if (Math.Abs(applied.Scale - target) > .001) throw new Exception("Display scale was not applied");
                var countdown = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DisplayChangeCountdown");
                var initialText = countdown.Text;
                await Task.Delay(1200);
                if (countdown.Text == initialText) throw new Exception("Countdown did not advance");
                var screenshot = new ProcessStartInfo("spectacle") { UseShellExecute = false };
                foreach (var value in new[] { "-b", "-n", "-f", "-o", Path.Combine(artifact, action + ".png") }) screenshot.ArgumentList.Add(value);
                using (var capture = Process.Start(screenshot)!) await capture.WaitForExitAsync();
                if (action != "timeout")
                {
                    var label = Loc.Get(action == "keep" ? "Keep changes" : "Revert");
                    panel.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await change.WaitAsync(TimeSpan.FromSeconds(25));
                var after = (await DisplaySettingsHelper.DetectAllDisplaysAsync()).Single(d => d.Name == original.Name);
                var expected = action == "keep" ? target : original.Scale;
                if (Math.Abs(after.Scale - expected) > .001) throw new Exception($"{action}: expected {expected}, got {after.Scale}");
                if (panel.IsVisible) throw new Exception("Panel remained open");
                results.Add(new { action, elapsedSeconds = started.Elapsed.TotalSeconds, beforeScale = original.Scale, appliedScale = applied.Scale, afterScale = after.Scale });
                if (action == "keep") await Restore(original);
                await Task.Delay(500);
            }
            File.WriteAllText(Path.Combine(artifact, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            exitCode = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        finally
        {
            if (original is not null) await Restore(original);
            window.Close();
        }
    };
})).StartWithClassicDesktopLifetime([]);
return exitCode;

static async Task Restore(DetectedDisplay display)
{
    var result = await new SafeCommandRunner().RunAsync("kscreen-doctor",
        [$"output.{display.Name}.scale.{display.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}"], TimeSpan.FromSeconds(3));
    if (!result.Succeeded) throw new Exception("Acceptance cleanup could not restore display scale");
}
