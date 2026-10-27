using System.Windows;
using System.Windows.Controls;
using BlockDock.Core;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

public partial class MainWindow
{
    private void OpenAccounts(object sender, RoutedEventArgs e) => NavAccounts.IsChecked = true;
    private void ReloadAccounts()
    {
        AccountsList.ItemsSource = state.Accounts.Select(account => new AccountCard(account, SkinImages.Avatar(account.SkinPng), account.Id == state.SelectedAccountId)).ToArray();
        NoAccounts.Visibility = state.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var active = state.Accounts.FirstOrDefault(a => a.Id == state.SelectedAccountId);
        SidebarPlayer.Text = active?.Name ?? "添加玩家";
        SidebarAccountType.Text = active is null ? "开始你的旅程" : active.Microsoft ? "Microsoft 正版账号" : "本地离线账号";
        SidebarPlayer.ToolTip = active?.Name;
        SidebarAccountButton.ToolTip = active is null ? "添加或管理玩家账号" : active.Name + " · " + SidebarAccountType.Text;
        SidebarAvatar.Source = SkinImages.Avatar(active?.SkinPng);
        SidebarDoll.SetSkin(active?.SkinPng, active?.SkinVariant ?? "classic");
        appearancePreview?.SetSkin(active?.SkinPng, active?.SkinVariant ?? "classic");
        foreach (var account in state.Accounts.Where(a => a.Microsoft && a.SkinPng is null && a.SkinUrl.Length > 0).ToArray())
            _ = LoadAccountSkinAsync(account);
    }
    private void AddOffline(object sender, RoutedEventArgs e)
    {
        var name = FormWindow.Prompt(this, "添加本地玩家", "用于离线单人游戏及支持离线登录的服务器。\n正版在线服务器需要 Microsoft 账号。\n\n玩家名：3–16 位英文字母、数字或下划线。", "Player");
        if (name is null) return;
        try
        {
            var account = PlayerAccount.Offline(name);
            var existing = state.Accounts.FirstOrDefault(a => !a.Microsoft && a.Uuid == account.Uuid);
            if (existing is not null) account = existing; else state.Accounts.Add(account);
            state.SelectedAccountId = account.Id; store.Save(state); ReloadAccounts();
            StatusLabel.Text = "●  当前玩家：" + account.Name;
        }
        catch (Exception ex) { Report(ex); }
    }
    private async void AddMicrosoft(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(async token =>
        {
            var clientId = MicrosoftApplication.ResolveClientId(state.MicrosoftClientId);
            using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates);
            var browser = new BrowserLoginWindow(this, auth, clientId, token);
            browser.ShowDialog();
            var result = browser.Result;
            if (browser.UseDeviceCode)
            {
                var device = await auth.BeginAsync(clientId, token);
                var dialog = new DeviceLoginWindow(this, auth, clientId, device, token);
                if (dialog.ShowDialog() != true) return;
                result = dialog.Result;
            }
            token.ThrowIfCancellationRequested();
            if (result is null) return;
            var index = state.Accounts.FindIndex(a => a.Microsoft && a.Uuid == result.Account.Uuid);
            if (index >= 0)
            {
                result.Account.Id = state.Accounts[index].Id;
                if (result.Account.SkinUrl == state.Accounts[index].SkinUrl) result.Account.SkinPng = state.Accounts[index].SkinPng;
                state.Accounts[index] = result.Account;
            }
            else state.Accounts.Add(result.Account);
            state.SelectedAccountId = result.Account.Id; store.Save(state); ReloadAccounts();
            StatusLabel.Text = "●  已登录：" + result.Account.Name;
        }, "正在准备 Microsoft 登录…");
    }
    private void UseAccount(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayerAccount account }) return;
        try { state.SelectedAccountId = account.Id; store.Save(state); ReloadAccounts(); StatusLabel.Text = "●  当前玩家：" + account.Name; }
        catch (Exception ex) { Report(ex); }
    }
    private void RemoveAccount(object sender, RoutedEventArgs e)
    {
        if (operation is not null) { Notice("请等待当前任务结束后再移除账号。"); return; }
        if (sender is not Button { Tag: PlayerAccount account }) return;
        if (!MessageWindow.Confirm(this, "移除账号", $"移除玩家 {account.Name}？本地存档会保留。", "移除账号")) return;
        try
        {
            state.Accounts.Remove(account);
            if (state.SelectedAccountId == account.Id) state.SelectedAccountId = state.Accounts.FirstOrDefault()?.Id;
            store.Save(state); ReloadAccounts();
        }
        catch (Exception ex) { Report(ex); }
    }
    private void BrowseRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择游戏根目录", Multiselect = false };
        if (dialog.ShowDialog(this) == true) RootBox.Text = dialog.FolderName;
    }
    private void BrowseJava(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Java", Filter = "Java 程序|java.exe;javaw.exe" };
        if (dialog.ShowDialog(this) == true) { JavaBox.SelectedItem = null; JavaBox.Text = dialog.FileName; }
    }
    private bool detectingJava;
    private void ApplyJavaChoices(IReadOnlyList<JavaInstallation> found, string value)
    {
        var choices = new[] { new JavaChoice("", "自动选择（推荐）") }.Concat(found.Select(j => new JavaChoice(j.Path, j.Display))).ToArray();
        JavaBox.ItemsSource = choices; JavaBox.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Path, value, StringComparison.OrdinalIgnoreCase));
        if (JavaBox.SelectedItem is null) JavaBox.Text = value;
    }
    private void ReportJavaUnavailable(string path)
    {
        var message = (File.Exists(path) ? "该路径的 Java 无法运行：" : "未找到该路径的 Java：") + path + "\n正在自动重新检测…";
        JavaStatus.Text = message; StatusLabel.Text = "●  已保存的 Java 不可用，正在重新检测";
        if (operation is not null) OperationLabel.Text = "已保存的 Java 不可用，正在重新检测…";
        Log(message); ShowRuntimeFeedback(message, true);
    }
    private async void DetectJava(object sender, RoutedEventArgs e) => await RefreshJavaAsync(true);
    private async Task RefreshJavaAsync(bool force)
    {
        if (detectingJava) return;
        detectingJava = true;
        DetectJavaButton.IsEnabled = false; JavaStatus.Text = "正在检测本机 Java…";
        var configured = ReadJavaValue();
        var saved = state.JavaPath;
        var missingSelection = !string.IsNullOrWhiteSpace(saved) && !File.Exists(saved);
        var snapshot = JavaRuntimes.Snapshot();
        var missing = missingSelection ? saved : snapshot.MissingPaths.FirstOrDefault();
        if (missing is not null) ReportJavaUnavailable(missing);
        try
        {
            var found = await JavaRuntimes.GetAvailableAsync(state.GameDirectory, configured, lifetime.Token, force || missingSelection);
            lifetime.Token.ThrowIfCancellationRequested();
            var current = ReadJavaValue();
            if (force) foreach (var preference in state.Instances.Values) preference.AutoSelectedJavaPath = "";
            if (missingSelection && state.JavaPath == saved)
            {
                state.JavaPath = "";
                try { store.Save(state); } catch { state.JavaPath = saved; throw; }
                if (string.Equals(current, saved, StringComparison.OrdinalIgnoreCase)) current = "";
            }
            else if (force) store.Save(state);
            ApplyJavaChoices(found, current);
            var result = found.Count == 0 ? "未检测到 Java。请安装 64 位 Java 后重新检测，或手动指定 java.exe。"
                : !force && !snapshot.NeedsRefresh && !missingSelection ? $"已载入上次检测的 {found.Count} 个 Java 环境。启动游戏时会校验所选 Java。"
                : $"检测到 {found.Count} 个 Java 环境，已更新检测记录。自动选择会按游戏版本匹配。";
            JavaStatus.Text = (missing is null ? "" : "未找到该路径的 Java：" + missing + "\n已自动重新检测。" + (missingSelection ? "已切换为自动选择。" : "") + "\n")
                + result + (JavaRuntimes.SaveWarning is null ? "" : "\n" + JavaRuntimes.SaveWarning);
            if (missing is not null) { Log(JavaStatus.Text); ShowRuntimeFeedback(JavaStatus.Text, true); StatusLabel.Text = "●  Java 已重新检测"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { JavaStatus.Text = ex.Message; }
        finally { detectingJava = false; DetectJavaButton.IsEnabled = true; }
    }
    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        if (operation is not null || game is not null) { Notice("请等待任务或游戏结束后再保存运行设置。"); return; }
        try
        {
            var root = RootBox.Text.Trim();
            if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("游戏目录需要是完整的绝对路径。");
            root = Path.GetFullPath(root);
            if (File.Exists(root)) throw new ArgumentException("游戏目录不能是一个文件。");
            var memory = GlobalMemoryEditor.ManualMemoryMb;
            var java = ReadJavaValue();
            if (java.Length > 0 && !File.Exists(java)) throw new ArgumentException("Java 路径不存在。请重新选择，或留空使用自动选择。");
            var jvm = globalJvm.Read();
            var themeColor = AppearanceSettings.NormalizeColor(ThemeColorBox.Text);
            if (!GlobalMemoryEditor.Automatic && jvm.InitialMemoryMb > memory) throw new ArgumentException("初始堆内存不能大于最大游戏内存。");
            var appearance = BackgroundImages.Persist(ReadAppearance(), store.DirectoryPath);
            var changedDirectory = !GameFolder.SamePath(state.GameDirectory, root);
            var previousAppearance = state.Appearance; var previousTheme = state.ThemeColor;
            var previousCleanup = state.CleanMemoryBeforeLaunch;
            state.MemoryMb = memory; state.JavaPath = java;
            state.AutoMemory = GlobalMemoryEditor.Automatic; state.Jvm = jvm;
            state.CleanMemoryBeforeLaunch = CleanMemoryBeforeLaunchBox.IsChecked == true;
            state.ThemeColor = themeColor; state.Appearance = appearance;
            state.Source = SourceBox.SelectedValue is DownloadSource source ? source : DownloadSource.Auto;
            state.DownloadParallelDownloads = DownloadParallelBox.SelectedItem is int parallel ? parallel : 8;
            try
            {
                if (changedDirectory) ActivateGameFolder(state.AddGameFolder(root, GameFolder.SuggestName(root)).Id);
                else store.Save(state);
            }
            catch { state.Appearance = previousAppearance; state.ThemeColor = previousTheme; state.CleanMemoryBeforeLaunch = previousCleanup; throw; }
            appearanceTimer.Stop(); ApplySavedAppearance(); UpdateThemeButtons(); appearanceEditor?.Load(appearance); RefreshAppearancePreview();
            if (!changedDirectory) ReloadLibrary();
            StatusLabel.Text = "●  设置已保存";
            QuickAppearanceStatus.Text = "配色与纸娃娃显示设置已应用并保存。";
        }
        catch (Exception ex) { Report(ex); }
    }
    private string ReadJavaValue() => JavaBox.SelectedItem is JavaChoice choice && (JavaBox.Text == choice.Display || JavaBox.Text == choice.Path) ? choice.Path : JavaBox.Text.Trim();
    private sealed record JavaChoice(string Path, string Display);
}
