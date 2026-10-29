$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
& $exe --settings | Out-Null
Start-Sleep -Seconds 3
$rootElement = [System.Windows.Automation.AutomationElement]::RootElement
$pidValue = (Get-Process CtrlCpp | Select-Object -First 1).Id
$pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidValue)
$nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'CtrlC++ 设置')
$andCondition = New-Object System.Windows.Automation.AndCondition($pidCondition, $nameCondition)
$window = $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $andCondition)
if ($null -eq $window) { throw 'Settings window not found' }
$buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '检查更新')
$button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
if ($null -eq $button) { throw 'Check update button not found' }
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$textCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$status = ''
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    foreach ($text in $texts) {
        $name = $text.Current.Name
        if ($name -match '当前已是最新版本|发现新版本|检查失败') { $status = $name; break }
    }
    if ($status.Length -gt 0) { break }
}
Write-Host "Update status: $status"
if ($status -notmatch '当前已是最新版本|发现新版本') { throw "Unexpected update result: $status" }
Write-Host 'Update check PASS'
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-update-check.png" | Out-Null
$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()