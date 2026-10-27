using System.Text.Json.Nodes;
using Island.Core;

internal static class InstanceCatalogTests
{
    public static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "loader-identities");
        void Write(string id, string json, string? metadata = null)
        {
            var directory = Path.Combine(root, "versions", id); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, id + ".json"), json);
            if (metadata is not null) File.WriteAllText(Path.Combine(directory, "blockdock-instance.json"), metadata);
        }
        Write("CT", """{"mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"net.minecraftforge:fmlloader:1.19.2-43.5.0"}]}""");
        Write("ATM9", """{"mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"net.minecraftforge:fmlloader:1.20.1-47.4.10"}]}""", """{"loader":"Vanilla"}""");
        Write("Neo", """{"mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"net.neoforged.fancymodloader:loader:4.0.42@jar"},{"name":"net.fabricmc:sponge-mixin:0.15.2"},{"name":"net.minecraftforge:srgutils:0.4.15"}]}""");
        Write("Cleanroom", """{"mainClass":"top.outlands.foundation.boot.Foundation","libraries":[{"name":"com.cleanroommc:cleanroom:0.3.31-alpha"}]}""");
        Write("Patched", """{"mainClass":"top.outlands.foundation.boot.Foundation","patches":[{"id":"game"},{"id":"cleanroom"}]}""");
        Write("Inherited", """{"inheritsFrom":"CT","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}""");
        Write("MissingParent", """{"inheritsFrom":"missing","libraries":[{"name":"net.minecraftforge:fmlloader:1.20.1-47.4.10"}]}""");
        Write("Fabric", """{"mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient"}""");
        Write("Quilt", """{"libraries":[{"name":"org.quiltmc:quilt-loader:0.26.0"}]}""");
        Write("Forge Adventure", """{"mainClass":"net.minecraft.client.main.Main","libraries":[{"name":"net.fabricmc:sponge-mixin:0.17.3"}]}""");
        Write("Unknown", """{"mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}""");
        Write("CycleA", """{"inheritsFrom":"CycleB"}"""); Write("CycleB", """{"inheritsFrom":"CycleA"}""");
        Write("Escape", """{"inheritsFrom":"../outside"}""");
        Write("CTIIIIIIIII", """{"id":"CTIIIIIIIII","clientVersion":"1.19.2","libraries":[{"name":"net.minecraftforge:fmlloader:1.19.2-43.5.0"}]}""");
        Write("VersionArgs", """{"arguments":{"game":["--fml.mcVersion","1.20.1"]}}""");
        Write("GamePatch", """{"patches":[{"id":"game","version":"1.12.2"}]}""");
        Write("CustomParent", """{"inheritsFrom":"CTIIIIIIIII"}""");
        Write("Unresolved", """{"id":"A custom display name","assets":"1.19"}""");
        var before = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        var instances = InstanceCatalog.ListInstalled(root).ToDictionary(instance => instance.Name);
        check(instances["CT"].Loader == "Forge" && instances["ATM9"].Loader == "Forge", "Modern Forge fmlloader profiles override missing / stale Vanilla labels");
        check(instances["Neo"].Loader == "NeoForge", "NeoForge FML identity wins over shared Forge utility and Fabric mixin libraries");
        check(instances["Cleanroom"].Loader == "Cleanroom" && instances["Patched"].Loader == "Cleanroom", "Cleanroom runtime coordinates and patches identify the correct loader");
        check(instances["Inherited"].Loader == "Forge" && instances["MissingParent"].Loader == "Forge", "Discovery follows inherited loaders and keeps leaf evidence when a parent is missing");
        check(instances["Fabric"].Loader == "Fabric" && instances["Quilt"].Loader == "Quilt", "Fabric and Quilt keep their own runtime identities");
        check(instances["Forge Adventure"].LoaderLabel == "原版", "Pack names and shared mixin libraries alone do not turn vanilla into a modded instance");
        check(instances["Unknown"].LoaderLabel == "未知加载器", "An unrecognized mod launcher is not falsely described as vanilla");
        check(instances.ContainsKey("CycleA") && instances.ContainsKey("Escape"), "Incomplete or cyclic parent metadata cannot hide the rest of the instance list");
        check(instances["CTIIIIIIIII"].Minecraft == "1.19.2" && instances["CustomParent"].Minecraft == "1.19.2", "Renamed and inherited instances use clientVersion rather than their directory or display names");
        check(instances["CT"].Minecraft == "1.19.2" && instances["VersionArgs"].Minecraft == "1.20.1" && instances["GamePatch"].Minecraft == "1.12.2", "Actual Minecraft versions can come from Forge coordinates, explicit game arguments or game patches");
        check(instances["Unresolved"].Minecraft == "未知版本" && instances["CycleA"].Minecraft == "未知版本", "Missing or cyclic version metadata does not turn an instance name or asset index into a Minecraft version");
        check(before.All(pair => File.ReadAllText(pair.Key) == pair.Value), "Loader discovery never rewrites imported game profiles or metadata");
    }
}
