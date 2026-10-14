namespace DaisyOS.Shell.Services;

/// <summary>Short, user-readable feedback for shell actions that cannot complete.</summary>
public sealed class ShellFeedbackService
{
    public event EventHandler<string>? MessageShown;

    public void Show(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) MessageShown?.Invoke(this, message);
    }
}
