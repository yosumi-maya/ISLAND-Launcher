using System.Text.Json.Nodes;
using BlockDock.Core;

internal static class InstalledLibraryTests
{
    public static void AddTo(List<(string Name, Func<Task> Test)> tests, string root)
    {
        tests.Add(("已安装整合包读取保存的图标与包名", () =>
        {
            var game = Path.Combine(root, "library-icons");
            Profile(game, "Pack", "{}");
            File.WriteAllText(Path.Combine(game, "versions/Pack/blockdock-instance.json"), """{"minecraft":"1.20.1","loader":"Fabric","modpack":"图标整合包","iconUrl":"https://cdn.modrinth.com/example/icon.png"}""");
            var installed = MinecraftInstaller.ListInstalled(game).Single();
            Check(Get(installed, "IconUrl") as string == "https://cdn.modrinth.com/example/icon.png", "应保留项目图标");
            Check(Get(installed, "PackName") as string == "图标整合包", "应显示整合包名称");
            Check(Get(installed, "IconLoader") is LoaderKind.Fabric, "缺图时应使用正确加载器");
            return Task.CompletedTask;
        }));
        tests.Add(("旧实例识别加载器，损坏附加元数据不隐藏有效版本", () =>
        {
            var game = Path.Combine(root, "library-legacy");
            Profile(game, "Fabric", """{"inheritsFrom":"1.20.1","libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
            Profile(game, "Forge", """{"inheritsFrom":"1.20.1","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.3.0"}]}""");
            Profile(game, "NeoForge", """{"inheritsFrom":"1.21.1","libraries":[{"name":"net.neoforged:neoforge:21.1.1"}]}""");
            File.WriteAllText(Path.Combine(game, "versions/NeoForge/blockdock-instance.json"), "{broken");
            var installed = MinecraftInstaller.ListInstalled(game);
            Check(installed.Count == 3, "附加元数据损坏时仍应显示有效实例");
            foreach (var row in installed) Check(Get(row, "IconLoader")?.ToString() == row.Name, "应识别旧实例 " + row.Name);
            return Task.CompletedTask;
        }));
        tests.Add(("已安装图标地址缺失或无效时使用加载器回退", () =>
        {
            var game = Path.Combine(root, "library-fallback");
            Profile(game, "Local", "{}");
            File.WriteAllText(Path.Combine(game, "versions/Local/blockdock-instance.json"), """{"loader":"NeoForge","iconUrl":"file:///C:/private.png"}""");
            var row = MinecraftInstaller.ListInstalled(game).Single();
            Check(Get(row, "IconUrl") is null && Get(row, "IconLoader") is LoaderKind.NeoForge, "只使用公开 HTTPS 图标地址，回退到加载器");
            return Task.CompletedTask;
        }));
    }
    private static void Profile(string root, string name, string content)
    {
        var folder = Path.Combine(root, "versions", name); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, name + ".json"), content);
    }
    private static object? Get(InstalledInstance instance, string name) => typeof(InstalledInstance).GetProperty(name)?.GetValue(instance);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
