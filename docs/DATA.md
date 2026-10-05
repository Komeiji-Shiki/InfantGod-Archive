# 数据与构建

仓库已经包含整理好的资料快照。只修改界面或插件时，直接使用它即可。

## 重建离线页

需要 Python 3.10 或更新版本，无需安装第三方依赖。

```powershell
python tools/build_web.py
```

输出为 `dist/archive.html`。字体、图像、剧情数据和 Dagre 布局库都嵌入这一个文件，打开后无需联网。

## 构建完整 Mod 包

需要 .NET SDK、Windows .NET Framework 4.8、Python 3，以及本机安装的 Windows Mono 版《幼神 Demo》。

```powershell
.\build.ps1 -GamePath '你的幼神 Demo\Build'
```

输出为 `dist/InfantGod-Archive-Mod`，以及自带全部安装文件的 `dist/InfantGod-Archive-Setup-v1.0.1.exe`。首次构建从官方发布源下载 BepInEx 5.4.23.5 和 Microsoft.Web.WebView2 SDK 1.0.4191.47；后续使用缓存。游戏程序集作为本机编译引用。运行游戏内网页还需要 Microsoft Edge WebView2 Runtime。

只修改安装器界面时，可以复用已经生成的完整 Mod 目录：

```powershell
python src/installer/build.py --package dist/InfantGod-Archive-Mod --output dist/InfantGod-Archive-Setup-v1.0.1.exe
```

安装器把安装包、像素字体和头像嵌入单个 EXE。它按需将安装文件解压到临时目录，调用随包的安装/卸载脚本，并在退出时清理临时文件。游戏内的备份、配置与进度由安装脚本管理，不随临时目录清理。Steam 路径通过注册表和 `libraryfolders.vdf` 查找，没有写入开发者的本机路径。

提交源码后，运行 `python tools/package_release.py --output dist/release`，整理安装器、手动安装 ZIP、离线页、发行说明和当前 Git 提交的源码包。

## 更新资料快照

数据更新需要 Pillow 和 UnityPy。先在游戏设置中导出脚本及数据，使 `Aistalt_Data/StreamingAssets/Mods` 下包含 `MyMod` 和 `SODefines`。

```powershell
python tools/extract_localization.py --game-path '你的幼神 Demo\Build' --output-dir .work/data
python tools/extract_game.py --game-path '你的幼神 Demo\Build' --output-dir .work/data
python tools/build_guide_data.py --input-dir .work/data --plugin-dir .work/plugin-assets
```

这三步依次生成中文名称表、剧情结构索引和共用资料。用 `guide-data.json` 更新 `data/archive.json`，保留现有的署名与字体许可字段；用 `mod-catalog.json` 更新 `data/catalog.json`。将 `.work/plugin-assets/previews` 和 `portraits` 分别复制到 `assets/cg` 与 `assets/portraits`，然后重新构建。资料生成过程会移除本机的 `sourceRoot` 路径。更新游戏版本时，也需要复核 `tools/curation.py` 的人工导读。

## 分支图的含义

单场景分支图展示顺序对话、玩家选项、条件、状态变化与跳转。`detour` 使用虚线表示插入对话，并标注返回后的继续位置。外部节点点击后进入对应场景；人物全线概览展示节点间的脚本连接。

图中表示的是脚本定义的可能路径。随机匹配、话题检定和游戏系统会决定实际进入哪条路径。原始对话和行号保留在节点详情中。

## 已探索模式

进度快照格式为 `infantgod-progress-v1`。资料按当前存档的精确节点访问、记忆变量和 CG 证据开放。共享节点内部可能有多条分支，因此已探索模式只显示能够确定的共同开场，完整选项留在全剧透模式。

快照只用于本地显示，不写回存档。未能确认的记录保持隐藏。
