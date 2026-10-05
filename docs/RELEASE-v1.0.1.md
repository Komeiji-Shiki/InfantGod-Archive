# 幼神资料终端 v1.0.1

新增独立的一键安装程序，使用与游戏一致的像素字体、奶黄色背景、深灰标题栏、橙色按钮和爱式塔·技术头像。

## 推荐下载

**`InfantGod-Archive-Setup-v1.0.1.exe`**：所有 Mod 文件已嵌入 EXE。双击运行，确认自动找到的游戏目录后点击安装即可，不需要解压或输入 PowerShell 命令。

安装器支持自动寻找 Steam 游戏目录、手动选择 `Aistalt.exe`、安装、更新、卸载和恢复原文件。旧版本通过脚本安装的记录可以继续使用；更新会保留首次安装前的备份。

安装后启动游戏，在“设置 → Mod 管理器”启用“幼神资料终端”，按 F8 或点击游戏桌面的资料终端图标打开。

## 其他下载

- `InfantGod-Archive-Mod-v1.0.1.zip`：手动安装包。将包内文件复制到 `Aistalt.exe` 所在的 `Build` 目录，合并文件夹，或运行随包安装脚本。
- `InfantGod-Archive.html`：单文件离线资料页，浏览器打开即可。
- `InfantGod-Archive-Source-v1.0.1.zip`：本版完整源码，包含安装器、资料终端和宣传片制作文件。

完整安装包文件夹不能直接作为原生 Mod 放进 `StreamingAssets/Mods`。游戏只会识别安装程序放置到 `Mods/InfantGodArchive` 的入口。

修复资料页后台进程可能在游戏退出后残留、导致更新文件被占用的问题。网页宿主现在直接跟随游戏进程退出；安装和卸载时也会清理当前游戏目录里本 Mod 的残留宿主。安装器输出正常中文信息，不再显示 PowerShell 的 CLIXML 内部记录。

本版保留 v1.0.0 的游戏内功能；[71 秒功能宣传片与字幕](https://github.com/Komeiji-Shiki/InfantGod-Archive/releases/tag/v1.0.0)继续提供下载。

适用于 Windows x64 的《幼神 Demo》，需要 .NET Framework 4.8 与 Microsoft Edge WebView2 Runtime。未检测到网页运行组件时，安装器提供微软官方下载入口。

原作《幼神》，作者 colsalley。原创程序代码与配乐采用 MIT；原作剧情与资产及对应整理资料采用 CC BY-NC-SA 4.0；Fusion Pixel 字体采用 OFL 1.1。完整署名和第三方许可保留在源码及安装包内。
