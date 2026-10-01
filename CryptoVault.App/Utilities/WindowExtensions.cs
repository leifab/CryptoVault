using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CryptoVault;

public static class WindowExtensions
{
    private const int GWL_EXSTYLE = -20;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL = 0;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint WM_SETICON = 0x0080;
    private const int WS_EX_DLGMODALFRAME = 0x0001;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    // Win32 Non-Client Icon Suppression:
    // WPF provides no native property to hide the default window icon on modal dialogs.
    // Setting WS_EX_DLGMODALFRAME and clearing both WM_SETICON targets (small & big) forces
    // the DWM non-client frame to render cleanly without the legacy icon placeholder.
    public static void RemoveIcon(Window window)
    {
        window.SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_DLGMODALFRAME);

            SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_SMALL), IntPtr.Zero);
            SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_BIG), IntPtr.Zero);

            _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);
}

