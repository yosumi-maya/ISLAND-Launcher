using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;
using Island.Core;

internal static class Skin3dUiTests
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static byte[] Render(SkinPreview3D preview)
    {
        preview.UpdateLayout();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(preview) { Stretch = Stretch.Fill }, null, new Rect(0, 0, preview.ActualWidth, preview.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
    private static int ChangedPixels(byte[] first, byte[] second)
    {
        var count = 0;
        for (int i = 0; i < first.Length; i += 4)
            if (first[i] != second[i] || first[i + 1] != second[i + 1] || first[i + 2] != second[i + 2] || first[i + 3] != second[i + 3]) count++;
        return count;
    }
    private static void Turn(SkinPreview3D preview, Key key)
    {
        preview.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(preview)!, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }
    public static void VerifyDialog(AccountSkinWindow dialog, MainWindow owner, Action<bool, string> check, Action<Window, string> capture)
    {
        var preview = Children<SkinPreview3D>(dialog).Single(); preview.FollowPointer = false; preview.SetPose(SkinPose.Standing); preview.RenderFrame(0);
        check(preview.ToolTip is null && preview.Rig.Model.Bounds.SizeY >= 32 && preview.Rig.OuterFaceCount > 6 && Children<Viewport3D>(preview).Any(), "Skin dialog renders a full layered 3D body without a hover tooltip");
        var withOuter = Render(preview);
        var outer = Children<CheckBox>(dialog).Single(box => box.Name == "ShowSkinOuterLayer"); outer.IsChecked = false; preview.RenderFrame(0);
        check(!preview.ShowOuterLayer && !preview.Rig.OuterVisible && ChangedPixels(withOuter, Render(preview)) > 200, "Hiding the outer layer reveals the underlying body across many rendered pixels");
        capture(dialog, "Skin3D-BaseLayer"); outer.IsChecked = true; preview.RenderFrame(0);
        check(ChangedPixels(withOuter, Render(preview)) == 0, "Restoring the outer layer preserves the exact original preview");
        VerifySeams(check, capture);
        for (int i = 0; i < 18; i++) Turn(preview, Key.Right);
        check(ChangedPixels(withOuter, Render(preview)) > 500, "Keyboard rotation reveals different rear and side skin faces");
        capture(dialog, "Skin3D-Back"); Turn(preview, Key.Home);
        var variant = Children<ComboBox>(dialog).Single(); variant.SelectedValue = "slim";
        check(preview.Rig.ArmWidth == 3, "Slim selection rebuilds three-pixel arms in the 3D model");
        variant.SelectedValue = "classic"; check(preview.Rig.ArmWidth == 4, "Classic selection restores four-pixel arms"); variant.SelectedValue = "slim";
        foreach (var pose in Enum.GetValues<SkinPose>())
        {
            Children<RadioButton>(dialog).Single(button => button.Tag is SkinPose value && value == pose).IsChecked = true;
            preview.RenderFrame(.2); var first = Render(preview); preview.RenderFrame(.5);
            check(preview.Pose == pose && ChangedPixels(first, Render(preview)) > 10, "Pose selection animates the model: " + pose);
            capture(dialog, "Skin3D-Pose-" + pose);
        }
        var look = Children<CheckBox>(dialog).Single(box => box.Name == "SkinFollowPointer"); look.IsChecked = true; preview.FollowPointer = true;
        (double Yaw, double Pitch, double Arm) Response(int framesPerSecond)
        {
            preview.ResetView(); preview.SetPose(SkinPose.Walking); preview.LookAt(new Point(preview.ActualWidth + 800, -600));
            for (int i = 1; i <= framesPerSecond / 2; i++) preview.RenderFrame((double)i / framesPerSecond, 1d / framesPerSecond);
            return (preview.Rig.HeadYaw, preview.Rig.HeadPitch, preview.Rig.RightArmPitch);
        }
        var at30 = Response(30); var at60 = Response(60); var at144 = Response(144);
        check(Math.Abs(at30.Yaw - at60.Yaw) < .001 && Math.Abs(at60.Yaw - at144.Yaw) < .001 && Math.Abs(at30.Pitch - at144.Pitch) < .001,
            "Pointer following has the same response after equal elapsed time at 30, 60 and 144 fps");
        check(Math.Abs(at30.Arm - at60.Arm) < .001 && Math.Abs(at60.Arm - at144.Arm) < .001, "Higher frame rates preserve the selected animation speed");
        preview.ResetView();
        preview.LookAt(new Point(preview.ActualWidth + 800, -600)); for (int i = 0; i < 40; i++) preview.RenderFrame(0);
        check(preview.Rig.HeadYaw is > 40 and <= 50 && preview.Rig.HeadPitch is >= -28 and < -20, "Head follows a pointer above and to the right within natural neck limits");
        preview.LookAt(new Point(-800, preview.ActualHeight + 600)); for (int i = 0; i < 40; i++) preview.RenderFrame(0);
        check(preview.Rig.HeadYaw is >= -50 and < -40 && preview.Rig.HeadPitch is > 20 and <= 28, "Head can look left and down independently of the body");
        capture(dialog, "Skin3D-LookAt"); look.IsChecked = false; preview.LookAt(new Point(2000, -2000)); preview.RenderFrame(0);
        check(preview.Rig.HeadYaw == 0 && preview.Rig.HeadPitch == 0, "Turning off pointer following restores the head and ignores further targets");
        look.IsChecked = true; preview.ResetView();
        Children<RadioButton>(dialog).Single(button => button.Tag is SkinPose.Standing).IsChecked = true; preview.RenderFrame(0);
        check(dialog.ActualWidth <= owner.ActualWidth && preview.ActualHeight >= 300 && Children<Button>(dialog).Single(button => button.Content as string == "保存皮肤").IsVisible,
            "3D skin controls and pinned save action fit the launcher width");
    }
    private static void VerifySeams(Action<bool, string> check, Action<Window, string> capture)
    {
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 50; pixels[i + 1] = 40; pixels[i + 2] = 30; }
        // A trouser overlay partly covers the neighbouring leg. Its transparent texels
        // must reveal that leg's base rather than punching a hole through both layers.
        for (var y = 36; y < 48; y++) for (var x = 5; x < 7; x++) pixels[(y * 64 + x) * 4 + 3] = 255;
        var texture = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(texture)); using var stream = new MemoryStream(); encoder.Save(stream);
        var preview = new SkinPreview3D { Width = 180, Height = 260, FollowPointer = false }; preview.SetSkin(stream.ToArray(), "slim");
        var window = new Window { Content = preview, SizeToContent = SizeToContent.WidthAndHeight, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        window.Show(); window.UpdateLayout();
        foreach (var model in new[] { "slim", "classic" })
        {
            preview.SetSkin(stream.ToArray(), model); preview.ResetView(); Turn(preview, Key.Right); Turn(preview, Key.Right); preview.RenderFrame(0);
            var rendered = Render(preview); var continuous = true;
            foreach (var fraction in new[] { .70, .75, .80, .85 })
            {
                var row = (int)(preview.ActualHeight * fraction);
                var occupied = Enumerable.Range(0, 180).Where(x => rendered[(row * 180 + x) * 4 + 3] > 240).ToArray();
                continuous &= occupied.Length > 15 && Enumerable.Range(occupied.First() + 1, occupied.Last() - occupied.First() - 1).All(x => rendered[(row * 180 + x) * 4 + 3] > 240);
            }
            check(continuous, $"Transparent clothing cannot erase the adjacent leg or leave a vertical body seam ({model})");
        }
        capture(window, "Skin3D-SeamRegression"); window.Close();
    }
    public static void VerifySidebar(MainWindow window, LauncherStore store, PlayerAccount stored, Action<bool, string> check, Action<Window, string> capture)
    {
        var doll = (SkinPreview3D)window.FindName("SidebarDoll");
        check(doll.Rig.ArmWidth == 3 && !doll.Rig.IsLegacy && !Children<TextBlock>(window).Any(text => text.Text.Contains("每一个世界")), "Saved model updates the sidebar paper doll and replaces the old motto");
        foreach (var size in new[] { (1020d, 700d), (1240d, 830d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            var bottom = doll.TransformToAncestor(window).Transform(new Point(0, doll.ActualHeight)).Y;
            var accountTop = ((Button)window.FindName("SidebarAccountButton")).TransformToAncestor(window).Transform(new Point()).Y;
            check(doll.ActualHeight >= 180 && bottom < accountTop, $"Sidebar doll fits above the account button at {(int)size.Item1}px");
            capture(window, "Skin3D-Sidebar-" + (int)size.Item1);
        }
        var texture = SkinImages.Decode(stored.SkinPng!); var legacy = new CroppedBitmap(texture, new Int32Rect(0, 0, 64, 32));
        var legacyPixels = new byte[64 * 32 * 4]; new FormatConvertedBitmap(legacy, PixelFormats.Bgra32, null, 0).CopyPixels(legacyPixels, 256, 0);
        for (int y = 20; y < 32; y++) for (int x = 4; x < 8; x++)
        {
            var index = (y * 64 + x) * 4; legacyPixels[index] = x < 6 ? (byte)0 : (byte)255; legacyPixels[index + 1] = 0; legacyPixels[index + 2] = x < 6 ? (byte)255 : (byte)0; legacyPixels[index + 3] = 255;
        }
        var legacyTexture = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgra32, null, legacyPixels, 256);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(legacyTexture)); using var stream = new MemoryStream(); encoder.Save(stream);
        doll.SetSkin(stream.ToArray(), "slim");
        check(doll.Rig.IsLegacy && doll.Rig.ArmWidth == 4 && doll.Rig.OuterFaceCount <= 6 && Render(doll).Any(value => value != 0),
            $"Legacy skins use mirrored classic limbs and hat-only overlay (legacy={doll.Rig.IsLegacy}, arm={doll.Rig.ArmWidth}, overlay={doll.Rig.OuterFaceCount})");
        doll.FollowPointer = false; doll.SetPose(SkinPose.Standing); doll.ResetView(); Turn(doll, Key.Right); Turn(doll, Key.Right); doll.RenderFrame(0);
        var renderedLegacy = Render(doll); var row = (int)(doll.ActualHeight * .8); var rowColors = new List<char>();
        for (int x = 0; x < (int)doll.ActualWidth; x++)
        {
            var index = (row * (int)doll.ActualWidth + x) * 4;
            var color = renderedLegacy[index + 2] > 100 && renderedLegacy[index] < 20 ? 'R' : renderedLegacy[index] > 100 && renderedLegacy[index + 2] < 20 ? 'B' : ' ';
            if (color != ' ' && (rowColors.Count == 0 || rowColors[^1] != color)) rowColors.Add(color);
        }
        check(new string(rowColors.ToArray()) == "RBR", "Legacy left-leg texture mirrors the asymmetric right-leg texture in rendered pixels");
        capture(window, "Skin3D-Legacy");
        var legacyBytes = stream.ToArray(); var legacyPath = Path.Combine(store.DirectoryPath, "legacy-skin.png"); File.WriteAllBytes(legacyPath, legacyBytes);
        string? savedVariant = null;
        var legacyDialog = new AccountSkinWindow(window, stored, (_, model, _) => { savedVariant = model; return Task.CompletedTask; }, picker: () => legacyPath);
        legacyDialog.Loaded += (_, _) =>
        {
            Children<Button>(legacyDialog).Single(button => button.Content as string == "选择 PNG 皮肤").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Children<ComboBox>(legacyDialog).Single().SelectedValue = "slim";
            check(Children<ComboBox>(legacyDialog).Single().SelectedValue as string == "classic" && Children<SkinPreview3D>(legacyDialog).Single().Rig.IsLegacy,
                "Importing a legacy skin keeps the model selector and preview on Classic");
            Children<Button>(legacyDialog).Single(button => button.Content as string == "保存皮肤").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        };
        legacyDialog.ShowDialog(); check(savedVariant == "classic", "Legacy skin saving cannot submit an incompatible Slim model");
        var switched = PlayerAccount.Offline("OtherPlayer"); switched.SkinPng = legacyBytes;
        var switchStore = new LauncherStore(Path.Combine(store.DirectoryPath, "account-switch"));
        switchStore.Save(new LauncherState { GameDirectory = Path.Combine(store.DirectoryPath, "empty-game"), Accounts = [stored, switched], SelectedAccountId = stored.Id });
        var switchWindow = new MainWindow(switchStore) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        switchWindow.Show(); ((RadioButton)switchWindow.FindName("NavAccounts")).IsChecked = true; switchWindow.UpdateLayout();
        void SelectAccount(string id)
        {
            switchWindow.UpdateLayout();
            Children<Button>(switchWindow).Single(button => button.Content as string == "设为当前" && button.Tag is PlayerAccount account && account.Id == id)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }
        SelectAccount(switched.Id);
        check(((SkinPreview3D)switchWindow.FindName("SidebarDoll")).Rig.IsLegacy && ((TextBlock)switchWindow.FindName("SidebarPlayer")).Text == switched.Name,
            "Switching the current account updates the sidebar skin and name together");
        SelectAccount(stored.Id); check(((SkinPreview3D)switchWindow.FindName("SidebarDoll")).Rig.ArmWidth == 3, "Switching back restores the saved Slim paper doll");
        switchWindow.Close();
        doll.SetSkin([1, 2, 3], "classic"); check(doll.Rig.ArmWidth == 4 && !doll.Rig.IsLegacy, "A corrupt cached skin falls back to a complete default 3D model");
        doll.SetSkin(stored.SkinPng, stored.SkinVariant); doll.ResetView(); doll.FollowPointer = true;
        var detachedPreview = new SkinPreview3D { Width = 160, Height = 220 };
        var temporary = new Window { Content = detachedPreview, Width = 180, Height = 260, Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        temporary.Show(); temporary.UpdateLayout(); temporary.WindowState = WindowState.Minimized;
        check(!detachedPreview.IsAnimating, "Minimizing a window detaches its 3D rendering callback"); temporary.Close();
        check(!detachedPreview.IsAnimating, "Unloading a 3D preview detaches its rendering callback");
    }
    public static async Task VerifyLiveFrames(MainWindow window, Action<bool, string> check)
    {
        var preview = (SkinPreview3D)window.FindName("SidebarDoll");
        var pose = preview.Pose; var following = preview.FollowPointer;
        preview.FollowPointer = false; preview.SetPose(SkinPose.Running);
        var frames = new List<(TimeSpan Time, double Angle)>(); var inactive = false;
        void Observe(object? sender, EventArgs e)
        {
            if (!preview.IsAnimating) { inactive = true; return; }
            if (e is RenderingEventArgs frame && (frames.Count == 0 || frame.RenderingTime != frames[^1].Time)) frames.Add((frame.RenderingTime, preview.Rig.RightArmPitch));
        }
        CompositionTarget.Rendering += Observe;
        try { await Task.Delay(1200); }
        finally { CompositionTarget.Rendering -= Observe; preview.SetPose(pose); preview.FollowPointer = following; }
        if (inactive || frames.Count < 5)
        {
            Console.WriteLine("LIVE FRAME PROBE SKIPPED: foreground rendering was unavailable; deterministic timing checks still ran."); return;
        }
        var changed = frames.Zip(frames.Skip(1)).Count(pair => pair.First.Angle != pair.Second.Angle);
        check(changed >= frames.Count - 2, "Live animation advances on each distinct WPF rendering frame instead of holding alternate frames");
        var elapsed = (frames[^1].Time - frames[0].Time).TotalSeconds;
        Console.WriteLine($"LIVE FRAME PROBE: {frames.Count} callbacks, {changed} pose changes, {(frames.Count - 1) / elapsed:0.0} rendering callbacks/s (test window).");
    }
}
