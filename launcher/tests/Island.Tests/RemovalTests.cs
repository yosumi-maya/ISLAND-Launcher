using System.Diagnostics;
using Island.Core;

internal static class RemovalTests
{
    internal static void Run(string root, Action<bool,string> check)
    {
        var fixture=Path.Combine(root,"removal");var first=Path.Combine(fixture,"first");var second=Path.Combine(fixture,"second");
        var state=new LauncherState {GameDirectory=first};state.EnsureGameFolders();var a=state.ActiveGameFolder;var b=state.AddGameFolder(second,"Second");
        Directory.CreateDirectory(first);File.WriteAllText(Path.Combine(first,"keep.txt"),"keep");
        state.For(first,"Same").DisplayName="First";state.RememberInstance("Same");state.SelectGameFolder(b.Id);state.RememberInstance("Other");state.SelectGameFolder(a.Id);
        state.RemoveGameFolder(a.Id);
        check(state.ActiveGameFolder.Id==b.Id && state.GameDirectory==second && state.SelectedInstanceId=="Other","Removing the active directory selects a retained folder and restores its selection");
        check(File.ReadAllText(Path.Combine(first,"keep.txt"))=="keep" && state.For(first,"Same").DisplayName=="First","Removing a directory preserves disk files and reimportable preferences");
        try {state.RemoveGameFolder(b.Id);throw new Exception("Last folder removed");}catch(InvalidOperationException){check(state.GameFolders.Count==1,"At least one game directory remains registered");}
        var store=new LauncherStore(Path.Combine(fixture,"settings"));store.Save(state);
        check(store.Load().GameFolders.Count==1 && store.Load().GameDirectory==second,"Removed active folder is not re-added when saved settings reload");
        state.AddGameFolder(first,"First");state.For(second,"Same").DisplayName="Second";state.SelectGameFolder(state.GameFolders.Single(f=>GameFolder.SamePath(f.Directory,first)).Id);state.RememberInstance("Same");state.ForgetInstance(first,"Same");
        check(state.SelectedInstanceId is null && state.ActiveGameFolder.LastSelectedInstanceId is null && state.For(second,"Same").DisplayName=="Second" && !state.Instances.ContainsKey(first.ToUpperInvariant()+"|Same"),"Forgetting an instance clears its selection and preferences while preserving same-named instances in other roots");
        var target=Path.Combine(first,"versions","Alpha");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"Alpha.json"),"{}");
        check(InstanceDeletion.Target(first,"Alpha",target,[first,second])==target,"Deletion resolves only the selected version directory");
        void Reject(Action action,string label)
        {try{action();throw new Exception("Unsafe removal accepted: "+label);}catch(Exception ex)when(ex is ArgumentException or IOException or InvalidDataException){check(true,label);}}
        Reject(()=>InstanceDeletion.Target(first,"..",first,[first]),"Deletion rejects parent traversal");
        Reject(()=>InstanceDeletion.Target(first,"Alpha",first,[first]),"Deletion rejects a mismatched or shared root target");
        Reject(()=>InstanceDeletion.Target(first,"Missing",Path.Combine(first,"versions","Missing"),[first]),"Deletion rejects missing instances");
        Reject(()=>InstanceDeletion.Target(first,"Alpha",target,[target]),"Deletion protects a version registered as a game root");
        Reject(()=>InstanceDeletion.Target(first,"Alpha",target,[Path.Combine(target,"nested")]),"Deletion protects a nested registered game root");
        var linked=Path.Combine(target,"linked");var outside=Path.Combine(fixture,"outside");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"keep.txt"),"keep");
        var start=new ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"/c","mklink","/J",linked,outside})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start)!){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Could not create owned junction fixture");}
        try
        {
            Reject(()=>InstanceDeletion.Target(first,"Alpha",target,[first]),"Deletion refuses nested junctions instead of touching their target");
            Reject(()=>InstanceDeletion.Target(target,"linked",linked,[first]),"Deletion refuses an unexpected linked target");
            check(File.ReadAllText(Path.Combine(outside,"keep.txt"))=="keep","Rejected deletion leaves linked data intact");
        }
        finally { Directory.Delete(linked); }
    }
}
