namespace DaisyOS.System.AppImage;

/// <summary>Reports an AppImage problem that can be shown directly to the user.</summary>
public sealed class AppImageException : Exception
{
    public AppImageException(string message) : base(message) { }

    public AppImageException(string message, Exception innerException) : base(message, innerException) { }
}
