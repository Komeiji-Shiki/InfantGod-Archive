# 游戏内网页窗口

资料终端使用游戏原生窗口作为外框，并把 WebView2 放进窗口的客户区。游戏内和离线版使用同一份 HTML、字体、剧情数据与流程图代码。

## 各部分负责什么

- **游戏插件**注册桌面快捷方式、Mod 管理条目和原生窗口，读取当前存档，渲染 CG 动画。
- **WebView2 宿主**作为 Unity 窗口的子窗口显示 HTML，并处理浏览器的文字输入、页面布局和 JavaScript。
- **网页**负责资料查询、分支图、人格台词、协议与 CG 界面。

标题栏的拖动、最小化、最大化和关闭由游戏窗口管理器处理。窗口客户区的坐标与大小实时传给网页宿主。失焦时，宿主使用 WebView2 的 `CapturePreviewAsync` 保存当前页面，再隐藏交互窗口；插件把该页面绘制在原生窗口客户区中，因此其他游戏窗口可以正常遮挡它。重新聚焦时恢复可交互的网页。切换存档或查看范围时清除旧的背景预览。

## 存档与 CG

插件和宿主通过当前游戏进程专用的 Named Pipe 交换 JSON。存档快照包含日期、时间、已访问节点、记忆变量与 CG 记录。进入或切换存档后，页面自动更新；离线文件仍可使用手动导入。

CG 使用独立的游戏相机和 RenderTexture 渲染，再把画面传给网页的 CG 查看器。页面提供动画选择、暂停与缩放；静态镜头直接嵌入 HTML。

## 运行与构建

宿主采用 Windows .NET Framework 4.8 和 Microsoft.Web.WebView2 SDK 1.0.4191.47。构建脚本下载固定版本 SDK，并把宿主 EXE、SDK 程序集及 WebView2Loader.dll 放入插件的 `webhost` 子目录。WebView2 Runtime 由系统提供，来源为 [Microsoft 官方下载](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)。

浏览器使用资料终端自己的 `webhost-profile` 目录。窗口内容来自安装包中的 `archive.html`。

## 选型资料

实现采用 Microsoft 官方 WebView2 控件，让中文输入法直接使用浏览器的输入流程。调研时也比较了 Unity 的纹理型网页组件。

- [Microsoft：WinForms WebView2 入门](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms)
- [Microsoft：WebView2 宿主窗口](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environment)
- [Microsoft：宿主向页面发送 JSON](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.postwebmessageasjson)
- [Microsoft：捕获 WebView2 当前页面](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.capturepreviewasync)
- [gree/unity-webview：Windows 接入](https://github.com/gree/unity-webview/blob/master/plugins/Windows/README.md)
- [WebViewToolkit](https://github.com/cantetfelix/WebViewToolkit)
- [Vuplex StandaloneWebView](https://developer.vuplex.com/webview/StandaloneWebView)
