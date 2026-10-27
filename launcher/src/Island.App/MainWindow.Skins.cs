using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private readonly HashSet<string> skinRequests = [];
    private async Task LoadAccountSkinAsync(PlayerAccount account)
    {
        var url = account.SkinUrl;
        if (!skinRequests.Add(account.Id + "|" + url)) return;
        try
        {
            using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var png = await auth.DownloadSkinAsync(url, timeout.Token); SkinImages.Decode(png);
            var current = state.Accounts.FirstOrDefault(a => a.Id == account.Id && a.SkinUrl == url);
            if (current is null || current.SkinPng is not null) return;
            current.SkinPng = png; store.Save(state); ReloadAccounts();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Log("皮肤预览暂未加载，可在玩家账号中重新同步。"); }
    }
    private void ConfigureSkin(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayerAccount selected }) return;
        if (operation is not null) { Notice("请等待当前任务结束后再配置皮肤。"); return; }
        var account = selected;
        async Task<AuthResult> Refresh(MicrosoftAuth auth, CancellationToken token)
        {
            var result = await auth.RefreshAsync(account, token);
            var index = state.Accounts.FindIndex(a => a.Id == account.Id);
            if (index < 0) throw new InvalidOperationException("此玩家账号已被移除。");
            account = result.Account; state.Accounts[index] = account;
            // Persist rotated refresh credentials even if the subsequent skin request fails.
            store.Save(state); ReloadAccounts(); return result;
        }
        var dialog = new AccountSkinWindow(this, account, async (png, variant, token) =>
        {
            if (account.Microsoft)
            {
                using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates); var result = await Refresh(auth, token);
                await auth.SetSkinAsync(account, result.Identity, png, variant, token);
            }
            else { account.SkinPng = png; account.SkinVariant = variant; }
            store.Save(state); ReloadAccounts();
        }, !account.Microsoft ? null : async token =>
        {
            using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates); await Refresh(auth, token);
            if (account.SkinUrl.Length > 0)
            {
                var png = await auth.DownloadSkinAsync(account.SkinUrl, token); SkinImages.Decode(png); account.SkinPng = png;
            }
            else account.SkinPng = null;
            store.Save(state); ReloadAccounts(); return account;
        }, selectSkin);
        dialog.ShowDialog();
    }
}
