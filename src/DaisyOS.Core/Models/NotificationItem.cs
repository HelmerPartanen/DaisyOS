using System;

namespace DaisyOS.Core.Models;

public enum NotificationCategory { General, Application, System, Network, Battery, Updates, Security }
public enum NotificationUrgency { Low, Normal, High, Critical }

public sealed record NotificationItem(
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    string? Id = null,
    string? SourceAppId = null,
    string? Icon = null,
    string? SystemIconGlyph = null,
    bool IsError = false,
    NotificationCategory Category = NotificationCategory.General,
    NotificationUrgency Urgency = NotificationUrgency.Normal,
    string? ActionLabel = null,
    string? ActionId = null,
    bool IsRead = false,
    string? SecondaryActionLabel = null,
    string? SecondaryActionId = null,
    string? PreviewImage = null,
    int TimeoutSeconds = 0,
    string? SourceAppName = null)
{
    public string Id { get; init; } = Id ?? Guid.NewGuid().ToString("N");

    public string DisplaySource => !string.IsNullOrWhiteSpace(SourceAppName) ? SourceAppName :
        string.IsNullOrWhiteSpace(SourceAppId) ? "System" : SourceAppId;

    public bool OpensFromCard => string.IsNullOrWhiteSpace(ActionId) || ActionId == "default" ||
        string.Equals(ActionLabel, "View", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ActionLabel, "Open", StringComparison.OrdinalIgnoreCase) ||
        ActionId.StartsWith("localsend-reveal:", StringComparison.Ordinal);

    public bool HasPrimaryButton => !OpensFromCard && !string.IsNullOrWhiteSpace(ActionLabel);
    public bool HasActionButtons => HasPrimaryButton || !string.IsNullOrWhiteSpace(SecondaryActionLabel);

    public bool HasActions => !string.IsNullOrWhiteSpace(ActionLabel) || !string.IsNullOrWhiteSpace(SecondaryActionLabel);

    public string EffectiveSystemIconGlyph => !string.IsNullOrWhiteSpace(SystemIconGlyph)
        ? SystemIconGlyph
        : Category switch
        {
            NotificationCategory.Network => "wifi-01",
            NotificationCategory.Battery => "battery-medium-01",
            NotificationCategory.Updates => "download-04",
            NotificationCategory.Security => "shield-01",
            NotificationCategory.System => "settings-01",
            _ when IsError || Urgency == NotificationUrgency.Critical => "alert-02",
            _ => "notification-03"
        };
}
