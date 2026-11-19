namespace Island.Core;

public static class MicrosoftApplication
{
    // Public application identifier registered by the ISLAND developer; never a client secret.
    public const string ClientId = "cac536d4-cbf5-469b-ad59-cd7e37d3174a";

    public static string ResolveClientId(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return ClientId;
        var value = configured.Trim();
        if (!Guid.TryParse(value, out _)) throw new ArgumentException("高级配置中的 MicrosoftClientId 无效，请清空该项以恢复启动器内置配置。");
        return value;
    }
}
