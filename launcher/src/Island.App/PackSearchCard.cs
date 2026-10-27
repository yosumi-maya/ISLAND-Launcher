using BlockDock.Core;

namespace Island.App;

internal sealed record PackSearchCard(CatalogProject Project)
{
    private static readonly IReadOnlyDictionary<string, string> CategoryNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["adventure"] = "冒险", ["challenging"] = "挑战", ["combat"] = "战斗", ["kitchen-sink"] = "综合",
        ["lightweight"] = "轻量", ["magic"] = "魔法", ["multiplayer"] = "多人", ["optimization"] = "性能优化",
        ["quests"] = "任务", ["technology"] = "科技", ["vanilla-like"] = "原版风格", ["worldgen"] = "世界生成",
        ["atmosphere"] = "氛围", ["bloom"] = "泛光", ["cartoon"] = "卡通", ["colored-lighting"] = "彩色光照", ["fantasy"] = "奇幻",
        ["foliage"] = "植被", ["gui"] = "界面", ["entities"] = "实体", ["fonts"] = "字体", ["locale"] = "语言",
        ["modded"] = "模组兼容", ["models"] = "模型", ["minecraft"] = "原版", ["realistic"] = "写实", ["semi-realistic"] = "半写实",
        ["shadows"] = "阴影", ["reflections"] = "反射", ["low"] = "低配置", ["medium"] = "中等配置", ["high"] = "高配置", ["potato"] = "入门配置", ["library"] = "前置库", ["utility"] = "实用"
    };
    public IReadOnlyList<string> Tags => Project.Categories.Where(c => c.ToLowerInvariant() is not ("fabric" or "forge" or "neoforge" or "quilt" or "iris" or "optifine") && !Project.Loaders.Contains(c, StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).Select(c => CategoryNames.GetValueOrDefault(c, c)).ToArray();
    public string Compatibility => string.Join(" / ", Project.Loaders) + (Project.Versions.Count > 0 ? "  " + string.Join(", ", Project.Versions.TakeLast(3)) : "");
    public string FullCompatibility => string.Join(" / ", Project.Loaders) + "  " + string.Join(", ", Project.Versions);
    public string Downloads => Project.Downloads >= 10000 ? $"{Project.Downloads / 10000:0.#} 万" : $"{Project.Downloads:N0}";
    public string Updated
    {
        get
        {
            if (Project.Updated == DateTimeOffset.MinValue) return "更新时间未知";
            var days = Math.Max(0, (DateTimeOffset.UtcNow - Project.Updated).TotalDays);
            return days switch { < 1 => "今天", < 30 => $"{(int)days} 天前", < 365 => $"{(int)(days / 30)} 个月前", _ => $"{(int)(days / 365)} 年前" };
        }
    }
}
