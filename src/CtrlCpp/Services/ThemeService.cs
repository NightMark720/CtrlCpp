using System.Runtime.InteropServices;
using CtrlCpp.Interop;
using Wpf.Ui.Appearance;
using static CtrlCpp.Interop.NativeConstants;
using static CtrlCpp.Interop.NativeMethods;

namespace CtrlCpp.Services;

/// <summary>WPF-UI 主题与 DWM 效果调用集中在此（Win10 兼容守卫）。</summary>
public static class ThemeService
{
    public static void ApplyDark()
    {
        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None);
        }
        catch (Exception ex) { LogService.LogEx("应用暗色主题失败", ex); }
    }

    /// <summary>窗口标题栏暗色（Win10 1809 前 DWM 属性不存在，静默忽略）。</summary>
    public static void ApplyDarkTitleBar(IntPtr hwnd, bool enabled)
    {
        try
        {
            var v = enabled ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4) != 0)
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref v, 4);
        }
        catch { /* Win10 老版本忽略 */ }
    }

    /// <summary>窗口圆角，仅 Win11 (build ≥ 22000)。</summary>
    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        try
        {
            var v = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4);
        }
        catch { /* 忽略 */ }
    }
}
