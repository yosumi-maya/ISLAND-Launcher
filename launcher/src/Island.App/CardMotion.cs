using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Island.Core;

namespace Island.App;

public static class CardMotion
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached("Mode", typeof(CardAnimationMode), typeof(CardMotion),
        new FrameworkPropertyMetadata(CardAnimationMode.Off, FrameworkPropertyMetadataOptions.Inherits, Changed), value => value is CardAnimationMode mode && Enum.IsDefined(mode));
    public static CardAnimationMode GetMode(DependencyObject target) => (CardAnimationMode)target.GetValue(ModeProperty);
    public static void SetMode(DependencyObject target, CardAnimationMode value) => target.SetValue(ModeProperty, value);
    public static readonly DependencyProperty IsCardProperty = DependencyProperty.RegisterAttached("IsCard", typeof(bool), typeof(CardMotion), new PropertyMetadata(false, Register));
    public static bool GetIsCard(DependencyObject target) => (bool)target.GetValue(IsCardProperty);
    public static void SetIsCard(DependencyObject target, bool value) => target.SetValue(IsCardProperty, value);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(Motion), typeof(CardMotion));
    private static void Register(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is FrameworkElement card && args.NewValue is true && card.GetValue(StateProperty) is null) card.SetValue(StateProperty, new Motion(card));
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target.GetValue(StateProperty) is Motion motion)
        {
            motion.Reset(false);
            if (target is FrameworkElement { IsMouseOver: true, IsVisible: true } card && GetMode(card) != CardAnimationMode.Off) motion.Follow(Mouse.GetPosition(card));
        }
    }
    internal static bool IsMoving(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current.GetValue(StateProperty) is Motion motion && motion.Active) return true;
        return false;
    }
    public static void PreviewPointer(FrameworkElement card, Point point)
    {
        if (card.GetValue(StateProperty) is Motion motion) motion.Follow(point);
    }
    private sealed class Motion
    {
        private readonly FrameworkElement card;
        private readonly ScaleTransform scale = new(1, 1);
        private readonly SkewTransform tilt = new();
        private readonly RotateTransform turn = new();
        private readonly TranslateTransform lift = new();
        private readonly DropShadowEffect shadow = new() { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 7, Direction = 270, Opacity = 0, RenderingBias = RenderingBias.Performance };
        public bool Active { get; private set; }
        private bool hovered;
        public Motion(FrameworkElement element)
        {
            card = element;
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(tilt); group.Children.Add(turn); group.Children.Add(lift);
            card.RenderTransformOrigin = new Point(.5, .5); card.RenderTransform = group;
            card.MouseEnter += (_, e) => Follow(e.GetPosition(card)); card.MouseMove += (_, e) => Follow(e.GetPosition(card));
            card.MouseLeave += (_, _) => Reset(); card.Unloaded += (_, _) => Reset(false);
        }
        public void Follow(Point point)
        {
            var mode = GetMode(card);
            if (mode == CardAnimationMode.Off || card.ActualWidth <= 0 || card.ActualHeight <= 0) return;
            var x = Math.Clamp(point.X / card.ActualWidth * 2 - 1, -1, 1); var y = Math.Clamp(point.Y / card.ActualHeight * 2 - 1, -1, 1);
            if (!hovered)
            {
                hovered = Active = true; card.CacheMode = new BitmapCache(); card.Effect = shadow; Panel.SetZIndex(card, 2);
                var size = mode switch { CardAnimationMode.Zoom => 1.025, CardAnimationMode.Spring => 1.015, CardAnimationMode.Lift => 1, CardAnimationMode.Magnetic => 1.008, _ => 1.012 };
                var offset = mode switch { CardAnimationMode.Lift or CardAnimationMode.Spring => -5, CardAnimationMode.FloatTilt => -3, _ => 0 };
                var duration = mode switch { CardAnimationMode.Spring => 320, CardAnimationMode.Zoom => 220, _ => 150 };
                IEasingFunction? easing = mode == CardAnimationMode.Spring ? new BackEase { Amplitude = .65, EasingMode = EasingMode.EaseOut } : null;
                Animate(scale, ScaleTransform.ScaleXProperty, size, duration, easing); Animate(scale, ScaleTransform.ScaleYProperty, size, duration, easing);
                Animate(lift, TranslateTransform.YProperty, offset, duration, easing);
                shadow.BlurRadius = mode == CardAnimationMode.Zoom ? 24 : 18;
                Animate(shadow, DropShadowEffect.OpacityProperty, mode == CardAnimationMode.FloatTilt ? .18 : .14);
            }
            if (mode == CardAnimationMode.FloatTilt)
            {
                Animate(tilt, SkewTransform.AngleXProperty, -y * 1.4, 90); Animate(tilt, SkewTransform.AngleYProperty, x * 1.2, 90);
                Animate(turn, RotateTransform.AngleProperty, x * .9, 90);
            }
            else if (mode == CardAnimationMode.Magnetic)
            {
                Animate(lift, TranslateTransform.XProperty, x * 4, 100); Animate(lift, TranslateTransform.YProperty, y * 3, 100);
            }
        }
        public void Reset(bool animate = true)
        {
            if (!Active && animate) return;
            hovered = false; Active = animate;
            foreach (var (target, property, value) in new (Animatable, DependencyProperty, double)[] {
                (scale, ScaleTransform.ScaleXProperty, 1), (scale, ScaleTransform.ScaleYProperty, 1), (lift, TranslateTransform.XProperty, 0), (lift, TranslateTransform.YProperty, 0),
                (tilt, SkewTransform.AngleXProperty, 0), (tilt, SkewTransform.AngleYProperty, 0), (turn, RotateTransform.AngleProperty, 0), (shadow, DropShadowEffect.OpacityProperty, 0) })
                if (animate) Animate(target, property, value); else { target.BeginAnimation(property, null); target.SetValue(property, value); }
            Panel.SetZIndex(card, 0);
            if (!animate) { card.Effect = null; card.CacheMode = null; }
            else
            {
                var finish = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
                finish.Completed += (_, _) => { if (!hovered) { Active = false; card.Effect = null; card.CacheMode = null; } };
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, finish);
            }
        }
        private static void Animate(Animatable target, DependencyProperty property, double value, int milliseconds = 150, IEasingFunction? easing = null)
            => target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
