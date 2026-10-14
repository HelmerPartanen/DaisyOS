using System;
using Avalonia.Controls;

namespace Avalonia.Wayland;

/// <summary>Chooses client decorations before the native window negotiates its first configure.</summary>
public static class ClientSideDecorationsWindow
{
    [ThreadStatic]
    private static bool _pending;

    internal static bool ConsumeRequest()
    {
        var pending = _pending;
        _pending = false;
        return pending;
    }

    public static T Create<T>(Func<T> factory) where T : Window
    {
        ArgumentNullException.ThrowIfNull(factory);
        var previous = _pending;
        _pending = true;
        try { return factory(); }
        finally { _pending = previous; }
    }
}
