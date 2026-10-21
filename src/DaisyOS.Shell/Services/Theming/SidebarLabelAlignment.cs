using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Centers sidebar label ink rather than a font's unequal ascent/descent space.</summary>
public sealed class SidebarLabelAlignment : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<SidebarLabelAlignment, TextBlock, bool>("Enabled");
    private static readonly ConditionalWeakTable<TextBlock, State> States = new();

    static SidebarLabelAlignment() => EnabledProperty.Changed.AddClassHandler<TextBlock>((text, change) =>
    {
        if (change.NewValue is true) States.GetValue(text, value => new State(value)).Attach();
        else if (States.TryGetValue(text, out var state)) state.Detach();
    });

    public static bool GetEnabled(TextBlock text) => text.GetValue(EnabledProperty);
    public static void SetEnabled(TextBlock text, bool value) => text.SetValue(EnabledProperty, value);

    private sealed class State
    {
        private readonly TextBlock _text;
        private readonly TranslateTransform _offset = new();
        private bool _attached;

        public State(TextBlock text)
        {
            _text = text;
            text.AttachedToVisualTree += (_, _) => Attach();
            text.DetachedFromVisualTree += (_, _) => Detach();
        }

        public void Attach()
        {
            if (_attached || !GetEnabled(_text) || TopLevel.GetTopLevel(_text) is null) return;
            // Leave authored animation transforms alone.
            if (_text.RenderTransform is not null && !ReferenceEquals(_text.RenderTransform, _offset)) return;
            _attached = true;
            _text.RenderTransform = _offset;
            _text.LayoutUpdated += Update;
        }

        public void Detach()
        {
            _text.LayoutUpdated -= Update;
            _attached = false;
            if (ReferenceEquals(_text.RenderTransform, _offset)) _text.RenderTransform = null;
        }

        private void Update(object? sender, EventArgs e)
        {
            if (_text.Bounds.Height <= 0 || string.IsNullOrWhiteSpace(_text.Text))
            {
                _offset.Y = 0;
                return;
            }
            var layout = _text.TextLayout;
            // Ink bottom = line height + overhang; subtract half its extent
            // to find the visible center. TextLayout includes fallback glyphs.
            var contentHeight = Math.Max(0, _text.Bounds.Height - _text.Padding.Top - _text.Padding.Bottom);
            var drawTop = Math.Min(0, (contentHeight - layout.Height) / 2);
            var shift = layout.Extent > 0
                ? contentHeight / 2 - drawTop - layout.Height - layout.OverhangAfter + layout.Extent / 2
                : 0;
            var scaling = TopLevel.GetTopLevel(_text)?.RenderScaling ?? 1;
            _offset.Y = double.IsFinite(shift) ? Math.Round(shift * scaling) / scaling : 0;
        }
    }
}
