using System.Diagnostics;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.System.Settings;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Opt-in benchmark handshake. Render completion is not KWin presentation.</summary>
public static class ApplicationPerformanceProbe
{
    private static readonly string? ResultPath = Environment.GetEnvironmentVariable("DAISYOS_PERFORMANCE_RESULT");
    public static string? Token => Environment.GetEnvironmentVariable("DAISYOS_PERFORMANCE_TOKEN");
    public static string? DataDirectory => Environment.GetEnvironmentVariable("DAISYOS_PERFORMANCE_DATA");
    public static bool Enabled => !string.IsNullOrWhiteSpace(ResultPath) && !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(DataDirectory);
    public static string InstanceSuffix => Enabled ? "|benchmark:" + Token : "";
    public static string AppId(string id) => Enabled ? "org.daisyos.benchmark." + id + "." + Token : "org.daisyos." + id;

    public static void Attach(Window window, string id)
    {
        if (!Enabled) return;
        window.Opened += async (_, _) =>
        {
            try
            {
                var deadline = Stopwatch.StartNew();
                while (!ContentReady(window, id))
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Initial content did not become ready.");
                    await Task.Delay(5);
                }
                window.UpdateLayout();
                // Let layout/render callbacks synchronize the actual scene, then
                // await its render-thread completion rather than window creation.
                var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.RequestAnimationFrame(_ => tick.TrySetResult());
                await tick.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var visual = ElementComposition.GetElementVisual(window) ?? throw new InvalidOperationException("No composition visual.");
                await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(TimeSpan.FromSeconds(10));
                // Avoid inheriting the compositor/render thread's inline continuation.
                await Dispatcher.UIThread.InvokeAsync(() => Publish(id, null));
                await Task.Delay(150);
                await Dispatcher.UIThread.InvokeAsync(window.Close);
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { Publish(id, ex.Message); window.Close(); });
            }
        };
    }

    internal static bool ContentReady(Window window, string id) => id switch
    {
        "files" => window.DataContext is FilesViewModel { ActiveTab.CurrentSession.IsReady: true } files && files.ActiveTab!.Entries.Count == 1000,
        "notes" => window.DataContext is NotesViewModel notes && notes.Notes.Count == 20,
        "settings" => window is SettingsWindow settings && settings.FindControl<SettingsView>("SettingsContent")?.FindControl<StackPanel>("CategoryContent") is { } content &&
            content.Children.Count >= 2 && !content.Children.OfType<TextBlock>().Any(text => text.Text?.EndsWith("…", StringComparison.Ordinal) == true),
        _ => false
    };

    private static void Publish(string id, string? error)
    {
        ApplicationStartupTrace.Mark(error is null ? "content-rendered" : "content-failed");
        var result = new PerformanceReadySignal(1, Token!, id, Environment.ProcessId, error is null ? "initial-content-rendered" : "failed", error);
        var path = ResultPath!;
        File.WriteAllText(path + ".tmp", global::System.Text.Json.JsonSerializer.Serialize(result, PerformanceProbeJsonContext.Default.PerformanceReadySignal));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

internal sealed record PerformanceReadySignal(int Protocol, string Token, string Application, int ProcessId, string Stage, string? Error);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PerformanceReadySignal))]
internal partial class PerformanceProbeJsonContext : JsonSerializerContext { }
