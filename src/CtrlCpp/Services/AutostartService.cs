using Microsoft.Win32;

namespace CtrlCpp.Services;

public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CtrlCpp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, false);
    }

    /// <summary>每次启动时刷新（exe 移动位置后自动修正路径）。</summary>
    public static void Refresh()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var want = $"\"{Environment.ProcessPath}\"";
        var cur = key.GetValue(ValueName) as string;
        if (cur != want) key.SetValue(ValueName, want);
    }
}
