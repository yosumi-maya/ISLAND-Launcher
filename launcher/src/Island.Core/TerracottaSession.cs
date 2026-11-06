using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace Island.Core;

public interface ITerracottaSession
{
    bool IsStarted { get; }
    Task StartAsync(IProgress<string>? progress,CancellationToken token);
    Task ImportAsync(string archive,CancellationToken token);
    Task<TerracottaState> StateAsync(CancellationToken token);
    Task HostAsync(string player,CancellationToken token);
    Task JoinAsync(string room,string player,CancellationToken token);
    Task LeaveAsync(CancellationToken token);
    Task ShutdownAsync();
}
public sealed class TerracottaSession(string root) : ITerracottaSession
{
    private readonly TerracottaInstaller installer=new(root);
    private readonly SemaphoreSlim gate=new(1,1);
    private TerracottaApi? api;
    private FileStream? lease;
    private string? portFile;
    private DateTime? startedAt;
    public bool IsStarted=>api is not null;
    public async Task ImportAsync(string archive,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try{if(api is not null)throw new InvalidOperationException("请先结束当前联机会话，再导入组件。");using var importLease=AcquireLease();await installer.ImportAsync(archive,token);}
        finally{gate.Release();}
    }
    private string SessionDirectory=>Path.Combine(Path.GetFullPath(root),"multiplayer","terracotta-0.4.2");
    private static int? ReadPort(string file,bool binary=false)
    {
        try
        {
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            if(stream.Length>1024)return null;
            if(binary) {var high=stream.ReadByte();var low=stream.ReadByte();return high>=0 && low>=0 ? high*256+low : null;}
            using var data=JsonDocument.Parse(stream);var port=data.RootElement.GetProperty("port").GetInt32();return port is >0 and <=65535 ? port : null;
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
    private FileStream AcquireLease()
    {
        Directory.CreateDirectory(SessionDirectory);
        try{return new FileStream(Path.Combine(SessionDirectory,"island-session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){throw new InvalidOperationException("另一个 ISLAND 窗口正在使用联机，请先退出该窗口的联机会话。");}
    }
    public async Task StartAsync(IProgress<string>? progress,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if(api is not null)return;
            if(!OperatingSystem.IsWindowsVersionAtLeast(10) || RuntimeInformation.ProcessArchitecture!=Architecture.X64)throw new PlatformNotSupportedException("此版本的陶瓦接入支持 Windows 10 及以上的 x64 启动器。");
            lease=AcquireLease();
            try
            {
                // Scope upstream's temp directory and singleton lock to ISLAND's data directory.
                // Other launchers keep their own instance and are never controlled by this session.
                var temp=Path.Combine(SessionDirectory,"temp");Directory.CreateDirectory(temp);
                var existing=ReadPort(Path.Combine(temp,"terracotta","terracotta.lock"),true);
                if(existing is >0)
                {
                    var candidate=new TerracottaApi(existing.Value);
                    try{await candidate.CheckVersionAsync(token);api=candidate;return;}
                    catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or IOException){candidate.Dispose();token.ThrowIfCancellationRequested();}
                }
                var exe=await installer.EnsureAsync(progress,token);token.ThrowIfCancellationRequested();
                portFile=Path.Combine(SessionDirectory,"port-"+Guid.NewGuid().ToString("N")+".json");
                var start=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=installer.InstallDirectory};
                start.ArgumentList.Add("--hmcl");start.ArgumentList.Add(portFile);start.Environment["TMP"]=temp;start.Environment["TEMP"]=temp;
                progress?.Report("正在启动陶瓦联机…");startedAt=DateTime.Now;
                using var bootstrap=Process.Start(start) ?? throw new IOException("陶瓦联机组件未能启动。");
                var deadline=DateTime.UtcNow.AddSeconds(20);
                while(DateTime.UtcNow<deadline)
                {
                    token.ThrowIfCancellationRequested();var port=ReadPort(portFile);
                    if(port is >0)
                    {
                        var candidate=new TerracottaApi(port.Value);
                        try{await candidate.CheckVersionAsync(token);api=candidate;return;}
                        catch{candidate.Dispose();throw;}
                    }
                    // The Windows bootstrap exits after handing off to its worker. Its exit
                    // alone does not mean the HTTP server has stopped.
                    await Task.Delay(100,token);
                }
                throw new TimeoutException("陶瓦启动超时，请检查防火墙提示后重试。");
            }
            catch{await StopCoreAsync();throw;}
        }
        finally{gate.Release();}
    }
    public async Task<TerracottaState> StateAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);try{return await (api ?? throw new InvalidOperationException("联机组件尚未启动。")).StateAsync(token);}finally{gate.Release();}
    }
    private async Task ControlAsync(Func<TerracottaApi,CancellationToken,Task> action,CancellationToken token)
    {
        await gate.WaitAsync(token);try{await action(api ?? throw new InvalidOperationException("联机组件尚未启动。"),token);}finally{gate.Release();}
    }
    public Task HostAsync(string player,CancellationToken token)=>ControlAsync(async (client,t)=>
    {
        if((await client.StateAsync(t)).State!="waiting")throw new InvalidOperationException("请先退出当前房间，再创建新房间。");
        await client.HostAsync(player,t);
    },token);
    public Task JoinAsync(string room,string player,CancellationToken token)=>ControlAsync(async (client,t)=>
    {
        TerracottaApi.NormalizeRoom(room);
        if((await client.StateAsync(t)).State!="waiting")throw new InvalidOperationException("请先退出当前房间，再加入其他房间。");
        await client.JoinAsync(room,player,t);
    },token);
    public Task LeaveAsync(CancellationToken token)=>ControlAsync((client,t)=>client.LeaveAsync(t),token);
    public async Task ShutdownAsync()
    {
        await gate.WaitAsync();try{await StopCoreAsync();}finally{gate.Release();}
    }
    private async Task StopCoreAsync()
    {
        try
        {
            if(api is null && portFile is not null && ReadPort(portFile) is int port)api=new TerracottaApi(port);
            if(api is not null)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try{await api.StopAsync(timeout.Token);}catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or IOException){}
            }
            if(startedAt is DateTime earliest)
            {
                await Task.Delay(150);
                // Kill only a worker launched from our verified install after this start.
                // No process name matching and no commands to another launcher's instance.
                var executable=Path.Combine(installer.InstallDirectory,TerracottaInstaller.Executable);
                foreach(var process in Process.GetProcesses())
                {
                    using(process)
                    {
                        try{if(process.StartTime>=earliest.AddSeconds(-1) && string.Equals(process.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase))process.Kill(true);}
                        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException){}
                    }
                }
            }
        }
        finally
        {
            api?.Dispose();api=null;startedAt=null;lease?.Dispose();lease=null;
            if(portFile is not null){try{File.Delete(portFile);}catch(IOException){}portFile=null;}
        }
    }
}
