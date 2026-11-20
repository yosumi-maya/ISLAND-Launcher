using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.App;

internal static class PageTransitions
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(TranslateTransform), typeof(PageTransitions));
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(170);
    internal static void Enter(FrameworkElement page, bool? animate = null)
    {
        Reset(page);
        if (!(animate ?? SystemParameters.ClientAreaAnimation)) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.35, 1, Duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        Slide(page, 7, Duration, ease);
    }

    internal static void EnterFolderResults(FrameworkElement results)
    {
        Reset(results);
        if (!SystemParameters.ClientAreaAnimation) return;
        Slide(results, 4, TimeSpan.FromMilliseconds(120), new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    private static void Slide(FrameworkElement page, double distance, TimeSpan duration, IEasingFunction ease)
    {
        var offset = page.GetValue(OffsetProperty) as TranslateTransform;
        if (offset is null)
        {
            offset = new TranslateTransform(); page.SetValue(OffsetProperty, offset);
            page.RenderTransform = offset;
        }
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    internal static void Reset(FrameworkElement page)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null); page.Opacity = 1;
        if (page.GetValue(OffsetProperty) is TranslateTransform offset)
        {
            offset.BeginAnimation(TranslateTransform.YProperty, null); offset.Y = 0;
        }
    }
}
