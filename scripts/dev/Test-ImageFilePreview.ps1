$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
$dir = Join-Path $env:TEMP 'ctrlcpp-image-file-preview'
New-Item -ItemType Directory -Force $dir | Out-Null
Add-Type -AssemblyName System.Drawing

$jpg = New-Object System.Drawing.Bitmap 640, 400
$g = [System.Drawing.Graphics]::FromImage($jpg)
$g.Clear([System.Drawing.Color]::FromArgb(230, 85, 60))
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 110, 230))
$g.FillRectangle($brush, 180, 90, 280, 220)
$g.Dispose(); $brush.Dispose()
$jpgPath = Join-Path $dir 'preview-test.jpg'
$jpg.Save($jpgPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$jpg.Dispose()

$png = New-Object System.Drawing.Bitmap 640, 400
$g = [System.Drawing.Graphics]::FromImage($png)
$g.Clear([System.Drawing.Color]::FromArgb(35, 125, 105))
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 200, 60))
$g.FillEllipse($brush, 200, 80, 240, 240)
$g.Dispose(); $brush.Dispose()
$pngPath = Join-Path $dir 'preview-test.png'
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

if (-not (Get-Process CtrlCpp -ErrorAction SilentlyContinue)) { Start-Process $exe; Start-Sleep -Seconds 5 }
Set-Clipboard -Path $jpgPath
Start-Sleep -Milliseconds 1500
Set-Clipboard -Path $pngPath
Start-Sleep -Milliseconds 1500
& $exe --dump 2 --out "$dir\dump.json" | Out-Null
$data = Get-Content "$dir\dump.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$data.items | ForEach-Object { Write-Host "Kind=$($_.Kind) Preview=$($_.Preview) Files=$($_.FileListJson)" }
if (@($data.items | Where-Object { $_.Kind -ne 3 }).Count -ne 0) { throw 'Expected file records' }
& $exe --history | Out-Null
Start-Sleep -Seconds 2
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$dir\preview.png" | Out-Null
Write-Host "Screenshot: $dir\preview.png"