using System;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    internal sealed class NativeArchiveMenuEntry : IDisposable
    {
        private static readonly FieldInfo PanelUi = AccessTools.Field(typeof(UIPanel), "_ui");
        private readonly ArchiveHost owner;
        private GComponent root;
        private GButton entry;
        private GButton exit;
        private float originalExitY;
        private float nextProbe;

        internal NativeArchiveMenuEntry(ArchiveHost owner) { this.owner = owner; }

        private static bool Alive(GObject value) => value != null && !value.isDisposed
            && value.displayObject != null && !value.displayObject.isDisposed && value.displayObject.cachedTransform != null;

        internal void Update(bool enabled)
        {
            if (!enabled) { Remove(); return; }
            if (!Alive(root) || !Alive(entry))
            {
                if (Time.unscaledTime < nextProbe) return;
                nextProbe = Time.unscaledTime + 0.5f;
                Remove();
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<UIPanel>())
                {
                    var candidate = PanelUi.GetValue(panel) as GComponent;
                    if (!Alive(candidate) || candidate.GetType().FullName != "MainMenu.UI_MainMenu") continue;
                    var template = candidate.GetChild("Btn_Settings") as GButton;
                    var exitButton = candidate.GetChild("Btn_Exit") as GButton;
                    if (!Alive(template) || !Alive(exitButton)) continue;
                    root = candidate; exit = exitButton; originalExitY = exit.y;
                    // 使用原主菜单同一资源，字号、边框、悬停效果随游戏主题变化。
                    entry = UIPackage.CreateObjectFromURL(template.resourceURL).asButton;
                    entry.name = "InfantGodArchive_MenuEntry";
                    entry.title = "资料终端";
                    entry.onClick.Add(() => owner.SetOpen(true));
                    root.AddChild(entry);
                    break;
                }
            }
            if (!Alive(root) || !Alive(entry)) return;
            var settings = root.GetChild("Btn_Settings");
            var load = root.GetChild("Btn_LoadGame");
            if (!Alive(settings) || !Alive(load)) return;
            float step = Math.Max(settings.height, settings.y - load.y);
            entry.SetXY(settings.x, settings.y + step);
            entry.SetSize(settings.width, settings.height);
            if (Alive(exit)) exit.y = entry.y + step;
            // 入口只属于主菜单，进入设置、载入页或游戏后不悬浮显示。
            entry.visible = root.GetController("Status")?.selectedIndex == 0;
        }

        private void Remove()
        {
            if (Alive(exit)) exit.y = originalExitY;
            if (Alive(entry)) entry.Dispose();
            root = null; entry = null; exit = null;
        }

        public void Dispose() => Remove();
    }
}
