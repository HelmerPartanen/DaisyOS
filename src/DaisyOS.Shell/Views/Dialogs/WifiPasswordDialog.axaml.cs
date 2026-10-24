using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DaisyOS.Core.Services;

namespace DaisyOS.Shell.Views.Dialogs;

public partial class WifiPasswordDialog : Window
{
    private readonly IWirelessNetworkService _wirelessService;
    private readonly string _ssid;
    private readonly Action<string>? _onDemoConnect;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isConnecting;
    private bool _closed;

    public bool WasConnected { get; private set; }

    public WifiPasswordDialog()
    {
        InitializeComponent();
        _wirelessService = null!;
        _ssid = string.Empty;
    }

    public WifiPasswordDialog(
        IWirelessNetworkService wirelessService,
        string ssid,
        string securityType,
        Action<string>? onDemoConnect = null,
        int signalPercent = 100)
    {
        InitializeComponent();
        _wirelessService = wirelessService;
        _ssid = ssid;
        _onDemoConnect = onDemoConnect;

        SsidText.Text = ssid;
        SecurityText.Text = string.IsNullOrWhiteSpace(securityType) ? Loc.Get("Secured Network") : securityType;
        WifiIcon.SignalPercent = Math.Clamp(signalPercent, 0, 100);

        ShowPasswordCheckBox.IsCheckedChanged += (_, _) =>
        {
            PasswordBox.PasswordChar = ShowPasswordCheckBox.IsChecked == true ? '\0' : '•';
        };

        Opened += (_, _) => PasswordBox.Focus();
        Deactivated += (_, _) =>
        {
            if (!_closed)
            {
                WasConnected = false;
                Close(false);
            }
        };
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        WasConnected = false;
        Close(false);
    }

    private async void OnConnectClicked(object? sender, RoutedEventArgs e) => await ConnectAsync();

    internal async Task ConnectAsync()
    {
        if (_isConnecting || _closed) return;
        // Spaces are part of a network credential. Let NetworkManager validate
        // the security type instead of assuming every network uses WPA-PSK.
        var password = PasswordBox.Text ?? string.Empty;
        if (password.Length == 0)
        {
            ShowFailure("Enter the Wi-Fi password.");
            return;
        }

        _isConnecting = true;
        var token = _lifetime.Token;
        StatusMessageText.IsVisible = false;
        ConnectingProgress.IsVisible = true;
        ConnectButton.IsEnabled = false;
        ConnectButton.Content = Loc.Get("Connecting…");
        PasswordBox.IsEnabled = false;
        ShowPasswordCheckBox.IsEnabled = false;

        try
        {
            bool success;
            if (_onDemoConnect is not null)
            {
                await Task.Delay(800, token);
                _onDemoConnect(_ssid);
                success = true;
            }
            else
            {
                success = await _wirelessService.ConnectToNetworkAsync(_ssid, password, token);
            }

            if (_closed || token.IsCancellationRequested) return;
            if (success)
            {
                WasConnected = true;
                Close(true);
            }
            else ShowFailure("Could not connect. Check the password and make sure the network is in range, then try again.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closed) ShowFailure("Could not connect. Check that Wi-Fi is available, then try again.");
        }
        finally
        {
            _isConnecting = false;
            if (!_closed)
            {
                ConnectingProgress.IsVisible = false;
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = Loc.Get("Connect");
                PasswordBox.IsEnabled = true;
                ShowPasswordCheckBox.IsEnabled = true;
                PasswordBox.Focus();
            }
        }
    }

    private void ShowFailure(string message)
    {
        StatusMessageText.Text = message;
        StatusMessageText.IsVisible = true;
        if (PasswordBox.IsEnabled) PasswordBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        PasswordBox.Text = string.Empty;
        base.OnClosed(e);
    }
}
