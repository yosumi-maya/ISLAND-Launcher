using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Island.Core;

namespace Island.App;

public static class WindowMaterials
{
    private const int SystemBackdropAttribute = 38, ExtendedStyle = -20;
    private const long Layered = 0x80000;
    private static readonly Dictionary<IntPtr, double> Opacities = [];
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data);
    private static readonly SubclassProc OpacityProcedure = KeepOpacity;
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    private static IntPtr KeepOpacity(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data)
    {
        // HwndTarget removes WS_EX_LAYERED from non-per-pixel WPF windows. Preserve it
        // only while our own window uses a constant alpha; let WPF handle all other styles.
        if (message == 0x7c && wparam.ToInt64() == ExtendedStyle && Opacities.TryGetValue(window, out var opacity) && opacity < .999)
        {
            Marshal.WriteInt32(lparam, sizeof(int), Marshal.ReadInt32(lparam, sizeof(int)) | (int)Layered);
            return IntPtr.Zero;
        }
        if (message == 0x82)
        {
            Opacities.Remove(window); RemoveWindowSubclass(window, OpacityProcedure, id);
        }
        return DefSubclassProc(window, message, wparam, lparam);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    public static double ReadOpacity(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && GetLayeredWindowAttributes(handle, out _, out var alpha, out _) ? alpha / 255d : 1;
    }
    public static int ReadBackdrop(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && DwmGetWindowAttribute(handle, SystemBackdropAttribute, out var value, sizeof(int)) >= 0 ? value : 0;
    }
    public static bool ReadDarkMode(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && DwmGetWindowAttribute(handle, 20, out var value, sizeof(int)) >= 0 && value == 1;
    }
    public static void ApplyCornerPreference(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        // Glass backdrops ignore WindowChrome.CornerRadius; ask DWM to round those windows.
        var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
        DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }
    public static int ReadCornerPreference(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && DwmGetWindowAttribute(handle, 33, out var value, sizeof(int)) >= 0 ? value : 0;
    }
    public static (bool Native, string Description) Apply(Window window, LauncherAppearance options)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return (false, "窗口显示后应用材质。");
        if (!Opacities.ContainsKey(handle) && !SetWindowSubclass(handle, OpacityProcedure, new UIntPtr(0x15441), UIntPtr.Zero))
            return (false, "窗口透明度初始化未完成。");
        Opacities[handle] = options.WindowOpacity;
        const bool native = false;
        var backdrop = 1; // Clear any system backdrop left by an older version.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var tint = AppearanceRenderer.ColorFor(options.Window, (Brush)window.FindResource("WindowBackground"));
            var dark = tint.R * .299 + tint.G * .587 + tint.B * .114 <= 145 ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, SystemBackdropAttribute, ref backdrop, sizeof(int));
        }
        var margins = native ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        if (WindowChrome.GetWindowChrome(window) is { } chrome) chrome.GlassFrameThickness = native ? new Thickness(-1) : new Thickness(0);
        DwmExtendFrameIntoClientArea(handle, ref margins);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
            target.BackgroundColor = native ? Colors.Transparent : ((SolidColorBrush)window.FindResource("WindowBackground")).Color;
        window.Background = native ? Brushes.Transparent : (Brush)window.FindResource("WindowBackground");
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        if (options.WindowOpacity < .999)
        {
            SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style | Layered));
            if (!SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(options.WindowOpacity * 255), 2))
                return (native, "窗口透明度未能应用：" + Marshal.GetLastWin32Error());
        }
        else if ((style & Layered) != 0) SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style & ~Layered));
        return (false, options.Window.Material switch
        {
            SurfaceMaterial.Gaussian => "当前窗口：高斯模糊背景。",
            SurfaceMaterial.LiquidGlass => "当前窗口：应用内液态玻璃。", _ => "当前窗口：纯色或原图背景。"
        });
    }
}
