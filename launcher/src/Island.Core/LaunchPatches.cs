using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BlockDock.Core;

namespace Island.Core;

public static class LaunchPatches
{
    internal static bool UseWrapper(JvmSettings options, int javaMajor, uint codePage) => !options.DisableJavaLaunchWrapper && javaMajor is >= 6 and <= 18 && codePage == 65001;
    public static bool UseWrapper(JvmSettings options, int javaMajor) => UseWrapper(options, javaMajor, GetACP());
    public static bool UseUnsafeAgent(JvmSettings options, int javaMajor, JsonObject profile)
    {
        if (options.DisableLwjglUnsafeAgent || javaMajor < 25) return false;
        var names = profile.Array("libraries").OfType<JsonObject>().Select(library => library.Str("name")).ToArray();
        // Keep Forge's module-based launch path free from this optional agent.
        return !names.Any(name => name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
            && names.Any(name => name is "org.lwjgl:lwjgl:3.4.0" or "org.lwjgl:lwjgl:3.4.1");
    }

    public static string Extract(string root, bool wrapper)
    {
        var resource = "Island.Core." + (wrapper ? "JavaLaunchWrapper" : "LwjglUnsafeAgent") + ".jar";
        using var input = typeof(LaunchPatches).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("启动兼容组件缺失。");
        using var buffer = new MemoryStream(); input.CopyTo(buffer); var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var name = wrapper ? "java-launch-wrapper-1.4.4.jar" : "lwjgl-unsafe-agent-2.0.jar";
        var target = SafePaths.Under(root, ".island/patches/" + hash + "/" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var licensePath = Path.Combine(Path.GetDirectoryName(target)!, "LICENSE.txt");
        if (!File.Exists(licensePath))
        {
            using var license = typeof(LaunchPatches).Assembly.GetManifestResourceStream(resource.Replace(".jar", ".LICENSE.txt", StringComparison.Ordinal))
                ?? throw new InvalidDataException("启动兼容组件许可缺失。");
            using var targetLicense = File.Create(licensePath); license.CopyTo(targetLicense);
        }
        if (File.Exists(target))
        {
            using var existing = File.OpenRead(target);
            if (Convert.ToHexString(SHA256.HashData(existing)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return target;
        }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return target;
    }
    [DllImport("kernel32.dll")] private static extern uint GetACP();
}
