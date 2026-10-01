using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CryptoVault;

// Coordinates automatic dark/light theme synchronization with Windows OS personalization settings.
public static class ThemeService
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 is the official Windows 10 20H1+ (Build 19041+) and Windows 11 attribute.
    // 19 is the fallback attribute used in Windows 10 Insider/preview builds 18985-19040.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    private static ResourceDictionary? s_darkThemeDictionary;
    private static readonly Uri s_darkThemeUri = new Uri("pack://application:,,,/Themes/DarkTheme.xaml", UriKind.RelativeOrAbsolute);
    private static bool s_initialized;

    public static void ApplyCurrentTheme()
    {
        bool isDark = IsWindowsDarkMode();

        var appResources = Application.Current?.Resources;
        if (appResources != null)
        {
            var merged = appResources.MergedDictionaries;
            ResourceDictionary? existing = null;
            foreach (var dict in merged)
            {
                if (dict.Source != null && dict.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    existing = dict;
                    break;
                }
            }

            if (isDark)
            {
                if (existing == null)
                {
                    s_darkThemeDictionary ??= new ResourceDictionary { Source = s_darkThemeUri };
                    merged.Add(s_darkThemeDictionary);
                }
            }
            else
            {
                if (existing != null)
                {
                    merged.Remove(existing);
                }
            }
        }

        // Apply to all currently open windows
        if (Application.Current != null)
        {
            foreach (Window win in Application.Current.Windows)
            {
                ApplyWindowTheme(win, isDark);
            }
        }
    }

    public static void ApplyTitleBarTheme(Window window, bool isDark)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
        {
            // If HWND is not yet created, defer until SourceInitialized so DwmSetWindowAttribute receives a valid Win32 handle
            void OnSourceInitialized(object? s, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyTitleBarTheme(window, isDark);
            }
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        int useDark = isDark ? 1 : 0;
        int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        if (hr != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
        }
    }

    public static void ApplyWindowTheme(Window window, bool isDark)
    {
        ApplyTitleBarTheme(window, isDark);

        if (isDark)
        {
            if (Application.Current?.Resources["WindowBackgroundBrush"] is Brush bg)
            {
                window.Background = bg;
            }
            if (Application.Current?.Resources["TextPrimaryBrush"] is Brush fg)
            {
                window.Foreground = fg;
            }
        }
        else
        {
            window.ClearValue(Control.BackgroundProperty);
            window.ClearValue(Control.ForegroundProperty);
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Initialize()
    {
        if (s_initialized) return;
        s_initialized = true;

        // Apply theme for any window created in the future when loaded
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    ApplyWindowTheme(window, IsWindowsDarkMode());
                }
            }));

        // Listen for live Windows OS theme/preference changes
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Window)
            {
                Application.Current?.Dispatcher?.InvokeAsync(ApplyCurrentTheme);
            }
        };

        // Apply current theme on startup
        ApplyCurrentTheme();
    }

    public static bool IsWindowsDarkMode()
    {
        try
        {
            // Windows 10/11 Personalization registry key: AppsUseLightTheme = 0 indicates Dark Mode.
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal)
            {
                return intVal == 0;
            }
        }
        catch
        {
            // If registry query fails, default to light mode
        }
        return false;
    }
}

