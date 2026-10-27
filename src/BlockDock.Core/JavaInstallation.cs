using System.Text.Json.Nodes;

namespace BlockDock.Core;

public sealed record JavaInstallation(string Path, string Version, int Major, string Architecture, string Vendor)
{
    public bool Is64Bit => Architecture is "x64" or "arm64";
    public bool IsEarlyAccess => Version.Contains("-ea", StringComparison.OrdinalIgnoreCase) || Version.Contains("internal", StringComparison.OrdinalIgnoreCase);
    public string Display => $"Java {Version} · {Architecture} · {Vendor} · {Path}";
}

public sealed record JavaRequirement(int MinimumMajor)
{
    public static JavaRequirement FromMetadata(JsonObject minecraft, JsonObject? loader = null)
    {
        static int Major(JsonObject? profile) => profile?["javaVersion"]?["majorVersion"] is JsonValue value
            && value.TryGetValue<int>(out var major) && major > 0 ? major : 8;
        return new(Math.Max(Major(minecraft), Major(loader)));
    }
}
