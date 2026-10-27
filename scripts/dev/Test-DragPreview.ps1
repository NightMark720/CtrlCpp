$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DragTestNative {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public static IntPtr FindHistoryWindow() {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            var title = new System.Text.StringBuilder(256);
            GetWindowText(h, title, title.Capacity);
            if (title.ToString() == "历史记录") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
if (-not (Get-Process CtrlCpp -ErrorAction SilentlyContinue)) { Start-Process $exe; Start-Sleep -Seconds 5 }
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Set-ClipboardImage.ps1" | Out-Null
Start-Sleep -Seconds 2
[DragTestNative]::SetCursorPos(850, 350) | Out-Null
& $exe --history | Out-Null
Start-Sleep -Seconds 2
$h = [DragTestNative]::FindHistoryWindow()
if ($h -eq [IntPtr]::Zero) { throw 'History window was not found' }
$before = New-Object DragTestNative+RECT
[DragTestNative]::GetWindowRect($h, [ref]$before) | Out-Null
Write-Host "Before drag: $($before.Left),$($before.Top) - $($before.Right),$($before.Bottom)"
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-preview.png" | Out-Null

$x = $before.Left + 45
$y = $before.Bottom - 12
[DragTestNative]::SetCursorPos($x, $y) | Out-Null
[DragTestNative]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
for ($i = 1; $i -le 12; $i++) {
    [DragTestNative]::SetCursorPos($x + [int](220 * $i / 12), $y + [int](140 * $i / 12)) | Out-Null
    Start-Sleep -Milliseconds 30
}
[DragTestNative]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 700
$after = New-Object DragTestNative+RECT
[DragTestNative]::GetWindowRect($h, [ref]$after) | Out-Null
Write-Host "After drag: $($after.Left),$($after.Top) - $($after.Right),$($after.Bottom)"
$dx = $after.Left - $before.Left
$dy = $after.Top - $before.Top
if ([Math]::Abs($dx - 220) -gt 8 -or [Math]::Abs($dy - 140) -gt 8) { throw "Drag did not move as expected: $dx,$dy" }
Write-Host 'Drag test PASS'
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-after-drag.png" | Out-Null

# 关闭后换一个鼠标位置重新打开，验证窗口会重新跟随鼠标
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 700
[DragTestNative]::SetCursorPos(500, 300) | Out-Null
& $exe --history | Out-Null
Start-Sleep -Seconds 2
$reopened = New-Object DragTestNative+RECT
[DragTestNative]::GetWindowRect($h, [ref]$reopened) | Out-Null
Write-Host "Reopened: $($reopened.Left),$($reopened.Top)"
if ([Math]::Abs($reopened.Left - 512) -gt 8 -or [Math]::Abs($reopened.Top - 312) -gt 8) { throw 'Reopen positioning failed' }
Write-Host 'Reopen positioning PASS'
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-reopened.png" | Out-Null