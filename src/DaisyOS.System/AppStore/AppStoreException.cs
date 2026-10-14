namespace DaisyOS.System.AppStore;

/// <summary>A store failure with a message suitable for display to the user.</summary>
public sealed class AppStoreException : IOException
{
    public AppStoreException(string message, Exception? inner = null) : base(message, inner) { }
}
