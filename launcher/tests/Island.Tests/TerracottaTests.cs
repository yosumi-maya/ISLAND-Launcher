using System.Net;
using System.Text.Json;
using Island.Core;
internal static class TerracottaTests
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    public static async Task RunAsync(string root,Action<bool,string> check,string? package,bool live,bool download=false)
    {
        check(TerracottaApi.NormalizeRoom(" u/abcd-1234-efgh-5678 ")=="U/ABCD-1234-EFGH-5678","Room codes normalize without changing their format");
        foreach(var bad in new[]{"", "http://127.0.0.1/", "U/ABCD-1234-EFGH-5678&player=other", "U/ABCD-1234"})
        {bool rejected=false;try{TerracottaApi.NormalizeRoom(bad);}catch(ArgumentException){rejected=true;}check(rejected,"Invalid room input is rejected before contacting the engine");}
        var states=new[]{"waiting","host-scanning","host-starting","host-ok","guest-connecting","guest-starting","guest-ok","exception"};
        foreach(var name in states)
        {
            var state=TerracottaApi.ParseState(JsonSerializer.Serialize(new {state=name,index=42,room="U/ABCD-1234-EFGH-5678",url="127.0.0.1:25566",difficulty="MEDIUM",type=4,profiles=new[]{new{name="Fixture",vendor="Terracotta",kind="HOST"}}}));
            check(state.State==name && state.Index==42 && state.Members.Single().Name=="Fixture" && state.Description.Length>0,"Official room state parses: "+name);
        }
        foreach(var url in new[]{"example.com:25565","127.0.0.1@evil.test","127.0.0.1:25565/path","127.0.0.1?target=other"})
        {bool rejected=false;try{TerracottaApi.ParseState(JsonSerializer.Serialize(new{state="guest-ok",index=1,url}));}catch(InvalidDataException){rejected=true;}check(rejected,"Untrusted game addresses cannot escape loopback");}
        var paths=new List<string>();
        using(var api=new TerracottaApi(12345,new Handler((request,token)=>
        {
            check(request.RequestUri!.Host=="127.0.0.1" && request.RequestUri.Port==12345,"Every engine request targets the selected loopback port");
            paths.Add(request.RequestUri.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath=="/meta" ? "{\"version\":\"v0.4.2\"}" : "{\"state\":\"waiting\",\"index\":1}")});
        })))
        {
            await api.CheckVersionAsync(default);await api.HostAsync("A & B",default);await api.JoinAsync("U/ABCD-1234-EFGH-5678","测试",default);await api.LeaveAsync(default);await api.StopAsync(default);
        }
        check(paths.Any(p=>p.Contains("player=A%20%26%20B")) && paths.Any(p=>p.Contains("room=U%2FABCD")),"Player names and room codes are escaped as query values");
        check(paths.Contains("/state/ide") && paths.Contains("/panic?peaceful=true"),"Leave and shutdown use the official safe endpoints");
        using(var api=new TerracottaApi(12345,new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)))))
        {bool rejected=false;try{await api.JoinAsync("U/ABCD-1234-EFGH-5678","x",default);}catch(InvalidOperationException){rejected=true;}check(rejected,"Invalid state responses become a recoverable user error");}
        if(package is null && !download)return;
        var data=Path.Combine(root,"terracotta-package");var installer=new TerracottaInstaller(data);
        if(download)
        {
            await installer.EnsureAsync(new Progress<string>(Console.WriteLine),default);check(await installer.IsInstalledAsync(default),"First-use download installs the pinned official component through verified mirrors");package=installer.ArchivePath;
        }
        await installer.ImportAsync(package!,default);check(await installer.IsInstalledAsync(default),"Official package imports with archive and per-file SHA256 verification");
        var exe=await installer.EnsureAsync(null,default);check(File.Exists(exe),"Verified installation reuses the local package without downloading");
        await File.WriteAllTextAsync(exe,"corrupt");check(!await installer.IsInstalledAsync(default),"Modified executable fails verification");
        await installer.EnsureAsync(null,default);check(await installer.IsInstalledAsync(default),"A modified executable is repaired from the verified cached archive");
        var badPackage=Path.Combine(root,"bad.tar.gz");await File.WriteAllTextAsync(badPackage,"not the upstream package");bool badRejected=false;
        try{await installer.ImportAsync(badPackage,default);}catch(InvalidDataException){badRejected=true;}
        check(badRejected && await installer.IsInstalledAsync(default),"Invalid imports preserve the working installation");
        if(!live)return;
        var engine=new TerracottaSession(data);var second=new TerracottaSession(data);TerracottaApi? probe=null;
        try
        {
            await engine.StartAsync(new Progress<string>(Console.WriteLine),default);
            check((await engine.StateAsync(default)).State=="waiting","Real official Windows engine starts in waiting state");
            await Task.Delay(400);check((await engine.StateAsync(default)).State=="waiting","Bootstrap exit does not invalidate the running official worker");
            bool locked=false;try{await second.StartAsync(null,default);}catch(InvalidOperationException){locked=true;}
            await second.ShutdownAsync();check(locked && (await engine.StateAsync(default)).State=="waiting","A second ISLAND session cannot take over or stop the active engine");
            var portFile=Directory.GetFiles(Path.Combine(data,"multiplayer","terracotta-0.4.2"),"port-*.json").Single();using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(portFile));probe=new TerracottaApi(doc.RootElement.GetProperty("port").GetInt32());
            await engine.LeaveAsync(default);await engine.ShutdownAsync();await Task.Delay(200);
            bool stopped=false;try{await probe.StateAsync(default);}catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException){stopped=true;}
            check(!engine.IsStarted && stopped,"Shutdown ends the real isolated worker and closes its HTTP port");
        }
        finally{probe?.Dispose();await second.ShutdownAsync();await engine.ShutdownAsync();}
    }
}
