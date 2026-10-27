using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.Core;

namespace Island.App;

public partial class MemoryAllocationEditor : UserControl
{
    private readonly Func<MemorySnapshot> readMemory;
    private readonly DispatcherTimer refreshTimer;
    private int manualMb = 4096, modCount;
    private bool updating;
    private string? directory;
    private MemorySnapshot? snapshot;
    public RadioButton AutomaticOption => AutomaticChoice;
    public RadioButton CustomOption => CustomChoice;
    public Slider MemorySlider => AllocationSlider;
    public bool Automatic { get => AutomaticChoice.IsChecked == true; set { AutomaticChoice.IsChecked = value; CustomChoice.IsChecked = !value; UpdateDisplay(); } }
    public int ManualMemoryMb { get => manualMb; set { manualMb = Math.Clamp(value, 512, 65536); UpdateDisplay(); } }
    public int DisplayMemoryMb { get; private set; }
    public object? OptimizationContent { get => OptimizationHost.Content; set => OptimizationHost.Content = value; }

    public MemoryAllocationEditor() : this(MemoryAllocator.ReadSystem) { }
    internal MemoryAllocationEditor(Func<MemorySnapshot> readMemory)
    {
        this.readMemory = readMemory;
        InitializeComponent();
        AutomaticChoice.GroupName = CustomChoice.GroupName = "MemoryMode-" + Guid.NewGuid().ToString("N");
        refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += (_, _) => { if (Window.GetWindow(this)?.WindowState != WindowState.Minimized) Refresh(); };
        Loaded += (_, _) => { Refresh(); UpdateTimer(); };
        Unloaded += (_, _) => refreshTimer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    public void Load(bool automatic, int memoryMb, string? modDirectory = null)
    {
        updating = true;
        manualMb = Math.Clamp(memoryMb, 512, 65536);
        AutomaticChoice.IsChecked = automatic; CustomChoice.IsChecked = !automatic;
        updating = false;
        SetContext(modDirectory); Refresh();
    }

    public void SetContext(string? modDirectory)
    {
        directory = modDirectory;
        try { modCount = directory is null ? 0 : MemoryAllocator.CountMods(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { modCount = 0; }
        UpdateDisplay();
    }

    public void Refresh()
    {
        try { snapshot = readMemory(); }
        catch (InvalidOperationException) { snapshot = null; }
        UpdateDisplay();
    }

    private void UpdateTimer()
    {
        if (refreshTimer is null) return;
        if (IsLoaded && IsVisible) { Refresh(); refreshTimer.Start(); }
        else refreshTimer.Stop();
    }

    private void ChangeMode(object sender, RoutedEventArgs e) { if (!updating) UpdateDisplay(); }
    private void ChangeLimit(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || AutomaticChoice is null || Automatic) return;
        manualMb = Math.Clamp((int)Math.Round(e.NewValue), 512, 65536); UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (AllocationSlider is null || updating) return;
        var total = Math.Max(0, snapshot?.TotalMb ?? 0);
        var available = Math.Clamp(snapshot?.AvailableMb ?? 0, 0, total);
        var used = total - available;
        var error = "";
        DisplayMemoryMb = manualMb;
        if (Automatic)
        {
            try { DisplayMemoryMb = snapshot is null ? 0 : MemoryAllocator.Recommend(snapshot, modCount); }
            catch (InvalidOperationException ex) { DisplayMemoryMb = 0; error = ex.Message; }
        }
        updating = true;
        try
        {
            AllocationSlider.Maximum = Math.Clamp(Math.Max(total / 256 * 256, manualMb), 512, 65536);
            AllocationSlider.Value = Math.Clamp(DisplayMemoryMb, 512, AllocationSlider.Maximum);
            AllocationSlider.IsEnabled = !Automatic;
            AllocationSlider.ToolTip = $"{DisplayMemoryMb} MB · {DisplayMemoryMb / 1024d:0.0} GB";
        }
        finally { updating = false; }
        if (total == 0)
        {
            UsedValue.Text = "内存信息暂不可用"; AllocationValue.Text = Automatic ? "启动时自动分配" : $"{manualMb / 1024d:0.0} GB";
            UsedColumn.Width = new(0, GridUnitType.Star); GameColumn.Width = new(0, GridUnitType.Star); FreeColumn.Width = new(1, GridUnitType.Star);
        }
        else
        {
            UsedValue.Text = $"{used / 1024d:0.0} GB / {total / 1024d:0.0} GB";
            AllocationValue.Text = (DisplayMemoryMb > 0 ? $"{DisplayMemoryMb / 1024d:0.0} GB" : "无法自动分配") + $"（可用 {available / 1024d:0.0} GB）";
            var game = Math.Min(DisplayMemoryMb, available);
            UsedColumn.Width = new(used, GridUnitType.Star); GameColumn.Width = new(game, GridUnitType.Star); FreeColumn.Width = new(Math.Max(0, available - game), GridUnitType.Star);
        }
        AllocationHint.Text = error.Length > 0 ? error : (Automatic ? "按当前系统可用内存及实例模组估算，启动时重新计算。" : $"自定义上限：{manualMb} MB。可用方向键调整，每步 256 MB。")
            + " 深色为系统已用内存，浅色为预计游戏分配；分配量超过当前可用内存时，浅色仅绘制可用部分。";
    }
}
