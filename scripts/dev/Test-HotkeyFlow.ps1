# 快捷键全流程测试：聚焦记事本 → Ctrl+V,V → 弹窗 → Enter 粘贴
param(
    [string]$Exe = 'D:\Claude\CtrlC++\src\CtrlCpp\bin\Release\net10.0-windows\CtrlCpp.exe',
    [string]$ClipText = '粘贴测试 ABC123'
)
$ErrorActionPreference = 'Continue'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
Add-Type -AssemblyName System.Windows.Forms

# 1. 预置剪贴板内容
Set-Clipboard -Value $ClipText
Start-Sleep -Milliseconds 1200

# 2. 启动并聚焦记事本（按标题部分匹配）
$np = Start-Process notepad -PassThru
Start-Sleep -Seconds 4
$shell = New-Object -ComObject WScript.Shell
$activated = $shell.AppActivate('记事本')
if (-not $activated) { $activated = $shell.AppActivate($np.Id) }
Start-Sleep -Milliseconds 800
$h = [W]::GetForegroundWindow()
Write-Host ("AppActivate={0} 前台窗口=0x{1:X}" -f $activated, $h.ToInt64())

# 3. 鼠标移到前台窗口中央（弹窗会出现在鼠标附近）
$r = New-Object W+RECT
[void][W]::GetWindowRect($h, [ref]$r)
$cx = [int](($r.L + $r.R) / 2); $cy = [int](($r.T + $r.B) / 2)
[void][W]::SetCursorPos($cx, $cy)
Write-Host "窗口中心: $cx,$cy"

# 4. 发送 Ctrl+V,V（第一次粘贴，第二次弹窗）
& powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\Claude\CtrlC++\scripts\dev\Send-VV.ps1' -GapMs 150
Start-Sleep -Seconds 2

# 5. 截图（应有历史弹窗）
& powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\Claude\CtrlC++\scripts\dev\Screenshot.ps1' -Path "$env:TEMP\ctrlcpp-shot-popup.png" | Out-Null

# 6. 发送 Enter → 自动粘贴回记事本
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 2

# 7. 截图（弹窗关闭，内容已粘贴）
& powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\Claude\CtrlC++\scripts\dev\Screenshot.ps1' -Path "$env:TEMP\ctrlcpp-shot-pasted.png" | Out-Null
Write-Host '测试序列完成'
