namespace Island.App;

public partial class MainWindow
{
    private void InitializeCurseForge()
    {
        try { state.CurseForgeApiKey = BundledCatalogCredential.Load(); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            state.CurseForgeApiKey = "";
            Log("内置下载服务暂不可用，请重新下载启动器。");
        }
    }
}
