# 发布新版本到 GitHub（需要已登录 gh）
# 用法：scripts\publish-release.ps1 -Version 1.3.0
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repo = 'NightMark720/CtrlCpp'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$tag = "v$Version"

Write-Host "==> 构建 $Version"
& (Join-Path $root 'scripts\build.ps1')
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

Write-Host "==> 生成安装包"
& (Join-Path $root 'tools\innosetup\ISCC.exe') (Join-Path $root 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }

$setup = Join-Path $root "installer\CtrlCpp-Setup-$Version.exe"
$portable = Join-Path $root 'dist\CtrlCpp.exe'
if (-not (Test-Path $setup)) { throw "安装包不存在：$setup" }

Write-Host "==> 提交并推送代码"
Push-Location $root
try {
    & git checkout -b "release/$tag" 2>$null
    & git add --all
    & git commit -m "发布 CtrlC++ $tag" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
    & git push -u origin "release/$tag"
    & gh pr create --repo $Repo --base main --head "release/$tag" `
        --title "发布 CtrlC++ $tag" `
        --body "## 更新内容`n`n详见更新日志。`n`n🤖 Generated with [Claude Code](https://claude.com/claude-code)"
    & gh pr merge "release/$tag" --repo $Repo --squash --delete-branch `
        -t "发布 CtrlC++ $tag (#$tag)"
    & git checkout main
    & git pull --ff-only
}
finally { Pop-Location }

Write-Host "==> 上传 Release 附件"
$hash = (Get-FileHash $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$shaFile = Join-Path $root "dist\CtrlCpp.sha256"
[System.IO.File]::WriteAllText($shaFile, "$hash  CtrlCpp.exe`n")

& gh release create $tag $setup $portable $shaFile --repo $Repo --target main `
    --title "CtrlC++ $tag" `
    --notes "自动更新所需的 CtrlCpp.exe 与 CtrlCpp.sha256 已一并提供。" `
    --latest
if ($LASTEXITCODE -ne 0) { throw "Release 创建失败" }

Write-Host ""
Write-Host "完成：https://github.com/$Repo/releases/tag/$tag"
