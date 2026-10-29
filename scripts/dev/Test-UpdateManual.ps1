$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class UM {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr FindSettings() {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            var t = new StringBuilder(256);
            GetWindowText(h, t, t.Capacity);
            if (t.ToString() == "CtrlC++ 设置") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
& $exe --settings | Out-Null
Start-Sleep -Seconds 4
$h = [UM]::FindSettings()
if ($h -eq [IntPtr]::Zero) { throw 'Settings not found' }
[UM]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '检查更新')
$btn = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($null -eq $btn) { throw 'Check button not found' }
Write-Host "button found: $($btn.Current.Name)"
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 25
$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
foreach ($t in $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)) {
    $n = $t.Current.Name
    if ($n -match '最新版本|新版本|检查失败|正在检查') { Write-Host "STATUS: $n" }
}
$r = New-Object UM+RECT
[UM]::GetWindowRect($h, [ref]$r) | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\upd-manual.png" | Out-Null
