# 第三方组件

本项目交付插件和 BepInEx 官方的 Windows x64 稳定版加载器 5.4.23.5。游戏的 Unity、Spine、FMOD、Yarn Spinner、FairyGUI、Newtonsoft.Json 和 InfantGod 程序集只从已安装游戏引用，不随插件包复制或随源码提交。

| 组件 | 来源 | 许可文本 |
|---|---|---|
| BepInEx 5.4.23.5 | https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 | LICENSES/BepInEx-MIT.txt |
| Unity Doorstop 4.5.0 | https://github.com/NeighTools/UnityDoorstop | LICENSES/UnityDoorstop.txt |
| HarmonyX / HarmonyXInterop | https://github.com/BepInEx/HarmonyX | LICENSES/HarmonyX.txt、LICENSES/Harmony-Original.txt |
| Harmony | https://github.com/pardeike/Harmony | LICENSES/Harmony.txt |
| MonoMod.RuntimeDetour / MonoMod.Utils | https://github.com/MonoMod/MonoMod | LICENSES/MonoMod.txt |
| Mono.Cecil / Mdb / Pdb / Rocks | https://github.com/jbevain/cecil | LICENSES/Mono.Cecil.txt |
| Microsoft.Web.WebView2 SDK 1.0.4191.47 | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47 | BepInEx/plugins/InfantGodArchive/webhost/WebView2-LICENSE.txt |

加载器二进制来自 BepInEx 的原始发布包，许可证保留各项目的原署名；相应项目的完整公开源代码可从上面的官方仓库取得。获取文本的准确 URL 另记在 LICENSES/SOURCES.txt。部分依赖文本取自官方仓库当前分支，BepInEx 自身取自对应的发布标签。

《幼神》导出的 MyMod 和 SODefines 各自附有 CC BY-NC-SA 4.0 署名说明，原作资产与剧情改编按该许可分享，完整文本是 LICENSES/CC-BY-NC-SA-4.0.txt。原作署名见 ATTRIBUTIONS.txt。
