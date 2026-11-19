using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
namespace Island.Core;

public sealed class TerracottaInstaller(string root)
{
    public const string Version="0.4.2";
    public const string Executable="terracotta-0.4.2-windows-x86_64.exe";
    public const string PackageHash="07ebe139e3ca5f74576e58b1a96efe59abdfbe148d3f1a49bfdca8b6f70745f0";
    public const long PackageSize=8135914;
    public static IReadOnlyDictionary<string,string> FileHashes { get; }=new Dictionary<string,string> {
        [Executable]="74c10568a7fea9c1d38cf8d2d4ca90baf1517f8e5a26c63d3349db70bc449796",
        ["VCRUNTIME140.DLL"]="475ab98b7722e965bd38c8fa6ed23502309582ccf294ff1061cb290c7988f0d1" };
    public string InstallDirectory=>Path.Combine(Path.GetFullPath(root),"components","terracotta",Version,"win-x64");
    public string ArchivePath=>Path.Combine(Path.GetFullPath(root),"components","terracotta","terracotta-0.4.2-windows-x86_64-pkg.tar.gz");
    private static readonly SemaphoreSlim gate=new(1,1);
    private static async Task<bool> Matches(string path,string hash,CancellationToken token)
    {
        if(!File.Exists(path))return false;
        await using var input=File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
    public async Task<bool> IsInstalledAsync(CancellationToken token)
    {
        foreach(var file in FileHashes) if(!await Matches(Path.Combine(InstallDirectory,file.Key),file.Value,token)) return false;
        return true;
    }
    public async Task<string> EnsureAsync(IProgress<string>? progress,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            progress?.Report("正在校验陶瓦联机组件…");
            if(await IsInstalledAsync(token)){await WriteNoticesAsync(token);return Path.Combine(InstallDirectory,Executable);}
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath)!);
            if(!await Matches(ArchivePath,PackageHash,token))
            {
                Exception? last=null;
                foreach(var direct in new[]{false,true})
                {
                    foreach(var host in new[]{"https://gitee.com/burningtnt/Terracotta","https://github.com/burningtnt/Terracotta"})
                    {
                        try
                        {
                            await DownloadAsync(host+"/releases/download/v0.4.2/terracotta-0.4.2-windows-x86_64-pkg.tar.gz",direct,progress,token);
                            last=null; break;
                        }
                        catch(Exception ex) when(ex is HttpRequestException or IOException or TaskCanceledException)
                        { token.ThrowIfCancellationRequested(); last=ex; }
                    }
                    if(last is null)break;
                }
                if(last is not null)throw new IOException("陶瓦组件下载失败，可稍后重试，或从陶瓦官网下载安装包后导入。",last);
            }
            await ExtractAsync(ArchivePath,token); return Path.Combine(InstallDirectory,Executable);
        }
        finally{gate.Release();}
    }
    private async Task DownloadAsync(string url,bool direct,IProgress<string>? progress,CancellationToken token)
    {
        var partial=ArchivePath+".part";
        try
        {
            using var http=new HttpClient(new SocketsHttpHandler { UseProxy=!direct,ConnectTimeout=TimeSpan.FromSeconds(8) }) { Timeout=TimeSpan.FromSeconds(90) };
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            await using(var output=File.Create(partial))
            {
                await using var input=await response.Content.ReadAsStreamAsync(token);var buffer=new byte[65536];int read;long total=0;
                while((read=await input.ReadAsync(buffer,token))>0)
                {
                    total+=read;if(total>PackageSize)throw new InvalidDataException("陶瓦安装包大小不匹配。");
                    await output.WriteAsync(buffer.AsMemory(0,read),token);progress?.Report($"正在下载陶瓦联机组件 · {total*100/PackageSize}%");
                }
                if(total!=PackageSize)throw new InvalidDataException("陶瓦安装包下载不完整。");
            }
            if(!await Matches(partial,PackageHash,token))throw new InvalidDataException("陶瓦安装包校验失败，已停止安装。");
            File.Move(partial,ArchivePath,true);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public async Task ImportAsync(string source,CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if(new FileInfo(source).Length!=PackageSize || !await Matches(source,PackageHash,token))throw new InvalidDataException("请选择官方陶瓦 0.4.2 Windows x86_64 的 tar.gz 安装包，文件校验未通过。");
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath)!);
            if(!Path.GetFullPath(source).Equals(ArchivePath,StringComparison.OrdinalIgnoreCase))File.Copy(source,ArchivePath,true);
            await ExtractAsync(ArchivePath,token);
        }
        finally{gate.Release();}
    }
    internal async Task ExtractAsync(string archive,CancellationToken token)
    {
        if(!await Matches(archive,PackageHash,token))throw new InvalidDataException("陶瓦安装包校验失败。");
        var staging=Path.Combine(Path.GetFullPath(root),"components","terracotta","stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            await using var input=File.OpenRead(archive);await using var gzip=new GZipStream(input,CompressionMode.Decompress);using var reader=new TarReader(gzip);
            var found=new HashSet<string>(StringComparer.Ordinal);TarEntry? entry;
            while((entry=await reader.GetNextEntryAsync(cancellationToken:token)) is not null)
            {
                if(!FileHashes.ContainsKey(entry.Name) || !found.Add(entry.Name) || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.Length>16*1024*1024 || entry.DataStream is null)
                    throw new InvalidDataException("陶瓦安装包包含意外文件。");
                await using(var output=File.Create(Path.Combine(staging,entry.Name)))await entry.DataStream.CopyToAsync(output,token);
                if(!await Matches(Path.Combine(staging,entry.Name),FileHashes[entry.Name],token))throw new InvalidDataException("陶瓦组件文件校验失败。");
            }
            if(found.Count!=FileHashes.Count)throw new InvalidDataException("陶瓦安装包缺少组件。");
            Directory.CreateDirectory(InstallDirectory);
            foreach(var name in found)File.Move(Path.Combine(staging,name),Path.Combine(InstallDirectory,name),true);
            await WriteNoticesAsync(token);
        }
        finally{Directory.Delete(staging,true);}
    }
    private async Task WriteNoticesAsync(CancellationToken token)
    {
        foreach(var name in new[]{"TERRACOTTA-LICENSE.txt","TERRACOTTA-NOTICE.txt"})
        {
            await using var source=typeof(TerracottaInstaller).Assembly.GetManifestResourceStream("Island.Core."+name) ?? throw new InvalidDataException("缺少陶瓦许可说明。");
            await using var output=File.Create(Path.Combine(InstallDirectory,name));await source.CopyToAsync(output,token);
        }
    }

}
