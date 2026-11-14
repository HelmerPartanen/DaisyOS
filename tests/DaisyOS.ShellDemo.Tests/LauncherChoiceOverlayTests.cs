using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LauncherChoiceOverlayTests
{
    [AvaloniaTheory]
    [InlineData(1, 1)]
    [InlineData(1.25, 1.25)]
    [InlineData(1.5, 1.5)]
    [InlineData(2, 2)]
    [InlineData(1.5, 2)]
    public async Task NativeChoiceUsesSeparateSurfaceAndAnchorsUsingBackendCoordinates(double sourceScale, double overlayScale)
    {
        var value = "Light";
        var registry = new SearchSettingsRegistry();
        registry.RegisterChoice(new("theme", "Theme", "Appearance", [], ["Light", "Dark", "Automatic"],
            _ => Task.FromResult(value), (choice, _) => { value = choice; return Task.CompletedTask; }));
        var model = new LauncherViewModel(null, [], new DaisySearchService(new ApplicationSearchProvider(), registry));
        var launcher = new LauncherView(model);
        var menu = launcher.DetachSearchChoiceMenuForNativeOverlay()!;
        var overlay = new Window { Width = 1280, Height = 800, Content = new Grid { Children = { menu } } };
        var source = new Window { Width = 900, Height = 700, Content = launcher };
        source.Show();
        overlay.Show();
        source.SetRenderScaling(sourceScale);
        overlay.SetRenderScaling(overlayScale);
        try
        {
            model.SearchText = "theme";
            await model.SearchReady;
            source.UpdateLayout();
            var choice = Assert.Single(launcher.GetVisualDescendants().OfType<ComboBox>());
            var states = new List<bool>();
            launcher.SearchChoiceMenuVisibilityChanged += (_, visible) => states.Add(visible);
            choice.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            overlay.UpdateLayout();
            Assert.True(launcher.IsSearchChoiceMenuVisible);
            Assert.False(choice.IsDropDownOpen);
            Assert.Same(overlay, TopLevel.GetTopLevel(menu));
            var anchor = overlay.PointToClient(choice.PointToScreen(new Point(choice.Bounds.Width, choice.Bounds.Height)));
            Assert.InRange(Math.Abs(menu.Bounds.Right - anchor.X), 0, 2);
            Assert.InRange(Math.Abs(menu.Bounds.Top - anchor.Y - 4), 0, 2);
            Assert.True(menu.Bounds.Bottom <= overlay.Bounds.Height - 6);
            var list = Assert.Single(menu.GetVisualDescendants().OfType<ListBox>());
            Assert.Equal(3, list.ItemCount);
            // Native items retain mouse selection, and the binding still uses canonical values.
            var dark = list.GetVisualDescendants().OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Dark"));
            var point = dark.TranslatePoint(new Point(dark.Bounds.Width / 2, dark.Bounds.Height / 2), overlay)!.Value;
            overlay.MouseDown(point, MouseButton.Left);
            overlay.MouseUp(point, MouseButton.Left);
            Assert.Equal("Dark", value);
            Assert.False(launcher.IsSearchChoiceMenuVisible);
            choice.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            overlay.UpdateLayout();
            Assert.InRange(Math.Abs(menu.Bounds.Right - anchor.X), 0, 2);
            Assert.True(list.IsKeyboardFocusWithin);
            overlay.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            overlay.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            overlay.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            overlay.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("Automatic", value);
            Assert.False(launcher.IsSearchChoiceMenuVisible);
            choice.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            overlay.UpdateLayout();
            overlay.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            overlay.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(launcher.IsSearchChoiceMenuVisible);
            Assert.Equal("Automatic", value);
            Assert.Equal(new[] { true, false, true, false, true, false }, states);
        }
        finally { launcher.CloseSearchChoiceMenu(); launcher.CloseImmediate(); source.Close(); overlay.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(20, 20)]
    [InlineData(600, 440)]
    public void PlacementClampsAndFlipsWithoutLeavingTheSurface(double x, double y)
    {
        var anchor = new Rect(x, y, 144, 40);
        var placement = LauncherView.PlaceSearchChoiceMenu(anchor, new Size(640, 480), new Size(180, 300));
        Assert.True(placement.Left >= 6 && placement.Right <= 634);
        Assert.True(placement.Top >= 6 && placement.Bottom <= 474);
        if (y > 400) Assert.True(placement.Bottom < anchor.Top);
        else Assert.True(placement.Top > anchor.Bottom);
    }
}
