# 发送 Win+B（聚焦系统托盘区，会强制显示任务栏）
$ErrorActionPreference = 'Stop'
$src = @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class WB {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    static void KeyVk(ushort vk, bool up) {
        var i = new INPUT(); i.type = 1; i.ki.wVk = vk;
        i.ki.dwFlags = (uint)(up ? 0x0002 : 0);
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static void WinB() {
        KeyVk(0x5B, false); Thread.Sleep(50);
        KeyVk(0x42, false); Thread.Sleep(50);
        KeyVk(0x42, true); Thread.Sleep(50);
        KeyVk(0x5B, true);
    }
}
"@
Add-Type -TypeDefinition $src
[WB]::WinB()
Start-Sleep -Milliseconds 1500
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $vs.Width, 200
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($vs.Left, $vs.Bottom - 200, 0, 0, $bmp.Size)
$g.Dispose(); $bmp.Save("$env:TEMP\tray-winb.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Host "已发送 Win+B 并截图"