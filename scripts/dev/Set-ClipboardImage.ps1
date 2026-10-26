# 向剪贴板写入一张测试图片（STA）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore

$w = 320; $h = 240
$bmp = New-Object System.Windows.Media.Imaging.WriteableBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Bgr32, $null)
$stride = $w * 4
$pixels = New-Object byte[] ($stride * $h)
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $i = $y * $stride + $x * 4
        if ($x -gt 60 -and $x -lt 200 -and $y -gt 40 -and $y -lt 160) {
            $pixels[$i] = 30; $pixels[$i + 1] = 80; $pixels[$i + 2] = 240
        }
        else {
            $pixels[$i] = 60; $pixels[$i + 1] = 60; $pixels[$i + 2] = 40
        }
    }
}
$rect = New-Object System.Windows.Int32Rect(0, 0, $w, $h)
$bmp.WritePixels($rect, $pixels, $stride, 0)
for ($i = 0; $i -lt 8; $i++) {
    try { [System.Windows.Clipboard]::SetImage($bmp); break }
    catch { Start-Sleep -Milliseconds 300 }
}
Write-Host "已写入测试图片 ${w}x${h}"
