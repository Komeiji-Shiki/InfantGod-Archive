# HTML 界面接入研究

Windows 版《幼神》使用 Unity Mono，当前机器也有 Microsoft Edge WebView2 Runtime。HTML 内容可以由 WebView2 绘制，再作为纹理放入游戏窗口。存档数据沿用插件的进度读取模块，通过宿主消息传给页面。

## 可用组件

| 方案 | 界面呈现 | 与本项目相关的接口 |
| --- | --- | --- |
| Microsoft WebView2 | Windows 子窗口或 CompositionController | 加载本地 HTML、宿主与 JavaScript 双向消息、窗口尺寸与焦点管理 |
| gree/unity-webview | Windows WebView2 画面捕获为 Unity 纹理 | Unity 输入转发、网页导航和脚本调用；Windows 采用 CapturePreview |
| WebViewToolkit | DirectComposition 与 DirectX 11/12 纹理 | WebViewManager、ExecuteScript、MessageReceived、鼠标与键盘事件转发 |
| Vuplex 3D WebView | Chromium 纹理与 Unity 控件 | 包含 IME 与输入接口，按商业组件许可提供 |

本项目的窗口宿主与内容绘制分开，存档读取也独立于界面。若进一步采用 HTML 内容层，可以继续使用游戏的桌面图标、窗口框架与任务栏，并复用网页已有的分支图、人格筛选和中文搜索。

调研日期：2026-10-05。当前组件资料与本机库均已核对，嵌入式 HTML 不包含在 v1.0 的运行依赖中。

## 官方来源

- [Microsoft：Win32 WebView2 入门](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/win32)
- [Microsoft：WebView2 宿主窗口](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environment)
- [gree/unity-webview：Windows 接入说明](https://github.com/gree/unity-webview/blob/master/plugins/Windows/README.md)
- [WebViewToolkit 源码](https://github.com/cantetfelix/WebViewToolkit)
- [Vuplex：StandaloneWebView](https://developer.vuplex.com/webview/StandaloneWebView)
