namespace DaisyOS.Core.Models;

public sealed record BatteryStatus(
    bool IsPresent,
    int? ChargePercent,
    string State,
    string Detail)
{
    public string DisplayText => !IsPresent
        ? "Battery unavailable"
        : ChargePercent is null
            ? State
            : $"{ChargePercent}% {State}".Trim();
}
