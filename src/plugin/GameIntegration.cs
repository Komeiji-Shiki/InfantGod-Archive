using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core.ModSystem;
using InfantGod.Core.OSSystem;
using InfantGod.Core.SaveSystem;
using UnityEngine;
using InfantGod.Core;

namespace Graywill.InfantGodCodex
{
    internal sealed class GameIntegration : IDisposable
    {
        internal const string ModId = "InfantGodArchive";
        internal const string ShortcutId = "ShortCut_InfantGodArchive";
        private readonly ArchiveHost owner;
        private readonly Harmony hooks;
        private readonly NativeWindow nativeWindow;
        private GButton menuEntry;
        private GGraph menuBackground;
        private GTextField menuText;
        private ShortCut desktopEntry;
        private ShortCutManager shortcuts;
        private Texture2D iconTexture;
        private Sprite iconSprite;
        private float nextDesktopProbe;
        private static readonly MethodInfo InstalledMods = typeof(ModLoader).GetMethod("GetInstalledMods", BindingFlags.Public | BindingFlags.Static);
        internal bool IsEnabled { get; private set; }
        internal bool WindowShowing => nativeWindow.IsShowing;
        internal bool WindowFocused => nativeWindow.IsFocused;
        internal Rect ClientBounds => nativeWindow.ScreenClientBounds();

        internal GameIntegration(ArchiveHost owner)
        {
            this.owner = owner;
            nativeWindow = new NativeWindow(owner);
            hooks = new Harmony("graywill.infantgod.archive.integration");
            hooks.Patch(AccessTools.Method(typeof(OSManager), "OnShortcutActivated"), prefix: new HarmonyMethod(typeof(GameIntegration), nameof(OpenDesktopEntry)));
            hooks.Patch(AccessTools.Method(typeof(ShortCutManager), "CreateSaveData"), postfix: new HarmonyMethod(typeof(GameIntegration), nameof(ExcludeRuntimeShortcut)));
            hooks.Patch(AccessTools.Method(typeof(ShortCut), "GetLocalizedName"), prefix: new HarmonyMethod(typeof(GameIntegration), nameof(GetArchiveShortcutName)));
            hooks.Patch(AccessTools.Method(typeof(OSManager), "CreateWindow"), prefix: new HarmonyMethod(typeof(GameIntegration), nameof(CreateArchiveWindow)));
            hooks.Patch(AccessTools.Method(typeof(OSManager), "OnWorkspaceClicked"), prefix: new HarmonyMethod(typeof(GameIntegration), nameof(KeepArchiveWindowSelection)));
            hooks.Patch(AccessTools.Method(typeof(FGUICommandManager), "GetBaseUserData"), prefix: new HarmonyMethod(typeof(GameIntegration), nameof(GetOwnBaseUserData)));
            ModLoader.ModPreferencesChanged += RefreshEnabled;
            ModLoader.ModsReloaded += RefreshEnabled;
            RefreshEnabled();
        }

        private static bool CreateArchiveWindow(string windowName, ref Window __result)
        {
            if (windowName != NativeWindow.WindowName || ArchiveHost.Instance == null) return true;
            __result = ArchiveHost.Instance.Integration.nativeWindow.Create(true);
            return false;
        }

        private static bool KeepArchiveWindowSelection()
        {
            // 原工作区命中判断没有处理最大化缩放，资料窗口的点击沿原生命中链判断。
            return ArchiveHost.Instance == null || !ArchiveHost.Instance.Integration.nativeWindow.IsOwnWorkspaceTouch();
        }

        private static bool GetOwnBaseUserData(GObject obj, ref string __result)
        {
            if (!(obj is GComponent) || obj.packageItem != null) return true;
            for (GObject current = obj; current != null; current = current.parent)
                if (current.name != null && (current.name.StartsWith("InfantGodArchive", StringComparison.Ordinal) || current.name == NativeWindow.WindowName))
                {
                    __result = "";
                    return false;
                }
            return true;
        }

        internal void OpenWindow() { nativeWindow.Open(); }
        internal void CloseWindow() { nativeWindow.Close(); }

        private static bool OpenDesktopEntry(ShortCut shortcut)
        {
            if (shortcut == null || shortcut.techName != ShortcutId) return true;
            if (ArchiveHost.Instance != null && ArchiveHost.Instance.IsAvailable) ArchiveHost.Instance.SetOpen(true);
            return false;
        }

        private static bool GetArchiveShortcutName(ShortCut __instance, ref string __result)
        {
            if (__instance.techName != ShortcutId) return true;
            // 桌面与任务栏都通过原生名称入口读取本 Mod 的显示名。
            __result = "资料终端";
            return false;
        }

        private static void ExcludeRuntimeShortcut(ShortcutSaveData __result)
        {
            // 桌面入口由启用的 Mod 重建，不写入主线存档的快捷方式名单。
            __result?.activeShortcutTechNames?.RemoveAll(name => name == ShortcutId);
        }

        private void RefreshEnabled()
        {
            bool enabled = false;
            var mods = InstalledMods.Invoke(null, null) as IEnumerable;
            if (mods != null)
                foreach (object mod in mods)
                {
                    Type type = mod.GetType();
                    string id = (string)type.GetField("Id").GetValue(mod);
                    string folder = (string)type.GetField("FolderName").GetValue(mod);
                    if (id == ModId || folder == ModId)
                    {
                        enabled = (bool)type.GetField("Enabled").GetValue(mod);
                        break;
                    }
                }
            IsEnabled = enabled;
            owner.SetAvailable(enabled);
            if (!enabled) RemoveDesktopEntry();
            nextDesktopProbe = 0;
            if (menuEntry != null) menuEntry.visible = enabled && (!owner.IsOpen || !nativeWindow.IsShowing);
        }

        internal void Tick()
        {
            if (IsEnabled) UpdateMenuEntry();
            if (menuEntry != null) menuEntry.visible = IsEnabled && (!owner.IsOpen || !nativeWindow.IsShowing);
            if (!IsEnabled || Time.unscaledTime < nextDesktopProbe) return;
            nextDesktopProbe = Time.unscaledTime + 1;
            if (shortcuts == null) shortcuts = UnityEngine.Object.FindObjectOfType<ShortCutManager>();
            if (shortcuts == null || !shortcuts.IsInitialized || shortcuts.HasShortcut(ShortcutId)) return;
            EnsureDesktopDefinition();
            shortcuts.AddShortcut(desktopEntry);
        }

        private void EnsureDesktopDefinition()
        {
            if (desktopEntry != null) return;
            string image = Path.Combine(owner.DataPath, "portraits", "Aistalt_Tech.png");
            iconTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(iconTexture, File.ReadAllBytes(image));
            iconTexture.filterMode = FilterMode.Point;
            iconSprite = Sprite.Create(iconTexture, new Rect(0, 0, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f), 100);
            desktopEntry = ScriptableObject.CreateInstance<ShortCut>();
            desktopEntry.techName = ShortcutId;
            desktopEntry.nameKey = "InfantGodArchive/Name";
            desktopEntry.windowName = NativeWindow.WindowName;
            desktopEntry.icon = iconSprite;
        }

        private void UpdateMenuEntry()
        {
            if (GRoot.inst == null) return;
            if (menuEntry == null || menuEntry.isDisposed)
            {
                // 主菜单尚未创建 OSManager，先按原生流程加载按钮所属的界面包。
                if (UIPackage.GetByName("OS") == null && UIPackage.AddPackage("FairyGUI/UI/OS") == null) return;
                GObject created = UIPackage.CreateObject("OS", "Button_Menu");
                if (created == null) return;
                menuEntry = created.asButton;
                if (menuEntry == null) { created.Dispose(); return; }
                menuEntry.name = "InfantGodArchive_MenuEntry";
                menuEntry.opaque = true;
                menuEntry.sortingOrder = 32766;
                menuEntry.SetSize(166, 46);
                menuBackground = new GGraph { touchable = false };
                menuBackground.DrawRect(166, 46, 2, new Color32(245, 141, 60, 255), new Color32(42, 48, 46, 255));
                menuEntry.AddChild(menuBackground);
                menuText = new GTextField { touchable = false, align = AlignType.Center, verticalAlign = VertAlignType.Middle };
                menuText.SetSize(166, 46);
                menuText.text = owner.OpenKey.Value + "  资料终端";
                var textFormat = menuText.textFormat;
                textFormat.font = UIConfig.defaultFont;
                textFormat.size = 20;
                textFormat.color = new Color32(229, 225, 181, 255);
                menuText.textFormat = textFormat;
                menuEntry.AddChild(menuText);
                menuEntry.onClick.Add(() => owner.SetOpen(true));
                GRoot.inst.AddChild(menuEntry);
            }
            if (menuEntry.parent != GRoot.inst) GRoot.inst.AddChild(menuEntry);
            menuEntry.SetXY(Math.Max(12, GRoot.inst.width - 184), 24);
        }

        private void RemoveDesktopEntry()
        {
            if (shortcuts != null && shortcuts.HasShortcut(ShortcutId)) shortcuts.RemoveShortcut(ShortcutId);
        }

        public void Dispose()
        {
            ModLoader.ModPreferencesChanged -= RefreshEnabled;
            ModLoader.ModsReloaded -= RefreshEnabled;
            RemoveDesktopEntry();
            nativeWindow.Dispose();
            menuEntry?.Dispose();
            if (desktopEntry != null) UnityEngine.Object.Destroy(desktopEntry);
            if (iconSprite != null) UnityEngine.Object.Destroy(iconSprite);
            if (iconTexture != null) UnityEngine.Object.Destroy(iconTexture);
            hooks.UnpatchSelf();
        }
    }
}
