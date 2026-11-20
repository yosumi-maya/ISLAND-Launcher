using System.Windows;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

public partial class MainWindow
{
    private sealed record RecentGame(InstanceCard Card, SavedWorld? World, DateTimeOffset? PlayedAt);
    private RecentGame? recentGame;
    private CancellationTokenSource? recentGameLookup;

    private async Task RefreshRecentGameAsync()
    {
        recentGameLookup?.Cancel();
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        recentGameLookup = lookup;
        var token = lookup.Token;
        var selected = Selected;
        var candidates = selected is null ? RecentGameCandidates() : [new RecentGame(selected, null, selected.Preferences.LastPlayed)];

        ShowRecentGame(selected is null ? null : candidates[0], selected is not null, loading: candidates.Length > 0);
        if (candidates.Length == 0)
        {
            recentGameLookup = null;
            return;
        }
        try
        {
            var (recent, thumbnail) = await Task.Run(() =>
            {
                var result = LoadRecentGame(candidates, selected is not null, token);
                token.ThrowIfCancellationRequested();
                return (result, WorldThumbnail.Load(result?.World?.IconPath));
            }, token);
            if (!token.IsCancellationRequested) ShowRecentGame(recent, selected is not null, thumbnail: thumbnail);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(recentGameLookup, lookup)) recentGameLookup = null;
        }
    }

    private RecentGame[] RecentGameCandidates()
    {
        var candidates = new List<RecentGame>();
        foreach (var group in cards.GroupBy(card => card.RuntimeDirectory, StringComparer.OrdinalIgnoreCase))
        {
            // Shared saves cannot identify a version. Prefer an actual launch record or the folder's last selection.
            var card = group.OrderByDescending(card => card.Preferences.LastPlayed).First();
            if (group.Count() > 1 && card.Preferences.LastPlayed is null)
            {
                var previous = state.GameFolders.First(folder => GameFolder.SamePath(folder.Directory, card.GameDirectory)).LastSelectedInstanceId;
                card = group.FirstOrDefault(candidate => candidate.Id == previous);
                if (card is null) continue;
            }
            candidates.Add(new(card, null, card.Preferences.LastPlayed));
        }
        return candidates.ToArray();
    }

    private static RecentGame? LoadRecentGame(RecentGame[] candidates, bool hasSelection, CancellationToken token)
    {
        RecentGame? recent = null;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var world = WorldHistory.FindLatest(candidate.Card.RuntimeDirectory, token);
            if (hasSelection) return candidate with { World = world };
            var played = candidate.PlayedAt;
            if (world is not null && (played is null || world.LastPlayed > played)) played = world.LastPlayed;
            if (played is not null && (recent is null || played > recent.PlayedAt))
                recent = candidate with { World = world, PlayedAt = played };
        }
        return recent;
    }

    private void ShowRecentGame(RecentGame? recent, bool hasSelection, bool loading = false, ImageSource? thumbnail = null)
    {
        recentGame = loading ? null : recent;
        RecentInstanceCaption.Text = hasSelection ? "当前实例" : "上次游玩";
        RecentInstanceName.Text = recent?.Card.DisplayName ?? "还没有游玩记录";
        RecentInstanceDetail.Text = recent is null ? "从下方选择实例，开始你的第一次游玩。" : recent.Card.FolderName + "  ·  " + recent.Card.VersionLine + "  ·  " + recent.Card.Loader;
        RecentWorldName.Text = loading ? "正在读取存档…" : recent?.World?.Name ?? (recent is null ? "还没有地图记录" : "尚未发现单人存档");
        RecentWorldName.ToolTip = recent?.World?.Name;
        RecentWorldThumbnail.Source = thumbnail;
        RecentWorldThumbnailFrame.Visibility = thumbnail is not null ? Visibility.Visible : Visibility.Collapsed;
        var lastPlayed = recent?.World?.LastPlayed ?? recent?.PlayedAt;
        RecentPlayedTime.Text = lastPlayed is null ? "尚未游玩" : "上次游玩  " + lastPlayed.Value.ToLocalTime().ToString("yyyy.MM.dd  HH:mm");
        ContinueButton.IsEnabled = recentGame is not null && operation is null && game is null && !DownloadCenterView.Queue.IsBusy;
    }
    private void ClipRecentWorld(object sender, SizeChangedEventArgs e) => ((FrameworkElement)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 11, 11);
    private void ClipWorldThumbnail(object sender, SizeChangedEventArgs e) => ((FrameworkElement)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);
    private async void ContinueGame(object sender, RoutedEventArgs e)
    {
        if (recentGame is not { } recent || operation is not null || game is not null) return;
        try
        {
            await LaunchInstanceAsync(recent.Card, recent.World?.FolderName, activateFolder: true);
        }
        catch (Exception ex) { Report(ex); }
    }
}
