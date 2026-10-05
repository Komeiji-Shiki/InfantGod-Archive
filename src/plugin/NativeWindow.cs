using System;
using System.Collections;
using System.Reflection;
using FairyGUI;
using InfantGod.Core.OSSystem;
using InfantGod.Core.UI;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    internal sealed class NativeWindow : IDisposable
    {
        internal const string WindowName = "Window_InfantGodArchive";
        private readonly ArchiveHost owner;
        private Window window;
        private GComponent frame;
        private OSManager os;
        private GGraph clientBackground;
        private Rect restoredBounds;
        private bool standaloneMaximized;
        private bool usingOS;
        private static readonly FieldInfo RegistryField = typeof(OSManager).GetField("_windowRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LookupField = typeof(WindowRegistry).GetField("_lookup", BindingFlags.Instance | BindingFlags.NonPublic);

        internal NativeWindow(ArchiveHost owner) { this.owner = owner; }
        internal bool IsShowing => window != null && !window.isDisposed && window.visible && window.isShowing;
        internal bool IsFocused
        {
            get
            {
                if (!IsShowing) return false;
                if (usingOS && os != null && os.GetCurrentSelectedWindowName() != WindowName) return false;
                // isTop 会把隐藏按钮计入层级；这里只判断实际位于前方的可见窗口。
                for (GObject branch = window; branch.parent != null; branch = branch.parent)
                {
                    GComponent parent = branch.parent;
                    if (parent.isDisposed || !parent.visible) return false;
                    for (int i = parent.numChildren - 1; i >= 0; i--)
                    {
                        GObject sibling = parent.GetChildAt(i);
                        if (sibling == branch) break;
                        var front = sibling as Window;
                        if (front != null && !front.isDisposed && front.visible && front.isShowing) return false;
                    }
                }
                return true;
            }
        }

        internal void Open()
        {
            if (os == null) os = UnityEngine.Object.FindObjectOfType<OSManager>();
            object registry = os == null ? null : RegistryField.GetValue(os);
            if (registry != null)
            {
                if (!usingOS && window != null && !window.isDisposed) { window.Dispose(); window = null; }
                var lookup = (IDictionary)LookupField.GetValue(registry);
                if (!lookup.Contains(WindowName)) lookup.Add(WindowName, new WindowConfigEntry { windowName = WindowName, packageName = "Media", hideWhenClose = true });
                usingOS = true;
                os.OpenWindowByName(WindowName);
            }
            else
            {
                usingOS = false;
                if (window == null || window.isDisposed) Create(false);
                window.Show();
                window.BringToFront();
            }
        }

        internal Window Create(bool managedByOS)
        {
            if (window != null && !window.isDisposed) return window;
            usingOS = managedByOS;
            if (UIPackage.GetByName("Media") == null) UIPackage.AddPackage("FairyGUI/UI/Media");
            window = new Window { name = WindowName, bringToFontOnClick = true };
            GComponent pane = UIPackage.CreateObject("Media", "Window_Media_Text").asCom;
            pane.name = "InfantGodArchive_Content";
            var textLoader = pane.GetChild("Panel_TextLoader");
            if (textLoader != null) textLoader.visible = false;
            window.contentPane = pane;
            frame = pane.GetChild("frame")?.asCom;
            if (frame != null)
            {
                // Media 模板的框体没有跟随内容面板的尺寸，需要显式建立关系。
                frame.SetXY(0, 0);
                frame.SetSize(pane.width, pane.height);
                frame.AddRelation(pane, RelationType.Size);
            }
            // OS 全屏按原模板缩放，桌面窗口保留模板基础尺寸；主菜单单独自适应。
            float width = pane.width;
            float height = pane.height;
            if (!managedByOS)
            {
                float screenScale = Mathf.Clamp(Screen.width / 1600f, 0.65f, 1.4f);
                Vector2 zero = GRoot.inst.LocalToGlobal(Vector2.zero);
                Vector2 one = GRoot.inst.LocalToGlobal(Vector2.one);
                float unitScale = Mathf.Max(0.01f, one.x - zero.x);
                width = Mathf.Min(GRoot.inst.width - 40, 1460 * screenScale / unitScale);
                height = Mathf.Min(GRoot.inst.height - 40, 920 * screenScale / unitScale);
            }
            // Window 的尺寸关系会同步内容面板，直接改 pane 会留下旧的外层尺寸。
            window.SetSize(width, height);
            window.SetXY(Mathf.Max(20, (GRoot.inst.width - width) / 2), Mathf.Max(20, (GRoot.inst.height - height) / 2));
            if (frame != null)
            {
                var title = frame.GetChild("title");
                if (title?.asTextField != null) title.asTextField.text = "OmniArchive.exe  /  资料终端";
                else if (title?.asLabel != null) title.asLabel.title = "OmniArchive.exe  /  资料终端";
                window.dragArea = frame.GetChild("dragArea");
                if (!managedByOS)
                {
                    frame.GetChild("minButton")?.onClick.Add(() => window.Hide());
                    frame.GetChild("closeButton")?.onClick.Add(() => owner.SetOpen(false));
                    frame.GetChild("maxButton")?.onClick.Add(ToggleStandaloneMaximize);
                }
            }
            clientBackground = new GGraph { name = "InfantGodArchive_Client", touchable = true };
            Rect client = ClientLocalBounds();
            clientBackground.DrawRect(client.width, client.height, 0, Color.clear, new Color32(229, 225, 181, 255));
            clientBackground.SetXY(client.x, client.y);
            pane.AddChildAt(clientBackground, 0);
            pane.onSizeChanged.Add(ResizeClient);
            frame?.onSizeChanged.Add(ResizeClient);
            ResizeClient();
            return window;
        }

        private void ResizeClient()
        {
            if (window == null || clientBackground == null) return;
            // 模板显示过渡可能重置框体尺寸，同步绝对尺寸后再计算客户区。
            if (frame != null && (frame.width != window.contentPane.width || frame.height != window.contentPane.height))
                frame.SetSize(window.contentPane.width, window.contentPane.height);
            Rect client = ClientLocalBounds();
            clientBackground.SetXY(client.x, client.y);
            clientBackground.SetSize(client.width, client.height);
        }

        private Rect ClientLocalBounds()
        {
            GComponent pane = window.contentPane;
            GComponent chrome = frame ?? pane;
            Vector2 top = chrome.TransformPoint(new Vector2(6, 6), pane);
            Vector2 bottom = chrome.TransformPoint(new Vector2(chrome.width - 6, chrome.height - 6), pane);
            if (frame != null)
            {
                // 使用标题栏控件的真实底边，网页不能覆盖原生拖动区和窗口按键。
                top.y = Mathf.Max(top.y, ChromeBottom("dragArea", pane));
                top.y = Mathf.Max(top.y, ChromeBottom("title", pane));
                top.y = Mathf.Max(top.y, ChromeBottom("minButton", pane));
                top.y = Mathf.Max(top.y, ChromeBottom("maxButton", pane));
                top.y = Mathf.Max(top.y, ChromeBottom("closeButton", pane));
            }
            return new Rect(top.x, top.y, Mathf.Max(0, bottom.x - top.x), Mathf.Max(0, bottom.y - top.y));
        }

        private float ChromeBottom(string name, GComponent pane)
        {
            GObject control = frame.GetChild(name);
            return control == null ? 0 : control.TransformPoint(new Vector2(0, control.height), pane).y;
        }

        private void ToggleStandaloneMaximize()
        {
            if (window == null) return;
            if (!standaloneMaximized)
            {
                restoredBounds = new Rect(window.x, window.y, window.width, window.height);
                Rect canvas = DisplaySettingsManager.GetCanvasRect();
                Rect global = GRoot.inst.LocalToGlobal(canvas);
                Vector2 top = window.parent.GlobalToLocal(new Vector2(Mathf.Max(0, global.xMin), Mathf.Max(0, global.yMin)));
                Vector2 bottom = window.parent.GlobalToLocal(new Vector2(Mathf.Min(Stage.inst.width, global.xMax), Mathf.Min(Stage.inst.height, global.yMax)));
                // 主菜单装饰区保留标题栏高度，真实画布坐标转换后再确定最大化范围。
                float titleHeight = ClientLocalBounds().yMin;
                window.SetXY(top.x + 6, top.y + titleHeight);
                window.SetSize(Mathf.Max(1, bottom.x - top.x - 12), Mathf.Max(1, bottom.y - top.y - titleHeight - 6));
            }
            else
            {
                window.SetXY(restoredBounds.x, restoredBounds.y);
                window.SetSize(restoredBounds.width, restoredBounds.height);
            }
            standaloneMaximized = !standaloneMaximized;
        }

        internal bool IsOwnWorkspaceTouch()
        {
            if (!usingOS || !IsShowing || GRoot.inst == null) return false;
            // 点击按钮后窗口会立刻缩放或还原，命中对象比变化后的矩形更准确。
            for (GObject target = GRoot.inst.touchTarget; target != null; target = target.parent)
                if (target == window) return true;
            return false;
        }

        internal Rect ScreenClientBounds()
        {
            if (!IsShowing) return Rect.zero;
            // LocalToGlobal 已包含父容器、原生最大化与拖动带来的变换。
            GComponent pane = window.contentPane;
            Rect client = ClientLocalBounds();
            Vector2 top = pane.LocalToGlobal(new Vector2(client.xMin, client.yMin));
            Vector2 bottom = pane.LocalToGlobal(new Vector2(client.xMax, client.yMax));
            float sx = Screen.width / Mathf.Max(1f, Stage.inst.width);
            float sy = Screen.height / Mathf.Max(1f, Stage.inst.height);
            return Rect.MinMaxRect(top.x * sx, top.y * sy, bottom.x * sx, bottom.y * sy);
        }

        internal void Close()
        {
            if (window == null || window.isDisposed) return;
            if (usingOS && os != null) os.CloseWindowByName(WindowName);
            else window.Hide();
        }

        public void Dispose()
        {
            Close();
            window?.Dispose();
            window = null;
            frame = null;
        }
    }
}
