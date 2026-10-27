using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using BlockDock.Core;
using Island.App;
using Island.Core;

internal static class Program
{
    private sealed class DisposeAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
    private static int checks;
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static T Find<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static Button Button(DependencyObject root, string content) => Descendants<Button>(root).First(b => b.Content as string == content);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void ManageSelectedInstance(MainWindow window)
    {
        var list = Find<ListBox>(window, "InstanceList"); list.UpdateLayout();
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var menu = container.ContextMenu; menu.PlacementTarget = container; menu.IsOpen = true;
        menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "实例管理").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
    private static void SelectFolder(MainWindow window, string directory) => Find<ItemsControl>(window, "FolderTabs").Items.Cast<RadioButton>().First(tab => GameFolder.SamePath((string)tab.ToolTip, directory)).IsChecked = true;
    private static async Task WaitUntil(Func<bool> condition, string message)
    {
        var until = DateTime.UtcNow.AddSeconds(45);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(60);
        if (!condition()) throw new Exception(message);
    }
    private static string? captureRoot;
    private static void Capture(Window window, string name)
    {
        if (captureRoot is null) return;
        App.SaveCapture((FrameworkElement)window.Content, Path.Combine(captureRoot, name + ".png"));
    }
    [STAThread]
    private static int Main(string[] args)
    {
        var captureOption = Array.IndexOf(args, "--capture-dialogs");
        if (captureOption >= 0) { captureRoot = Path.GetFullPath(args[captureOption + 1]); Directory.CreateDirectory(captureRoot); }
        var root = Path.Combine(Path.GetTempPath(), "Island UI " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var store = new LauncherStore(Path.Combine(root, "settings"));
        var state = new LauncherState { GameDirectory = Path.Combine(root, ".minecraft"), AutoMemory = false };
        foreach (var id in new[] { "Alpha", "Beta" })
        {
            var directory = Path.Combine(state.GameDirectory, "versions", id); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"), "{\"id\":\"" + id + "\",\"mainClass\":\"IslandUiFixture\",\"arguments\":{\"game\":[]}}");
        }
        var offline = PlayerAccount.Offline("UiTester"); state.Accounts.Add(offline); state.SelectedAccountId = offline.Id; state.For("Beta").Favorite = true; store.Save(state);
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/岛屿启动器;component/Theme.xaml") });
        IReadOnlyList<string>? folderSelection = null;
        string? skinSelection = null;
        string? backgroundSelection = null;
        var cleanupCalls = 0;
        var cleanupOutcome = MemoryCleanupOutcome.Completed;
        var cleanupThrows = false;
        var cleanupCanceled = false;
        var gpuRequests = 0; var gpuRestores = 0;
        TaskCompletionSource? cleanupGate = null;
        var window = new MainWindow(store, () => folderSelection, () => skinSelection, () => backgroundSelection, async token =>
        {
            cleanupCalls++; await Task.Delay(30, token);
            if (cleanupGate is not null) await cleanupGate.Task.WaitAsync(token);
            if (cleanupCanceled) throw new OperationCanceledException(token);
            if (cleanupThrows) throw new IOException("模拟内存清理失败");
            return new(cleanupOutcome);
        }, _ => { gpuRequests++; return new DisposeAction(() => gpuRestores++); }) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        var exit = 0;
        async Task VerifyDollMemoryMenu()
        {
            Find<RadioButton>(window, "NavLibrary").IsChecked = true;
            var doll = Find<SkinPreview3D>(window, "SidebarDoll");
            var menu = doll.ContextMenu;
            string FeedbackText() => ((TextBlock)((Border)window.DollMemoryFeedback.Child).Child).Text;
            var item = menu.Items.OfType<MenuItem>().Single(entry => entry.Name == "DollCleanMemoryMenuItem");
            Check(item.Header as string == "清理内存" && item.IsEnabled && menu.Items.OfType<MenuItem>().Count(entry => entry.Tag is SkinPose) == Enum.GetValues<SkinPose>().Length
                && menu.Items.OfType<MenuItem>().Any(entry => entry.Header as string == "重置视角"), "Paper doll menu adds memory cleanup while preserving every pose and reset action");
            Check(!new SkinPreview3D().ContextMenu.Items.OfType<MenuItem>().Any(entry => entry.Name == "DollCleanMemoryMenuItem"), "Account skin previews do not expose an unconnected system cleanup command");
            var instances = Find<ListBox>(window, "InstanceList"); var originalIndex = instances.SelectedIndex;
            window.IsEnabled = false; instances.SelectedIndex = (originalIndex + 1) % instances.Items.Count; window.IsEnabled = true;
            Check(item.IsEnabled, "A temporarily disabled owner does not leave the paper doll cleanup command permanently disabled");
            instances.SelectedIndex = originalIndex;
            menu.PlacementTarget = doll; menu.Placement = PlacementMode.Right; menu.IsOpen = true; menu.UpdateLayout(); await Task.Delay(60);
            if (captureRoot is not null) App.SaveCapture(menu, Path.Combine(captureRoot, "Doll-Memory-ContextMenu.png")); menu.IsOpen = false;
            var beforeSaved = File.ReadAllText(Path.Combine(store.DirectoryPath, "settings.json"));
            cleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await WaitUntil(() => cleanupCalls == 1, "Paper doll cleanup did not start");
            Check(!item.IsEnabled && item.Header as string == "清理中…" && !Find<Button>(window, "CleanMemoryNowButton").IsEnabled, "Paper doll and settings cleanup entries share the busy state");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Check(cleanupCalls == 1, "Repeated paper doll cleanup clicks do not start another helper");
            cleanupGate.SetResult(); await WaitUntil(() => item.IsEnabled, "Paper doll cleanup did not complete"); cleanupGate = null;
            Check(window.DollMemoryFeedback.IsOpen && FeedbackText().Contains("已回收系统工作集")
                && Find<RadioButton>(window, "NavLibrary").IsChecked == true, "Paper doll cleanup shows a visible result without navigating away from the library");
            Check(File.ReadAllText(Path.Combine(store.DirectoryPath, "settings.json")) == beforeSaved && !Find<TextBox>(window, "LogBox").Text.Contains("游戏进程已启动"), "Paper doll cleanup neither saves settings nor starts a game");
            window.DollMemoryFeedback.UpdateLayout(); await Task.Delay(60);
            if (captureRoot is not null) App.SaveCapture((FrameworkElement)window.DollMemoryFeedback.Child, Path.Combine(captureRoot, "Doll-Memory-Feedback.png"));
            cleanupOutcome = MemoryCleanupOutcome.AuthorizationDeclined; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntil(() => item.IsEnabled, "Declined paper doll cleanup did not recover");
            Check(window.DollMemoryFeedback.IsOpen && FeedbackText().Contains("已取消管理员授权"), "Declined authorization is visible beside the paper doll");
            cleanupThrows = true; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await WaitUntil(() => item.IsEnabled, "Failed paper doll cleanup did not recover");
            Check(FeedbackText().Contains("模拟内存清理失败") && item.Header as string == "清理内存", "Paper doll cleanup failures show the error and restore the menu caption");
            cleanupThrows = false; cleanupOutcome = MemoryCleanupOutcome.Completed;
            cleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var beforeCancel = cleanupCalls; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await WaitUntil(() => cleanupCalls > beforeCancel, "Cancelable paper doll cleanup did not start");
            Click(Find<Button>(window, "CancelButton")); await WaitUntil(() => item.IsEnabled, "Paper doll cleanup cancel did not recover"); cleanupGate = null;
            Check(window.DollMemoryFeedback.IsOpen && FeedbackText().Contains("已取消内存清理"), "Canceling paper doll cleanup shows the canceled result and restores availability");
            await WaitUntil(() => !window.DollMemoryFeedback.IsOpen, "Paper doll result did not dismiss itself");
            Check(!window.DollMemoryFeedback.IsOpen, "Paper doll cleanup feedback automatically dismisses after five seconds");
            Find<TextBox>(window, "LogBox").Clear(); cleanupCalls = 0;
        }
        async Task VerifyManualCleanup()
        {
            Find<RadioButton>(window, "NavSettings").IsChecked = true;
            var button = Find<Button>(window, "CleanMemoryNowButton");
            var checkbox = Find<CheckBox>(window, "CleanMemoryBeforeLaunchBox");
            var feedback = Find<TextBlock>(window, "MemoryCleanupStatus");
            var memoryBox = Find<MemoryAllocationEditor>(window, "GlobalMemoryEditor");
            var originalMemory = memoryBox.ManualMemoryMb; memoryBox.ManualMemoryMb = 7168;
            Check(button.IsEnabled && checkbox.IsChecked == false, "Manual cleanup is available with pre-launch cleanup disabled");
            cleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(button); await WaitUntil(() => cleanupCalls == 1, "Manual cleanup was not invoked");
            Check(!button.IsEnabled && button.Content as string == "清理中…" && !Find<Button>(window, "LaunchButton").IsEnabled, "Manual cleanup shows busy feedback and prevents overlapping game launch");
            Click(button); Check(cleanupCalls == 1, "Repeated cleanup clicks cannot start a second helper");
            cleanupGate.SetResult();
            await WaitUntil(() => button.IsEnabled, "Manual cleanup did not complete"); cleanupGate = null;
            Check(feedback.Text.Contains("已回收系统工作集") && Find<TextBlock>(window, "StatusLabel").Text.Contains("清理完成"), "Manual cleanup completion appears inline and in the status bar");
            Check(memoryBox.ManualMemoryMb == 7168 && store.Load().MemoryMb != 7168 && !store.Load().CleanMemoryBeforeLaunch, "Immediate cleanup preserves unsaved settings without enabling automatic cleanup");
            Check(!Find<TextBox>(window, "LogBox").Text.Contains("游戏进程已启动"), "Immediate cleanup never starts a game");
            foreach (var size in new[] { (1240d, 830d), (1020d, 700d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); feedback.BringIntoView(); window.UpdateLayout(); await Task.Delay(60);
                var row = (Grid)button.Parent;
                Check(Math.Abs(button.TranslatePoint(new Point(button.ActualWidth, 0), row).X - row.ActualWidth) < 1 &&
                    checkbox.TranslatePoint(new Point(checkbox.ActualWidth, 0), row).X < button.TranslatePoint(new Point(0, 0), row).X,
                    $"Immediate cleanup button stays on the right without overlapping its checkbox at {(int)size.Item1}px");
                Capture(window, "MemoryCleanupNow-" + (int)size.Item1);
            }
            window.Width = 1240; window.Height = 830;
            foreach (var outcome in new[] { MemoryCleanupOutcome.AuthorizationDeclined, MemoryCleanupOutcome.WorkingSetsOnly, MemoryCleanupOutcome.Failed })
            {
                cleanupOutcome = outcome; Click(button); await WaitUntil(() => button.IsEnabled, "Manual cleanup result did not recover");
                Check(feedback.Text.Contains(new MemoryCleanupResult(outcome).Summary) && !feedback.Text.Contains("继续启动"), "Manual result uses standalone feedback: " + outcome);
            }
            cleanupThrows = true; Click(button); await WaitUntil(() => button.IsEnabled, "Manual cleanup failure did not recover");
            Check(feedback.Text.Contains("模拟内存清理失败") && !app.Windows.OfType<MessageWindow>().Any(), "Manual cleanup failure remains visible without a blocking dialog");
            cleanupThrows = false; cleanupOutcome = MemoryCleanupOutcome.Completed;
            cleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var beforeCancel = cleanupCalls; Click(button); await WaitUntil(() => cleanupCalls > beforeCancel, "Cancelable cleanup did not start");
            Click(Find<Button>(window, "CancelButton")); await WaitUntil(() => button.IsEnabled, "Canceled cleanup did not recover"); cleanupGate = null;
            Check(feedback.Text.Contains("已取消") && button.Content as string == "立即清理", "Canceling manual cleanup restores the button and shows the canceled state");
            memoryBox.ManualMemoryMb = originalMemory; Find<TextBox>(window, "LogBox").Clear();
            Find<RadioButton>(window, "NavLibrary").IsChecked = true; cleanupCalls = 0;
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(100);
                if (args.Contains("--map-appearance-only"))
                {
                    await MapAppearanceUiTests.RunAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} MAP APPEARANCE CHECKS PASSED"); return;
                }
                if (args.Contains("--design-only"))
                {
                    await DesignPolishUiTests.RunAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} DESIGN POLISH CHECKS PASSED"); return;
                }
                if (args.Contains("--content-live-only"))
                {
                    await ContentDownloadUiTests.RunLiveAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} LIVE CONTENT CHECKS PASSED"); return;
                }
                if (args.Contains("--content-only"))
                {
                    await ContentDownloadUiTests.RunAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} CONTENT UI CHECKS PASSED"); return;
                }
                if (args.Contains("--catalog-live-only"))
                {
                    await DownloadCenterUiTests.RunLiveAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} LIVE CATALOG CHECKS PASSED"); return;
                }
                if (args.Contains("--downloads-only"))
                {
                    await DownloadCenterUiTests.RunAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} DOWNLOAD CENTER UI CHECKS PASSED"); return;
                }
                if (args.Contains("--auth-only"))
                {
                    await MicrosoftLoginUiTests.RunAsync(window, Check, Capture);
                    Console.WriteLine($"ALL {checks} MICROSOFT LOGIN UI CHECKS PASSED"); return;
                }
                if (args.Contains("--java-cache-only"))
                {
                    await JavaCacheUiTests.RunAsync(window, root, Check, Capture);
                    Console.WriteLine($"ALL {checks} JAVA CACHE UI CHECKS PASSED"); return;
                }
                if (args.Contains("--motion-only"))
                {
                    await LibraryMotionTests.Run(window, store, Check, Capture);
                    Console.WriteLine($"ALL {checks} LIBRARY MOTION CHECKS PASSED"); return;
                }
                if (args.Contains("--presets-only"))
                {
                    ((RadioButton)window.FindName("NavSettings")).IsChecked = true; window.UpdateLayout();
                    await AppearanceUiTests.VerifyQuickSettings(window, store, Check, Capture);
                    Console.WriteLine($"ALL {checks} INSTANT PRESET CHECKS PASSED"); return;
                }
                if (args.Contains("--materials-only"))
                {
                    MaterialRenderingTests.Run(AppearanceUiTests.MakeImage(root), Check);
                    Console.WriteLine($"ALL {checks} MATERIAL RENDERING CHECKS PASSED"); return;
                }
                if (args.Contains("--inputs-only"))
                {
                    InputLayoutPreview.Save(window, captureRoot ?? throw new ArgumentException("Provide --capture-dialogs for input layout preview."));
                    return;
                }
                if (args.Contains("--outline-only"))
                {
                    foreach (var size in new[] { (1020d, 700d), (1240d, 830d), (1600d, 900d) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
                        WindowOutlineUiTests.Verify(window, Check); Capture(window, "RoundedWindow-" + (int)size.Item1);
                    }
                    Console.WriteLine($"ALL {checks} WINDOW OUTLINE CHECKS PASSED"); return;
                }
                if (args.Contains("--launch-options-only"))
                {
                    await LaunchOptionsUiTests.Run(window, root, Check, Capture, captureRoot); Console.WriteLine($"ALL {checks} LAUNCH OPTIONS CHECKS PASSED"); return;
                }
                if (args.Contains("--instance-menu-only"))
                {
                    await InstanceContextMenuUiTests.RunAsync(window, root, Check, Capture, captureRoot); Console.WriteLine($"ALL {checks} INSTANCE MENU CHECKS PASSED"); return;
                }
                if (args.Contains("--doll-memory-only"))
                {
                    await VerifyDollMemoryMenu(); Console.WriteLine($"ALL {checks} DOLL MEMORY MENU CHECKS PASSED"); return;
                }
                if (args.Contains("--allocation-only"))
                {
                    await MemoryAllocationUiTests.Run(window, store, Check, Capture, captureRoot); Console.WriteLine($"ALL {checks} MEMORY ALLOCATION CHECKS PASSED"); return;
                }
                if (args.Contains("--memory-only"))
                {
                    await VerifyManualCleanup(); Console.WriteLine($"ALL {checks} MANUAL MEMORY CLEANUP CHECKS PASSED"); return;
                }
                await VerifyDollMemoryMenu();
                await LaunchOptionsUiTests.Run(window, root, Check, Capture, captureRoot);
                await MicrosoftLoginUiTests.RunAsync(window, Check, Capture);
                await DownloadCenterUiTests.RunAsync(window, root, Check, Capture);
                await ContentDownloadUiTests.RunAsync(window, root, Check, Capture);
                await DesignPolishUiTests.RunAsync(window, root, Check, Capture);
                await MapAppearanceUiTests.RunAsync(window, root, Check, Capture);
                await JavaCacheUiTests.RunAsync(window, root, Check, Capture);
                await InstanceContextMenuUiTests.RunAsync(window, root, Check, Capture, captureRoot);
                await MemoryAllocationUiTests.Run(window, store, Check, Capture, captureRoot);
                await VerifyManualCleanup();
                Check(Find<ListBox>(window, "InstanceList").Items.Count == 2, "Discover actual version folders");
                Check(Find<TextBlock>(window, "SidebarPlayer").Text == "UiTester", "Restore active player");
                Check(Find<Button>(window, "LaunchButton").IsEnabled, "Selected instance enables launch");
                Find<TextBox>(window, "SearchBox").Text = "alpha";
                Check(Find<ListBox>(window, "InstanceList").Items.Count == 1, "Case-insensitive instance search");
                Find<TextBox>(window, "SearchBox").Text = "no match";
                Check(Find<StackPanel>(window, "EmptyLibrary").Visibility == Visibility.Visible && !Find<Button>(window, "LaunchButton").IsEnabled, "Empty search clears launch selection");
                Find<TextBox>(window, "SearchBox").Clear(); Find<RadioButton>(window, "FilterFavorites").IsChecked = true;
                Check(Find<ListBox>(window, "InstanceList").Items.Count == 1 && ((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).Id == "Beta", "Favorites filter");
                Find<RadioButton>(window, "NavAccounts").IsChecked = true;
                Check(Find<Grid>(window, "AccountsPage").Visibility == Visibility.Visible && Find<Grid>(window, "LibraryPage").Visibility == Visibility.Collapsed, "Accounts navigation");
                skinSelection = SkinUiTests.MakeSkin(root);
                SkinUiTests.Run(window, store, Check, Capture);
                await Skin3dUiTests.VerifyLiveFrames(window, Check);
                if (!args.Contains("--skins-only") && !args.Contains("--appearance-only")) await LibraryMotionTests.Run(window, store, Check, Capture);
                if (args.Contains("--skins-only")) return;
                if (args.Contains("--appearance-only"))
                {
                    backgroundSelection = AppearanceUiTests.MakeImage(root);
                    await AppearanceUiTests.Run(window, store, backgroundSelection, Check, Capture);
                    Console.WriteLine($"ALL {checks} APPEARANCE / SKIN UI CHECKS PASSED"); return;
                }
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                window.UpdateLayout();
                var sourceChoice = Find<ComboBox>(window, "SourceBox");
                sourceChoice.BringIntoView(); window.UpdateLayout(); await Task.Delay(60);
                sourceChoice.SelectedIndex = 0;
                sourceChoice.Focus();
                sourceChoice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sourceChoice)!, Environment.TickCount, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
                Check(sourceChoice.SelectedIndex == 1, "Styled combo retains keyboard selection");
                sourceChoice.IsDropDownOpen = true; await Task.Delay(80);
                var sourcePopup = (Popup)sourceChoice.Template.FindName("PART_Popup", sourceChoice);
                var sourcePanel = (FrameworkElement)sourcePopup.Child;
                Check(sourcePopup.IsOpen && Math.Abs(sourcePanel.ActualWidth - sourceChoice.ActualWidth) < 2, "Styled dropdown opens at field width");
                if (captureRoot is not null) App.SaveCapture(sourcePanel, Path.Combine(captureRoot, "DownloadSource-Dropdown.png"));
                sourceChoice.IsDropDownOpen = false; sourceChoice.SelectedIndex = 0;
                Capture(window, "DownloadSettings-ClientIdHidden");
                var javaChoice = Find<ComboBox>(window, "JavaBox");
                var javaEditor = (TextBox)javaChoice.Template.FindName("PART_EditableTextBox", javaChoice);
                javaEditor.Text = "F:\\custom Java\\bin\\java.exe";
                Check(javaChoice.Text == javaEditor.Text, "Styled editable combo preserves manual Java input");
                javaEditor.Clear();
                Capture(window, "Settings");
                Find<MemoryAllocationEditor>(window, "GlobalMemoryEditor").MemorySlider.Value = 6144;
                Click(Button(window, "保存设置"));
                Check(store.Load().MemoryMb == 6144, "Settings form persists memory");
                Check(Find<CheckBox>(window, "CleanMemoryBeforeLaunchBox").IsChecked == false && !store.Load().CleanMemoryBeforeLaunch, "Memory-cleanup setting starts disabled in the global memory card");
                var helperInfo = MemoryCleanupProcess.CreateStartInfo(@"F:\Program Files\Island\岛屿启动器.exe");
                Check(helperInfo is { UseShellExecute: true, Verb: "runas", WindowStyle: ProcessWindowStyle.Hidden } && helperInfo.Arguments == "--clean-memory-helper", "Only the fixed cleanup helper receives elevation, with no game or settings arguments");
                Check(MemoryCleanupProcess.DecodeExitCode(10).Outcome == MemoryCleanupOutcome.WorkingSetsOnly && MemoryCleanupProcess.DecodeExitCode(999).Outcome == MemoryCleanupOutcome.Failed, "Helper partial and unexpected exit codes are not reported as complete success");
                Find<RadioButton>(window, "NavLibrary").IsChecked = true; SelectFolder(window, state.GameDirectory);
                var card = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().First(c => c.Id == "Alpha");
                using (var stream = File.Create(Path.Combine(root, "example.jar"))) stream.Write([1, 2, 3]);
                ModFiles.Add(card.Instance.Directory, [Path.Combine(root, "example.jar")]);
                var managerState = store.Load();
                var managerCard = new InstanceCard(card.Instance, managerState.For(card.Id));
                var manager = new InstanceWindow(window, managerCard, managerState, () => store.Save(managerState)) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                manager.Show(); manager.UpdateLayout();
                Check(!Descendants<ComboBox>(manager).Single(box => box.Name == "InstanceJavaBox").IsEnabled, "Instance runtime fields inherit global settings until independent mode is enabled");
                var favorite = Descendants<CheckBox>(manager).Single(box => box.Name == "FavoriteBox");
                var toggleFavorite = (IToggleProvider)new CheckBoxAutomationPeer(favorite).GetPattern(PatternInterface.Toggle)!;
                toggleFavorite.Toggle();
                var checkMark = (FrameworkElement)favorite.Template.FindName("CheckMark", favorite);
                Check(favorite.IsChecked == true && checkMark.Visibility == Visibility.Visible, "Styled checkbox toggles and displays checked state");
                var editMenu = nameMenu(manager);
                var editor = Descendants<TextBox>(manager).First(t => t.Text == "Alpha");
                // The menu-routing check does not need the desktop clipboard. Avoid
                // querying clipboard contents in restricted test environments.
                editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => { }, (_, e) => { e.CanExecute = false; e.Handled = true; }));
                editMenu.PlacementTarget = editor; editMenu.IsOpen = true; await Task.Delay(80);
                var selectAll = editMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "全选");
                Check(selectAll.CommandTarget == editor, "Text context menu routes commands to its editor");
                if (captureRoot is not null) App.SaveCapture(editMenu, Path.Combine(captureRoot, "Text-ContextMenu.png"));
                editMenu.IsOpen = false;
                _ = window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var modWindow = app.Windows.OfType<ModManagerWindow>().Single(); modWindow.UpdateLayout();
                    var modList = Descendants<ListView>(modWindow).Single(); modList.SelectedIndex = 0; Click(Button(modWindow, "停用所选"));
                    Check(!ModFiles.List(card.Instance.Directory).Single().Enabled, "Mod manager disables a real mod file");
                    Capture(modWindow, "ModManager"); Click(Button(modWindow, "关闭"));
                })); Click(Button(manager, "管理模组"));
                manager.UpdateLayout(); Capture(manager, "InstanceManager");
                var nameBox = Descendants<TextBox>(manager).First(t => t.Text == "Alpha"); nameBox.Text = "Renamed Alpha";
                Click(Button(manager, "保存设置"));
                Check(store.Load().For("Alpha").DisplayName == "Renamed Alpha", "Display rename preserves version directory");
                Check(Directory.Exists(card.Instance.Directory), "Rename leaves game files intact");
                var message = new MessageWindow(window, "移除账号", "移除这个玩家账号？本地游戏和存档会保留。", "移除账号", true) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
                message.Loaded += (_, _) => { Capture(message, "Confirmation"); Check(Button(message, "取消").IsDefault, "Confirmation defaults to cancel"); Click(Button(message, "取消")); };
                Check(message.ShowDialog() == false, "Themed confirmation preserves cancellation");
                Find<RadioButton>(window, "NavLogs").IsChecked = true;
                var logBox = Find<TextBox>(window, "LogBox");
                logBox.Text = string.Join('\n', Enumerable.Repeat(new string('W', 240), 90)); window.UpdateLayout();
                var logScroll = (ScrollViewer)logBox.Template.FindName("PART_ContentHost", logBox);
                logScroll.ScrollToHorizontalOffset(180); logScroll.ScrollToVerticalOffset(80); await Task.Delay(50); window.UpdateLayout();
                var horizontal = Descendants<ScrollBar>(logBox).Single(b => b.Orientation == Orientation.Horizontal);
                Capture(window, "Logs");
                Check(logScroll.HorizontalOffset > 0 && logScroll.VerticalOffset > 0 && horizontal.ActualWidth > 200 && horizontal.ActualHeight <= 12, $"Custom log scrollbars scroll on both axes with correct dimensions (offset={logScroll.HorizontalOffset},{logScroll.VerticalOffset}; bar={horizontal.ActualWidth}×{horizontal.ActualHeight})");
                logBox.Clear(); Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                // Compile a small game stand-in and launch it through the real WPF launch button.
                var candidates = await JavaDiscovery.DiscoverAsync(JavaSearchPaths.ForGameDirectory(root, ""), CancellationToken.None);
                var java = candidates.First(j => j.Is64Bit && j.Major >= 17 && File.Exists(Path.Combine(Path.GetDirectoryName(j.Path)!, "javac.exe")));
                var detectingState = store.Load();
                var detecting = new InstanceWindow(window, new InstanceCard(card.Instance, detectingState.For(card.Id)), detectingState, () => { }) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                detecting.Show(); detecting.UpdateLayout();
                Descendants<CheckBox>(detecting).Single(box => box.Name == "IndependentRuntimeBox").IsChecked = true;
                var detectButton = Button(detecting, "检测"); Click(detectButton);
                await WaitUntil(() => detectButton.IsEnabled, "Per-version Java detection did not finish");
                var detectedChoices = Descendants<ComboBox>(detecting).Single(box => box.Name == "InstanceJavaBox");
                Check(detectedChoices.Items.Count > 2 && detectedChoices.SelectedIndex == 0, "Per-version Java detection lists real runtimes and preserves inherited selection");
                detectedChoices.IsDropDownOpen = true; await Task.Delay(50);
                if (captureRoot is not null) App.SaveCapture((FrameworkElement)((Popup)detectedChoices.Template.FindName("PART_Popup", detectedChoices)).Child, Path.Combine(captureRoot, "InstanceJava-Dropdown.png"));
                detectedChoices.IsDropDownOpen = false;
                var pinnedSave = Button(detecting, "保存设置");
                Check(pinnedSave.TransformToAncestor(detecting).Transform(new Point(0, pinnedSave.ActualHeight)).Y < detecting.ActualHeight, "Instance settings keep the save action visible below the scrolling form");
                detecting.Close();
                var source = Path.Combine(root, "IslandUiFixture.java");
                File.WriteAllText(source, "public class IslandUiFixture { public static void main(String[] a) throws Exception { System.out.println(\"ISLAND_UI_PROCESS_OK\"); System.out.println(\"WORKING_DIRECTORY=\" + System.getProperty(\"user.dir\")); System.out.println(\"JVM_SETTING=\" + System.getProperty(\"island.ui.setting\")); for (String arg : a) System.out.println(\"GAME_ARG=\" + arg); System.err.println(\"ISLAND_STDERR_OK\"); String hold = System.getProperty(\"island.ui.hold\"); long until = System.currentTimeMillis() + 30000; while (hold != null && new java.io.File(hold).exists() && System.currentTimeMillis() < until) Thread.sleep(40); } }", new UTF8Encoding(false));
                var compileInfo = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(java.Path)!, "javac.exe")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--release", "8", "-encoding", "UTF-8", source }) compileInfo.ArgumentList.Add(arg);
                using (var compile = Process.Start(compileInfo)!) { await compile.WaitForExitAsync(); Check(compile.ExitCode == 0, "Compile UI game fixture"); }
                using (var zip = ZipFile.Open(Path.Combine(card.Instance.Directory, "Alpha.jar"), ZipArchiveMode.Create)) zip.CreateEntryFromFile(Path.Combine(root, "IslandUiFixture.class"), "IslandUiFixture.class");
                await RunningGameBrowsingUiTests.RunAsync(window, root, java.Path, Path.Combine(card.Instance.Directory, "Alpha.jar"), Check, Capture);
                await JavaCacheUiTests.VerifyLaunchAsync(window, root, java, Path.Combine(card.Instance.Directory, "Alpha.jar"), Check, Capture);
                // Reloading preferences via Refresh avoids writing stale test state after the UI has saved settings.
                Find<ListBox>(window, "InstanceList").SelectedItem = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().First(c => c.Id == "Alpha");
                Click(Find<Button>(window, "LaunchButton"));
                var deadline = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < deadline && !Find<TextBox>(window, "LogBox").Text.Contains("游戏已退出")) await Task.Delay(100);
                var log = Find<TextBox>(window, "LogBox").Text;
                Check(log.Contains("ISLAND_UI_PROCESS_OK") && log.Contains("ISLAND_STDERR_OK") && log.Contains("退出码 0"), "WPF launch button starts JVM, drains both streams, observes clean exit");
                await WaitUntil(() => Find<Button>(window, "LaunchButton").IsEnabled, "Launch controls did not recover after cleanup");
                Check(Find<Button>(window, "LaunchButton").IsEnabled, "Launch controls recover after exit");
                var files = Directory.GetFiles(Path.Combine(store.DirectoryPath, "logs"), "*.log");
                var savedLog = File.ReadAllText(files.Single());
                Check(savedLog.Contains("ISLAND_UI_PROCESS_OK") && savedLog.Contains("ISLAND_STDERR_OK"), "Concurrent stdout and stderr persist without sharing errors");
                Check(store.Load().For("Alpha").LastPlayed is not null, "Real launch persists play history");
                var otherRoot = Path.Combine(root, "other game", ".minecraft");
                var emptyRoot = Path.Combine(root, "empty game", ".minecraft");
                var otherInstance = Path.Combine(otherRoot, "versions", "Alpha"); Directory.CreateDirectory(otherInstance); Directory.CreateDirectory(emptyRoot);
                File.Copy(Path.Combine(card.Instance.Directory, "Alpha.json"), Path.Combine(otherInstance, "Alpha.json"));
                File.Copy(Path.Combine(card.Instance.Directory, "Alpha.jar"), Path.Combine(otherInstance, "Alpha.jar"));
                folderSelection = [otherRoot, emptyRoot, otherRoot.ToUpperInvariant() + Path.DirectorySeparatorChar];
                _ = window.Dispatcher.BeginInvoke(new Action(() => Click(Button(app.Windows.OfType<AddInstanceWindow>().Single(), "添加游戏文件夹（可多选）  →"))));
                Click(Find<Button>(window, "AddInstanceButton"));
                await WaitUntil(() => app.Windows.OfType<GameFoldersWindow>().Any(), "Folder naming dialog did not open");
                var naming = app.Windows.OfType<GameFoldersWindow>().Single(); naming.UpdateLayout();
                var folderNames = Descendants<TextBox>(naming).Where(box => box.Name == "FolderName").ToArray();
                Check(folderNames.Length == 2 && store.Load().GameFolders.Count == 1, "Batch folder import previews unique paths before committing");
                folderNames[0].Text = "文件夹1"; folderNames[1].Text = "文件夹2"; Capture(naming, "NameImportedFolders"); Click(Button(naming, "保存并导入"));
                await WaitUntil(() => Find<Border>(window, "OperationPanel").Visibility == Visibility.Collapsed, "Folder import did not finish");
                Check(store.Load().GameFolders.Count == 3 && store.Load().ActiveGameFolder.Name == "文件夹1", "Import retains existing directory and saves names for multiple new folders");
                var tabs = Find<ItemsControl>(window, "FolderTabs");
                Check(tabs.Items.Count == 3 && ((StackPanel)Find<ScrollViewer>(window, "FolderTabsScroll").Content).Children[0] == Find<RadioButton>(window, "FilterFavorites"), "Favorites is first, followed by horizontal folder tabs");
                Check(window.FindName("LibrarySelector") is null && window.FindName("FilterAll") is null, "Folder tabs replace the all-instances filter without a directory dropdown");
                Check(((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).GameDirectory == otherRoot, "Selecting imported folder shows its own same-named instance");
                _ = window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<GameFoldersWindow>().Single(); dialog.UpdateLayout();
                    Descendants<TextBox>(dialog).Single(t => t.Text == "文件夹1").Text = "模组收藏"; Capture(dialog, "FolderSettings"); Click(Button(dialog, "保存设置"));
                }));
                Click(Find<Button>(window, "ManageFoldersButton"));
                Check(store.Load().ActiveGameFolder.Name == "模组收藏" && tabs.Items.Cast<RadioButton>().Any(tab => ((TextBlock)tab.Content).Text == "模组收藏"), "Renaming an imported folder updates its tab and persists");
                _ = window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = app.Windows.OfType<InstanceWindow>().Single(); dialog.UpdateLayout();
                    Descendants<CheckBox>(dialog).Single(box => box.Name == "FavoriteBox").IsChecked = true; Click(Button(dialog, "保存设置"));
                }));
                ManageSelectedInstance(window);
                SelectFolder(window, state.GameDirectory); Find<RadioButton>(window, "FilterFavorites").IsChecked = true;
                var favoriteCards = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().ToArray();
                Check(favoriteCards.Any(c => c.GameDirectory == state.GameDirectory && c.Id == "Beta") && favoriteCards.Any(c => c.GameDirectory == otherRoot && c.Id == "Alpha"), "Favorites aggregates instances from all imported folders");
                Find<ListBox>(window, "InstanceList").SelectedItem = favoriteCards.Single(c => c.GameDirectory == otherRoot);
                logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Cross-folder favorite did not finish launching");
                Check(logBox.Text.Contains("WORKING_DIRECTORY=" + otherInstance), "Favorite launches with its own game root rather than the active folder");
                var worldDate = DateTimeOffset.Now;
                FolderAndWorldTests.WriteWorld(card.Instance.Directory, "Cottage", "林间小屋（测试地图）", worldDate);
                Click(Button(window, "↻"));
                Check(Find<TextBlock>(window, "RecentWorldName").Text == "林间小屋（测试地图）" && Find<TextBlock>(window, "RecentPlayedTime").Text.Contains(worldDate.ToString("yyyy.MM.dd  HH:mm")), "Recent play reads actual world name and time from level.dat");
                SelectFolder(window, otherRoot); logBox.Clear(); Click(Find<Button>(window, "ContinueButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Continue did not complete");
                Check(GameFolder.SamePath(store.Load().GameDirectory, state.GameDirectory) && logBox.Text.Contains("WORKING_DIRECTORY=" + card.Instance.Directory), "One-click continue switches to the last game's folder and launches the correct instance");
                Check(cleanupCalls == 0 && !logBox.Text.Contains("内存清理："), "Disabled setting bypasses cleanup for both launch entries");
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                var cleanupBox = Find<CheckBox>(window, "CleanMemoryBeforeLaunchBox"); cleanupBox.IsChecked = true;
                cleanupBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(store.Load().CleanMemoryBeforeLaunch, "Clicking the memory-cleanup option saves it immediately without a separate settings confirmation");
                Click(Button(window, "保存设置"));
                Check(store.Load().CleanMemoryBeforeLaunch, "Saving global settings enables pre-launch memory cleanup");
                cleanupBox.BringIntoView(); window.UpdateLayout(); Capture(window, "MemoryCleanupSettings");
                var restoredCleanupWindow = new MainWindow(store) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                restoredCleanupWindow.Show();
                Check(Find<CheckBox>(restoredCleanupWindow, "CleanMemoryBeforeLaunchBox").IsChecked == true, "A new launcher window restores the enabled memory-cleanup option");
                restoredCleanupWindow.Close();
                Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                cleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => cleanupCalls == 1, "Pre-launch cleanup did not start");
                Check(window.DollMemoryFeedback.IsOpen && !logBox.Text.Contains("游戏进程已启动")
                    && ((TextBlock)((Border)window.DollMemoryFeedback.Child).Child).Text.Contains("清理结束后再启动"), "A visible progress popup accompanies cleanup and the game waits for it to finish");
                if (captureRoot is not null) App.SaveCapture((FrameworkElement)window.DollMemoryFeedback.Child, Path.Combine(captureRoot, "PreLaunch-Memory-Progress.png"));
                cleanupGate.SetResult(); cleanupGate = null;
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Launch after cleanup did not finish");
                Check(cleanupCalls == 1 && logBox.Text.IndexOf("已回收系统工作集", StringComparison.Ordinal) < logBox.Text.IndexOf("内存：", StringComparison.Ordinal) && logBox.Text.Contains("退出码 0"), "Launch awaits exactly one cleanup before memory allocation and the real fixture JVM");
                Check(window.DollMemoryFeedback.IsOpen && ((TextBlock)((Border)window.DollMemoryFeedback.Child).Child).Text.Contains("本次释放："), "Automatic cleanup shows the measured released memory in a small result popup");
                if (captureRoot is not null) App.SaveCapture((FrameworkElement)window.DollMemoryFeedback.Child, Path.Combine(captureRoot, "PreLaunch-Memory-Result.png"));
                cleanupOutcome = MemoryCleanupOutcome.AuthorizationDeclined; logBox.Clear(); Click(Find<Button>(window, "ContinueButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Continue after declined authorization did not finish");
                Check(cleanupCalls == 2 && logBox.Text.Contains("已取消管理员授权") && logBox.Text.Contains("退出码 0"), "Continue uses the same cleanup path and declined UAC still allows the fixture game");
                cleanupThrows = true; logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Launch after cleanup failure did not finish");
                Check(cleanupCalls == 3 && logBox.Text.Contains("模拟内存清理失败") && logBox.Text.Contains("退出码 0"), "Cleanup errors are logged without preventing game launch");
                cleanupThrows = false; cleanupCanceled = true; logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => logBox.Text.Contains("任务已取消") && Find<Button>(window, "LaunchButton").IsEnabled, "Canceling cleanup did not recover the launch UI");
                Check(cleanupCalls == 4 && !logBox.Text.Contains("游戏进程已启动"), "Canceling the launch during cleanup never starts Java");
                cleanupCanceled = false; Find<RadioButton>(window, "NavSettings").IsChecked = true; cleanupBox.IsChecked = false; Click(Button(window, "保存设置"));
                Check(!store.Load().CleanMemoryBeforeLaunch, "Disabling cleanup persists for subsequent launches");
                Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                SelectFolder(window, emptyRoot);
                Check(Find<ListBox>(window, "InstanceList").Items.Count == 0 && !Find<Button>(window, "LaunchButton").IsEnabled && Find<Button>(window, "ContinueButton").IsEnabled, "Empty folder keeps its own empty state while global continue remains available");
                SelectFolder(window, state.GameDirectory);
                Find<ListBox>(window, "InstanceList").SelectedItem = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().Single(c => c.Id == "Beta");
                SelectFolder(window, otherRoot); SelectFolder(window, state.GameDirectory);
                Check(((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).Id == "Beta", "Horizontal folder switching restores each folder's selection");
                window.UpdateLayout(); Capture(window, "FolderTabs-RecentPlay");
                window.Width = 1020; window.Height = 700; window.UpdateLayout(); await Task.Delay(100); Capture(window, "FolderTabs-RecentPlay-1020");
                Check(Find<ScrollViewer>(window, "FolderTabsScroll").ActualWidth > 250 && Find<Button>(window, "ContinueButton").IsVisible, "Folder tabs and continue remain visible at minimum window size");
                window.Width = 1240; window.Height = 830;
                folderSelection = [otherRoot];
                _ = window.Dispatcher.BeginInvoke(new Action(() => Click(Button(app.Windows.OfType<AddInstanceWindow>().Single(), "添加游戏文件夹（可多选）  →"))));
                Click(Find<Button>(window, "AddInstanceButton"));
                await WaitUntil(() => app.Windows.OfType<GameFoldersWindow>().Any(), "Reimport naming dialog did not open");
                var canceled = app.Windows.OfType<GameFoldersWindow>().Single(); canceled.UpdateLayout(); Descendants<TextBox>(canceled).Single(box => box.Name == "FolderName").Text = "不应保存"; Click(Button(canceled, "取消"));
                await WaitUntil(() => Find<Border>(window, "OperationPanel").Visibility == Visibility.Collapsed, "Canceled import did not finish");
                Check(store.Load().GameFolders.Count == 3 && store.Load().GameFolders.Any(f => f.Name == "模组收藏"), "Canceling import preserves existing folder names and does not add duplicates");
                void EditSelected(Action<InstanceWindow> edit)
                {
                    _ = window.Dispatcher.BeginInvoke(new Action(() => { var dialog = app.Windows.OfType<InstanceWindow>().Single(); dialog.UpdateLayout(); edit(dialog); }));
                    ManageSelectedInstance(window);
                }
                void SetIsolation(VersionIsolation mode)
                {
                    _ = window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var dialog = app.Windows.OfType<GameFoldersWindow>().Single(); dialog.UpdateLayout();
                        Descendants<ComboBox>(dialog).Single(box => box.Tag as string == otherRoot).SelectedValue = mode;
                        Capture(dialog, "FolderIsolation-" + mode); Click(Button(dialog, "保存设置"));
                    }));
                    Click(Find<Button>(window, "ManageFoldersButton"));
                }
                SelectFolder(window, state.GameDirectory);
                Find<ListBox>(window, "InstanceList").SelectedItem = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().Single(c => c.Id == "Alpha");
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                var brokenJava = Path.Combine(root, "not-a-java.exe"); File.WriteAllText(brokenJava, "fixture");
                javaChoice.SelectedItem = null; javaChoice.Text = brokenJava; Click(Button(window, "保存设置"));
                Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                EditSelected(dialog =>
                {
                    Descendants<CheckBox>(dialog).Single(box => box.Name == "IndependentRuntimeBox").IsChecked = true;
                    var choice = Descendants<ComboBox>(dialog).Single(box => box.Name == "InstanceJavaBox");
                    choice.SelectedItem = null; choice.Text = Path.Combine(root, "missing-java.exe"); Click(Button(dialog, "保存设置"));
                    Check(dialog.IsVisible && Descendants<TextBlock>(dialog).Any(block => block.Text.Contains("Java 路径不存在")), "Per-version settings reject a missing Java path without closing");
                    choice.Text = java.Path; Capture(dialog, "InstanceJava-Custom"); Click(Button(dialog, "保存设置"));
                });
                Check(store.Load().For("Alpha").JavaPath == java.Path && store.Load().JavaPath == brokenJava && store.Load().For(otherRoot, "Alpha").JavaPath is null, "Instance Java form saves an override without changing global or same-named instance settings");
                logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Per-version Java launch did not complete");
                Check(logBox.Text.Contains("ISLAND_UI_PROCESS_OK") && logBox.Text.Contains("退出码 0"), "WPF launch uses the per-version Java override even when global Java cannot run");
                EditSelected(dialog => { Descendants<ComboBox>(dialog).Single(box => box.Name == "InstanceJavaBox").SelectedIndex = 1; Click(Button(dialog, "保存设置")); });
                Check(store.Load().For("Alpha").JavaPath == "", "Instance Java form persists explicit automatic selection");
                EditSelected(dialog => { Descendants<ComboBox>(dialog).Single(box => box.Name == "InstanceJavaBox").SelectedIndex = 0; Click(Button(dialog, "保存设置")); });
                Check(store.Load().For("Alpha").JavaPath is null, "Instance Java form restores inheritance of global setting");
                Find<RadioButton>(window, "NavSettings").IsChecked = true; javaChoice.SelectedItem = null; javaChoice.Text = ""; Click(Button(window, "保存设置"));
                Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                ModFiles.Add(otherRoot, [Path.Combine(root, "example.jar")]);
                FolderAndWorldTests.WriteWorld(otherRoot, "Shared World", "共享地图（测试地图）", DateTimeOffset.Now.AddMinutes(1));
                SetIsolation(VersionIsolation.Disabled); SelectFolder(window, otherRoot);
                Check(store.Load().ActiveGameFolder.Isolation == VersionIsolation.Disabled && ((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).RuntimeDirectory == otherRoot, "Folder isolation form persists disabled mode and updates resolved runtime directory");
                EditSelected(dialog =>
                {
                    _ = window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var modWindow = app.Windows.OfType<ModManagerWindow>().Single(); modWindow.UpdateLayout(); var list = Descendants<ListView>(modWindow).Single();
                        Check(list.Items.Count == 1 && ((ModFile)list.Items[0]).Path.StartsWith(Path.Combine(otherRoot, "mods")), "Shared mod manager lists root mods");
                        list.SelectedIndex = 0; Click(Button(modWindow, "停用所选"));
                        Check(!ModFiles.List(otherRoot).Single().Enabled && ModFiles.List(otherInstance).Count == 0, "Shared mod toggle changes only the resolved root mod file");
                        Capture(modWindow, "ModManager-Shared"); Click(Button(modWindow, "关闭"));
                    })); Click(Button(dialog, "管理模组"));
                    Capture(dialog, "InstanceManager-Shared"); Click(Button(dialog, "关闭"));
                });
                Check(Find<TextBlock>(window, "RecentWorldName").Text == "共享地图（测试地图）", "Recent play reads shared root world metadata");
                SelectFolder(window, state.GameDirectory); logBox.Clear(); Click(Find<Button>(window, "ContinueButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Shared-root continue did not complete");
                Check(logBox.Text.Contains("WORKING_DIRECTORY=" + otherRoot), "Continue launches shared saves using the root working directory");
                SetIsolation(VersionIsolation.Enabled);
                Check(((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).RuntimeDirectory == otherInstance, "Folder isolation form enables a separate version runtime directory");
                SetIsolation(VersionIsolation.Auto);
                Check(((InstanceCard)Find<ListBox>(window, "InstanceList").SelectedItem).RuntimeDirectory == otherRoot, "Automatic folder mode detects existing shared root data");
                SelectFolder(window, state.GameDirectory);
                Find<ListBox>(window, "InstanceList").SelectedItem = Find<ListBox>(window, "InstanceList").Items.Cast<InstanceCard>().Single(c => c.Id == "Alpha");
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                Find<MemoryAllocationEditor>(window, "GlobalMemoryEditor").AutomaticOption.IsChecked = true;
                var globalArguments = Descendants<TextBox>(window).Single(box => box.Name == "ExtraJvmArgumentsBox");
                globalArguments.Text = "-Disland.ui.setting=global"; Click(Button(window, "保存设置"));
                var globalEditor = Descendants<JvmSettingsEditor>(window).Single();
                Descendants<TextBox>(globalEditor).Single(box => box.Name == "ExtraGameArgumentsBox").Text = "--island-test \"中文 参数\"";
                Descendants<TextBox>(globalEditor).Single(box => box.Name == "PreLaunchCommandBox").Text = "echo pre-ok>island-before.txt";
                Descendants<ComboBox>(globalEditor).Single(box => box.Name == "GarbageCollectorBox").SelectedValue = GarbageCollectorMode.PreferGenerationalZ;
                Descendants<CheckBox>(globalEditor).Single(box => box.Name == "HighPerformanceGpuBox").IsChecked = true;
                Descendants<CheckBox>(globalEditor).Single(box => box.Name == "VerifyLoginCertificatesBox").IsChecked = false;
                Click(Button(window, "保存设置"));
                Check(store.Load().Jvm is { GarbageCollector: GarbageCollectorMode.PreferGenerationalZ, HighPerformanceGpu: true, VerifyLoginCertificates: false, PreLaunchCommand: "echo pre-ok>island-before.txt" }, "Global form persists collector, game arguments, pre-command, GPU request and explicit SSL preference");
                Check(store.Load().AutoMemory && !Find<MemoryAllocationEditor>(window, "GlobalMemoryEditor").MemorySlider.IsEnabled && store.Load().Jvm.ExtraArguments == "-Disland.ui.setting=global", "Global form persists automatic memory and JVM arguments and disables manual limit");
                Find<ContentControl>(window, "JvmHost").BringIntoView(); window.UpdateLayout(); Capture(window, "GlobalJvmSettings");
                Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                EditSelected(dialog =>
                {
                    Descendants<CheckBox>(dialog).Single(box => box.Name == "IndependentRuntimeBox").IsChecked = false;
                    Check(!Descendants<MemoryAllocationEditor>(dialog).Single().MemorySlider.IsEnabled, "Restoring global mode disables independent memory input");
                    Click(Button(dialog, "保存设置"));
                });
                Check(!store.Load().For("Alpha").HasIndependentSettings, "Restoring global mode clears all Java, memory and JVM overrides");
                logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => (logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled) || app.Windows.OfType<MessageWindow>().Any(), "Inherited automatic-memory launch did not complete");
                if (app.Windows.OfType<MessageWindow>().FirstOrDefault() is { } memoryNotice)
                {
                    Check(logBox.Text.Contains("可用内存不足"), "Automatic-memory launch reports insufficient available system memory without starting a game");
                    Click(Button(memoryNotice, "知道了")); await WaitUntil(() => Find<Button>(window, "LaunchButton").IsEnabled, "Launch did not recover after memory notice");
                }
                else Check(logBox.Text.Contains("JVM_SETTING=global") && logBox.Text.Contains("自动分配") && logBox.Text.Contains("退出码 0"), "Actual WPF launch inherits global JVM arguments and allocates memory automatically");
                Check(File.Exists(Path.Combine(card.RuntimeDirectory, "island-before.txt")) && gpuRequests == gpuRestores
                    && (!logBox.Text.Contains("游戏已退出") || (logBox.Text.Contains("GAME_ARG=中文 参数") && gpuRequests > 0)), "Actual WPF launch executes its pre-command, passes quoted game arguments and releases the injected GPU preference lease");
                EditSelected(dialog =>
                {
                    Descendants<CheckBox>(dialog).Single(box => box.Name == "IndependentRuntimeBox").IsChecked = true;
                    Descendants<MemoryAllocationEditor>(dialog).Single().CustomOption.IsChecked = true;
                    Descendants<MemoryAllocationEditor>(dialog).Single().MemorySlider.Value = 1024;
                    Click(Button(dialog, "Java 虚拟机设置  ▸")); dialog.UpdateLayout();
                    foreach (var field in new[] { "OmitDefaultJvmBox", "OmitJvmOptimizationsBox", "SkipJavaCompatibilityBox" }) Descendants<CheckBox>(dialog).Single(box => box.Name == field).IsChecked = true;
                    var argumentsBox = Descendants<TextBox>(dialog).Single(box => box.Name == "ExtraJvmArgumentsBox");
                    argumentsBox.Text = "-Dtest=\"unclosed"; Click(Button(dialog, "保存设置"));
                    Check(dialog.IsVisible && Descendants<TextBlock>(dialog).Any(block => block.Text.Contains("引号未闭合")), "Invalid custom JVM syntax is rejected before independent settings are saved");
                    argumentsBox.Text = "-Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Disland.ui.setting=\"独立 参数\"";
                    Descendants<JvmSettingsEditor>(dialog).Single().BringIntoView(); dialog.UpdateLayout(); Capture(dialog, "IndependentJvmSettings");
                    Click(Button(dialog, "保存设置"));
                });
                var independentSaved = store.Load();
                Check(independentSaved.For("Alpha") is { AutoMemory: false, MemoryMb: 1024, Jvm: { OmitDefaultArguments: true, OmitOptimizations: true, SkipCompatibilityCheck: true } } && independentSaved.AutoMemory,
                    "Independent settings persist all three JVM switches and manual memory without altering global auto allocation");
                Find<RadioButton>(window, "NavSettings").IsChecked = true; globalArguments.Text = "-Disland.ui.setting=changed"; Click(Button(window, "保存设置")); Find<RadioButton>(window, "NavLibrary").IsChecked = true;
                logBox.Clear(); Click(Find<Button>(window, "LaunchButton"));
                await WaitUntil(() => logBox.Text.Contains("游戏已退出") && Find<Button>(window, "LaunchButton").IsEnabled, "Independent JVM launch did not complete");
                Check(logBox.Text.Contains("JVM_SETTING=独立 参数") && logBox.Text.Contains("内存：1024 MB · 手动设置") && logBox.Text.Contains("退出码 0"), "Actual WPF launch retains independent JVM and memory after global settings change");
                EditSelected(dialog => { Descendants<CheckBox>(dialog).Single(box => box.Name == "IndependentRuntimeBox").IsChecked = false; Click(Button(dialog, "保存设置")); });
                Check(store.Load().RuntimeFor(state.GameDirectory, "Alpha").Jvm.ExtraArguments == "-Disland.ui.setting=changed", "Returning to global mode immediately inherits the latest global JVM settings");
                for (var sample = 0; sample < 10; sample++)
                {
                    var sampleId = $"界面测试 {sample + 1:00} · 林间小屋";
                    var sampleDirectory = Path.Combine(state.GameDirectory, "versions", sampleId); Directory.CreateDirectory(sampleDirectory);
                    File.WriteAllText(Path.Combine(sampleDirectory, sampleId + ".json"), "{\"id\":\"" + sampleId + "\",\"mainClass\":\"IslandUiFixture\"}");
                    File.WriteAllText(Path.Combine(sampleDirectory, "blockdock-instance.json"), System.Text.Json.JsonSerializer.Serialize(new { name = sampleId, minecraft = "1.21.1", loader = sample % 3 == 0 ? "Fabric" : sample % 3 == 1 ? "Forge" : "Vanilla" }));
                }
                Click(Button(window, "↻"));
                foreach (var size in new[] { (1240d, 830d), (1020d, 700d), (1600d, 900d) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); await Task.Delay(80); window.UpdateLayout();
                    var panel = Descendants<AdaptiveCardPanel>(Find<ListBox>(window, "InstanceList")).Single();
                    var tiles = panel.Children.Cast<FrameworkElement>().ToArray(); var firstRow = tiles.Where(tile => Math.Abs(tile.TranslatePoint(new Point(), panel).Y) < 1).ToArray();
                    Check(firstRow.Length >= 3 && firstRow.All(tile => Math.Abs(tile.ActualWidth - firstRow[0].ActualWidth) < 1) && Math.Abs(firstRow[^1].TranslatePoint(new Point(firstRow[^1].ActualWidth, 0), panel).X - panel.ActualWidth) < 1,
                        $"Responsive cards evenly fill the available row at {(int)size.Item1}px without a right-side gap");
                    var accountButton = Find<Button>(window, "SidebarAccountButton"); var launchButton = Find<Button>(window, "LaunchButton");
                    Check(Math.Abs(accountButton.TranslatePoint(new Point(0, accountButton.ActualHeight / 2), window).Y - launchButton.TranslatePoint(new Point(0, launchButton.ActualHeight / 2), window).Y) < 1.1,
                        $"Account and launch buttons align vertically at {(int)size.Item1}px");
                    var sidebarSurface = Find<MaterialBorder>(window, "SidebarSurface");
                    var contentSurface = Find<MaterialBorder>(window, "MainContentSurface");
                    var sidebarBounds = sidebarSurface.TransformToAncestor(window).TransformBounds(new Rect(sidebarSurface.RenderSize));
                    var contentBounds = contentSurface.TransformToAncestor(window).TransformBounds(new Rect(contentSurface.RenderSize));
                    var windowControls = Find<StackPanel>(window, "WindowControls");
                    var controlsBounds = windowControls.TransformToAncestor(window).TransformBounds(new Rect(windowControls.RenderSize));
                    var trafficLights = Find<StackPanel>(window, "WindowTrafficLights");
                    var lightsBounds = trafficLights.TransformToAncestor(window).TransformBounds(new Rect(trafficLights.RenderSize));
                    var editionLabel = Find<TextBlock>(window, "WindowEditionLabel");
                    var editionBounds = editionLabel.TransformToAncestor(window).TransformBounds(new Rect(editionLabel.RenderSize));
                    Check(Math.Abs(sidebarBounds.Left - 12) < 1 && Math.Abs(window.ActualWidth - contentBounds.Right - 12) < 1
                        && Math.Abs(sidebarBounds.Top - 40) < 1 && Math.Abs(sidebarBounds.Top - contentBounds.Top) < 1
                        && Math.Abs(sidebarBounds.Bottom - contentBounds.Bottom) < 1 && Math.Abs(window.ActualHeight - contentBounds.Bottom - 12) < 1
                        && Math.Abs(contentBounds.Left - sidebarBounds.Right - 12) < 1 && contentSurface.Area == SurfaceArea.Content,
                        $"The outer background surrounds two aligned inset modules at {(int)size.Item1}px");
                    Check(controlsBounds.Bottom <= contentBounds.Top && controlsBounds.Right <= window.ActualWidth
                        && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(windowControls)
                        && Find<Button>(window, "WindowCloseButton").IsVisible && Find<Button>(window, "WindowMaximizeButton").IsVisible
                        && lightsBounds.Right < editionBounds.Left && editionBounds.Right < controlsBounds.Left && lightsBounds.Bottom <= sidebarBounds.Top
                        && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(trafficLights)
                        && trafficLights.Children.OfType<Button>().Count(button => button.IsVisible && button.Focusable && button.ActualWidth >= 20 && button.ToolTip is not null) == 3,
                        $"Mac and Windows window controls stay accessible without overlapping the title at {(int)size.Item1}px");
                    WindowOutlineUiTests.Verify(window, Check);
                    Capture(window, "ResponsiveCards-" + (int)size.Item1);
                }
                Check(!Find<TextBlock>(window, "StatusLabel").IsVisible, "Launch footer status text stays hidden");
                window.Width = 1240; window.Height = 830; window.UpdateLayout();
                var libraryList = Find<ListBox>(window, "InstanceList");
                libraryList.SelectedItem = libraryList.Items.Cast<InstanceCard>().Single(item => item.Id == "Beta");
                Button Star(string id) => Descendants<Button>(libraryList).Single(button => button.Name == "CardFavoriteButton" && button.Tag is InstanceCard item && item.Id == id && GameFolder.SamePath(item.GameDirectory, state.GameDirectory));
                Click(Star("Alpha")); window.UpdateLayout();
                Check(store.Load().For("Alpha").Favorite && Star("Alpha").Content as string == "★" && ((InstanceCard)libraryList.SelectedItem).Id == "Beta", "Card corner favorite persists immediately and preserves the selected instance");
                var favoriteKey = libraryList.Items.Cast<InstanceCard>().Single(item => item.Id == "Alpha").Key;
                Find<RadioButton>(window, "FilterFavorites").IsChecked = true; window.UpdateLayout(); Click(Star("Alpha"));
                Check(!store.Load().For("Alpha").Favorite && !libraryList.Items.Cast<InstanceCard>().Any(item => item.Key == favoriteKey), "Unfavoriting from the card removes it from the aggregate favorites view");
                SelectFolder(window, state.GameDirectory); window.UpdateLayout();
                var accidentalManager = false;
                _ = window.Dispatcher.BeginInvoke(new Action(() => { foreach (var opened in app.Windows.OfType<InstanceWindow>().ToArray()) { accidentalManager = true; opened.Close(); } }));
                libraryList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = Star("Alpha") }); await Task.Delay(30);
                Check(!accidentalManager, "Double-clicking the favorite control does not open instance settings");
                Find<RadioButton>(window, "NavSettings").IsChecked = true;
                Click(Descendants<Button>(window).Single(button => button.Name == "ThemePresetButton" && button.Tag as string == "#32699B")); window.UpdateLayout();
                var themePreview = (AppearancePreview)Find<ContentControl>(window, "AppearancePreviewHost").Content;
                var appliedTheme = ((SolidColorBrush)Find<Button>(window, "LaunchButton").Background).Color;
                Check(themePreview.RenderContext.ThemeColor == "#32699B" && appliedTheme == (Color)ColorConverter.ConvertFromString("#32699B") && store.Load().ThemeColor == "#32699B",
                    "Theme preset immediately changes the live launch button and persists the selected color");
                Click(Find<Button>(window, "OpenAppearanceButton")); window.UpdateLayout();
                Find<TextBox>(window, "ThemeColorBox").Text = "#D7B84A"; window.UpdateLayout();
                Check(((SolidColorBrush)themePreview.Resources["OnAccent"]).Color == Colors.Black && ((SolidColorBrush)Find<Button>(window, "LaunchButton").Background).Color == appliedTheme,
                    "Bright custom tuning stays inside the miniature until saved and uses readable dark text");
                Find<TextBox>(window, "ThemeColorBox").Text = "#7856A4"; Click(Button(Find<Grid>(window, "AppearancePage"), "保存外观设置"));
                Check(store.Load().ThemeColor == "#7856A4", "Custom theme color persists through the settings form");
                Find<ScrollViewer>(window, "AppearanceSettingsScroll").ScrollToTop(); window.UpdateLayout(); Capture(window, "Theme-Settings");
                Find<RadioButton>(window, "NavLibrary").IsChecked = true; window.UpdateLayout(); Capture(window, "Theme-Violet");
                var restoredThemeWindow = new MainWindow(store) { Left = -16000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false }; restoredThemeWindow.Show(); restoredThemeWindow.UpdateLayout();
                Check(Find<TextBox>(restoredThemeWindow, "ThemeColorBox").Text == "#7856A4" && ((SolidColorBrush)Find<Button>(restoredThemeWindow, "LaunchButton").Background).Color == (Color)ColorConverter.ConvertFromString("#7856A4"), "A new window restores the saved theme color"); restoredThemeWindow.Close();
                var modCard = libraryList.Items.Cast<InstanceCard>().Single(item => item.Id == "Alpha"); var sampleMods = Path.Combine(modCard.RuntimeDirectory, "mods"); Directory.CreateDirectory(sampleMods);
                for (var sample = 0; sample < 300; sample++) File.WriteAllText(Path.Combine(sampleMods, $"fixture-{sample:000}-很长的模组文件名用于测试省略与完整路径提示.jar"), "fixture");
                var largeModWindow = new ModManagerWindow(window, modCard) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false }; largeModWindow.Show(); largeModWindow.UpdateLayout();
                var largeModList = Descendants<ListView>(largeModWindow).Single(); var modSearch = Descendants<TextBox>(largeModWindow).Single(box => box.Name == "ModSearchBox"); var modFilter = Descendants<ComboBox>(largeModWindow).Single(box => box.Name == "ModFilterBox");
                Check(largeModList.Items.Count == 301 && largeModList.ActualHeight > 240, "Dedicated mod manager shows a tall scrollable table for hundreds of mods");
                modSearch.Text = "FIXTURE-"; Check(largeModList.Items.Count == 300, "Mod filename search ignores case and excludes nonmatching files");
                largeModList.SelectedItems.Add(largeModList.Items[0]); largeModList.SelectedItems.Add(largeModList.Items[1]); Click(Button(largeModWindow, "停用所选")); modFilter.SelectedIndex = 2;
                Check(largeModList.Items.Count == 2 && ModFiles.List(modCard.RuntimeDirectory).Count(file => file.Name.StartsWith("fixture-") && !file.Enabled) == 2, "Batch mod disable updates files and works with disabled-state filtering");
                foreach (var item in largeModList.Items.Cast<ModFile>().ToArray()) largeModList.SelectedItems.Add(item); Click(Button(largeModWindow, "启用所选"));
                Check(largeModList.Items.Count == 0 && ModFiles.List(modCard.RuntimeDirectory).Count(file => file.Name.StartsWith("fixture-") && file.Enabled) == 300, "Batch re-enable updates the disabled filter without deleting mod files");
                modSearch.Clear(); modFilter.SelectedIndex = 0; largeModWindow.UpdateLayout();
                foreach (var size in new[] { (920d, 690d), (740d, 520d) })
                {
                    largeModWindow.Width = size.Item1; largeModWindow.Height = size.Item2; largeModWindow.UpdateLayout();
                    var columns = ((GridView)largeModList.View).Columns;
                    Check(Math.Abs(columns.Sum(column => column.Width) - largeModList.ActualWidth) <= 20 && Button(largeModWindow, "关闭").TransformToAncestor(largeModWindow).Transform(new Point(0, Button(largeModWindow, "关闭").ActualHeight)).Y < largeModWindow.ActualHeight,
                        $"Mod columns fill the table and actions remain visible at {(int)size.Item1}px");
                    Capture(largeModWindow, "ModManager-Large-" + (int)size.Item1);
                }
                largeModWindow.Close();
                backgroundSelection = AppearanceUiTests.MakeImage(root);
                await AppearanceUiTests.Run(window, store, backgroundSelection, Check, Capture);
                if (args.Contains("--network"))
                {
                    var create = new NewInstanceWindow(window, state.DownloadSettings()) { Left = -15000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                    Exception? dialogError = null;
                    create.Loaded += async (_, _) =>
                    {
                        try
                        {
                            var combo = Descendants<ComboBox>(create).ToArray();
                            var until = DateTime.UtcNow.AddSeconds(50);
                            while (combo[0].Items.Count == 0 && DateTime.UtcNow < until) await Task.Delay(80);
                            Check(combo[0].Items.Count > 100, "Creation dialog loads live release catalog");
                            combo[0].SelectedItem = combo[0].Items.Cast<GameVersion>().First(v => v.Id == "1.21.1");
                            combo[1].SelectedItem = LoaderKind.Fabric;
                            var install = Button(create, "创建并安装  →");
                            while (!install.IsEnabled && DateTime.UtcNow < until) await Task.Delay(80);
                            Check(combo[2].Items.Count > 0 && install.IsEnabled, "Creation dialog queries compatible Fabric releases");
                            combo[0].IsDropDownOpen = true; await Task.Delay(80);
                            var versionPopup = (Popup)combo[0].Template.FindName("PART_Popup", combo[0]);
                            var menuScroll = Descendants<ScrollViewer>(versionPopup.Child).First(); menuScroll.ScrollToVerticalOffset(40); await Task.Delay(50);
                            Check(menuScroll.VerticalOffset > 0, "Version dropdown scrolls through the live catalog");
                            if (captureRoot is not null) App.SaveCapture((FrameworkElement)versionPopup.Child, Path.Combine(captureRoot, "Minecraft-Dropdown.png"));
                            combo[0].IsDropDownOpen = false;
                            var captureIndex = Array.IndexOf(args, "--capture-dialogs");
                            if (captureIndex >= 0)
                            {
                                var output = Path.GetFullPath(args[captureIndex + 1]); Directory.CreateDirectory(output);
                                App.SaveCapture((FrameworkElement)create.Content, Path.Combine(output, "CreateInstance.png"));
                            }
                            Click(install);
                        }
                        catch (Exception ex) { dialogError = ex; create.Close(); }
                    };
                    var accepted = create.ShowDialog();
                    if (dialogError is not null) throw dialogError;
                    Check(accepted == true && create.Request is { MinecraftVersion: "1.21.1", Loader: LoaderKind.Fabric, LoaderVersion: not null }, "Creation dialog emits complete install request");
                }
                Console.WriteLine($"ALL {checks} UI CHECKS PASSED");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            finally
            {
                foreach (var message in app.Windows.OfType<MessageWindow>().ToArray()) message.Close();
                var finish = DateTime.UtcNow.AddSeconds(5);
                while (Find<Border>(window, "OperationPanel").Visibility == Visibility.Visible && DateTime.UtcNow < finish) await Task.Delay(50);
                foreach (Window open in app.Windows.Cast<Window>().Where(w => w != window).ToArray()) open.Close();
                window.Close();
            }
        };
        app.Run(window);
        if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        return exit;
    }
    private static ContextMenu nameMenu(Window manager) => Descendants<TextBox>(manager).First(t => t.Text == "Alpha").ContextMenu;
}
