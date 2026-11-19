using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlockDock.Core;
using Island.Core;

internal static class MultiMcImportTests
{
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    public static async Task RunAsync(string root, Action<bool,string> check)
    {
        var folder = Path.Combine(root,"multimc-patches"); Directory.CreateDirectory(folder);
        var payload = Encoding.UTF8.GetBytes("minecraft-or-library-fixture");
        var hash = Convert.ToHexString(SHA1.HashData(payload));
        byte[] LocalJar()
        {
            using var memory = new MemoryStream();
            using(var zip = new ZipArchive(memory,ZipArchiveMode.Create,true))
            using(var output = zip.CreateEntry("com/gtnewhorizons/retrofuturabootstrap/MainStartOnFirstThread.class").Open())
                output.Write(new byte[]{0xca,0xfe,0xba,0xbe,0,0,0,61});
            return memory.ToArray();
        }
        var jar = LocalJar();
        JsonObject Artifact(string url) => new() { ["url"] = url, ["size"] = (long)payload.Length, ["sha1"] = hash };
        JsonObject Library(string name,string url) => new() { ["name"]=name,["downloads"]=new JsonObject { ["artifact"]=Artifact(url) } };
        var components = JsonNode.Parse("""[{"uid":"me.eigenraven.lwjgl3ify.launchargs"},{"uid":"net.minecraftforge","version":"10.13.4.1614"},{"uid":"net.minecraft","version":"1.7.10"},{"uid":"me.eigenraven.lwjgl3ify.forgepatches"},{"uid":"org.lwjgl3","version":"3.4.3"}]""")!.AsArray();
        var minecraft = new JsonObject { ["uid"]="net.minecraft",["version"]="1.7.10",["formatVersion"]=1,["order"]=-2,["compatibleJavaMajors"]=new JsonArray(17,21),["mainClass"]="net.minecraft.client.main.Main",["minecraftArguments"]="--username ${auth_player_name} --version ${version_name}",["mainJar"]=new JsonObject { ["downloads"]=new JsonObject { ["artifact"]=Artifact("https://fixture.invalid/client") } },["libraries"]=new JsonArray(Library("test:base:1","https://fixture.invalid/base")) };
        var forge = new JsonObject { ["uid"]="net.minecraftforge",["version"]="10.13.4.1614",["formatVersion"]=1,["order"]=5,["mainClass"]="com.gtnewhorizons.retrofuturabootstrap.Main",["+tweakers"]=new JsonArray("cpw.mods.fml.common.launcher.FMLTweaker"),["libraries"]=new JsonArray(Library("net.minecraftforge:forge:1.7.10-10.13.4.1614:universal","https://fixture.invalid/forge")) };
        JsonObject Native(string suffix) => Library("org.lwjgl:lwjgl-natives-"+suffix+":3.4.3","https://fixture.invalid/"+suffix);
        var natives = new JsonObject { ["uid"]="org.lwjgl3",["version"]="3.4.3",["formatVersion"]=1,["order"]=-1,["libraries"]=new JsonArray(Native("windows"),Native("windows-x86"),Native("windows-arm64")) };
        var early = new JsonObject { ["uid"]="me.eigenraven.lwjgl3ify.forgepatches",["version"]="3.0.42",["order"]=3,["+jvmArgs"]=new JsonArray("-Djava.system.class.loader=com.gtnewhorizons.retrofuturabootstrap.RfbSystemClassLoader","--add-opens","java.base/java.lang=ALL-UNNAMED"),["libraries"]=new JsonArray(new JsonObject { ["name"]="com.github.GTNewHorizons:lwjgl3ify:3.0.42:forgePatches",["MMC-hint"]="local" }) };
        var launch = new JsonObject { ["uid"]="me.eigenraven.lwjgl3ify.launchargs",["version"]="3.0.42",["order"]=100,["mainClass"]="com.gtnewhorizons.retrofuturabootstrap.MainStartOnFirstThread" };
        var contents = new Dictionary<string,byte[]>
        {
            ["GTNH/mmc-pack.json"]=Encoding.UTF8.GetBytes(new JsonObject { ["components"]=components }.ToJsonString()),
            ["GTNH/instance.cfg"]=Encoding.UTF8.GetBytes("name=GTNH fixture\n"),
            ["GTNH/.minecraft/config/fixture.txt"]=Encoding.UTF8.GetBytes("keep configuration"),
            ["GTNH/.minecraft/mods/fixture.jar"]=payload,
            ["GTNH/libraries/lwjgl3ify-3.0.42-forgePatches.jar"]=jar
        };
        foreach(var patch in new[]{minecraft,forge,natives,early,launch}) contents["GTNH/patches/"+patch.Str("uid")+".json"]=Encoding.UTF8.GetBytes(patch.ToJsonString());
        async Task<string> Archive(string id, Dictionary<string,byte[]> entries)
        {
            var path=Path.Combine(folder,id+".zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var (name,bytes) in entries) { await using var output=zip.CreateEntry(name).Open();await output.WriteAsync(bytes); }
            return path;
        }
        var archive=await Archive("gtnh",contents);
        var pack=await ModpackInstaller.InspectAsync(archive,default);
        check(pack.Loader==LoaderKind.Forge && pack.ComponentPatches.Count==5 && pack.ComponentPatches.Last().Str("uid").EndsWith("launchargs"),"GTNH custom MultiMC components are ordered patches instead of conflicting mod loaders");
        var vanilla=new JsonObject { ["id"]="1.7.10",["mainClass"]="net.minecraft.client.main.Main",["downloads"]=new JsonObject { ["client"]=Artifact("https://fixture.invalid/client") },["libraries"]=new JsonArray(Library("org.lwjgl.lwjgl:lwjgl:2.9.1","https://fixture.invalid/old-lwjgl")) };
        var profile=MultiMcProfile.Compose(pack,vanilla,"GTNH installed");
        check(profile.Str("mainClass").EndsWith("MainStartOnFirstThread") && JavaRequirement.FromMetadata(profile).MinimumMajor==17,"Patched GTNH imports retain their RFB entry point and Java 17 requirement");
        check(!profile.Array("libraries").Any(lib=>lib.Str("name").Contains("2.9.1")) && profile["arguments"].Array("jvm").Any(arg=>arg!.GetValue<string>().Contains("RfbSystemClassLoader")),"Replacing the base Minecraft component removes LWJGL2 and retains early classloader arguments");
        var selected=profile.Array("libraries").OfType<JsonObject>().Where(MinecraftInstaller.LibraryAllowed).ToArray();
        check(selected.Count(lib=>lib.Str("name").StartsWith("org.lwjgl:"))==1 && selected.Any(lib=>lib.Str("name")=="org.lwjgl:lwjgl:3.4.3:natives-windows"),"Synthetic native coordinates normalize to Maven classifiers and exclude x86 or ARM64 jars on x64");
        var local=profile.Array("libraries").Single(lib=>lib.Str("MMC-hint")=="local");
        check(local?["downloads"]?["artifact"].Str("sha1")==Convert.ToHexString(SHA1.HashData(jar)).ToLowerInvariant(),"Bundled forgePatches jars receive an integrity hash and an instance-independent library path");
        var versionBytes=Encoding.UTF8.GetBytes(vanilla.ToJsonString());
        var manifest=new JsonObject { ["versions"]=new JsonArray(new JsonObject { ["id"]="1.7.10",["type"]="release",["url"]="https://fixture.invalid/version",["sha1"]=Convert.ToHexString(SHA1.HashData(versionBytes)) }) };
        var requested=new List<string>();
        using var network=new DownloadClient(new(){Source=DownloadSource.Official},new Handler(request=>
        {
            var url=request.RequestUri!.AbsoluteUri;requested.Add(url);
            byte[] bytes=url==MinecraftInstaller.ManifestUrl ? Encoding.UTF8.GetBytes(manifest.ToJsonString()) : url=="https://fixture.invalid/version" ? versionBytes : url is "https://fixture.invalid/client" or "https://fixture.invalid/base" or "https://fixture.invalid/forge" or "https://fixture.invalid/windows" ? payload : throw new Exception("Unexpected import request: "+url);
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }));
        var game=Path.Combine(folder,"game");
        var installed=await new MinecraftInstaller(new(){GameDirectory=game},network).InstallAsync(new("1.7.10","GTNH installed",LoaderKind.Forge,"10.13.4.1614"),null,default,pack);
        var resolved=VersionProfile.Read(game,"GTNH installed");
        check(File.Exists(Path.Combine(installed,"GTNH installed.jar")) && File.ReadAllText(Path.Combine(installed,"config/fixture.txt"))=="keep configuration", "Custom MultiMC import installs the client and configuration through the normal transactional installer");
        check(requested.Count(url=>url.EndsWith("/forge"))==1 && requested.All(url=>!url.Contains("installer") && !url.Contains("old-lwjgl") && !url.Contains("windows-x86") && !url.Contains("arm64")),"Bundled Forge patches bypass standard Forge installer downloads and wrong-architecture natives");
        check(File.ReadAllBytes(LaunchLibraries.PathFor(game,local!.AsObject())).SequenceEqual(jar) && LaunchLibraries.Files(game,resolved.Profile).All(file=>File.Exists(file.Path)),"Bundled early classpath jars and all patched dependencies are available after installation");
        var fixtureJava=Path.Combine(folder,"fixture-java.exe"); File.WriteAllText(fixtureJava,"fixture");
        var plan=GameLauncher.Prepare(game,resolved,fixtureJava,4096,new LaunchIdentity("Fixture", "00000000000000000000000000000000", "0", false),javaMajor:17);
        try { check(plan.Arguments.Contains("cpw.mods.fml.common.launcher.FMLTweaker") && plan.Arguments.Contains("--username") && plan.Arguments.Any(a=>a.Contains("RfbSystemClassLoader")),"Imported GTNH prepares a launch plan retaining authentication arguments, Forge tweaker and RFB classloader"); }
        finally { Directory.Delete(plan.NativesDirectory,true); }
        async Task Reject(string id,Dictionary<string,byte[]> entries)
        {
            var rejected=false;
            try { await ModpackInstaller.InspectAsync(await Archive(id,entries),default); }
            catch(Exception ex) when(ex is InvalidDataException or NotSupportedException or ArgumentException) { rejected=true; }
            check(rejected,"Malformed or incomplete MultiMC components are rejected: "+id);
        }
        var missing=new Dictionary<string,byte[]>(contents);missing.Remove("GTNH/libraries/lwjgl3ify-3.0.42-forgePatches.jar");await Reject("missing-local",missing);
        var mismatch=new Dictionary<string,byte[]>(contents){["GTNH/patches/net.minecraftforge.json"]=Encoding.UTF8.GetBytes(forge.ToJsonString().Replace("10.13.4.1614","wrong-version"))};await Reject("wrong-version",mismatch);
        var missingLoader=new Dictionary<string,byte[]>(contents);missingLoader.Remove("GTNH/patches/net.minecraftforge.json");await Reject("missing-loader-patch",missingLoader);
        var missingCustom=new Dictionary<string,byte[]>(contents);missingCustom.Remove("GTNH/patches/me.eigenraven.lwjgl3ify.forgepatches.json");await Reject("missing-custom-patch",missingCustom);
        var missingBase=new Dictionary<string,byte[]>(contents);missingBase.Remove("GTNH/patches/net.minecraft.json");await Reject("missing-base-patch",missingBase);
        var missingLwjgl=new Dictionary<string,byte[]>(contents);missingLwjgl.Remove("GTNH/patches/org.lwjgl3.json");await Reject("missing-lwjgl-patch",missingLwjgl);
        var unsafePatch=(JsonObject)early.DeepClone();unsafePatch["libraries"]![0]!["name"]="bad:../escape:1";
        var unsafeEntries=new Dictionary<string,byte[]>(contents){["GTNH/patches/me.eigenraven.lwjgl3ify.forgepatches.json"]=Encoding.UTF8.GetBytes(unsafePatch.ToJsonString())};await Reject("unsafe-coordinate",unsafeEntries);
    }
}
