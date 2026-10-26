namespace CtrlCpp.Models;

public sealed class AppSettings
{
    public int RetentionDays { get; set; } = 3;
    public int CacheLimitMB { get; set; } = 3072;
    public bool StartWithWindows { get; set; } = true;
    public List<string> ExcludedApps { get; set; } = new();

    /// <summary>打开历史窗口的快捷键（默认 Ctrl=1 + 空格 0x20，见 HotkeyMods 枚举）。</summary>
    public int HotkeyMods { get; set; } = 1;
    public int HotkeyVk { get; set; } = 0x20;
}
