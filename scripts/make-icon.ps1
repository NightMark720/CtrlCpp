# 生成 Assets\app.ico（黑底圆角 + 白色 C++ + 蓝色下划线）
# 全部使用 BMP 条目（GDI+ Icon 类不支持 PNG 压缩条目，全 BMP 兼容性最好）
param([string]$OutPath = "$PSScriptRoot\..\src\CtrlCpp\Assets\app.ico")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)

$dir = Split-Path $OutPath
New-Item -ItemType Directory -Force $dir | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [Math]::Max(2, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 13, 13, 13))
    $g.FillPath($brush, $path)

    $font = New-Object System.Drawing.Font('Segoe UI', ($size * 0.40), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = [System.Drawing.Brushes]::White
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF(0, (-$size * 0.05), $size, ($size * 0.88))
    $g.DrawString('C++', $font, $white, $rect, $sf)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 76, 194, 255), [Math]::Max(1, $size * 0.06))
    $y = $size * 0.86
    $g.DrawLine($pen, ($size * 0.24), $y, ($size * 0.76), $y)

    $g.Dispose()
    return $bmp
}

function New-BmpEntryBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $xorLen = $w * $h * 4
    $xor = New-Object byte[] $xorLen
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $xor, 0, $xorLen)
    $bmp.UnlockBits($data)
    $andRow = 4 * [Math]::Ceiling([Math]::Ceiling($w / 8.0) / 4.0)
    $andLen = [int]($andRow * $h)
    $and = New-Object byte[] $andLen
    $biSizeImage = $xorLen + $andLen

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)             # biSize
    $bw.Write([Int32]$w)              # biWidth
    $bw.Write([Int32]($h * 2))        # biHeight (XOR + AND)
    $bw.Write([UInt16]1)              # biPlanes
    $bw.Write([UInt16]32)             # biBitCount
    $bw.Write([UInt32]0)              # biCompression = BI_RGB
    $bw.Write([UInt32]$biSizeImage)
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    $bw.Write($xor)
    $bw.Write($and)
    $bw.Flush()
    return ,$ms.ToArray()
}

$entries = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    [void]$entries.Add(@($s, (New-BmpEntryBytes $bmp)))
    $bmp.Dispose()
}

$count = $entries.Count
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$count)
$offset = 6 + 16 * $count
foreach ($e in $entries) {
    $s = $e[0]
    $b = $e[1]
    $sizeByte = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$sizeByte); $bw.Write([Byte]$sizeByte)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$b.Length)
    $bw.Write([UInt32]$offset)
    $offset += $b.Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())

# 验证：GDI 能正确加载
$ico = New-Object System.Drawing.Icon($OutPath)
if ($null -eq $ico) { throw "ICO load failed" }
$bmpTest = $ico.ToBitmap()
Write-Host "ICO OK: $OutPath ($($out.Length) bytes, loaded $($bmpTest.Width)x$($bmpTest.Height))"
$bmpTest.Dispose()
$ico.Dispose()
