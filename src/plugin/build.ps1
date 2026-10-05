param(
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$CatalogPath,
    [string]$PreviewPath,
    [string]$PortraitPath,
    [string]$HtmlPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not $CatalogPath) { $CatalogPath = Join-Path $PSScriptRoot 'catalog.json' }
if (-not $PreviewPath) { $PreviewPath = Join-Path $PSScriptRoot 'previews' }
if (-not $PortraitPath) { $PortraitPath = Join-Path $PSScriptRoot 'portraits' }
if (-not $HtmlPath) { $HtmlPath = Join-Path $PSScriptRoot 'archive.html' }
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'dist\幼神资料终端-Mod' }
$taskManaged = Join-Path $GamePath 'Aistalt_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $taskManaged 'InfantGod.dll'))) {
    throw '没有找到幼神的 Managed 程序集，请通过 -GamePath 指定 Aistalt.exe 所在的 Build 目录。'
}
if (-not (Test-Path -LiteralPath $CatalogPath)) {
    throw '没有找到剧情 catalog.json，请通过 -CatalogPath 指定资料文件。'
}

$taskDependencies = Join-Path $PSScriptRoot 'deps'
$taskBootstrap = Join-Path $taskDependencies 'bepinex'
New-Item -ItemType Directory -Path $taskDependencies -Force | Out-Null
& python (Join-Path $PSScriptRoot 'bootstrap.py')
if ($LASTEXITCODE -ne 0) { throw '编译依赖下载未成功。' }

$taskOldCliHome = $env:DOTNET_CLI_HOME
$taskOldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$env:DOTNET_CLI_HOME = Join-Path $taskDependencies 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
try {
    & dotnet build (Join-Path $PSScriptRoot 'InfantGodCodex.csproj') -c Release "-p:GameManagedPath=$taskManaged" --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw '插件编译未成功，未更新交付包。' }
} finally {
    $env:DOTNET_CLI_HOME = $taskOldCliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $taskOldTelemetry
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskBootstrap 'BepInEx') -Destination $OutputPath -Recurse -Force
foreach ($taskName in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskBootstrap $taskName) -Destination $OutputPath -Force
}
$taskPluginOutput = Join-Path $OutputPath 'BepInEx\plugins\InfantGodArchive'
New-Item -ItemType Directory -Path $taskPluginOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Graywill.InfantGodCodex.dll') -Destination $taskPluginOutput -Force
Copy-Item -LiteralPath $CatalogPath -Destination (Join-Path $taskPluginOutput 'catalog.json') -Force
$taskChatVoices = Join-Path (Split-Path -Parent $CatalogPath) 'chat-voices.json'
if (Test-Path -LiteralPath $taskChatVoices) {
    Copy-Item -LiteralPath $taskChatVoices -Destination (Join-Path $taskPluginOutput 'chat-voices.json') -Force
}
if (Test-Path -LiteralPath $PreviewPath) {
    $taskPreviewOutput = Join-Path $taskPluginOutput 'previews'
    if ([IO.Path]::GetFullPath($PreviewPath) -ne [IO.Path]::GetFullPath($taskPreviewOutput)) {
        Copy-Item -LiteralPath $PreviewPath -Destination $taskPluginOutput -Recurse -Force
    }
}
if (Test-Path -LiteralPath $HtmlPath) {
    if ([IO.Path]::GetFullPath($HtmlPath) -ne [IO.Path]::GetFullPath((Join-Path $taskPluginOutput 'archive.html'))) {
        Copy-Item -LiteralPath $HtmlPath -Destination (Join-Path $taskPluginOutput 'archive.html') -Force
    }
}
if (Test-Path -LiteralPath $PortraitPath) {
    $taskPortraitOutput = Join-Path $taskPluginOutput 'portraits'
    if ([IO.Path]::GetFullPath($PortraitPath) -ne [IO.Path]::GetFullPath($taskPortraitOutput)) {
        Copy-Item -LiteralPath $PortraitPath -Destination $taskPluginOutput -Recurse -Force
    }
}
foreach ($taskName in @('Install-Mod.ps1','Uninstall-Mod.ps1','Installer-Common.ps1','README.md','ATTRIBUTIONS.txt','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $OutputPath -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSES') -Destination $OutputPath -Recurse -Force
$taskNativeMod = Join-Path $OutputPath 'Aistalt_Data\StreamingAssets\Mods\InfantGodArchive'
New-Item -ItemType Directory -Path $taskNativeMod -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native-mod\InfantGodArchive\mod.json') -Destination $taskNativeMod -Force
if (Test-Path -LiteralPath (Join-Path $PortraitPath 'Aistalt_Tech.png')) {
    Copy-Item -LiteralPath (Join-Path $PortraitPath 'Aistalt_Tech.png') -Destination (Join-Path $taskNativeMod 'image.png') -Force
}
Write-Output "已生成插件包：$taskPluginOutput"
