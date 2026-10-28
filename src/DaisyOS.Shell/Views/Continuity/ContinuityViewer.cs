using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Continuity;
using global::System.Globalization;
using global::System.Threading.Channels;

namespace DaisyOS.Shell.Views.Continuity;

/// <summary>Focused remote control. Input is captured only inside this window.</summary>
public sealed class ContinuityViewer : Window
{
    private readonly ContinuityClient _client = new();
    private readonly ContinuityLiveSession _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Focusable = true };
    private readonly Channel<string> _input = Channel.CreateBounded<string>(64);
    private Bitmap? _bitmap;
    private bool _closing;
    private string? _pendingMotion;

    public ContinuityViewer(ContinuityLiveSession session)
    {
        _session = session;
        Title = session.Name + " · " + Loc.Get(session.Control ? "Remote control" : "Screen viewing");
        Width = 1056; Height = 680; MinWidth = 640; MinHeight = 400;
        Background = Brushes.Black;
        var stop = new Button { Content = Loc.Get("Stop sharing"), HorizontalAlignment = HorizontalAlignment.Right };
        stop.Click += (_, _) => Close();
        var tip = new TextBlock { Text = Loc.Get("Ctrl+Alt+Escape stops remote control. Leaving this window stops control."),
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var bar = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(stop, Dock.Right); bar.Children.Add(stop); bar.Children.Add(tip);
        var layout = new DockPanel(); DockPanel.SetDock(bar, Dock.Top); layout.Children.Add(bar); layout.Children.Add(_image); Content = layout;
        if (session.Control)
        {
            AddHandler(KeyDownEvent, OnKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnKeyUp, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _image.PointerPressed += (_, e) => { _image.Focus(); e.Pointer.Capture(_image); SendButton(e, true); e.Handled = true; };
            _image.PointerReleased += (_, e) =>
            {
                SendButton(e, false);
                var buttons = e.GetCurrentPoint(_image).Properties;
                if (!buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed && !buttons.IsMiddleButtonPressed) e.Pointer.Capture(null);
                e.Handled = true;
            };
            _image.PointerMoved += (_, e) =>
            {
                var p = e.GetPosition(_image); var bounds = _image.Bounds;
                var scale = Math.Min(bounds.Width / 1280, bounds.Height / 720);
                if (scale <= 0) return;
                var x = (p.X - (bounds.Width - 1280 * scale) / 2) / (1280 * scale);
                var y = (p.Y - (bounds.Height - 720 * scale) / 2) / (720 * scale);
                if (x is >= 0 and <= 1 && y is >= 0 and <= 1) Queue(FormattableString.Invariant($"5 0 0 {x:F5} {y:F5}"));
            };
            _image.PointerWheelChanged += (_, e) => { Queue(FormattableString.Invariant($"4 0 0 {-e.Delta.X * 15:F2} {-e.Delta.Y * 15:F2}")); e.Handled = true; };
            Deactivated += (_, _) => { if (IsVisible && !_closing) Close(); };
        }
        Opened += (_, _) => { _image.Focus(); _ = PollFramesAsync(); _ = SendInputAsync(); };
        Closed += (_, _) => { _closing = true; _stop.Cancel(); _bitmap?.Dispose(); _ = StopRemoteAsync(); };
    }
    private async Task StopRemoteAsync()
    { try { await _client.ExecuteAsync(new("live-stop", _session.Id)); } catch { } }
    private void Queue(string input)
    {
        if (_closing) return;
        if (input.StartsWith("5 ", StringComparison.Ordinal)) { _pendingMotion = input; return; }
        var motion = Interlocked.Exchange(ref _pendingMotion, null);
        if (motion is not null && !_input.Writer.TryWrite(motion)) { Close(); return; }
        if (!_input.Writer.TryWrite(input)) Close();
    }
    private void SendButton(PointerEventArgs e, bool down)
    {
        var kind = e.GetCurrentPoint(_image).Properties.PointerUpdateKind;
        var code = kind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 272,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 273,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 274,
            _ => 0
        };
        if (code != 0) Queue($"2 {code} {(down ? 1 : 0)} 0 0");
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers.HasFlag(KeyModifiers.Control | KeyModifiers.Alt)) { e.Handled = true; Close(); return; }
        SendKey(e, true);
    }
    private void OnKeyUp(object? sender, KeyEventArgs e) => SendKey(e, false);
    private void SendKey(KeyEventArgs e, bool down)
    {
        var code = EvdevKey(e.PhysicalKey == PhysicalKey.None ? e.Key : e.PhysicalKey.ToQwertyKey());
        if (code == 0) return;
        Queue($"1 {code} {(down ? 1 : 0)} 0 0"); e.Handled = true;
    }
    // evdev codes, independent of the target keyboard layout; the target applies its layout.
    internal static int EvdevKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return LetterCodes[(int)key - (int)Key.A];
        if (key >= Key.D1 && key <= Key.D9) return 2 + (int)key - (int)Key.D1;
        if (key >= Key.F1 && key <= Key.F10) return 59 + (int)key - (int)Key.F1;
        return key switch
        {
            Key.D0 => 11, Key.Escape => 1, Key.Back => 14, Key.Tab => 15, Key.Enter => 28,
            Key.LeftCtrl => 29, Key.RightCtrl => 97, Key.LeftShift => 42, Key.RightShift => 54,
            Key.LeftAlt => 56, Key.RightAlt => 100, Key.LWin => 125, Key.RWin => 126,
            Key.Space => 57, Key.CapsLock => 58, Key.Delete => 111, Key.Insert => 110,
            Key.Home => 102, Key.End => 107, Key.PageUp => 104, Key.PageDown => 109,
            Key.Up => 103, Key.Down => 108, Key.Left => 105, Key.Right => 106,
            Key.OemMinus => 12, Key.OemPlus => 13, Key.OemOpenBrackets => 26, Key.OemCloseBrackets => 27,
            Key.OemSemicolon => 39, Key.OemQuotes => 40, Key.OemTilde => 41, Key.OemPipe => 43,
            Key.OemComma => 51, Key.OemPeriod => 52, Key.OemQuestion => 53, Key.F11 => 87, Key.F12 => 88,
            _ => 0
        };
    }
    private static readonly int[] LetterCodes = [30,48,46,32,18,33,34,35,23,36,37,38,50,49,24,25,16,19,31,20,22,47,17,45,21,44];
    private async Task SendInputAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var motion = Interlocked.Exchange(ref _pendingMotion, null);
                if (motion is not null) await _client.ExecuteAsync(new("live-input", _session.Id, motion), _stop.Token);
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                tick.CancelAfter(TimeSpan.FromMilliseconds(10));
                string input;
                try { input = await _input.Reader.ReadAsync(tick.Token); }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { continue; }
                await _client.ExecuteAsync(new("live-input", _session.Id, input), _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closing) Close(); }
    }
    private async Task PollFramesAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            var misses = 0;
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var bytes = await _client.ReadFrameAsync(_session.Id, _stop.Token);
                if (bytes.Length == 0)
                {
                    if (++misses % 20 == 0)
                    {
                        var state = await _client.ExecuteAsync(new("status"), _stop.Token);
                        if (state.Sessions?.Any(s => s.Id == _session.Id) != true) Close();
                    }
                    continue;
                }
                misses = 0;
                using var stream = new MemoryStream(bytes, false);
                var next = new Bitmap(stream);
                var previous = _bitmap; _bitmap = next; _image.Source = next; previous?.Dispose();
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closing) Close(); }
    }
}
