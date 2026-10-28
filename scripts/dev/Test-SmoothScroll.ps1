$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SmoothScrollNative {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RECT { public int Left, Top, Right, Bottom; }
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
    public static void Wheel(int delta) { mouse_event(0x0800, 0, 0, (uint)delta, IntPtr.Zero); }
}
"@
if (-not (Get-Process CtrlCpp -ErrorAction SilentlyContinue)) { Start-Process $exe; Start-Sleep -Seconds 5 }
for ($i = 1; $i -le 10; $i++) {
    Set-Clipboard -Value "scroll-test-item-$i"
    Start-Sleep -Milliseconds 250
}
Start-Sleep -Seconds 2
& $exe --history | Out-Null
Start-Sleep -Seconds 2
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 20 -and $h -eq [IntPtr]::Zero; $i++) {
    $h = [SmoothScrollNative]::FindHistoryWindow()
    if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500 }
}
if ($h -eq [IntPtr]::Zero) { throw 'History window not found' }
$rect = New-Object SmoothScrollNative+RECT
[SmoothScrollNative]::GetWindowRect($h, [ref]$rect) | Out-Null
[SmoothScrollNative]::SetCursorPos($rect.Left + 180, $rect.Top + 160) | Out-Null
$window = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$listCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
$list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCondition)
if ($null -eq $list) { throw 'List not found' }
$scroll = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
$p0 = $scroll.Current.VerticalScrollPercent
Write-Host "Before: $p0"
[SmoothScrollNative]::Wheel(-120)
Start-Sleep -Milliseconds 35
$p1 = $scroll.Current.VerticalScrollPercent
Write-Host "During 35ms: $p1"
Start-Sleep -Milliseconds 60
$p2 = $scroll.Current.VerticalScrollPercent
Write-Host "During 95ms: $p2"
Start-Sleep -Milliseconds 350
$p3 = $scroll.Current.VerticalScrollPercent
Write-Host "Final: $p3"
if ($p3 -le $p0) { throw 'Wheel did not scroll the list' }
if ($p3 -le $p1) { throw 'No progressive movement detected; it jumped directly to final offset' }
Write-Host 'Smooth wheel scrolling PASS'
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-smooth-scroll.png" | Out-Null