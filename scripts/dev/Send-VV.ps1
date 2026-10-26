# 模拟按住 Ctrl 后连按两次 V（用于自动化测试快捷键）
param([int]$GapMs = 150)
$ErrorActionPreference = 'Stop'
$src = @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class VI {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    static void Key(ushort scan, bool up) {
        var i = new INPUT(); i.type = 1; i.ki.wScan = scan;
        i.ki.dwFlags = (uint)(0x0008 | (up ? 0x0002 : 0));
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void VV(int gap) {
        Key(0x1D, false); Thread.Sleep(15);
        Key(0x2F, false); Thread.Sleep(15);
        Key(0x2F, true);
        Thread.Sleep(gap);
        Key(0x2F, false); Thread.Sleep(15);
        Key(0x2F, true); Thread.Sleep(15);
        Key(0x1D, true);
    }
}
"@
Add-Type -TypeDefinition $src
Start-Sleep -Milliseconds 300
[VI]::VV($GapMs)
Write-Host "已发送 Ctrl+V,V (间隔 $GapMs ms)"
