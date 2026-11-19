using BlockDock.Core;

namespace Island.Core;

public static class JavaRuntimeCatalog
{
    public static Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(string root, string configured, CancellationToken token)
    {
        var paths = JavaSearchPaths.ForGameDirectory(root, configured);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        paths = paths with { Roots = paths.Roots.Concat(new[] { Path.Combine(user, "AppData/Roaming/.hmcl/java"), Path.Combine(user, "AppData/Roaming/.hmcl/jre") }).ToArray() };
        return JavaDiscovery.DiscoverAsync(paths, token);
    }

    public static Task<JavaInstallation> ResolveAsync(AppSettings settings, JavaRequirement requirement, JvmSettings options, CancellationToken token)
        => JavaRuntimeCache.Transient.ResolveAsync(settings, requirement, options, token);
}
