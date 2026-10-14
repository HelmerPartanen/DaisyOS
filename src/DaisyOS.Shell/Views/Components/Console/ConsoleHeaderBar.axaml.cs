using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Gaming;

namespace DaisyOS.Shell.Views.Components.Console;

public partial class ConsoleHeaderBar : UserControl
{
    public event EventHandler<string>? TabSelected;
    public event EventHandler<RoutedEventArgs>? SettingsClicked;

    public Button RecentButton => HeaderRecentButton;
    public Button LibraryButton => HeaderLibraryButton;
    public Button SettingsButton => HeaderSettingsIconButton;

    public Button? MediaPrevButton => HeaderMediaWidget?.FindControl<Button>("PreviousButton");
    public Button? MediaPlayButton => HeaderMediaWidget?.FindControl<Button>("PlayPauseButton");
    public Button? MediaNextButton => HeaderMediaWidget?.FindControl<Button>("NextButton");

    public ConsoleHeaderBar()
    {
        InitializeComponent();
        UpdateClock();
    }

    public void UpdateClock()
    {
        if (ClockText != null)
        {
            ClockText.Text = DateTime.Now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern, CultureInfo.CurrentCulture);
        }
    }

    public void UpdateControllerInfo(ControllerType type, string name)
    {
    }

    public void SetActiveTab(string tabName)
    {
        HeaderRecentButton.Classes.Remove("Active");
        HeaderLibraryButton.Classes.Remove("Active");

        switch (tabName)
        {
            case "Recents":
                HeaderRecentButton.Classes.Add("Active");
                break;
            case "Library":
                HeaderLibraryButton.Classes.Add("Active");
                break;
        }
    }

    private void OnHeaderRecentClicked(object? sender, RoutedEventArgs e)
    {
        SetActiveTab("Recents");
        TabSelected?.Invoke(this, "Recents");
    }

    private void OnHeaderLibraryClicked(object? sender, RoutedEventArgs e)
    {
        SetActiveTab("Library");
        TabSelected?.Invoke(this, "Library");
    }

    private void OnSettingsButtonClicked(object? sender, RoutedEventArgs e)
    {
        SettingsClicked?.Invoke(this, e);
    }
}
