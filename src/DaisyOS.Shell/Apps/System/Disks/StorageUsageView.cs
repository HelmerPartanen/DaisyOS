using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.System.Disks;

/// <summary>A themed, proportional storage bar with a wrapping legend and per-segment explanations.</summary>
public sealed class StorageUsageView : UserControl
{
    public StorageUsageView(DiskStorageSummary summary)
    {
        var track = new Grid { Height = 14 };
        var legend = new WrapPanel { ItemSpacing = 24, LineSpacing = 16 };
        for (var i = 0; i < summary.Segments.Count; i++)
        {
            var segment = summary.Segments[i];
            track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(segment.Bytes, GridUnitType.Star)));
            var fill = new Border();
            fill.Bind(Border.BackgroundProperty, this.GetResourceObservable(segment.BrushKey));
            ToolTip.SetTip(fill, $"{segment.Label}: {DiskItem.Bytes(segment.Bytes)}\n{segment.Description}");
            Grid.SetColumn(fill, i); track.Children.Add(fill);
            var swatch = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            swatch.Bind(Border.BackgroundProperty, this.GetResourceObservable(segment.BrushKey));
            var label = Text(segment.Label, false); label.FontWeight = FontWeight.SemiBold;
            var entry = new StackPanel { MinWidth = 112, Spacing = 6, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { swatch, label } }, Text(DiskItem.Bytes(segment.Bytes), true) } };
            ToolTip.SetTip(entry, segment.Description); legend.Children.Add(entry);
        }
        // Clip the complete track so even tiny final segments retain a true pill cap.
        track.SizeChanged += (_, _) => track.Clip = new RectangleGeometry
        {
            Rect = new Rect(track.Bounds.Size), RadiusX = 7, RadiusY = 7
        };
        Content = new StackPanel { Spacing = 14, Children = { track, legend } };
    }
    private TextBlock Text(string value, bool secondary)
    {
        var text = new TextBlock { Text = value };
        text.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("TypeSizeCaption"));
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(secondary ? "ContentSecondaryBrush" : "ContentPrimaryBrush"));
        return text;
    }
}
