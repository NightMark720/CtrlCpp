using CtrlCpp.Models;

namespace CtrlCpp.Services;

/// <summary>快捷键的校验与显示格式化。</summary>
public static class HotkeyFormat
{
    public const int DefaultMods = (int)HotkeyMods.Ctrl;
    public const int DefaultVk = 0x20;

    /// <summary>组合键是否可用：F1~F24 可单独使用；其他按键必须搭配 Ctrl 或 Alt。</summary>
    public static bool IsValid(HotkeyMods mods, int vk)
    {
        if (vk <= 0) return false;
        // 修饰键本身不能作为主键
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return false;
        if (vk is >= 0x70 and <= 0x87) return true; // F1~F24
        return (mods & (HotkeyMods.Ctrl | HotkeyMods.Alt)) != 0;
    }

    public static string ToString(HotkeyMods mods, int vk)
    {
        var parts = new List<string>();
        if (mods.HasFlag(HotkeyMods.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyMods.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyMods.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyMods.Win)) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int vk) => vk switch
    {
        0 => "…",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "空格",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "PrtSc",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0x90 => "NumLock",
        _ => ((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk)).ToString(),
    };
}
