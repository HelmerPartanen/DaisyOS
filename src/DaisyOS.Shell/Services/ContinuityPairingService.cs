using Avalonia.Threading;
using DaisyOS.Core.Continuity;
using DaisyOS.System.Continuity;
using DaisyOS.Shell.Services.DesktopIntegration;
using DaisyOS.Shell.Views.Continuity;

namespace DaisyOS.Shell.Services;

/// <summary>One shell-owned dialog for each pairing attempt, independent of Settings.</summary>
internal sealed class ContinuityPairingService
{
    private readonly IContinuityClient _client;
    private readonly Func<ContinuityPrompt, ContinuityPairingDialog> _create;
    internal ContinuityPairingService(IContinuityClient? client = null, Func<ContinuityPrompt, ContinuityPairingDialog>? create = null)
    {
        _client = client ?? new ContinuityClient();
        _create = create ?? (prompt => PortalDialog.Create(() => new ContinuityPairingDialog(prompt)));
    }
    private readonly Dictionary<string, (ContinuityPrompt Prompt, ContinuityPairingDialog Window)> _windows = new();
    private readonly HashSet<string> _dismissed = new();
    private readonly Dictionary<string, DateTimeOffset> _missingSince = new();

    internal async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            do
            {
                try
                {
                    var status = await _client.ExecuteAsync(new("status"), token);
                    await Dispatcher.UIThread.InvokeAsync(() => Update(status, token));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var item in _windows.Values) item.Window.Complete(false, "Continuity is unavailable. Try pairing again.");
                    });
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in _windows.Values.ToArray()) { item.Window.Complete(false); item.Window.Close(); }
                _windows.Clear();
            });
        }
    }

    internal void Update(ContinuityStatus state, CancellationToken token = default)
    {
        foreach (var item in _windows.Values.ToArray())
        {
            if (!state.Enabled || !state.SessionReady || state.Paused)
            { item.Window.Complete(false); item.Window.Close(); continue; }
            if (!item.Window.Finished && !state.Prompts.Any(p => p.Id == item.Prompt.Id))
            {
                if (state.Devices.Any(d => d.Id == item.Prompt.PeerId && d.Trusted)) item.Window.Complete(true);
                else if (_missingSince.TryGetValue(item.Prompt.Id, out var missing) && DateTimeOffset.UtcNow - missing > TimeSpan.FromSeconds(1)) item.Window.Complete(false);
                else _missingSince.TryAdd(item.Prompt.Id, DateTimeOffset.UtcNow);
            }
        }
        foreach (var prompt in state.Prompts.Where(p => p.Kind is "pair-code" or "pair-display"))
        {
            if (!state.Enabled || !state.SessionReady || state.Paused || _dismissed.Contains(prompt.Id) || _windows.ContainsKey(prompt.Id)) continue;
            foreach (var finished in _windows.Values.Where(item => item.Window.Finished).ToArray()) finished.Window.Close();
            var window = _create(prompt);
            _windows.Add(prompt.Id, (prompt, window));
            window.Submitted += code => _ = SubmitAsync(window, new("pair-code", prompt.Id, code), token);
            window.Cancelled += () => _ = SubmitAsync(window, new("pair-cancel", prompt.Id), token);
            window.Closed += (_, _) => { _windows.Remove(prompt.Id); _missingSince.Remove(prompt.Id); _dismissed.Add(prompt.Id); };
            window.Show();
        }
        _dismissed.RemoveWhere(id => !state.Prompts.Any(p => p.Id == id));
    }

    private async Task SubmitAsync(ContinuityPairingDialog window, ContinuityCommand command, CancellationToken token)
    {
        try { await _client.ExecuteAsync(command, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { await Dispatcher.UIThread.InvokeAsync(() => window.Complete(false, "Couldn’t pair devices. Try again.")); }
    }
}
