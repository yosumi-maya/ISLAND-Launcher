using System.Text.RegularExpressions;

namespace Island.Core;

public static class GarbageCollectors
{
    public static IReadOnlyList<string> Arguments(JvmSettings options, int javaMajor, IEnumerable<string> configured)
    {
        var existing = configured.ToArray();
        if (options.OmitOptimizations || options.GarbageCollector == GarbageCollectorMode.Custom || javaMajor < 8
            || existing.Any(argument => Regex.IsMatch(argument, @"^-XX:[+-]Use\w*GC$"))) return [];
        if (options.GarbageCollector is GarbageCollectorMode.PreferZ or GarbageCollectorMode.PreferGenerationalZ && javaMajor >= 15)
        {
            if (options.GarbageCollector == GarbageCollectorMode.PreferGenerationalZ && javaMajor is >= 21 and <= 22)
                return ["-XX:+UseZGC", "-XX:+ZGenerational"];
            return ["-XX:+UseZGC"];
        }
        if (options.GarbageCollector == GarbageCollectorMode.G1) return ["-XX:+UseG1GC"];
        var arguments = new List<string> { "-XX:+UseG1GC" };
        if (!existing.Any(argument => argument.StartsWith("-XX:MaxGCPauseMillis=", StringComparison.Ordinal))) arguments.Add("-XX:MaxGCPauseMillis=200");
        if (!existing.Any(argument => argument is "-XX:+ParallelRefProcEnabled" or "-XX:-ParallelRefProcEnabled")) arguments.Add("-XX:+ParallelRefProcEnabled");
        return arguments;
    }
}
