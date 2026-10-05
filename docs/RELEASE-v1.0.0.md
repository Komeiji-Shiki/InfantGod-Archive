# 幼神资料终端 v1.0.0

监督，资料已经整理好了。

《幼神 Demo》的游戏内资料终端与单文件离线网页。游戏桌面提供“资料终端”快捷方式，按 F8 也能打开；窗口复用原生标题栏与任务栏，正文使用与离线网页相同的 HTML 界面。

## 本版内容

- 211 份剧情脚本、1,221 个场景节点、1,445 个选项的查询与可交互分支图。
- 角色档案、完整对话与原作头像，爱式塔四种人格的发言可以分别查阅。
- 话题门槛、属性与骰子检定、191 项协议、188 项记忆和 14 项成就资料。
- CG 镜头图鉴、游戏内动画预览、暂停与缩放。
- 全部剧透和仅已探索两种范围，自动同步当前存档，也可以导出进度供离线网页导入。
- 分支图支持拖动、缩放、点击后续场景，以及返回时恢复原来的浏览位置。

## 下载文件

- `InfantGod-Archive-Mod-v1.0.0.zip`：Windows x64 完整 Mod 安装包，含加载器、资料页与安装/卸载脚本。
- `InfantGod-Archive.html`：可直接打开的离线资料页。
- `InfantGod-Archive-Trailer.mp4`：约 71 秒、1080p/30fps 功能演示，带中文字幕与原创配乐。
- `InfantGod-Archive-Trailer.srt`：独立字幕文件。

## 安装

需要 Windows x64 的《幼神 Demo》以及 Microsoft Edge WebView2 Runtime。关闭游戏，将安装包解压，然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -GamePath '你的幼神 Demo\Build'
```

启动游戏后，在“设置 → Mod 管理器”启用“幼神资料终端”并应用。进入存档后双击桌面图标，或按 F8 打开。

## 署名

原作《幼神》，作者 colsalley。程序代码与原创配乐采用 MIT；原作剧情、资产及对应整理资料采用 CC BY-NC-SA 4.0；字体与第三方组件保留各自许可。详见仓库和安装包内的 LICENSE。
