using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;
using Island.Core;

internal static class SkinUiTests
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
    private static void Click(DependencyObject root, string text) =>
        Children<Button>(root).First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    public static string MakeSkin(string root)
    {
        var pixels = new byte[64 * 64 * 4];
        void Fill(int x, int y, int w, int h, byte r, byte g, byte b, byte a = 255)
        {
            for (var row = y; row < y + h; row++)
                for (var col = x; col < x + w; col++)
                { int i = (row * 64 + col) * 4; pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = a; }
        }
        void Box(int u, int v, int w, int h, int d, byte r, byte g, byte b)
        {
            Fill(u, v + d, d * 2 + w * 2, h, r, g, b); Fill(u + d, v, w * 2, d, r, g, b);
        }
        Box(0, 0, 8, 8, 8, 220, 150, 105); Box(16, 16, 8, 12, 4, 85, 120, 160);
        Box(40, 16, 4, 12, 4, 220, 150, 105); Box(32, 48, 4, 12, 4, 220, 150, 105);
        Box(0, 16, 4, 12, 4, 50, 55, 65); Box(16, 48, 4, 12, 4, 50, 55, 65);
        Fill(8, 0, 16, 8, 44, 39, 34); Fill(24, 8, 8, 8, 44, 39, 34);
        Fill(0, 8, 8, 2, 44, 39, 34); Fill(16, 8, 8, 2, 44, 39, 34);
        Fill(40, 8, 8, 2, 115, 60, 155, 180); Fill(32, 8, 8, 2, 115, 60, 155, 180); Fill(48, 8, 16, 2, 115, 60, 155, 180);
        Fill(20, 36, 8, 12, 130, 65, 170, 160); Fill(23, 36, 2, 12, 0, 0, 0, 0);
        Fill(32, 36, 8, 12, 130, 65, 170, 160);
        Fill(44, 36, 4, 8, 130, 65, 170); Fill(52, 52, 4, 8, 50, 155, 145);
        Fill(4, 36, 4, 4, 160, 100, 45); Fill(4, 52, 4, 4, 160, 100, 45);
        Fill(8, 8, 8, 8, 220, 150, 105); Fill(8, 8, 8, 2, 44, 39, 34);
        Fill(9, 11, 2, 1, 245, 240, 225); Fill(13, 11, 2, 1, 245, 240, 225);
        Fill(10, 11, 1, 1, 70, 120, 140); Fill(13, 11, 1, 1, 70, 120, 140);
        Fill(11, 14, 2, 1, 145, 80, 60);
        Fill(20, 20, 8, 12, 85, 120, 160); Fill(44, 20, 4, 12, 220, 150, 105); Fill(36, 52, 4, 12, 220, 150, 105);
        Fill(4, 20, 4, 12, 50, 55, 65); Fill(20, 52, 4, 12, 50, 55, 65);
        Fill(8, 8, 1, 1, 255, 0, 0); Fill(40, 8, 1, 1, 0, 0, 255, 128);
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var path = Path.Combine(root, "player-skin.png"); using var file = File.Create(path); encoder.Save(file); return path;
    }
    public static void Run(MainWindow window, LauncherStore store, Action<bool, string> check, Action<Window, string> capture)
    {
        window.UpdateLayout();
        void Skin(Action<AccountSkinWindow> action)
        {
            window.UpdateLayout();
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<AccountSkinWindow>().Single(); dialog.UpdateLayout(); action(dialog);
            }));
            Click(window, "配置皮肤");
        }
        Skin(dialog =>
        {
            Click(dialog, "选择 PNG 皮肤"); dialog.UpdateLayout();
            Skin3dUiTests.VerifyDialog(dialog, window, check, capture);
            capture(dialog, "PlayerSkin");
            Click(dialog, "保存皮肤");
        });
        var stored = store.Load().Accounts.Single();
        check(stored.SkinPng is { Length: > 0 }, "Import and save skin through the real player account controls");
        var avatar = (BitmapSource)((Image)window.FindName("SidebarAvatar")).Source;
        var pixel = new byte[4]; avatar.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        check(pixel[0] is >= 127 and <= 129 && pixel[2] is >= 126 and <= 128 && pixel[3] == 255, "Player avatar composites the skin face and translucent hat layer");
        check(((AccountCard)((ItemsControl)window.FindName("AccountsList")).Items[0]).Current, "Account avatar cards identify the current player");
        Skin3dUiTests.VerifySidebar(window, store, stored, check, capture);
        capture(window, "PlayerAccounts");
        Skin(dialog => { Click(dialog, "恢复默认皮肤"); Click(dialog, "取消"); });
        check(store.Load().Accounts.Single().SkinPng!.SequenceEqual(stored.SkinPng!), "Canceling a skin edit preserves the saved skin");
        Skin(dialog => { Click(dialog, "恢复默认皮肤"); Click(dialog, "保存皮肤"); });
        check(store.Load().Accounts.Single().SkinPng is null, "Restoring a local default skin persists");
        var accountButton = (Button)window.FindName("SidebarAccountButton");
        var label = (TextBlock)window.FindName("SidebarAccountType");
        var bottom = label.TransformToAncestor(accountButton).Transform(new Point(0, label.ActualHeight)).Y;
        check(bottom <= accountButton.ActualHeight - 5 && label.ActualHeight >= 12, "Sidebar account type fits fully within its button");
        check(window.FindName("PageTitle") is null && window.FindName("DateLabel") is null, "Remove the whole top title and date area");
        var library = (Grid)window.FindName("LibraryPage");
        check(library.RowDefinitions[0].Height.Value <= 180, "Recent-play hero has a shorter height");
        var menu = new AddInstanceWindow(window);
        menu.Loaded += (_, _) =>
        {
            check(Children<Button>(menu).Count(b => (b.Content as string)?.Contains("  →") == true) == 3, "Add instance offers download, folder and modpack options");
            capture(menu, "AddInstance");
            Click(menu, "导入 .mrpack / .zip 整合包  →");
        };
        check(menu.ShowDialog() == true && menu.Choice == AddInstanceChoice.Archive, "Add instance selects the requested action");
        var png = stored.SkinPng!; var invalid = png.ToArray(); invalid[19] = 65;
        bool rejected = false;
        try { SkinImages.Decode(invalid); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Skin import rejects unsupported dimensions before image decoding");
        rejected = false;
        try { SkinImages.Decode(png[..33]); } catch (Exception) { rejected = true; }
        check(rejected, "Skin import rejects corrupt PNG image data");
        Exception? failure = null;
        var online = new PlayerAccount { Name = "OnlineFixture", Microsoft = true, SkinPng = png };
        var failedDialog = new AccountSkinWindow(window, online, async (_, _, token) =>
        {
            await Task.Delay(40, token); throw new InvalidOperationException("MOCK_SKIN_UPLOAD_REJECTED");
        });
        failedDialog.Loaded += async (_, _) =>
        {
            try
            {
                Children<ComboBox>(failedDialog).Single().SelectedValue = "slim";
                Click(failedDialog, "保存并上传");
                var save = Children<Button>(failedDialog).Single(b => b.Content as string == "保存并上传");
                check(!save.IsEnabled, "Skin upload disables duplicate submission while awaiting a response");
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!save.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(20);
                check(failedDialog.IsVisible && save.IsEnabled && Children<TextBlock>(failedDialog).Any(t => t.Text == "MOCK_SKIN_UPLOAD_REJECTED"),
                    "Skin upload failure keeps the editable dialog open and shows the error");
                capture(failedDialog, "SkinUploadFailure"); Click(failedDialog, "取消");
            }
            catch (Exception ex) { failure = ex; failedDialog.Close(); }
        };
        failedDialog.ShowDialog(); if (failure is not null) throw failure;
        var canceled = false;
        var cancelDialog = new AccountSkinWindow(window, online, async (_, _, token) =>
        {
            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { canceled = true; throw; }
        });
        cancelDialog.Loaded += (_, _) =>
        {
            Children<ComboBox>(cancelDialog).Single().SelectedValue = "slim";
            Click(cancelDialog, "保存并上传"); Click(cancelDialog, "取消");
        };
        cancelDialog.ShowDialog();
        check(canceled, "Canceling a skin upload cancels the request and closes cleanly");
    }
}
