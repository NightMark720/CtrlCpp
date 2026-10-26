# 统计系统托盘主区 + 溢出区（^）的图标数量
$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class TrayCount {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cl, string t);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    static int Count(IntPtr toolbar) { return (int)SendMessage(toolbar, 0x418, IntPtr.Zero, IntPtr.Zero); }
    public static int MainCount() {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return -1;
        var tn = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        var sp = FindWindowEx(tn, IntPtr.Zero, "SysPager", null);
        var tb = FindWindowEx(sp, IntPtr.Zero, "ToolbarWindow32", null);
        return tb == IntPtr.Zero ? -1 : Count(tb);
    }
    public static int OverflowCount() {
        var ov = FindWindow("NotifyIconOverflowWindow", null);
        if (ov == IntPtr.Zero) return -1;
        var tb2 = FindWindowEx(ov, IntPtr.Zero, "ToolbarWindow32", null);
        return tb2 == IntPtr.Zero ? -1 : Count(tb2);
    }
}
"@
$m = [TrayCount]::MainCount(); $o = [TrayCount]::OverflowCount()
"托盘主区: $m 个图标, 溢出区(^): $o 个图标, 合计: $($m + $o)"