$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WT {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] p, int cb);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Wheel(int delta) {
        var i = new INPUT(); i.type = 0;
        i.mi.mouseData = unchecked((uint)delta);
        i.mi.dwFlags = 0x0800;
        SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }
    public static IntPtr FindHistory() {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            var t = new StringBuilder(256);
            GetWindowText(h, t, t.Capacity);
            if (t.ToString() == "历史记录") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
if (-not (Get-Process CtrlCpp -ErrorAction SilentlyContinue)) { Start-Process $exe; Start-Sleep -Seconds 5 }
for ($i = 1; $i -le 12; $i++) { Set-Clipboard -Value "方向测试-$i"; Start-Sleep -Milliseconds 200 }
Start-Sleep -Seconds 2
& $exe --history | Out-Null
Start-Sleep -Seconds 2
$h = [WT]::FindHistory()
if ($h -eq [IntPtr]::Zero) { throw 'History window not found' }
$r = New-Object WT+RECT
[WT]::GetWindowRect($h, [ref]$r) | Out-Null
[WT]::SetCursorPos([int](($r.Left + $r.Right)/2), [int](($r.Top + $r.Bottom)/2)) | Out-Null
Start-Sleep -Milliseconds 400

function Get-Offset {
    param([IntPtr]$Handle)
    $ai = Add-Type -AssemblyName UIAutomationClient -PassThru
    $ae = [System.Windows.Automation.AutomationElement]::FromHandle($Handle)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
    $list = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $list) { return -1 }
    $sp = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    return [double]$sp.Current.VerticalScrollPercent
}

$before = Get-Offset $h
# 注入与原生探针相同的方向：raw +120（WPF 报告 e.Delta=-120）
[WT]::Wheel(120) | Out-Null
Start-Sleep -Milliseconds 900
$after = Get-Offset $h
Write-Host "offsetPercent before=$before after=$after"
if ($after -le $before) { throw "Scroll direction does not match native WPF (expected offset to increase)" }
Write-Host 'Scroll direction matches native WPF: PASS'
