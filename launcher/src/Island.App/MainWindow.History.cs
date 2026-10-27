using System.Windows;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private sealed record RecentGame(InstanceCard Card, SavedWorld? World, DateTimeOffset PlayedAt);
    private RecentGame? recentGame;
    private void RefreshRecentGame()
    {
        RecentGame? recent = null;
        foreach (var group in cards.GroupBy(card => card.RuntimeDirectory, StringComparer.OrdinalIgnoreCase))
        {
            // Shared saves cannot identify a version. Prefer an actual launch record or the folder's last selection.
            var card = group.OrderByDescending(card => card.Preferences.LastPlayed).First();
            if (group.Count() > 1 && card.Preferences.LastPlayed is null)
            {
                var selected = state.GameFolders.First(folder => GameFolder.SamePath(folder.Directory, card.GameDirectory)).LastSelectedInstanceId;
                card = group.FirstOrDefault(candidate => candidate.Id == selected);
                if (card is null) continue;
            }
            var world = WorldHistory.FindLatest(card.RuntimeDirectory);
            var played = card.Preferences.LastPlayed;
            if (world is not null && (played is null || world.LastPlayed > played)) played = world.LastPlayed;
            if (played is not null && (recent is null || played > recent.PlayedAt)) recent = new(card, world, played.Value);
        }
        recentGame = recent;
        RecentInstanceName.Text = recent?.Card.DisplayName ?? "还没有游玩记录";
        RecentInstanceDetail.Text = recent is null ? "从下方选择实例，开始你的第一次游玩。" : recent.Card.FolderName + "  ·  " + recent.Card.VersionLine + "  ·  " + recent.Card.Loader;
        RecentWorldName.Text = recent?.World?.Name ?? (recent is null ? "还没有地图记录" : "尚未发现单人存档");
        RecentWorldName.ToolTip = recent?.World?.Name;
        RecentWorldThumbnail.Source = WorldThumbnail.Load(recent?.World?.IconPath);
        RecentWorldThumbnailFrame.Visibility = RecentWorldThumbnail.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        RecentPlayedTime.Text = recent is null ? "游玩后会在这里显示" : "上次游玩  " + (recent.World?.LastPlayed ?? recent.PlayedAt).ToLocalTime().ToString("yyyy.MM.dd  HH:mm");
        ContinueButton.IsEnabled = recent is not null && operation is null && game is null;
    }
    private void ClipRecentWorld(object sender, SizeChangedEventArgs e) => ((FrameworkElement)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 11, 11);
    private void ClipWorldThumbnail(object sender, SizeChangedEventArgs e) => ((FrameworkElement)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);
    private async void ContinueGame(object sender, RoutedEventArgs e)
    {
        if (recentGame is not { } recent || operation is not null || game is not null) return;
        try
        {
            var folder = state.GameFolders.First(f => GameFolder.SamePath(f.Directory, recent.Card.GameDirectory));
            var key = recent.Card.Key;
            ActivateGameFolder(folder.Id);
            InstanceList.SelectedItem = InstanceList.Items.Cast<InstanceCard>().FirstOrDefault(card => card.Key == key);
            if (Selected is { } selected) await LaunchInstanceAsync(selected, recent.World?.FolderName);
        }
        catch (Exception ex) { Report(ex); }
    }
}
