using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Audio;
using DaisyOS.System.Diagnostics;
using DaisyOS.System.Gaming;
using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using DaisyOS.System.Session;

namespace DaisyOS.Shell.Views.Components.Console;

public partial class ConsoleQuickMenuOverlay : UserControl
{
    private readonly IAudioService? _audioService;
    private readonly IPowerService? _powerService;
    private readonly IUserSessionService? _userSessionService;
    private readonly IGameDiscoveryService? _gameDiscoveryService;
    private readonly ISystemMetricsService? _systemMetricsService;
    private readonly IGameArtworkResolver? _gameArtworkResolver;

    private ConsoleGameItemViewModel? _activeRunningGame;

    public event EventHandler? Closed;
    public event EventHandler? GoHomeRequested;
    public event EventHandler? ReturnToDesktopRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? ShutdownRequested;
    public event EventHandler<ConsoleGameItemViewModel>? RestartGameRequested;
    public event EventHandler<ConsoleGameItemViewModel>? CloseGameRequested;

    public void SetActiveGame(ConsoleGameItemViewModel? runningGame)
    {
        _activeRunningGame = runningGame;
        UpdateCurrentGamePanel();
    }

    private CancellationTokenSource? _loadCts;
    private DispatcherTimer? _clockTimer;

    public ConsoleQuickMenuOverlay()
        : this(
            new LinuxAudioService(new SafeCommandRunner()),
            new LinuxPowerService(new SafeCommandRunner()),
            new LinuxUserSessionService(),
            new LinuxGameDiscoveryService(),
            new LinuxSystemMetricsService(),
            new GameArtworkResolver())
    {
    }

    public ConsoleQuickMenuOverlay(
        IAudioService audioService,
        IPowerService powerService,
        IUserSessionService userSessionService,
        IGameDiscoveryService gameDiscoveryService,
        ISystemMetricsService systemMetricsService,
        IGameArtworkResolver gameArtworkResolver)
    {
        _audioService = audioService;
        _powerService = powerService;
        _userSessionService = userSessionService;
        _gameDiscoveryService = gameDiscoveryService;
        _systemMetricsService = systemMetricsService;
        _gameArtworkResolver = gameArtworkResolver;
        
        InitializeComponent();
        UpdateControllerHintIcons("DualSense Wireless Controller");

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (s, e) => UpdateClock();
    }

    private void UpdateClock()
    {
        if (ClockText != null)
        {
            ClockText.Text = DateTime.Now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
        }
    }

    public void ShowQuickMenu()
    {
        IsVisible = true;
        Opacity = 1;
        IsHitTestVisible = true;

        UpdateClock();
        _clockTimer?.Start();

        UpdateControllerHintIcons("DualSense Wireless Controller");

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _ = LoadDataAsync(_loadCts.Token);

        Dispatcher.UIThread.Post(() =>
        {
            BtnGoHome?.Focus();
        }, DispatcherPriority.Input);
    }

    private async Task LoadDataAsync(CancellationToken token)
    {
        try
        {
            if (_systemMetricsService != null)
            {
                var metrics = await _systemMetricsService.GetStatusAsync(token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (SystemStatusDescription != null)
                    {
                        SystemStatusDescription.Text = Loc.Format("CPU: {0:F0}% • RAM: {1:F0}%", metrics.CpuUsagePercent ?? 0, (metrics.MemoryUsagePercent ?? 0));
                    }
                });
            }

            Dispatcher.UIThread.Post(UpdateCurrentGamePanel);
            if (_audioService != null)
            {
                var masterVol = await _audioService.GetVolumeAsync(token);
                var inputVol = await _audioService.GetInputVolumeAsync(token);
                
                Dispatcher.UIThread.Post(() =>
                {
                    if (QuickMasterVolumeSlider != null && masterVol.HasValue)
                    {
                        QuickMasterVolumeSlider.Value = masterVol.Value;
                    }
                    if (QuickMicVolumeSlider != null && inputVol.HasValue)
                    {
                        QuickMicVolumeSlider.Value = inputVol.Value;
                    }
                });
            }
        }
        catch { }
    }

    private string _activeControllerName = "DualSense Wireless Controller";

    public void UpdateControllerHintIcons(string? name = null)
    {
        if (!string.IsNullOrEmpty(name))
        {
            _activeControllerName = name;
        }

        var type = ControllerConnectionStatus.DetectType(_activeControllerName);
        var isPlayStation = type == ControllerType.PlayStation;
        var prefix = isPlayStation
            ? "avares://DaisyOS.Shell/Assets/PlaystationController/"
            : "avares://DaisyOS.Shell/Assets/XboxController/";

        try
        {
            if (HintCategoryDpadImage != null)
                HintCategoryDpadImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "plain-bottom.png" : "dpad.png"))));

            if (HintConfirmImage != null)
                HintConfirmImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "outline-blue-cross.png" : "a-filled-green.png"))));

            if (HintBackImage != null)
                HintBackImage.Source = new Bitmap(AssetLoader.Open(new Uri(prefix + (isPlayStation ? "outline-red-circle.png" : "b-filled red.png"))));
        }
        catch
        {
            // Graceful fallback
        }
    }

    public async void HideQuickMenu()
    {
        _clockTimer?.Stop();
        Opacity = 0;
        IsHitTestVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);

        await Task.Delay(250);
        if (Opacity == 0) IsVisible = false;
    }

    public void Navigate(ControllerNavigationAction action)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

        if (action == ControllerNavigationAction.Confirm)
        {
            if (focused is Button btn)
            {
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            return;
        }

        if (action == ControllerNavigationAction.Back || action == ControllerNavigationAction.OpenConsole)
        {
            HideQuickMenu();
            return;
        }

        if (focused == null) return;
        
        if (action == ControllerNavigationAction.Left && focused is ConsolePillSlider pillL)
        {
            pillL.Value = Math.Max(pillL.Minimum, pillL.Value - pillL.Step);
            return;
        }
        if (action == ControllerNavigationAction.Right && focused is ConsolePillSlider pillR)
        {
            pillR.Value = Math.Min(pillR.Maximum, pillR.Value + pillR.Step);
            return;
        }

        IInputElement? next = null;

        // Custom grid mapping
        if (focused == BtnGoHome)
        {
            if (action == ControllerNavigationAction.Right) next = BtnExitDesktop;
            else if (action == ControllerNavigationAction.Down) next = BtnRestart;
        }
        else if (focused == BtnExitDesktop)
        {
            if (action == ControllerNavigationAction.Left) next = BtnGoHome;
            else if (action == ControllerNavigationAction.Down) next = BtnPowerOff;
        }
        else if (focused == BtnRestart)
        {
            if (action == ControllerNavigationAction.Right) next = BtnPowerOff;
            else if (action == ControllerNavigationAction.Up) next = BtnGoHome;
            else if (action == ControllerNavigationAction.Down) next = QuickMasterVolumeSlider;
        }
        else if (focused == BtnPowerOff)
        {
            if (action == ControllerNavigationAction.Left) next = BtnRestart;
            else if (action == ControllerNavigationAction.Up) next = BtnExitDesktop;
            else if (action == ControllerNavigationAction.Down) next = QuickMasterVolumeSlider;
        }
        else if (focused == QuickMasterVolumeSlider)
        {
            if (action == ControllerNavigationAction.Up) next = BtnRestart;
            else if (action == ControllerNavigationAction.Down) next = QuickMicVolumeSlider;
        }
        else if (focused == QuickMicVolumeSlider)
        {
            if (action == ControllerNavigationAction.Up) next = QuickMasterVolumeSlider;
            else if (action == ControllerNavigationAction.Down) next = PanelMediaWidget?.FindControl<Button>("PlayPauseButton");
        }
        
        // Media buttons
        var prev = PanelMediaWidget?.FindControl<Button>("PreviousButton");
        var play = PanelMediaWidget?.FindControl<Button>("PlayPauseButton");
        var nextBtn = PanelMediaWidget?.FindControl<Button>("NextButton");

        if (focused == prev)
        {
            if (action == ControllerNavigationAction.Right) next = play;
            else if (action == ControllerNavigationAction.Up) next = QuickMicVolumeSlider;
            else if (action == ControllerNavigationAction.Down) next = BtnRestartGame;
        }
        else if (focused == play)
        {
            if (action == ControllerNavigationAction.Left) next = prev;
            else if (action == ControllerNavigationAction.Right) next = nextBtn;
            else if (action == ControllerNavigationAction.Up) next = QuickMicVolumeSlider;
            else if (action == ControllerNavigationAction.Down) next = BtnRestartGame;
        }
        else if (focused == nextBtn)
        {
            if (action == ControllerNavigationAction.Left) next = play;
            else if (action == ControllerNavigationAction.Up) next = QuickMicVolumeSlider;
            else if (action == ControllerNavigationAction.Down) next = BtnCloseGame;
        }

        if (focused == BtnRestartGame)
        {
            if (action == ControllerNavigationAction.Right) next = BtnCloseGame;
            else if (action == ControllerNavigationAction.Up) next = play;
        }
        else if (focused == BtnCloseGame)
        {
            if (action == ControllerNavigationAction.Left) next = BtnRestartGame;
            else if (action == ControllerNavigationAction.Up) next = nextBtn;
        }

        if (next != null)
        {
            next.Focus();
            if (next is Control c)
            {
                Dispatcher.UIThread.Post(() => c.BringIntoView(), DispatcherPriority.Render);
            }
        }
    }

    private void OnGoHomeClicked(object? sender, RoutedEventArgs e)
    {
        HideQuickMenu();
        GoHomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnQuickMasterVolumeChanged(object? sender, double value)
    {
        if (_audioService != null)
        {
            try { await _audioService.SetVolumeAsync(value).ConfigureAwait(true); }
            catch { }
        }
    }

    private async void OnQuickMicVolumeChanged(object? sender, double value)
    {
        if (_audioService != null)
        {
            try { await _audioService.SetInputVolumeAsync(value).ConfigureAwait(true); }
            catch { }
        }
    }

    private void OnQuickRestartClicked(object? sender, RoutedEventArgs e)
    {
        HideQuickMenu();
        if (_powerService != null) _ = _powerService.RebootAsync();
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnQuickReturnDesktopClicked(object? sender, RoutedEventArgs e)
    {
        HideQuickMenu();
        ReturnToDesktopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnQuickShutdownClicked(object? sender, RoutedEventArgs e)
    {
        HideQuickMenu();
        if (_powerService != null) _ = _powerService.ShutdownAsync();
        ShutdownRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCurrentGamePanel()
    {
        if (CurrentGamePanel == null) return;

        if (_activeRunningGame != null && _activeRunningGame.IsRunning)
        {
            CurrentGamePanel.IsVisible = true;
            if (CurrentGameTitle != null) CurrentGameTitle.Text = _activeRunningGame.Title;
            if (CurrentGameIcon != null)
            {
                CurrentGameIcon.Source = _activeRunningGame.CoverImage ?? _activeRunningGame.HeroImage ?? _activeRunningGame.LogoImage;
            }
        }
        else
        {
            CurrentGamePanel.IsVisible = false;
        }
    }

    private void OnRestartGameClicked(object? sender, RoutedEventArgs e)
    {
        if (_activeRunningGame != null)
        {
            HideQuickMenu();
            RestartGameRequested?.Invoke(this, _activeRunningGame);
        }
    }

    private void OnCloseGameClicked(object? sender, RoutedEventArgs e)
    {
        if (_activeRunningGame != null)
        {
            var game = _activeRunningGame;
            game.IsRunning = false;
            _activeRunningGame = null;
            UpdateCurrentGamePanel();
            HideQuickMenu();
            CloseGameRequested?.Invoke(this, game);
        }
    }
}
