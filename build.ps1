param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$OutputPath=(Join-Path $PSScriptRoot 'dist\InfantGod-Archive-Mod')
)
$ErrorActionPreference='Stop'

# 使用仓库内的资料快照构建，原作程序集只从玩家的本机安装目录引用。
$taskHtml=Join-Path $PSScriptRoot 'dist\archive.html'
& python (Join-Path $PSScriptRoot 'tools\build_web.py') --output $taskHtml
if ($LASTEXITCODE -ne 0) { throw '离线资料页生成失败。' }
& (Join-Path $PSScriptRoot 'src\plugin\build.ps1') `
    -GamePath $GamePath `
    -CatalogPath (Join-Path $PSScriptRoot 'data\catalog.json') `
    -PreviewPath (Join-Path $PSScriptRoot 'assets\cg') `
    -PortraitPath (Join-Path $PSScriptRoot 'assets\portraits') `
    -HtmlPath $taskHtml `
    -OutputPath $OutputPath
if ($LASTEXITCODE -ne 0) { throw '插件生成失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $OutputPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\OFL-1.1.txt') -Destination (Join-Path $OutputPath 'LICENSES\FusionPixel-OFL.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\web\vendor\LICENSE') -Destination (Join-Path $OutputPath 'LICENSES\Dagre-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\web\vendor\dagre-NOTICES.txt') -Destination (Join-Path $OutputPath 'LICENSES\Dagre-NOTICES.txt') -Force
Write-Output "资料终端完整发布目录：$OutputPath"
