using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherIconIdentityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwipesKeepDistinctIconsMatchedToTheirApplication(bool animated)
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 80)
            .Select(i => new AppEntry($"identity-{i}", $"App {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        if (!animated) launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 800, Content = launcher };
        var icons = new List<WriteableBitmap>();
        var colors = new Dictionary<LauncherItemViewModel, SkiaSharp.SKColor>();
        window.Show();
        try
        {
            window.UpdateLayout();
            for (var i = 0; i < model.AllApps.Count; i++)
            {
                var color = new SkiaSharp.SKColor((byte)(i * 53 + 15), (byte)(i * 97 + 30), (byte)(i * 31 + 45));
                var icon = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var buffer = icon.Lock())
                    for (var y = 0; y < 2; y++)
                        for (var x = 0; x < 2; x++)
                            Marshal.WriteInt32(buffer.Address, y * buffer.RowBytes + x * 4,
                                unchecked((int)(0xff000000u | (uint)color.Red << 16 | (uint)color.Green << 8 | color.Blue)));
                icons.Add(icon);
                colors[model.AllApps[i]] = color;
                model.AllApps[i].IconBitmap = icon;
            }
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            ulong time = 1000;
            void Scroll(double pixels, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), time += 16,
                new PointerPointProperties(), KeyModifiers.None, new Vector(pixels / 4, 0))
                { IsPrecise = true, IsScrollEnd = end });
            void CheckFrame()
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using var encoded = new MemoryStream();
                frame.Save(encoded, PngBitmapEncoderOptions.Default);
                using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
                foreach (var image in launcher.GetVisualDescendants().OfType<Image>().Where(image => image.IsEffectivelyVisible))
                {
                    var item = Assert.IsType<LauncherItemViewModel>(image.DataContext);
                    Assert.Same(item.IconBitmap, image.Source);
                    var center = image.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height / 2), window)!.Value;
                    var clip = viewport.TranslatePoint(default, window)!.Value;
                    if (center.X < clip.X + 1 || center.X >= clip.X + viewport.Bounds.Width - 1 ||
                        center.Y < clip.Y + 1 || center.Y >= clip.Y + viewport.Bounds.Height - 1) continue;
                    Assert.Equal(colors[item], pixels.GetPixel((int)center.X, (int)center.Y));
                }
            }
            CheckFrame();
            foreach (var direction in new[] { -1, -1, 1, -1, 1, 1 })
            {
                Scroll(direction * viewport.Bounds.Width * 0.35);
                CheckFrame();
                Scroll(direction * viewport.Bounds.Width * 0.4);
                var incomingPage = launcher.NeighbourPageGrid;
                var incomingTiles = Enumerable.Range(0, incomingPage.ItemCount).Select(incomingPage.ContainerFromIndex).ToArray();
                CheckFrame();
                Scroll(0, true);
                for (var tick = 0; tick < (animated ? 20 : 3); tick++)
                {
                    await Task.Delay(16);
                    CheckFrame();
                }
                // The incoming page must remain the same visual tree after
                // settlement, rather than recycling the old page underneath it.
                Assert.Same(incomingPage, launcher.CurrentPageGrid);
                Assert.Equal(incomingTiles, Enumerable.Range(0, incomingPage.ItemCount).Select(incomingPage.ContainerFromIndex));
            }
        }
        finally
        {
            launcher.CloseImmediate(); window.Close();
            foreach (var icon in icons) icon.Dispose();
        }
    }
}
