using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Notes;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SidebarWindowChromeTests
{
    [AvaloniaFact]
    public async Task AppWindowsKeepFullHeightSidebarsAndNativeCaptionStates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-chrome-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var window in new Window[] { new SettingsWindow(), new NotesWindow(new NotesViewModel(folder)), new FilesWindow(folder, false) })
            {
                window.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                    var title = window is FilesWindow
                        ? window.FindControl<Control>("TabRow")!
                        : window.GetVisualDescendants().OfType<AppWindowTitleBar>().Single();
                    var sidebar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NavigationSidebar");
                    var layout = (Grid)sidebar.Parent!;
                    Assert.Equal(layout.Bounds.Height, sidebar.Bounds.Height, precision: 1);
                    if (window is FilesWindow)
                        foreach (var caption in window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("CaptionButton")))
                        {
                            Assert.Equal(0, caption.Bounds.Y, precision: 1);
                            Assert.Equal(((StackPanel)caption.Parent!).Bounds.Height, caption.Bounds.Height, precision: 1);
                        }
                    foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    {
                        window.RequestedThemeVariant = theme;
                        await Task.Delay(250);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(sidebar.TryFindResource("SurfaceRaisedSubtleBrush", theme, out var brush));
                        Assert.Equal(((ISolidColorBrush)brush!).Color, ((ISolidColorBrush)sidebar.Background!).Color);
                        if (title is AppWindowTitleBar)
                        {
                            Assert.Empty(title.GetVisualDescendants().OfType<Image>());
                            Assert.Empty(title.GetVisualDescendants().OfType<TextBlock>());
                        }
                    }
                    // Raw second presses must use the caption-button transition,
                    // without handing that press to the compositor's move grab.
                    var dragSurface = title as Border ?? title.GetVisualDescendants().OfType<Border>().First();
                    using (var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true))
                    {
                        foreach (var expected in new[] { WindowState.Maximized, WindowState.Normal })
                        {
                            var press = new PointerPressedEventArgs(dragSurface, pointer, window,
                                dragSurface.TranslatePoint(new Point(20, 20), window)!.Value, 0,
                                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                                KeyModifiers.None, clickCount: 2);
                            dragSurface.RaiseEvent(press);
                            Dispatcher.UIThread.RunJobs();
                            Assert.True(press.Handled);
                            Assert.Equal(expected, window.WindowState);
                            // A gesture recognizer must not toggle the same press again.
                            dragSurface.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press));
                            Assert.Equal(expected, window.WindowState);
                        }
                    }
                    window.WindowState = WindowState.Maximized;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(WindowCaptionKind.Restore, window.GetVisualDescendants().OfType<WindowCaptionGlyph>().Single(g => g.Name is "MaximizeIcon" or "MaximizeRestoreIcon").Kind);
                    window.KeyPressQwerty(PhysicalKey.F11, Avalonia.Input.RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(title.IsVisible);
                    window.KeyPressQwerty(PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(title.IsVisible);
                    Assert.Equal(WindowState.Maximized, window.WindowState);
                }
                finally { window.Close(); }
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
