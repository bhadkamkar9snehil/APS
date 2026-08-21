using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace APS.DesktopHost;

internal static class NativeWindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaCaptionColor = 35;
    internal const int DwmwaTextColor = 36;

    // COLORREF stores channels as 0x00BBGGRR.
    internal const uint GraphiteCaption = 0x001B1E20;
    private const uint WarmWhiteText = 0x00F4F4F5;

    internal static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var darkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        var caption = GraphiteCaption;
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref caption, sizeof(uint));

        var text = WarmWhiteText;
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref text, sizeof(uint));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int valueSize);
}
