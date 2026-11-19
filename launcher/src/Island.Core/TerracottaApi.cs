using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Island.Core;

public sealed record TerracottaMember(string Name, string Vendor, string Kind);
public sealed record TerracottaState(string State, long Index, string Room, string Address, string Difficulty, int? Error, IReadOnlyList<TerracottaMember> Members)
{
    public bool InRoom => State is "host-scanning" or "host-starting" or "host-ok" or "guest-connecting" or "guest-starting" or "guest-ok";
    public string Description => State switch {
        "waiting" => "尚未加入房间", "host-scanning" => "等待游戏对局域网开放", "host-starting" => "正在创建房间…", "host-ok" => "房间已创建，等待好友加入",
        "guest-connecting" => "正在连接房主…", "guest-starting" => "正在建立联机连接…", "guest-ok" => "已加入房间", "exception" => Error switch {
            0 => "无法连接房主，请检查房间码和双方网络。", 1 => "与房主的连接已中断。", 2 => "加入方的联机组件已退出。", 3 => "房主的联机组件已退出。",
            4 => "房主已退出存档，房间已关闭。", 5 => "联机协议出现异常，请重试。", _ => "联机出现异常，请退出房间后重试。" }, _ => "暂不支持此联机状态，请检查组件版本。" };
}
public sealed class TerracottaApi : IDisposable
{
    private readonly HttpClient http;
    public TerracottaApi(int port, HttpMessageHandler? handler = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy=false, AllowAutoRedirect=false, ConnectTimeout=TimeSpan.FromSeconds(2) })
            { BaseAddress=new Uri($"http://127.0.0.1:{port}/"), Timeout=TimeSpan.FromSeconds(4), MaxResponseContentBufferSize=1024*1024 };
    }
    public static string NormalizeRoom(string room)
    {
        room=room.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(room, @"^U/[0-9A-Z]{4}(?:-[0-9A-Z]{4}){3}$", RegexOptions.CultureInvariant)) throw new ArgumentException("请填写完整的陶瓦房间码，例如 U/XXXX-XXXX-XXXX-XXXX。");
        return room;
    }
    public async Task CheckVersionAsync(CancellationToken token)
    {
        using var data=JsonDocument.Parse(await http.GetStringAsync("meta",token));
        if (data.RootElement.GetProperty("version").GetString() is not ("v0.4.2" or "0.4.2")) throw new InvalidDataException("陶瓦组件版本不匹配，请重新安装联机组件。");
    }
    public async Task<TerracottaState> StateAsync(CancellationToken token)
    {
        var json=await http.GetStringAsync("state",token); return ParseState(json);
    }
    public static TerracottaState ParseState(string json)
    {
        using var doc=JsonDocument.Parse(json); var root=doc.RootElement;
        string Text(JsonElement obj,string key)=>obj.TryGetProperty(key,out var item) && item.ValueKind==JsonValueKind.String ? item.GetString() ?? "" : "";
        var state=Text(root,"state"); var address=Text(root,"url");
        if (address.Length>0 && (!Uri.TryCreate("http://"+address,UriKind.Absolute,out var uri) || uri.Host!="127.0.0.1" || uri.UserInfo.Length>0 || uri.AbsolutePath!="/" || uri.Query.Length>0 || uri.Fragment.Length>0))
            throw new InvalidDataException("陶瓦返回了无效的本地游戏地址。");
        var members=new List<TerracottaMember>();
        if(root.TryGetProperty("profiles",out var profiles) && profiles.ValueKind==JsonValueKind.Array)
            foreach(var item in profiles.EnumerateArray().Take(256)) members.Add(new(Text(item,"name"),Text(item,"vendor"),Text(item,"kind")));
        return new(state,root.GetProperty("index").GetInt64(),Text(root,"room"),address,Text(root,"difficulty"),root.TryGetProperty("type",out var error) && error.TryGetInt32(out var code) ? code : null,members);
    }
    private async Task ControlAsync(string path,CancellationToken token)
    {
        using var response=await http.GetAsync(path,token);
        if(response.StatusCode==HttpStatusCode.BadRequest) throw new InvalidOperationException("房间码无效，或陶瓦尚未回到待机状态，请先退出当前房间。");
        response.EnsureSuccessStatusCode();
    }
    public Task HostAsync(string player,CancellationToken token)=>ControlAsync("state/scanning?player="+Uri.EscapeDataString(player),token);
    public Task JoinAsync(string room,string player,CancellationToken token)=>ControlAsync("state/guesting?room="+Uri.EscapeDataString(NormalizeRoom(room))+"&player="+Uri.EscapeDataString(player),token);
    public Task LeaveAsync(CancellationToken token)=>ControlAsync("state/ide",token);
    public Task StopAsync(CancellationToken token)=>ControlAsync("panic?peaceful=true",token);
    public void Dispose()=>http.Dispose();
}
