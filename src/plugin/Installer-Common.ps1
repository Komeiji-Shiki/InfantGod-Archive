function Stop-ArchiveWebHost {
    param([Parameter(Mandatory = $true)][string]$GameRoot)
    $taskHostPath = [IO.Path]::GetFullPath((Join-Path $GameRoot 'BepInEx\plugins\InfantGodArchive\webhost\Graywill.InfantGod.WebHost.exe'))
    # 调用方已确认游戏关闭，只处理当前安装目录中本 Mod 的残留宿主。
    foreach ($taskHost in @(Get-Process -Name 'Graywill.InfantGod.WebHost' -ErrorAction SilentlyContinue)) {
        try {
            if ($taskHost.Path -ne $taskHostPath) { continue }
            Write-Output '正在关闭游戏退出后残留的资料终端后台程序…'
            $taskHost.Kill()
            if (-not $taskHost.WaitForExit(5000)) { throw '资料终端后台程序尚未退出，请稍后重试。' }
        } finally {
            $taskHost.Dispose()
        }
    }
}
