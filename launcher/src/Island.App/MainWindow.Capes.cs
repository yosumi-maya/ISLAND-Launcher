using System.Windows;
using System.Windows.Controls;
using Island.Core;
namespace Island.App;
internal sealed class CapeForm : FormWindow
{
    public new StackPanel Body => base.Body;
    public new StackPanel Actions => base.Actions;
    public CapeForm(Window owner) : base(owner, "正版披风", "选择此账号拥有的披风，应用后同步到 Minecraft Java 档案。", 620) { }
}
public partial class MainWindow
{
    private readonly HashSet<string> capeRequests = [];
    private async Task LoadAccountCapeAsync(PlayerAccount account)
    {
        var key = account.Id + "|" + account.ActiveCapeId;
        if (account.CapesLoaded && (account.ActiveCapeId.Length == 0 || account.CapePng is not null)) return;
        if (!capeRequests.Add(key)) return;
        try
        {
            using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            if (!account.CapesLoaded)
            {
                var result = await auth.RefreshAsync(account, timeout.Token);
                var index = state.Accounts.FindIndex(a => a.Id == account.Id); if (index < 0) return;
                account = result.Account; state.Accounts[index] = account; store.Save(state);
            }
            var cape = account.Capes.FirstOrDefault(c => c.Id == account.ActiveCapeId);
            if (cape is not null) { var png = await auth.DownloadCapeAsync(cape.Url, timeout.Token); SkinImages.Decode(png); account.CapePng = png; }
            store.Save(state); ReloadAccounts();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Log("披风暂未加载，可在皮肤配置中重新同步。"); }
    }
    private void OpenCapeWindow(PlayerAccount selected)
    {
        if (!selected.Microsoft || operation is not null) return;
        var window = new CapeForm(this);
        var preview = new SkinPreview3D { Height = 290 }; preview.SetSkin(selected.SkinPng, selected.SkinVariant); preview.SetCape(selected.CapePng);
        window.Body.Children.Add(preview);
        var list = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id", MinWidth = 240 };
        window.Body.Children.Add(list);
        var status = new TextBlock { Text = "正在同步披风…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) }; window.Body.Children.Add(status);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var account = selected; bool busy = false, closed = false; int version = 0;
        var apply = new Button { Content = "应用披风", IsEnabled = false }; window.Actions.Children.Add(apply);
        var refresh = new Button { Content = "重新同步", Margin = new Thickness(0,0,8,0) }; window.Actions.Children.Add(refresh);
        window.Body.Children.Add(window.Actions);
        async Task Sync()
        {
            if (busy) return; busy = true; apply.IsEnabled = refresh.IsEnabled = list.IsEnabled = false;
            try
            {
                using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates);
                var result = await auth.RefreshAsync(account, cancel.Token); if (closed) return;
                var index = state.Accounts.FindIndex(a => a.Id == account.Id); if (index < 0) throw new InvalidOperationException("账号已移除。");
                account = result.Account; state.Accounts[index] = account; capeRequests.Remove(account.Id + "|" + account.ActiveCapeId); store.Save(state); ReloadAccounts();
                list.ItemsSource = new[] { new PlayerCape("", "不穿戴披风", "") }.Concat(account.Capes).ToArray(); list.SelectedValue = account.ActiveCapeId;
                status.Text = account.Capes.Count == 0 ? "此账号暂无可用披风。" : $"拥有 {account.Capes.Count} 件披风，选择后点击应用。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; if (!closed) { refresh.IsEnabled = list.IsEnabled = true; apply.IsEnabled = list.SelectedItem is PlayerCape; } }
        }
        list.SelectionChanged += async (_, _) =>
        {
            var current = ++version;
            if (list.SelectedItem is not PlayerCape cape) return;
            if (cape.Id.Length == 0) { preview.SetCape(null); return; }
            try { using var auth = new MicrosoftAuth(); var png = await auth.DownloadCapeAsync(cape.Url, cancel.Token); if (!closed && current == version) preview.SetCape(png); }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!closed && current == version) { preview.SetCape(null); status.Text = "披风预览加载失败，可以重新同步后重试。"; } }
        };
        refresh.Click += async (_, _) => await Sync();
        apply.Click += async (_, _) =>
        {
            if (busy || list.SelectedItem is not PlayerCape cape) return;
            busy = true; apply.IsEnabled = refresh.IsEnabled = list.IsEnabled = false; status.Text = "正在应用披风…";
            try
            {
                using var auth = new MicrosoftAuth(verifyCertificates: state.Jvm.VerifyLoginCertificates);
                var result = await auth.RefreshAsync(account, cancel.Token);
                var index = state.Accounts.FindIndex(a => a.Id == account.Id); if (index < 0) throw new InvalidOperationException("账号已移除。");
                account = result.Account; state.Accounts[index] = account; store.Save(state);
                await auth.SetCapeAsync(account, result.Identity, cape.Id, cancel.Token); capeRequests.Remove(account.Id + "|" + account.ActiveCapeId); store.Save(state); await LoadAccountCapeAsync(account); ReloadAccounts();
                status.Text = "已应用披风。已运行的游戏可能需要重新进入服务器。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; if (!closed) apply.IsEnabled = refresh.IsEnabled = list.IsEnabled = true; }
        };
        window.Loaded += async (_, _) => await Sync();
        window.Closed += (_, _) => { closed = true; cancel.Cancel(); };
        window.ShowDialog();
    }
}
