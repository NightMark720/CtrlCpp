# 回归测试：模拟按住 Ctrl+V 产生的自动重复键（不应弹窗）
$ErrorActionPreference = 'Stop'
$src = @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class HR {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    static void Key(ushort scan, bool up) {
        var i = new INPUT(); i.type = 1; i.ki.wScan = scan;
        i.ki.dwFlags = (uint)(0x0008 | (up ? 0x0002 : 0));
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    // 按住 Ctrl+V 约 700ms，期间模拟 2 次系统自动重复
    public static void HoldPaste() {
        Key(0x1D, false); Thread.Sleep(20);
        Key(0x2F, false); Thread.Sleep(500);
        Key(0x2F, false); Thread.Sleep(40);   // 自动重复
        Key(0x2F, false); Thread.Sleep(100);  // 自动重复
        Key(0x2F, true); Thread.Sleep(20);
        Key(0x1D, true);
    }
}
"@
Add-Type -TypeDefinition $src
Start-Sleep -Milliseconds 300
[HR]::HoldPaste()
Write-Host '已发送按住 Ctrl+V（含自动重复）'
