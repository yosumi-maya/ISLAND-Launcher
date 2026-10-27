using System.Windows;
using System.Windows.Controls;
using BlockDock.Core;

namespace Island.App;

public partial class MainWindow
{
    private void InitializeInstanceMenus()
    {
        InstanceList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (InstanceList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
            foreach (var item in InstanceList.Items)
                if (InstanceList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                    InitializeInstanceMenu(container, new RoutedEventArgs());
        };
    }

    private void InitializeInstanceMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem container || container.ContextMenu is not null) return;
        var menu = new ContextMenu { MinWidth = 170, FontFamily = FontFamily, FontSize = 12 };
        menu.SetResourceReference(Control.BackgroundProperty, "Surface");
        menu.SetResourceReference(Control.ForegroundProperty, "Ink");
        menu.SetResourceReference(Control.BorderBrushProperty, "Line");
        MenuItem Add(string label, Action<InstanceCard> action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                if (menu.Tag is not InstanceCard card) return;
                menu.IsOpen = false;
                try { action(card); } catch (Exception ex) { Report(ex); }
            };
            menu.Items.Add(item); return item;
        }
        var manage = Add("实例管理", OpenInstanceManagement);
        var mods = Add("Mod 管理", card =>
        {
            if (operation is not null || game is not null) return;
            new ModManagerWindow(this, card).ShowDialog();
        });
        menu.Items.Add(new Separator());
        foreach (var (label, relative) in new[] { ("游戏目录", ""), ("存档", "saves"), ("资源包", "resourcepacks"), ("光影", "shaderpacks") })
            Add(label, card =>
            {
                var path = relative.Length == 0 ? card.RuntimeDirectory : SafePaths.Under(card.RuntimeDirectory, relative);
                Directory.CreateDirectory(path); openInstanceDirectory(path);
            });
        menu.Opened += (_, _) =>
        {
            if (container.DataContext is not InstanceCard card) { menu.IsOpen = false; return; }
            // Each menu belongs to its card; never resolve actions through a stale selection.
            menu.Tag = card; InstanceList.SelectedItem = card;
            manage.IsEnabled = mods.IsEnabled = operation is null && game is null;
        };
        container.ContextMenu = menu;
    }

    private void OpenInstanceManagement(InstanceCard card)
    {
        if (operation is not null || game is not null) return;
        new InstanceWindow(this, card, state, () => store.Save(state)).ShowDialog();
        ReloadLibrary();
    }
}
