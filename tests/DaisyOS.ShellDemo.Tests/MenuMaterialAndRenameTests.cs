using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Compositor;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class MenuMaterialAndRenameTests
{
    [AvaloniaFact]
    public void DesktopMenuUsesSeparateOverlayAndReleasesItWhenClosed()
    {
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Rename" } } };
        var owner = new Button { ContextMenu = menu, Content = "Desktop item", Width = 100, Height = 40 };
        var desktop = new Grid { Children = { owner } };
        var host = new Grid();
        var sourceWindow = new Window { Content = desktop, Width = 500, Height = 400 };
        var hostWindow = new Window { Content = host, Width = 500, Height = 400 };
        bool capturingInput = false;
        var overlay = new DesktopContextMenuOverlay(desktop, host, visible => capturingInput = visible);
        sourceWindow.Show(); hostWindow.Show();
        try
        {
            owner.RaiseEvent(new ContextRequestedEventArgs());
            hostWindow.UpdateLayout();
            Assert.True(menu.IsOpen);
            Assert.True(overlay.IsOpen);
            Assert.True(capturingInput);
            Assert.True(DesktopContextMenuOverlay.GetIsHosted(menu));
            Assert.Same(host, menu.PlacementTarget);
            Assert.Same(hostWindow, TopLevel.GetTopLevel(menu));
            overlay.Close();
            Assert.False(overlay.IsOpen);
            Assert.False(capturingInput);
            Assert.False(DesktopContextMenuOverlay.GetIsHosted(menu));
            Assert.Null(menu.PlacementTarget);
        }
        finally { menu.Close(); hostWindow.Close(); sourceWindow.Close(); }
    }

    [AvaloniaFact]
    public void PopupBlurMaskIncludesSiblingPopupAndClearsOnClose()
    {
        var content = new Grid();
        var button = new Button { Content = "Item" };
        content.Children.Add(button);
        var window = new Window { Content = content, Width = 500, Height = 400 };
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Rename" } } };
        button.ContextMenu = menu;
        var blurType = typeof(DesktopContextMenuOverlay).Assembly.GetType("DaisyOS.Shell.Services.Compositor.KWinBlur")!;
        var flags = global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic;
        var collect = blurType.GetMethod("PopupMaterials", flags)!;
        IEnumerable<Control> Regions() => (IEnumerable<Control>)collect.Invoke(null, [window])!;
        var blur = (IDisposable)Activator.CreateInstance(blurType, [window, window, (Func<IEnumerable<Control>>)Regions])!;
        window.Show();
        try
        {
            menu.Open(button); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(content.GetVisualDescendants(), visual => visual is Border { Name: "MenuBlurHost" });
            Assert.Single(Regions());
            blurType.GetMethod("RefreshImmediately", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.Invoke(blur, null);
            var regions = (global::System.Collections.ICollection)blurType.GetField("_lastRegions", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.GetValue(blur)!;
            Assert.True(regions.Count > 0);
            menu.Close(); window.UpdateLayout();
            blurType.GetMethod("RefreshImmediately", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.Invoke(blur, null);
            Assert.Empty(Regions());
            Assert.Empty((global::System.Collections.IEnumerable)blurType.GetField("_lastRegions", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.GetValue(blur)!);
        }
        finally { menu.Close(); blur.Dispose(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MenuIconsAndRenameFieldsRenderWithReadableThemeColors(bool dark)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        var rename = new TextBox { Classes = { "RenameBox" }, Text = "game-controller-03.svg", Width = 280 };
        var desktopRename = new TextBox { Classes = { "RenameBox", "DesktopRenameBox" }, Text = "Google Chrome", Width = 74, FontSize = 12, TextAlignment = TextAlignment.Center };
        var icon = new DaisyIcon { Icon = "copy", Classes = { "MenuIcon" } };
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Copy", Icon = icon }, new MenuItem { Header = "Open", Icon = new DaisyIcon { Icon = "arrow-square-out", Classes = { "MenuIcon" } } } } };
        var button = new Button { Content = "Menu", ContextMenu = menu };
        panel.Children.Add(rename); panel.Children.Add(desktopRename); panel.Children.Add(button);
        var window = new Window { Content = panel, Width = 360, Height = 350, Background = dark ? Brushes.Black : Brushes.White, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            rename.Focus(); rename.SelectAll();
            Assert.Equal(28, rename.Bounds.Height);
            Assert.InRange(desktopRename.Bounds.Height, 24, 48);
            Directory.CreateDirectory("/tmp/menu-refinements");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/menu-refinements/rename-{dark}.png");
            menu.Open(button);
            await Task.Delay(300); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/menu-refinements/menu-{dark}.png");
            Assert.True(Application.Current!.Resources.TryGetResource("ContentPrimaryBrush", dark ? ThemeVariant.Dark : ThemeVariant.Light, out var resource));
            Assert.Equal(((ISolidColorBrush)resource!).Color, icon.CurrentColor);
        }
        finally { menu.Close(); window.Close(); }
    }
}
