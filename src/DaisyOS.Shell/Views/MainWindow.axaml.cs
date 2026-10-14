using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DaisyOS.Shell;

namespace DaisyOS.Shell.Views
{
    /// <summary>
    /// Development-only fullscreen prototype. The production display-manager
    /// session never creates this xdg_toplevel: it is retained so visual work
    /// can continue while the layer-shell backend is maintained separately.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        public ShellView ShellContent => Shell;

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ShellContent.ToggleMetricsPanel();
                e.Handled = true;
                return;
            }

            // PrintScreen or Super+Shift+S -> Snipping / Screenshot tool
            if (e.Key == Key.PrintScreen ||
                (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            {
                ShellContent.Screenshot.Open(mode: e.Key == Key.PrintScreen
                    ? Services.Screenshot.ScreenshotCaptureMode.Screen
                    : Services.Screenshot.ScreenshotCaptureMode.Area);
                e.Handled = true;
                return;
            }

            // Super key -> Toggle Task Overview
            if (e.Key == Key.LWin || e.Key == Key.RWin)
            {
                (Application.Current as App)?.ToggleOverview();
                e.Handled = true;
                return;
            }

            // Super+E -> Open Files
            if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                if (Application.Current is App app)
                {
                    app.OpenFilesWindow();
                }
                e.Handled = true;
                return;
            }

            // Super+V -> Open Clipboard History
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                (Application.Current as App)?.ShowClipboardHistory();
                e.Handled = true;
                return;
            }

            // Super+I -> Settings
            if (e.Key == Key.I && e.KeyModifiers == KeyModifiers.Meta)
            {
                (Application.Current as App)?.ShowSettings();
                e.Handled = true;
                return;
            }

            // Super+N -> Notifications; Super+Shift+N -> create a note.
            if (e.Key == Key.N && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift))
            {
                (Application.Current as App)?.ShowNewNote();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Meta)
            {
                ShellContent.SystemBar.ShowNotificationPanel();
                e.Handled = true;
                return;
            }

            // Ctrl+Shift+Esc -> Task Manager
            if (e.Key == Key.Escape && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                (Application.Current as App)?.ShowTaskManager();
                e.Handled = true;
                return;
            }

            // Super+T or Ctrl+Alt+T -> Open Terminal
            if ((e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Meta)) ||
                (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
            {
                _ = DaisyOS.System.Processes.TerminalLauncher.OpenTerminal();
                e.Handled = true;
                return;
            }

            // Super+L -> Lock screen
            if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                (Application.Current as App)?.LockSession();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && (Application.Current as App)?.DismissTransientShellSurfaces() == true)
            {
                e.Handled = true;
            }
        }
    }
}
