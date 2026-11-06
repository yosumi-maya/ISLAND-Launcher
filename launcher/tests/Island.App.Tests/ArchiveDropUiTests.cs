using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.App;
using Island.Core;
using BlockDock.Core;

internal static class ArchiveDropUiTests
{
    private static DragEventArgs Drag(UIElement target, IDataObject data, RoutedEvent routed, DragDropEffects allowed = DragDropEffects.Copy)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.None, allowed, target, new Point(30, 30)], null)!;
        args.RoutedEvent = routed; target.RaiseEvent(args); return args;
    }
    private static DataObject Files(params string[] paths) => new(DataFormats.FileDrop, paths);
    private static async Task Wait(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < end) await Task.Delay(20);
        if (!done()) throw new Exception("Archive drop did not complete.");
    }
    public static async Task RunAsync(Window owner, string root, Action<bool,string> check, Action<Window,string> capture)
    {
        var folder = Path.Combine(root,"archive-drop"); Directory.CreateDirectory(folder);
        var pack = Path.Combine(folder,"中文整合包.MRPACK");
        using (var zip = ZipFile.Open(pack,ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Dropped Pack","versionId":"1.0","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
        var zipPath = Path.Combine(folder,"整合包.zip"); File.Copy(pack,zipPath);
        var invalid = Path.Combine(folder,"broken.zip"); File.WriteAllText(invalid,"not a zip");
        var store = new LauncherStore(Path.Combine(folder,"settings"));
        var destination = Path.Combine(folder,"game"); store.Save(new LauncherState { GameDirectory = destination });
        var service = new DownloadCenterUiTests.Service(); var calls = 0;
        TaskCompletionSource? gate = null; var ignoreCancellation = false;
        service.Inspect = async (path, token) => { calls++; if (gate is not null) { if (ignoreCancellation) await gate.Task; else await gate.Task.WaitAsync(token); } return await new LauncherDownloads().InspectAsync(path,token); };
        var cache = new JavaRuntimeCache(store.DirectoryPath,(_,_,_) => Task.FromResult<IReadOnlyList<JavaInstallation>>([]));
        var window = new MainWindow(store,javaRuntimes:cache,downloadService:service) { Owner=owner,Left=-16000,Top=0,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual };
        var confirmationCount=0; var errorCount=0; var correctDestination=true; var hintHidden=true;
        var center = (DownloadCenter)window.FindName("DownloadCenterView");
        var hint = (Border)window.FindName("ArchiveDropHint");
        var timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_,_) =>
        {
            foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(w => w.Owner==window && w.IsVisible).ToArray())
            {
                if (dialog is PackConfirmationWindow confirmation)
                {
                    confirmationCount++; correctDestination &= confirmation.Settings.GameDirectory == destination;
                    hintHidden &= hint.Visibility == Visibility.Collapsed; capture(confirmation,"Archive-Drop-Confirmation"); confirmation.Close();
                }
                else if (dialog is MessageWindow) { errorCount++; dialog.Close(); }
            }
        };
        try
        {
            window.Show(); window.UpdateLayout(); timer.Start();
            var search=(TextBox)window.FindName("SearchBox"); search.Text="keep search";
            var over=Drag(search,Files(pack),DragDrop.PreviewDragOverEvent);
            check(over.Handled && over.Effects==DragDropEffects.Copy && hint.IsVisible,"Archive over a text field is accepted by the window before the editor handles it");
            capture(window,"Archive-Drop-Hint");
            foreach (var bad in new[] { Files(pack,zipPath),Files(folder),Files(Path.Combine(folder,"missing.zip")),Files(Path.Combine(folder,"other.jar")) })
            {
                var result=Drag(search,bad,DragDrop.PreviewDragOverEvent);
                check(result.Handled && result.Effects==DragDropEffects.None && !hint.IsVisible,"Multiple files, directories, missing files and other extensions are rejected");
            }
            check(Drag(search,Files(pack),DragDrop.PreviewDragOverEvent,DragDropEffects.Move).Effects==DragDropEffects.None,"A source without copy permission is rejected");
            var text=Drag(search,new DataObject(DataFormats.Text,"ordinary text"),DragDrop.PreviewDragOverEvent);
            check(!text.Handled,"Ordinary text drag retains the editor's behavior");
            Drag(search,Files(pack),DragDrop.PreviewDropEvent);
            await Wait(()=>confirmationCount==1 && center.CanImportArchive);
            check(service.GameRequests == 0 && service.SearchRequests == 0, "Local import does not start a Minecraft manifest or catalog refresh");
            check(calls==1 && correctDestination && hintHidden && center.IsVisible,"MRPACK drop opens the existing confirmation with the selected game directory");
            check(search.Text=="keep search" && center.Queue.Items.Count==0,"Dropping leaves text unchanged and canceling does not enqueue installation");
            ((RadioButton)window.FindName("NavSettings")).IsChecked=true;
            var rootBox=(TextBox)window.FindName("ThemeColorBox");
            Drag(rootBox,Files(zipPath),DragDrop.PreviewDropEvent);
            await Wait(()=>confirmationCount==2 && center.CanImportArchive);
            check(calls==2 && center.IsVisible,"ZIP drop from settings also reaches real pack inspection and confirmation");
            Drag(search,Files(invalid),DragDrop.PreviewDropEvent);
            await Wait(()=>errorCount==1 && center.CanImportArchive);
            check(center.Queue.Items.Count==0 && !hint.IsVisible,"Corrupt ZIP shows an import error and restores availability without installation");
            gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            Drag(search,Files(pack),DragDrop.PreviewDropEvent);
            check(!center.CanImportArchive && Drag(search,Files(pack),DragDrop.PreviewDragOverEvent).Effects==DragDropEffects.None,"Repeated drops are rejected while pack inspection is pending");
            check(((Border)center.FindName("ArchiveImportStatusPanel")).IsVisible && ((TextBlock)center.FindName("ArchiveImportStatus")).Text.Contains(Path.GetFileName(pack)),
                "Pending inspection immediately shows the archive name and progress");
            capture(window,"Archive-Import-Reading");
            var before=calls; Drag(search,Files(zipPath),DragDrop.PreviewDropEvent);
            check(calls==before,"Pending import cannot be replaced by another drop");
            gate.SetResult(); await Wait(()=>confirmationCount==3 && center.CanImportArchive); gate=null;
            ignoreCancellation = true; gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var priorCalls = calls; Drag(search,Files(pack),DragDrop.PreviewDropEvent);
            await Wait(() => calls > priorCalls);
            ((Button)center.FindName("CancelArchiveImportButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Wait(() => center.CanImportArchive);
            check(!((Border)center.FindName("ArchiveImportStatusPanel")).IsVisible && confirmationCount == 3,
                "Cancel immediately restores importing even if an inspection provider ignores cancellation");
            gate.SetResult(); await Task.Delay(100); gate = null; ignoreCancellation = false;
            check(confirmationCount == 3 && center.Queue.Items.Count == 0, "A canceled inspection cannot open a late confirmation or install anything");
            var operationGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation=window.RunOperationAsync(token=>operationGate.Task.WaitAsync(token),"fixture operation");
            check(Drag(search,Files(pack),DragDrop.PreviewDragOverEvent).Effects==DragDropEffects.None,"Launcher operations reject archive drops");
            operationGate.SetResult(); await operation;
            check(Drag(search,Files(pack),DragDrop.PreviewDragOverEvent).Effects==DragDropEffects.Copy,"Archive dropping becomes available after the operation ends");
        }
        finally { timer.Stop(); await center.ShutdownAsync(); window.Close(); }
    }
}
