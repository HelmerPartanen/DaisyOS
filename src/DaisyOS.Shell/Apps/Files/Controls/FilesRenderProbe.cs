using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.Files;

public sealed class FilesRenderProbe : Control
{
    public static readonly StyledProperty<FilesTabViewModel?> TabProperty =
        AvaloniaProperty.Register<FilesRenderProbe, FilesTabViewModel?>(nameof(Tab));
    public FilesTabViewModel? Tab { get => GetValue(TabProperty); set => SetValue(TabProperty, value); }
    public FilesRenderProbe() => IsHitTestVisible = false;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TabProperty) return;
        if (change.OldValue is FilesTabViewModel previous) previous.PropertyChanged -= OnTabChanged;
        if (change.NewValue is FilesTabViewModel current) current.PropertyChanged += OnTabChanged;
        InvalidateVisual();
    }
    private void OnTabChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilesTabViewModel.IsLoading) or nameof(FilesTabViewModel.Entries)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Tab?.MarkFirstFrame();
    }
}
