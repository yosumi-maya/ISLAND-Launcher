using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;

internal static class WindowOutlineUiTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr region);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static bool HasRoundedRegion(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            return region != IntPtr.Zero && GetWindowRect(handle, out var rect) && GetWindowRgn(handle, region) == 3
                && !PtInRegion(region, 0, 0) && !PtInRegion(region, rect.Right - rect.Left - 1, 0)
                && !PtInRegion(region, 0, rect.Bottom - rect.Top - 1)
                && PtInRegion(region, (rect.Right - rect.Left) / 2, 2);
        }
        finally { if (region != IntPtr.Zero) DeleteObject(region); }
    }
    public static void Verify(MainWindow window, Action<bool, string> check)
    {
        var surface = (MaterialBorder)window.FindName("WindowSurface");
        check(HasRoundedRegion(window), $"The actual HWND excludes its outer corners after resizing to {(int)window.ActualWidth}px");
        var image = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(surface);
        byte[] pixel = new byte[4];
        bool TransparentAt(int x, int y) { image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[3] == 0; }
        check(TransparentAt(0, 0) && TransparentAt(image.PixelWidth - 1, 0) && TransparentAt(0, image.PixelHeight - 1)
            && TransparentAt(image.PixelWidth - 1, image.PixelHeight - 1) && surface.Clip is RectangleGeometry { RadiusX: 14, RadiusY: 14 },
            $"All four visual corners are clipped without an opaque square behind them at {(int)window.ActualWidth}px");
        var handle = new WindowInteropHelper(window).Handle; GetWindowRect(handle, out var bounds);
        var dpi = VisualTreeHelper.GetDpi(window); var inset = (int)Math.Ceiling(5 * dpi.DpiScaleX);
        int Hit(int x, int y) => SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr(unchecked((ushort)x | ((long)(ushort)y << 16)))).ToInt32();
        check(Hit(bounds.Left + inset, bounds.Top + inset) == 13
            && Hit((bounds.Left + bounds.Right) / 2, bounds.Top + (int)(20 * dpi.DpiScaleY)) == 2,
            $"Rounded corners retain diagonal resize and title-bar drag hit testing at {(int)window.ActualWidth}px");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            check(WindowMaterials.ReadCornerPreference(window) == 2, "The native DWM corner preference is rounded");
    }
}
