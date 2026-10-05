param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'

$taskGameRoot = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$taskRootPrefix = $taskGameRoot + '\'
if (-not (Test-Path -LiteralPath (Join-Path $taskGameRoot 'Aistalt.exe'))) { throw '请指定包含 Aistalt.exe 的幼神 Build 目录。' }
if (Get-Process -Name 'Aistalt' -ErrorAction SilentlyContinue) { throw '请先关闭幼神，再重新运行卸载脚本。' }
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
Stop-ArchiveWebHost -GameRoot $taskGameRoot
$taskStatePath = Join-Path $taskGameRoot '.InfantGodArchive-install.json'
if (-not (Test-Path -LiteralPath $taskStatePath)) { throw '没有找到本安装脚本的记录，未改动游戏文件。' }
$taskState = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($taskState.status -eq 'Uninstalled') { Write-Output '资料终端已卸载。'; exit 0 }
$taskBackupRoot = [IO.Path]::GetFullPath([string]$taskState.backupRoot)
if (-not $taskBackupRoot.StartsWith($taskRootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定的游戏目录。' }
foreach ($taskRecord in $taskState.files) {
    $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskGameRoot ([string]$taskRecord.path)))
    if (-not $taskDestination.StartsWith($taskRootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '恢复目标超出了指定的游戏目录。' }
    if ($taskRecord.existed) {
        $taskBackup = [IO.Path]::GetFullPath((Join-Path $taskBackupRoot ([string]$taskRecord.path)))
        if (-not $taskBackup.StartsWith($taskBackupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '备份文件路径无效。' }
        if (-not (Test-Path -LiteralPath $taskBackup)) { throw "缺少原文件备份：$taskBackup" }
        Copy-Item -LiteralPath $taskBackup -Destination $taskDestination -Force
    } elseif (Test-Path -LiteralPath $taskDestination) {
        Remove-Item -LiteralPath $taskDestination -Force
    }
}
$taskState.status = 'Uninstalled'
$taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
Write-Output '资料终端已卸载，安装前已有的文件已恢复。'
Write-Output '导出的 progress.json、BepInEx 的配置/日志和回退备份均保留。'
Write-Output "备份目录：$taskBackupRoot"
