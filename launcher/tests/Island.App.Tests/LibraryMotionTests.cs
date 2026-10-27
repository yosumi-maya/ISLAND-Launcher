using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Island.App;
using Island.Core;

internal static class LibraryMotionTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    public static async Task Run(MainWindow window, LauncherStore store, Action<bool, string> check, Action<Window, string> capture)
    {
        T Find<T>(string name) => (T)window.FindName(name);
        var initial = store.Load().Appearance;
        var continueButton = Find<Button>("ContinueButton"); var add = Find<Button>("AddInstanceButton");
        check(ReferenceEquals(continueButton.Style, add.Style) && continueButton.Padding == add.Padding && continueButton.FontSize == add.FontSize
            && ((SolidColorBrush)continueButton.Background).Color == ((SolidColorBrush)add.Background).Color,
            "Continue uses the same primary button style, color, text size and padding as add instance");
        Find<RadioButton>("NavSettings").IsChecked = true;
        var approach = Find<RadioButton>("LaunchBarApproachBox"); var always = Find<RadioButton>("LaunchBarAlwaysBox"); var choices = Find<WrapPanel>("CardAnimationChoices");
        RadioButton Choice(CardAnimationMode mode) => choices.Children.OfType<RadioButton>().Single(button => (CardAnimationMode)button.Tag == mode);
        check(choices.Children.OfType<RadioButton>().Select(button => button.Content as string).SequenceEqual(new[] { "关", "悬浮跟随", "轻盈上浮", "柔和缩放", "弹性跃动", "磁吸跟随" }),
            "Card animation settings offer Off, the existing effect and four distinct effects without a dropdown");
        foreach (var width in new[] { 1020d, 1240d })
        {
            window.Width = width; window.Height = width == 1020 ? 700 : 830; window.UpdateLayout(); choices.BringIntoView(); window.UpdateLayout();
            check(choices.Children.OfType<RadioButton>().All(button => button.ActualWidth > 0 && button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), choices).X <= choices.ActualWidth + 1),
                $"All animation choices fit inside the settings card at {width}px");
            capture(window, "CardAnimation-Settings-" + (int)width);
        }
        approach.IsChecked = true; Find<RadioButton>("NavLibrary").IsChecked = true; window.UpdateLayout();
        var host = Find<LaunchBarPresenter>("LaunchBarHost"); var bar = Find<MaterialBorder>("LaunchBarSurface"); var library = Find<Grid>("LibraryPage");
        check(store.Load().Appearance.LaunchBarMode == LaunchBarMode.OnApproach && library.RowDefinitions[3].Height.Value == 0 && !host.IsRevealed && !bar.IsHitTestVisible,
            "Approach mode persists immediately, reclaims the footer row and makes the hidden bar click-through");
        host.TrackPointer(new Point(library.ActualWidth / 2, library.ActualHeight - 20), library.RenderSize);
        await Task.Delay(75);
        check(host.IsRevealed && host.SlideOffset is > 0 and < 84 && bar.Opacity is > 0 and < 1, "Approaching the bottom starts a real upward slide and fade transition");
        capture(window, "LaunchBar-Revealing"); await Task.Delay(230);
        check(Math.Abs(host.SlideOffset) < .01 && bar.Opacity > .99 && bar.IsHitTestVisible, "The revealed launch bar is fully visible and interactive");
        capture(window, "LaunchBar-Revealed");
        host.TrackPointer(new Point(100, 80), library.RenderSize); await Task.Delay(50);
        host.TrackPointer(new Point(100, library.ActualHeight - 10), library.RenderSize); await Task.Delay(250);
        check(host.IsRevealed && host.SlideOffset < .01 && bar.Opacity > .99, "Rapid pointer exit and return cannot complete an obsolete hide animation");
        host.TrackPointer(new Point(100, 80), library.RenderSize); await Task.Delay(210);
        check(!host.IsRevealed && host.SlideOffset >= 83 && !bar.IsHitTestVisible, "Moving away slides the bar below its clipped host");
        capture(window, "LaunchBar-Hidden");
        library.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(library)!, Environment.TickCount, Key.F6) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        check(host.IsRevealed, "F6 reveals the automatic launch bar for keyboard access");

        var list = Find<ListBox>("InstanceList"); window.UpdateLayout();
        var card = Children<Grid>(list).First(CardMotion.GetIsCard); var selected = list.SelectedItem;
        CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .9, card.ActualHeight * .25)); await Task.Delay(210);
        var transforms = (TransformGroup)card.RenderTransform;
        check(transforms.Children.OfType<ScaleTransform>().Single().ScaleX > 1 && transforms.Children.OfType<TranslateTransform>().Single().Y < 0
            && transforms.Children.OfType<RotateTransform>().Single().Angle > .1 && card.Effect is not null && card.CacheMode is BitmapCache && ReferenceEquals(list.SelectedItem, selected),
            "Hover lifts and softly turns the card toward the pointer without changing selection");
        capture(window, "Instance-Hover");
        CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .1, card.ActualHeight * .8)); await Task.Delay(130);
        check(transforms.Children.OfType<RotateTransform>().Single().Angle < -.1, "Moving across the card reverses its slight rotation");
        Find<RadioButton>("NavSettings").IsChecked = true; Choice(CardAnimationMode.Off).IsChecked = true; window.UpdateLayout();
        check(store.Load().Appearance.CardAnimation == CardAnimationMode.Off && card.Effect is null && transforms.Children.OfType<ScaleTransform>().Single().ScaleX == 1
            && transforms.Children.OfType<RotateTransform>().Single().Angle == 0, "Disabling card motion cancels the active transform, shadow and cache immediately");
        CardMotion.PreviewPointer(card, new Point(0, 0)); await Task.Delay(120);
        check(transforms.Children.OfType<RotateTransform>().Single().Angle == 0, "Disabled cards ignore further pointer movement");
        var scale = transforms.Children.OfType<ScaleTransform>().Single(); var lift = transforms.Children.OfType<TranslateTransform>().Single();
        var turn = transforms.Children.OfType<RotateTransform>().Single(); var tilt = transforms.Children.OfType<SkewTransform>().Single();
        void Start(CardAnimationMode mode)
        {
            Find<RadioButton>("NavSettings").IsChecked = true; Choice(mode).IsChecked = true;
            Find<RadioButton>("NavLibrary").IsChecked = true; window.UpdateLayout();
            CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .9, card.ActualHeight * .25));
        }
        foreach (var mode in new[] { CardAnimationMode.Lift, CardAnimationMode.Zoom, CardAnimationMode.Spring, CardAnimationMode.Magnetic })
        {
            Start(mode);
            if (mode == CardAnimationMode.Spring)
            {
                await Task.Delay(160);
                check(lift.Y < -5 && scale.ScaleX > 1.015, "Spring rises beyond its resting position before settling, rather than using the plain lift animation");
                capture(window, "CardAnimation-Spring-Bounce");
                CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .2, card.ActualHeight * .8));
            }
            await Task.Delay(mode == CardAnimationMode.Spring ? 240 : 270);
            check(store.Load().Appearance.CardAnimation == mode && CardMotion.GetMode(card) == mode && ReferenceEquals(list.SelectedItem, selected), "Animation changes apply and save immediately without selecting another instance: " + mode);
            check(turn.Angle == 0 && tilt.AngleX == 0 && tilt.AngleY == 0 && card.Effect is not null, "Alternative animation clears the previous tilt and retains a soft shadow: " + mode);
            switch (mode)
            {
                case CardAnimationMode.Lift: check(Math.Abs(lift.Y + 5) < .01 && lift.X == 0 && scale.ScaleX == 1, "Lift translates upward without scaling or rotating"); break;
                case CardAnimationMode.Zoom: check(scale.ScaleX > 1.02 && lift.X == 0 && lift.Y == 0, "Zoom grows around its center without translating or rotating"); break;
                case CardAnimationMode.Spring: check(Math.Abs(lift.Y + 5) < .01 && Math.Abs(scale.ScaleX - 1.015) < .001, "Spring settles without restarting when the pointer moves inside the card"); break;
                case CardAnimationMode.Magnetic:
                    check(lift.X > 2 && lift.Y < 0, "Magnetic mode gently translates toward the pointer");
                    CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .1, card.ActualHeight * .8)); await Task.Delay(150);
                    check(lift.X < -2 && lift.Y > 0, "Magnetic movement reverses horizontally and vertically with the pointer"); break;
            }
            capture(window, "CardAnimation-" + mode);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent }); await Task.Delay(210);
            check(lift.X == 0 && lift.Y == 0 && scale.ScaleX == 1 && turn.Angle == 0 && card.Effect is null && card.CacheMode is null && !CardMotion.IsMoving(card),
                "Leaving a card restores its geometry and releases the shadow and bitmap cache: " + mode);
        }
        Start(CardAnimationMode.Magnetic); await Task.Delay(70);
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent }); await Task.Delay(40);
        CardMotion.PreviewPointer(card, new Point(card.ActualWidth * .8, card.ActualHeight * .4)); await Task.Delay(220);
        check(CardMotion.IsMoving(card) && card.Effect is not null && lift.X > 0, "Rapid leave and return cannot let an obsolete completion clear the new animation");
        Find<RadioButton>("NavSettings").IsChecked = true;
        var preset = Find<WrapPanel>("ThemePresetButtons").Children.OfType<Button>().Single(button => button.Tag as string == "#32634D");
        preset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(store.Load().Appearance.LaunchBarMode == LaunchBarMode.OnApproach && store.Load().Appearance.CardAnimation == CardAnimationMode.Magnetic,
            "Switching theme presets preserves launch bar and card interaction preferences");
        var failureShown = false;
        using (var locked = new FileStream(Path.Combine(store.DirectoryPath, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var notice in Application.Current.Windows.OfType<MessageWindow>().Where(dialog => dialog.Owner == window).ToArray()) { failureShown = true; notice.Close(); }
            }));
            Choice(CardAnimationMode.Zoom).IsChecked = true;
        }
        check(failureShown && store.Load().Appearance.CardAnimation == CardAnimationMode.Magnetic && Choice(CardAnimationMode.Magnetic).IsChecked == true
            && CardMotion.GetMode(list) == CardAnimationMode.Magnetic, "A failed save restores the selected animation and keeps the live and persisted effect unchanged");
        var originalColor = store.Load().ThemeColor;
        Find<TextBox>("ThemeColorBox").Text = "#7744AA"; Choice(CardAnimationMode.Lift).IsChecked = true;
        check(store.Load().ThemeColor == originalColor && Find<TextBox>("ThemeColorBox").Text == "#7744AA" && store.Load().Appearance.CardAnimation == CardAnimationMode.Lift,
            "Changing animation saves only that preference and leaves custom color tuning as an unapplied draft");
        Find<Button>("OpenAppearanceButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Children<Button>(window).First(button => button.Content as string == "保存外观设置").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(store.Load().ThemeColor == "#7744AA" && store.Load().Appearance.CardAnimation == CardAnimationMode.Lift && Choice(CardAnimationMode.Lift).IsChecked == true,
            "Saving custom appearance retains the chosen animation instead of restoring a stale draft preference");
        Find<RadioButton>("NavSettings").IsChecked = true; preset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        always.IsChecked = true; Choice(CardAnimationMode.FloatTilt).IsChecked = true; Find<RadioButton>("NavLibrary").IsChecked = true; window.UpdateLayout();
        check(host.IsRevealed && host.SlideOffset == 0 && library.RowDefinitions[3].Height.Value == 84, "Permanent mode restores the reserved footer row without an animation or blank overlay");
        capture(window, "LaunchBar-Always");
        foreach (var width in new[] { 1020d, 1240d })
        {
            window.Width = width; window.Height = width == 1020 ? 700 : 830; window.UpdateLayout();
            check(bar.ActualWidth > 600 && bar.TransformToAncestor(library).Transform(new Point(0, bar.ActualHeight)).Y <= library.ActualHeight + 1,
                $"The permanent launch bar stays inside the content module at {width}px");
            capture(window, "LibraryMotion-" + (int)width);
        }
        if (initial.LaunchBarMode == LaunchBarMode.OnApproach) approach.IsChecked = true;
        Choice(initial.CardAnimation).IsChecked = true;
    }
}
