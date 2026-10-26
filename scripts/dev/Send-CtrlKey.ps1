# 模拟按下 Ctrl+某键（扫描码方式，用于自动化测试快捷键）
param([int]$KeyScan = 0x30)  # 0x30 = B
$ErrorActionPreference = 'Stop'
$src = @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class CK {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    static void Key(ushort scan, bool up) {
        var i = new INPUT(); i.type = 1; i.ki.wScan = scan;
        i.ki.dwFlags = (uint)(0x0008 | (up ? 0x0002 : 0));
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void Press(int keyScan) {
        Key(0x1D, false); Thread.Sleep(30);
        Key((ushort)keyScan, false); Thread.Sleep(30);
        Key((ushort)keyScan, true); Thread.Sleep(30);
        Key(0x1D, true);
    }
}
"@
Add-Type -TypeDefinition $src
Start-Sleep -Milliseconds 300
[CK]::Press($KeyScan)
Write-Host "已发送 Ctrl+扫描码 $KeyScan"
