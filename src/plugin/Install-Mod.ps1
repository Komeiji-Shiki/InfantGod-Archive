param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'

$taskGameRoot = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$taskRootPrefix = $taskGameRoot + '\'
if (-not (Test-Path -LiteralPath (Join-Path $taskGameRoot 'Aistalt.exe'))) { throw '请指定包含 Aistalt.exe 的幼神 Build 目录。' }
if (Get-Process -Name 'Aistalt' -ErrorAction SilentlyContinue) { throw '请先关闭幼神，再重新运行安装脚本。' }
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
Stop-ArchiveWebHost -GameRoot $taskGameRoot
$taskStatePath = Join-Path $taskGameRoot '.InfantGodArchive-install.json'
$taskExisting = $null
if (Test-Path -LiteralPath $taskStatePath) {
    $taskExisting = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($taskExisting.status -eq 'Uninstalled') { $taskExisting = $null }
}

$taskPackageRoot = $PSScriptRoot
$taskPluginRelative = 'BepInEx\plugins\InfantGodArchive'
$taskSourcePlugin = Join-Path $taskPackageRoot $taskPluginRelative
if (-not (Test-Path -LiteralPath (Join-Path $taskSourcePlugin 'Graywill.InfantGodCodex.dll'))) { throw '安装包缺少资料终端 DLL。' }
$taskCorePath = Join-Path $taskGameRoot 'BepInEx\core\BepInEx.dll'
$taskAlreadyHasLoader = Test-Path -LiteralPath $taskCorePath
if ($taskAlreadyHasLoader) {
    $taskVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskCorePath)
    if ($taskVersion.FileMajorPart -ne 5) { throw '当前游戏安装了其他主版本的 BepInEx。此包需要 BepInEx 5，请先处理加载器版本。' }
}

$taskFiles = [Collections.Generic.List[string]]::new()
foreach ($taskFile in (Get-ChildItem -LiteralPath $taskSourcePlugin -Recurse -File)) {
    $taskFiles.Add($taskPluginRelative + '\' + $taskFile.FullName.Substring($taskSourcePlugin.Length + 1))
}
$taskNativeRelative = 'Aistalt_Data\StreamingAssets\Mods\InfantGodArchive'
$taskSourceNative = Join-Path $taskPackageRoot $taskNativeRelative
foreach ($taskFile in (Get-ChildItem -LiteralPath $taskSourceNative -Recurse -File)) {
    $taskFiles.Add($taskNativeRelative + '\' + $taskFile.FullName.Substring($taskSourceNative.Length + 1))
}
$taskOwnsLoader = -not $taskAlreadyHasLoader
if ($taskExisting) { $taskOwnsLoader = -not $taskExisting.reusedBepInEx }
if ($taskOwnsLoader) {
    foreach ($taskFile in (Get-ChildItem -LiteralPath (Join-Path $taskPackageRoot 'BepInEx\core') -Recurse -File)) {
        $taskFiles.Add('BepInEx\core\' + $taskFile.FullName.Substring((Join-Path $taskPackageRoot 'BepInEx\core').Length + 1))
    }
    foreach ($taskRelative in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) { $taskFiles.Add($taskRelative) }
}

$taskBackupRoot = Join-Path $taskGameRoot ('.InfantGodArchive-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$taskRecords = [Collections.Generic.List[object]]::new()
$taskRecorded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($taskExisting) {
    $taskBackupRoot = [IO.Path]::GetFullPath([string]$taskExisting.backupRoot)
    if (-not $taskBackupRoot.StartsWith($taskRootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定的游戏目录。' }
    # 更新或继续中断的安装时，沿用首次安装前的备份，不能用旧版 Mod 覆盖原文件。
    foreach ($taskRecord in $taskExisting.files) {
        $taskRecords.Add($taskRecord)
        $taskRecorded.Add([string]$taskRecord.path) | Out-Null
    }
}
$taskState = [ordered]@{ status='Installing'; version='1.0.3'; gameRoot=$taskGameRoot; backupRoot=$taskBackupRoot; reusedBepInEx=(-not $taskOwnsLoader); files=$taskRecords }
New-Item -ItemType Directory -Path $taskBackupRoot -Force | Out-Null
try {
    foreach ($taskRelative in $taskFiles) {
        $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskGameRoot $taskRelative))
        if (-not $taskDestination.StartsWith($taskRootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '安装目标超出了指定的游戏目录。' }
        if (-not $taskRecorded.Contains($taskRelative)) {
            $taskHadOriginal = Test-Path -LiteralPath $taskDestination
            if ($taskHadOriginal) {
                $taskBackup = Join-Path $taskBackupRoot $taskRelative
                New-Item -ItemType Directory -Path (Split-Path -Parent $taskBackup) -Force | Out-Null
                Copy-Item -LiteralPath $taskDestination -Destination $taskBackup -Force
            }
            $taskRecords.Add([ordered]@{ path=$taskRelative; existed=$taskHadOriginal })
            $taskRecorded.Add($taskRelative) | Out-Null
        }
        # 每一步先记录回退信息，即使复制中断也可以运行卸载脚本恢复。
        $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskPackageRoot $taskRelative) -Destination $taskDestination -Force
    }
    $taskState.status = 'Installed'
    $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
} catch {
    Write-Warning '安装未完成。已记录已执行步骤，可以用 Uninstall-Mod.ps1 恢复。'
    throw
}
Write-Output "资料终端已安装：$taskGameRoot"
Write-Output '启动游戏，先在“设置 → Mod 管理器”中启用“幼神资料终端”。'
Write-Output '启用后可点击主菜单右上角入口、游戏桌面的“资料终端”，或按 F8 打开。'
Write-Output '第一次打开时选择已探索内容或全剧透资料，游戏内会自动同步当前存档进度。'
Write-Output "原文件备份：$taskBackupRoot"
