using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core;

namespace Island.App;

public sealed class LaunchBarPresenter : Grid
{
    private readonly TranslateTransform slide = new();
    private FrameworkElement? area;
    private RowDefinition? reservedRow;
    private UIElement? surface;
    private LaunchBarMode mode;
    public bool IsRevealed { get; private set; } = true;
    public double SlideOffset => slide.Y;
    public LaunchBarPresenter() { ClipToBounds = true; }
    public void Attach(FrameworkElement ownerArea, RowDefinition row)
    {
        area = ownerArea; reservedRow = row; surface = Children[0]; surface.RenderTransform = slide;
        area.PreviewMouseMove += (_, e) => TrackPointer(e.GetPosition(area), area.RenderSize);
        area.MouseLeave += (_, _) => { if (!IsKeyboardFocusWithin && mode == LaunchBarMode.OnApproach) Reveal(false); };
        IsKeyboardFocusWithinChanged += (_, _) => { if (IsKeyboardFocusWithin) Reveal(true); else if (mode == LaunchBarMode.OnApproach && !IsMouseOver) Reveal(false); };
        area.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F6 || mode != LaunchBarMode.OnApproach) return;
            Reveal(true); surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true;
        };
        area.IsVisibleChanged += (_, _) => { if (!area.IsVisible && mode == LaunchBarMode.OnApproach) Reveal(false, false); };
    }
    public void Apply(LaunchBarMode value)
    {
        mode = value;
        if (reservedRow is not null) reservedRow.Height = new GridLength(mode == LaunchBarMode.Always ? 84 : 0);
        Reveal(mode == LaunchBarMode.Always || IsKeyboardFocusWithin, false);
    }
    public void TrackPointer(Point point, Size bounds)
    {
        if (mode != LaunchBarMode.OnApproach || IsKeyboardFocusWithin) return;
        if (point.X >= 0 && point.X <= bounds.Width && point.Y >= bounds.Height - 104 && point.Y <= bounds.Height) Reveal(true);
        else if (point.Y < bounds.Height - 144 || point.X < 0 || point.X > bounds.Width || point.Y > bounds.Height) Reveal(false);
    }
    private void Reveal(bool show, bool animate = true)
    {
        if (surface is null) return;
        if (IsRevealed == show && animate) return;
        IsRevealed = show; surface.IsHitTestVisible = show;
        var offset = show ? 0 : 84d;
        if (!animate)
        {
            slide.BeginAnimation(TranslateTransform.YProperty, null); surface.BeginAnimation(OpacityProperty, null);
            slide.Y = offset; surface.Opacity = show ? 1 : 0; return;
        }
        var duration = TimeSpan.FromMilliseconds(show ? 220 : 170);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        surface.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration));
    }
}
