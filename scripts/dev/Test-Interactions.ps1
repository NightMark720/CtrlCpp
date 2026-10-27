$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class InteractionNative {
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
}
"@
Get-Process CtrlCpp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Start-Process $exe
Start-Sleep -Seconds 5
Set-Clipboard -Value 'drag-click-test'
Start-Sleep -Milliseconds 1500
$notepad = Start-Process "$env:WINDIR\System32\notepad.exe" -PassThru
Start-Sleep -Seconds 3
& $exe --history | Out-Null
Start-Sleep -Seconds 2
$h = [InteractionNative]::FindHistoryWindow()
if ($h -eq [IntPtr]::Zero) { throw 'History window was not found' }

function Get-PopupItems {
    $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    return $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond).Count
}
$before = Get-PopupItems
if ($before -lt 1) { throw 'No list items before search' }
$popupElement = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$editCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$searchElement = $popupElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
if ($null -eq $searchElement) { throw 'Search box not found' }
$searchElement.SetFocus()
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('zzzz-no-match')
Start-Sleep -Milliseconds 800
$filtered = Get-PopupItems
if ($filtered -ne 0) { throw "Search filter failed: $filtered items remain" }
Write-Host 'Search filtering PASS'
$searchElement.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
Start-Sleep -Milliseconds 800
if ((Get-PopupItems) -lt 1) { throw 'Clear search did not restore items' }

$rect = New-Object InteractionNative+RECT
[InteractionNative]::GetWindowRect($h, [ref]$rect) | Out-Null
$x = $rect.Left + 160
$y = $rect.Top + 125
[InteractionNative]::SetCursorPos($x, $y) | Out-Null
[InteractionNative]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 80
[InteractionNative]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Seconds 2
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-click-paste.png" | Out-Null
Write-Host 'Click completed; inspect screenshot for pasted text'
Stop-Process -Id $notepad.Id -Force -ErrorAction SilentlyContinue