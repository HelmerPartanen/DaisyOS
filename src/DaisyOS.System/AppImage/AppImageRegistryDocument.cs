namespace DaisyOS.System.AppImage;

internal sealed class AppImageRegistryDocument
{
    public int Version { get; set; } = 1;
    public List<AppImageInstallation> Installations { get; set; } = [];
}
