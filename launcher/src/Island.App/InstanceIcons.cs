using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Island.App;
public sealed record InstanceIconChoice(string Key, string Name);
public static class InstanceIcons
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public static IReadOnlyList<InstanceIconChoice> Choices { get; } = new[] {
        new InstanceIconChoice("grass","草方块"),new("chest","箱子"),new("chicken","鸡"),new("command","命令方块"),
        new("april_fools","史莱姆块"),new("optifine","OptiFine"),new("craft_table","工作台"),new("fabric","Fabric"),
        new("forge","Forge · 铁砧"),new("legacyfabric","地图 · Legacy Fabric"),new("neoforge","NeoForge"),new("furnace","熔炉"),new("quilt","Quilt"),new("cleanroom","Cleanroom") };
    private static readonly Dictionary<string,ImageSource> cache = [];
    public static bool Contains(string key) => Choices.Any(c => c.Key == key);
    public static ImageSource Builtin(string key)
    {
        if (!Contains(key)) key = "grass";
        if (cache.TryGetValue(key,out var cached)) return cached;
        var image = new BitmapImage(new Uri($"pack://application:,,,/岛屿启动器;component/Assets/InstanceIcons/{key}.png")); image.Freeze();
        cache[key]=image; return image;
    }
    public static ImageSource Default(string loader) => Builtin(loader switch { "Forge"=>"forge", "NeoForge"=>"neoforge", "Fabric"=>"fabric", "Quilt"=>"quilt", "Cleanroom"=>"cleanroom", "OptiFine"=>"optifine", _=>"grass" });
    public static BitmapSource Decode(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaximumBytes) throw new ArgumentException("请选择不超过 2 MB 的图标图片。");
        using var stream=new MemoryStream(data,false);
        var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=256; image.StreamSource=stream; image.EndInit(); image.Freeze(); return image;
    }
    public static byte[] Import(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new ArgumentException("请选择不超过 2 MB 的图标图片。");
        var image=Decode(File.ReadAllBytes(path));
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream=new MemoryStream(); encoder.Save(stream);
        if (stream.Length > MaximumBytes) throw new ArgumentException("图标图片过大，请使用较小的图片。");
        return stream.ToArray();
    }
}
