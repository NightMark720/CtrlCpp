$ErrorActionPreference = 'Stop'
$root = 'D:\Claude\CtrlC++'
$exe = Join-Path $root 'dist\CtrlCpp.exe'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName System.Drawing
if (-not (Get-Process CtrlCpp -ErrorAction SilentlyContinue)) { Start-Process $exe; Start-Sleep -Seconds 5 }

$width = 320; $height = 200
$pixels = New-Object byte[] ($width * $height * 4)
for ($y = 0; $y -lt $height; $y++) {
    for ($x = 0; $x -lt $width; $x++) {
        $i = ($y * $width + $x) * 4
        $inside = $x -ge 100 -and $x -lt 220 -and $y -ge 50 -and $y -lt 150
        $pixels[$i] = 40
        $pixels[$i + 1] = $(if ($inside) { 190 } else { 70 })
        $pixels[$i + 2] = $(if ($inside) { 40 } else { 230 })
        $pixels[$i + 3] = 0
    }
}
$source = [System.Windows.Media.Imaging.BitmapSource]::Create(
    $width, $height, 96, 96,
    [System.Windows.Media.PixelFormats]::Bgra32, $null, $pixels, $width * 4)
$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($source))
$ms = New-Object System.IO.MemoryStream
$encoder.Save($ms)
$bytes = $ms.ToArray(); $ms.Dispose()
[System.Windows.Clipboard]::SetData('PNG', (New-Object System.IO.MemoryStream(,$bytes)))
Start-Sleep -Seconds 2

$out = Join-Path $env:TEMP 'alpha-dump.json'
& $exe --dump 1 --out $out | Out-Null
$item = (Get-Content $out -Raw -Encoding UTF8 | ConvertFrom-Json).items[0]
if ($item.Kind -ne 2) { throw 'Expected image record' }
$media = Join-Path "$env:LOCALAPPDATA\CtrlCpp\media" ([System.IO.Path]::GetFileName($item.MediaPath))
$bitmap = New-Object System.Drawing.Bitmap $media
$pixel = $bitmap.GetPixel(10, 10)
$bitmap.Dispose()
Write-Host "Stored pixel: A=$($pixel.A) R=$($pixel.R) G=$($pixel.G) B=$($pixel.B)"
if ($pixel.A -ne 255 -or $pixel.R -lt 200) { throw 'Fully transparent image was not repaired' }
Write-Host 'Alpha repair PASS'
& $exe --history | Out-Null
Start-Sleep -Seconds 2
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\dev\Screenshot.ps1" -Path "$env:TEMP\ctrlcpp-alpha-fixed.png" | Out-Null