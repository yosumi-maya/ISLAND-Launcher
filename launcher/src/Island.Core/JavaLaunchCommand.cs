using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Island.Core;

internal static class JavaLaunchCommand
{
    internal static void Apply(ProcessStartInfo start, IReadOnlyList<string> args, int generatedJvmCount, int javaMajor,
        string temporaryDirectory, string? accessToken)
    {
        IEnumerable<string> command = args;
        if (!Fits(start.FileName, args))
        {
            command = javaMajor >= 9
                ? WriteArgumentFiles(args, generatedJvmCount, temporaryDirectory, accessToken)
                : WriteLegacyClassPath(args, generatedJvmCount, temporaryDirectory, start.WorkingDirectory);
            if (!Fits(start.FileName, command))
                throw new InvalidDataException("Java 启动命令仍超过 Windows 长度限制。请缩短游戏目录路径或减少自定义启动参数。");
        }
        foreach (var argument in command) start.ArgumentList.Add(argument);
    }

    // CreateProcessW's limit includes the executable, quoting and the terminating null.
    private static bool Fits(string executable, IEnumerable<string> args)
        => executable.Length + 3L + args.Sum(arg => arg.Length + 3L + arg.Count(character => character is '\\' or '"')) <= 32_767;

    private static IReadOnlyList<string> WriteArgumentFiles(IReadOnlyList<string> args, int generatedJvmCount, string temporaryDirectory, string? accessToken)
    {
        var command = new List<string>();
        var fileIndex = 0;
        var utf8 = new UTF8Encoding(false, true);
        // Windows Java decodes classpath and module path bytes with its native code page.
        var native = CodePagesEncodingProvider.Instance.GetEncoding((int)LaunchPatches.GetACP(),
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ?? utf8;

        // Spill only launcher-generated path options. Game arguments and custom JVM options stay on the command line.
        for (var i = 0; i < generatedJvmCount; i++)
        {
            var option = args[i];
            var takesPath = (option is "-cp" or "-classpath" or "--class-path" or "-p" or "--module-path" or "--upgrade-module-path" or "--patch-module") &&
                i + 1 < generatedJvmCount;
            var pathProperty = option.StartsWith("-Djava.class.path=", StringComparison.Ordinal) ||
                option.StartsWith("--class-path=", StringComparison.Ordinal) || option.StartsWith("--module-path=", StringComparison.Ordinal) ||
                option.StartsWith("--upgrade-module-path=", StringComparison.Ordinal) || option.StartsWith("--patch-module=", StringComparison.Ordinal);
            var legacyClassPath = option.StartsWith("-DlegacyClassPath=", StringComparison.Ordinal);
            if (!takesPath && !pathProperty && !legacyClassPath)
            {
                command.Add(option);
                continue;
            }
            var arguments = takesPath ? new[] { option, args[++i] } : [option];
            if (accessToken is { Length: > 0 } && arguments.Any(argument => argument.Contains(accessToken, StringComparison.Ordinal)))
            {
                command.AddRange(arguments);
                continue;
            }
            var path = Path.Combine(temporaryDirectory, $"java-{++fileIndex}.args");
            try { File.WriteAllLines(path, arguments.Select(Quote), legacyClassPath ? utf8 : native); }
            catch (EncoderFallbackException ex)
            {
                throw new InvalidDataException("当前 Java 的 Windows 路径编码无法表示某个依赖路径。请将游戏移到此 Java 能识别的目录后重试。", ex);
            }
            command.Add("@" + path);
        }
        command.AddRange(args.Skip(generatedJvmCount));
        return command;
    }

    private static string Quote(string value) => '"' + value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\f", "\\f", StringComparison.Ordinal) + '"';

    private static IReadOnlyList<string> WriteLegacyClassPath(IReadOnlyList<string> args, int generatedJvmCount,
        string temporaryDirectory, string workingDirectory)
    {
        var command = args.ToArray();
        var classPathJars = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < generatedJvmCount - 1; i++)
        {
            if (command[i] is not ("-cp" or "-classpath")) continue;
            var classPath = command[++i];
            if (!classPathJars.TryGetValue(classPath, out var jar))
            {
                jar = Path.Combine(temporaryDirectory, $"class-path-{classPathJars.Count + 1}.jar");
                var entries = classPath.Split(Path.PathSeparator).SelectMany(entry =>
                {
                    if (Path.GetFileName(entry) == "*")
                    {
                        var parent = Path.GetFullPath(Path.GetDirectoryName(entry) is { Length: > 0 } directory ? directory : ".", workingDirectory);
                        return Directory.Exists(parent) ? Directory.EnumerateFiles(parent, "*.jar") : [];
                    }
                    return [Path.GetFullPath(entry.Length == 0 ? "." : entry, workingDirectory)];
                });
                WriteClassPathJar(jar, entries);
                classPathJars.Add(classPath, jar);
            }
            command[i] = jar;
        }
        return command;
    }

    private static void WriteClassPathJar(string jar, IEnumerable<string> entries)
    {
        // A manifest preserves the lookup order while keeping the Java 8 command line short.
        var value = "Class-Path: " + string.Join(' ', entries.Select(path =>
            new Uri(Directory.Exists(path) ? Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar : path).AbsoluteUri));
        using var archive = ZipFile.Open(jar, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open(), Encoding.ASCII) { NewLine = "\r\n" };
        writer.WriteLine("Manifest-Version: 1.0");
        for (var offset = 0; offset < value.Length;)
        {
            if (offset > 0) writer.Write(' ');
            var count = Math.Min(offset == 0 ? 72 : 71, value.Length - offset);
            writer.WriteLine(value.AsSpan(offset, count));
            offset += count;
        }
        writer.WriteLine();
    }
}
