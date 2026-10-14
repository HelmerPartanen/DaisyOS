using System;

namespace DaisyOS.Core.Desktop;

public readonly record struct DesktopItemId(string Value)
{
    public static DesktopItemId NewId() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
