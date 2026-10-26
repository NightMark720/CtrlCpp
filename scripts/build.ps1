# 构建 CtrlCpp.exe 到 dist\
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

& $dotnet publish (Join-Path $root 'src\CtrlCpp\CtrlCpp.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o (Join-Path $root 'dist')
if ($LASTEXITCODE -ne 0) { throw "publish 失败 ($LASTEXITCODE)" }

Copy-Item (Join-Path $root 'README-使用说明.txt') (Join-Path $root 'dist') -Force
Write-Host ''
Write-Host '构建完成：dist\CtrlCpp.exe'
