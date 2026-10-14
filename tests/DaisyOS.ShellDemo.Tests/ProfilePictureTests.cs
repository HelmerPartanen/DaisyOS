using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Services.Appearance;
using SkiaSharp;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class ProfilePictureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daisy-profile-" + Guid.NewGuid());
    public ProfilePictureTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [AvaloniaFact]
    public async Task ImportRetainsAnIndependentBoundedPictureAndPersistsSelection()
    {
        var source = Path.Combine(_directory, "source.png");
        using (var original = new SKBitmap(2048, 1024))
        {
            original.Erase(SKColors.Red);
            using var encoded = original.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(source);
            encoded.SaveTo(output);
        }
        var store = new AppearanceSettingsStore(Path.Combine(_directory, "appearance.json"));
        var imported = await ProfilePictureStore.ImportAsync(source, store.PreferencesPath);
        File.Delete(source);
        using (var bitmap = SKBitmap.Decode(imported))
        {
            Assert.Equal(512, bitmap.Width);
            Assert.Equal(256, bitmap.Height);
            Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 10));
        }
        await store.SaveAsync(new AppearanceSettings { ProfilePictureUri = imported });
        Assert.Equal(imported, store.Load().ProfilePictureUri);
        await store.SaveAsync(new AppearanceSettings());
        ProfilePictureStore.RemoveOwned(imported, store.PreferencesPath);
        Assert.False(File.Exists(imported));
        Assert.Empty(store.Load().ProfilePictureUri);
    }

    [AvaloniaFact]
    public async Task PhonePhotoOrientationIsAppliedBeforeResizing()
    {
        var source = Path.Combine(_directory, "phone.jpg");
        using var original = new SKBitmap(640, 320);
        original.Erase(SKColors.Blue);
        using var encoded = original.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();
        byte[] exif = [0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0,
            73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0,
            1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        await File.WriteAllBytesAsync(source, jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray());
        var imported = await ProfilePictureStore.ImportAsync(source, Path.Combine(_directory, "appearance.json"));
        using var bitmap = SKBitmap.Decode(imported);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
    }

    [AvaloniaFact]
    public async Task InvalidPictureCreatesNoManagedCopyAndCleanupPreservesExternalFiles()
    {
        var source = Path.Combine(_directory, "invalid.png");
        await File.WriteAllTextAsync(source, "not an image");
        var preferences = Path.Combine(_directory, "appearance.json");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfilePictureStore.ImportAsync(source, preferences));
        Assert.False(Directory.Exists(Path.Combine(_directory, "profile-pictures")));
        ProfilePictureStore.RemoveOwned(source, preferences);
        Assert.True(File.Exists(source));
    }
}
