using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Converters;
using DaisyOS.Shell.Services.Notifications;
using DaisyOS.Shell.Views.Components.Notifications;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class NotificationArtworkTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationLayoutKeepsBrandingCompactAndAttachmentsUncropped(bool withAttachment)
    {
        using var fixture = new Fixture();
        var photo = fixture.Image("photo.png");
        var item = new NotificationItem("New message", "A notification with readable content and consistent application branding.", DateTimeOffset.Now,
            SourceAppId: "org.daisyos.mail", SourceAppName: "Mail", PreviewImage: withAttachment ? photo : null);
        var toast = new NotificationToastView { Width = 380, Margin = new Thickness(12),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, DataContext = item };
        var window = new Window { Width = 420, Height = 400, Content = toast,
            Background = Brushes.DimGray, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            var card = toast.NotificationCard;
            Assert.Equal(withAttachment, card.FindControl<Border>("PreviewFrame")!.IsVisible);
            Assert.Equal(Stretch.Uniform, card.FindControl<Image>("PreviewImage")!.Stretch);
            Assert.Equal(32, card.FindControl<Image>("ApplicationIcon")!.Bounds.Width);
            Assert.True(toast.Bounds.Height < (withAttachment ? 300 : 200));
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save($"/tmp/daisy-notification-{(withAttachment ? "attachment" : "compact")}.png", new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationLogoHintBecomesHeaderArtworkRatherThanLargePreview(bool fileUri)
    {
        using var fixture = new Fixture();
        var path = fixture.Image("icons/Antigravity.png");
        var item = new NotificationItem("Antigravity", "Agent finished", DateTimeOffset.Now,
            SourceAppId: "unknown-artwork-fixture", SourceAppName: "Antigravity", Icon: "notes",
            PreviewImage: fileUri ? new Uri(path).AbsoluteUri : path);
        var artwork = NotificationArtwork.Resolve(item);
        Assert.Equal(path, artwork.AppIconPath);
        Assert.Null(artwork.PreviewSource);
        Assert.IsAssignableFrom<IImage>(new NotificationIconValueConverter().Convert(item, typeof(IImage), null, CultureInfo.InvariantCulture));
        var card = new NotificationCard { DataContext = item, Width = 380 };
        var window = new Window { Width = 420, Height = 400, Content = card };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(card.FindControl<Image>("ApplicationIcon")!.IsVisible);
            Assert.False(card.FindControl<Border>("PreviewFrame")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InstalledSenderIdentityWinsOverAnUnrelatedGenericAppIcon()
    {
        var item = new NotificationItem("Notice", "Body", DateTimeOffset.Now,
            SourceAppId: "org.daisyos.mail", Icon: "notes");
        var result = NotificationArtwork.Resolve(item);
        Assert.EndsWith("/Mail.png", result.AppIconPath);
        Assert.Null(result.PreviewSource);
    }

    [AvaloniaFact]
    public void GenuineSquareAttachmentsRemainPreviewsAndReplacementClearsOldImage()
    {
        using var fixture = new Fixture();
        var icon = fixture.Image("header.png");
        var photo = fixture.Image("photo.png");
        var item = new NotificationItem("Photo", "Body", DateTimeOffset.Now, Icon: icon, PreviewImage: photo);
        Assert.Equal(icon, NotificationArtwork.Resolve(item).AppIconPath);
        Assert.Equal(photo, NotificationArtwork.Resolve(item).PreviewSource);
        var converter = new NotificationPreviewValueConverter();
        Assert.Equal(true, converter.Convert(item, typeof(bool), "Visible", CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert(item with { PreviewImage = icon }, typeof(bool), "Visible", CultureInfo.InvariantCulture));
        Assert.Equal(false, converter.Convert(item with { PreviewImage = null }, typeof(bool), "Visible", CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void ExplicitIconFileNamedLikeABuiltInAppKeepsItsActualImage()
    {
        using var fixture = new Fixture();
        var path = fixture.Image("notes.png");
        var item = new NotificationItem("Notice", "Body", DateTimeOffset.Now, Icon: new Uri(path).AbsoluteUri);
        Assert.Equal(path, NotificationArtwork.Resolve(item).AppIconPath);
    }

    [AvaloniaFact]
    public void FriendlyAppNamesDoNotOverrideExplicitIconsAndPhotoFoldersDoNotImplyLogos()
    {
        using var fixture = new Fixture();
        var icon = fixture.Image("actual-icon.png");
        var photo = fixture.Image("icons/photo.png");
        var item = new NotificationItem("Notice", "Body", DateTimeOffset.Now,
            SourceAppId: "Mail", SourceAppName: "Mail", Icon: icon, PreviewImage: photo);
        var result = NotificationArtwork.Resolve(item);
        Assert.Equal(icon, result.AppIconPath);
        Assert.Equal(photo, result.PreviewSource);
    }

    [AvaloniaFact]
    public void MissingOrInvalidImagesDoNotCreateBlankPreviewAreas()
    {
        using var fixture = new Fixture();
        var invalid = Path.Combine(fixture.Directory, "invalid.png");
        File.WriteAllText(invalid, "not an image");
        var converter = new NotificationPreviewValueConverter();
        foreach (var image in new[] { invalid, invalid + ".missing.png", "data:image/png;base64,not-base64", "https://example.invalid/notes.png" })
        {
            var item = new NotificationItem("Notice", "Body", DateTimeOffset.Now, PreviewImage: image);
            Assert.Equal(false, converter.Convert(item, typeof(bool), "Visible", CultureInfo.InvariantCulture));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "daisy-notification-artwork-" + Guid.NewGuid().ToString("N"));
        public Fixture() => global::System.IO.Directory.CreateDirectory(Directory);
        public string Image(string relative)
        {
            var path = Path.Combine(Directory, relative);
            global::System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new SkiaSharp.SKBitmap(32, 32);
            bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path);
            data.SaveTo(stream);
            return path;
        }
        public void Dispose() => global::System.IO.Directory.Delete(Directory, true);
    }
}
