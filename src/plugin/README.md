# 幼神资料终端

把爱式塔·技术整理的剧情资料放进游戏桌面。窗口使用游戏原生的标题栏、拖动、最小化、最大化和任务栏，内容通过 WebView2 显示与离线资料终端相同的 HTML 界面，支持搜索、人物分支图、协议、记忆、成就与 CG。游戏内自动同步当前存档进度，首次打开可选择已探索内容或全剧透资料。

原作《幼神》（Infant God）由 colsalley 制作。

## 安装

推荐从发布页下载独立的 `InfantGod-Archive-Setup-v1.1.0.exe`。它自带全部安装文件，双击后选择游戏目录并点击安装即可，也支持更新和卸载。

这个 ZIP 是手动安装包，不能把外层文件夹直接放进 `StreamingAssets/Mods`。手动安装时，将包内的 `BepInEx`、`Aistalt_Data`、`winhttp.dll`、`doorstop_config.ini` 和 `.doorstop_version` 复制到 `Aistalt.exe` 所在的 `Build` 目录并合并文件夹。需要自动备份时，关闭游戏，在解压目录用 PowerShell 执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -GamePath '你的幼神 Demo\Build'
```

`GamePath` 是包含 `Aistalt.exe` 的目录。安装脚本会为覆盖的文件创建备份，已有 BepInEx 5 时复用加载器；更新或继续中断的安装时，保留首次安装前的备份。

安装完成后，启动游戏，在 **设置 → Mod 管理器** 中启用 **幼神资料终端**。随后可点击主菜单右上角的资料入口、进入存档后双击 OS 桌面的 **资料终端**，或按 **F8** 打开。标题栏可拖动，`_` 最小化，`□` 最大化或还原，`×` 关闭；F8 也可关闭当前资料窗。

正式 Mod 条目位于 `Aistalt_Data/StreamingAssets/Mods/InfantGodArchive/mod.json`。在 Mod 管理器中停用资料终端后，窗口关闭，菜单入口和桌面快捷方式移除；重新启用后恢复。

运行需要 Windows x64、系统的 .NET Framework 4.8 和 **Microsoft Edge WebView2 Runtime**。安装包已包含网页宿主程序与 SDK 组件；缺少运行时时，可从 [微软官方 WebView2 页面](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)安装 Evergreen Runtime。

请保留本安装包的 `Uninstall-Mod.ps1` 和 `Installer-Common.ps1`，需要回退时关闭游戏并执行，或直接使用图形安装器中的卸载按钮：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall-Mod.ps1 -GamePath '你的幼神 Demo\Build'
```

卸载会按安装记录恢复原文件或移除新增文件，并保留进度导出、配置、浏览器资料、日志和备份。

## 爱式塔自由交谈与本机 MCP

打开原作设置中的 **LLM** 页，自行填写 API 地址、模型名称、Key 和协议。支持 Chat Completions 与 Responses，基础页与高级参数页可填写采样参数、推理强度等请求 JSON 和自定义请求头。进入存档后按 **F9**，在原作爱式塔窗口中打字交流；模型可使用电脑界面、配装与已探索资料工具。点击“停止”或按 Esc 取消当前操作，发送新指令会中断旧请求。

在 **LLM → 本机 MCP** 启用并保存后，可复制 Codex 命令或通用 stdio 配置，让其他本机客户端操作游戏。使用外部客户端不要求填写游戏内 API。

**上下文** 页可查看实际输入、输出、缓存用量和预计上下文大小，并设置自动整理阈值或手动整理。原记录、玩家原话、操作记录和近期完整回合保留；模型可用历史工具回查。详细说明见随包的 `LIVE-CHAT.md`。

Key 与请求头使用当前 Windows 用户的 DPAPI 加密，聊天记录与配置位于 `BepInEx/plugins/InfantGodArchive/livechat-data`。所有实际模型参数由玩家填写。

## 查看范围与 CG

已探索模式只使用当前运行中的游戏进度：精确访问节点、明确获得的记忆标记和本次读档后真正显示过的 CG。搜索同样受查看范围限制。一个共享节点有多个分支时，只提供确认的开场段落，未选选项不会随节点一起开放。

CG 画廊先显示包内画面，游戏内可继续查看原生 Spine 动画，并切换动画、暂停与缩放。原生预览使用 CG 的独立副本和摄像机；关窗、切换存档或停用 Mod 时释放。开发用资源单独筛选。

游戏内资料窗自动读取当前存档，读档后更新已探索范围。导出按钮将进度写入 `BepInEx/plugins/InfantGodArchive/progress.json`。同目录的 `archive.html` 可以单独在浏览器中打开；离线使用时，导入这份进度文件即可按已探索范围查阅。

## 设置与问题定位

游戏侧打开快捷键的设置位于 `BepInEx/config/graywill.infantgod.archive.cfg`，默认是 F8。网页界面由共用的 HTML 和 CSS 提供。

首次使用先确认 Mod 管理器已启用资料终端。程序加载日志位于 `BepInEx/LogOutput.log`，网页宿主日志位于 `BepInEx/plugins/InfantGodArchive/webhost-profile/webhost.log`。浏览器资料保存在同一个 `webhost-profile` 目录。当前适配游戏为 Windows x64、Unity Mono 的幼神 Demo，核对版本是 Unity 2022.3.46f1。

## 构建源码

需要 .NET SDK、Python 3、Windows .NET Framework 4.8 和本机安装的游戏。从项目源码仓库根目录执行：

```powershell
.\build.ps1 -GamePath '你的幼神 Demo\Build'
```

构建脚本会生成离线网页、原生插件和 WebView2 宿主，输出到 `dist/InfantGod-Archive-Mod`。完整构建与资料更新说明见源码仓库的 `docs/DATA.md`。

## 署名与许可

原作：Infant God（幼神）

作者：colsalley

原作地址：[幼神 Steam 页面](https://store.steampowered.com/app/4238140/_/)

导出的原作内容、剧情整理和授权资产按 **CC BY-NC-SA 4.0** 分享，需署名、非商业并使用相同许可。本项目原创的插件、网页程序和构建工具采用 **MIT** 开源许可。

BepInEx、Doorstop、Harmony、MonoMod 和 Mono.Cecil 的许可与来源单独列在 `THIRD-PARTY-NOTICES.md` 和 `LICENSES` 文件夹。WebView2 SDK 的许可随 `webhost/WebView2-LICENSE.txt` 提供。
