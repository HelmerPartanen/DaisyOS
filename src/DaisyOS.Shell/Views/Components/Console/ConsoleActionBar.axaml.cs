using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.Shell.Views.Components.Console;

public partial class ConsoleActionBar : UserControl
{
    public ConsoleActionBar()
    {
        InitializeComponent();
        SetControllerLayout(ControllerType.PlayStation);
    }

    public void SetControllerLayout(ControllerType controllerType)
    {
        try
        {
            string baseFolder = controllerType == ControllerType.Xbox ? "XboxController" : "PlaystationController";
            string confirmFile = controllerType == ControllerType.Xbox ? "a-filled-green.png" : "outline-blue-cross.png";
            string settingsFile = controllerType == ControllerType.Xbox ? "x-filled-blue.png" : "outline-purple-square.png";
            string tabLeftFile = controllerType == ControllerType.Xbox ? "left-bumper.png" : "plain-L1.png";
            string tabRightFile = controllerType == ControllerType.Xbox ? "right-bumper.png" : "plain-R1.png";

            ConfirmBtnImage.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/{baseFolder}/{confirmFile}")));
            SettingsBtnImage.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/{baseFolder}/{settingsFile}")));
            TabLeftBtnImage.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/{baseFolder}/{tabLeftFile}")));
            TabRightBtnImage.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/{baseFolder}/{tabRightFile}")));
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[ConsoleActionBar] Error loading button icons: {ex.Message}");
        }
    }
}
