using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using FairyGUI;
using InfantGod.Core.OSSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Graywill.InfantGodCodex.LiveChat.GameToolbox;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class GameUiTools
    {
        private readonly LiveChatController owner;
        private readonly Dictionary<string, GObject> handles = new Dictionary<string, GObject>();
        private readonly Dictionary<string, DisplayObject> linkTargets = new Dictionary<string, DisplayObject>();
        private int snapshotVersion;
        private static readonly FieldInfo CustomInput = typeof(Stage).GetField("_customInput", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CustomButton = typeof(Stage).GetField("_customInputButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RenderedText = typeof(GTextField).GetField("_textField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ProcessCustomInput = typeof(Stage).GetMethod("HandleCustomInput", BindingFlags.Instance | BindingFlags.NonPublic);

        internal GameUiTools(LiveChatController owner) { this.owner = owner; }

        internal void Register(GameToolbox box)
        {
            box.Add("read_ui", "查看电脑界面", "读取游戏电脑当前可见的窗口、文字和可操作控件。ref 只适用于这次观察，操作后应使用新返回的 ref。query 可筛选文字，offset 可翻页；空字符串表示不筛选。", Props("window", Text("窗口名，留空读取整个桌面"), "query", Text("文字筛选，留空显示全部"), "offset", Integer("结果起点，通常为 0")),
                args => { owner.YieldToGame(); return Snapshot((string)args["window"], (string)args["query"], Number(args, "offset", 0, 10000)); });
            box.Add("read_ui_text", "阅读完整界面文字", "分页读取当前控件的完整显示文字，适合邮件正文和长文档。offset 为字符起点；此工具保持现有 ref 有效。", Props("ref", Text("文字控件 ref"), "offset", Integer("字符起点，通常为 0")), args =>
                {
                    var obj = Resolve(Required(args, "ref"), false); string text = DisplayText(obj);
                    int offset = Number(args, "offset", 0, 1000000);
                    return new JObject { ["text"] = offset < text.Length ? text.Substring(offset, Math.Min(8000, text.Length - offset)) : "", ["total"] = text.Length, ["next_offset"] = offset + 8000 < text.Length ? offset + 8000 : -1 };
                });
            box.AddAsync("click_ui", "点击界面", "点击 read_ui 中可操作控件的 ref。button 为 left 或 right；count 为 1 或 2，桌面图标通常需要双击。使用实际界面事件，游戏会执行原有的扣款、条件和确认流程。", Props("ref", Text("当前界面控件 ref"), "button", Text("left 或 right"), "count", Integer("1 为单击，2 为双击")),
                (args, cancel) => owner.RunOperation(() => Click(args, cancel), cancel));
            box.AddAsync("drag_ui", "拖动界面", "从控件中心或指定的控件内部位置拖动到游戏屏幕坐标。用于窗口、物品及其他拖放交互。坐标使用 read_ui 返回的屏幕坐标，左上角为原点。", Props("ref", Text("起点控件 ref"), "from_x", Integer("控件内部 x，-1 表示中心"), "from_y", Integer("控件内部 y，-1 表示中心"), "to_x", Integer("终点屏幕 x"), "to_y", Integer("终点屏幕 y")),
                (args, cancel) => owner.RunOperation(() => Drag(args, cancel), cancel));
            box.AddAsync("hover_ui", "查看悬停提示", "把指针移到控件上，等待并读取它的说明或提示框。", Props("ref", Text("控件 ref")),
                (args, cancel) => owner.RunOperation(() => Hover(args, cancel), cancel));
            box.AddAsync("hold_ui", "长按界面", "在控件中心按住左键指定毫秒数后松开，用于原作的长按确认或拆解。milliseconds 为 100～5000。", Props("ref", Text("控件 ref"), "milliseconds", Integer("按住时长，100～5000 毫秒")),
                (args, cancel) => owner.RunOperation(() => Hold(args, cancel), cancel));
            box.AddAsync("type_text", "填写文字", "向游戏内的可编辑输入框填写文字。submit=true 时提交该输入框。", Props("ref", Text("输入框 ref"), "text", Text("要填写的完整文字"), "submit", Boolean("是否提交")),
                (args, cancel) => owner.RunOperation(() => Type(args, cancel), cancel));
            box.AddAsync("scroll_ui", "滚动内容", "滚动控件所在的列表或内容区。direction 为 up/down/left/right；pages 为 1～5。", Props("ref", Text("列表或列表内部控件 ref"), "direction", Text("up/down/left/right"), "pages", Integer("滚动页数 1～5")),
                (args, cancel) => owner.RunOperation(() => Scroll(args, cancel), cancel));
            box.AddAsync("set_ui_value", "调整选项", "修改下拉框或滑块，触发原有的变更事件。下拉框 value 为从 0 开始的序号；滑块 value 使用界面返回的范围。复选框请使用 click_ui。", Props("ref", Text("下拉框或滑块 ref"), "value", new JObject { ["type"] = "number", ["description"] = "新值" }),
                (args, cancel) => owner.RunOperation(() => SetValue(args, cancel), cancel));
            box.AddAsync("advance_dialogue", "继续对白", "相当于点击原作的继续对白按钮；正在打字时先显示全文，再调用一次才进入下一句。选项需要使用 click_ui。", Props(),
                (args, cancel) => owner.RunOperation(() => Advance(cancel), cancel));
            box.AddAsync("wait_game", "等待游戏", "等待界面动画或剧情处理完成后重新读取界面。seconds 为 0～5。", Props("seconds", Integer("等待秒数，0～5")),
                (args, cancel) => owner.RunOperation(() => Wait(Number(args, "seconds", 0, 5), cancel), cancel));
        }

        internal JObject Snapshot(string windowName, string query, int offset)
        {
            snapshotVersion++;
            handles.Clear(); linkTargets.Clear();
            var entries = new List<JObject>(); var windows = new JArray();
            var visited = new HashSet<GObject>();
            if (GRoot.inst != null) Walk(GRoot.inst, "桌面", windowName ?? "", query ?? "", entries, windows, visited, 0);
            // 原作 UIPanel 会将 UI 挂到独立 Stage 容器，并非所有窗口都在 GRoot 下。
            foreach (var panel in UnityEngine.Object.FindObjectsOfType<UIPanel>())
                if (panel.isActiveAndEnabled && panel.ui != null)
                    Walk(panel.ui, string.IsNullOrEmpty(panel.ui.name) ? panel.name : panel.ui.name, windowName ?? "", query ?? "", entries, windows, visited, 0);
            return new JObject { ["screen"] = new JObject { ["width"] = Screen.width, ["height"] = Screen.height }, ["windows"] = windows,
                ["items"] = new JArray(entries.Skip(offset).Take(140)), ["total"] = entries.Count, ["next_offset"] = offset + 140 < entries.Count ? offset + 140 : -1,
                ["dialogue"] = owner.NativeDialogue() };
        }

        private void Walk(GObject obj, string windowName, string filterWindow, string query, List<JObject> entries, JArray windows, HashSet<GObject> visited, int depth)
        {
            if (depth > 28 || !Visible(obj) || IsOwn(obj) || !visited.Add(obj)) return;
            if (obj is Window win)
            {
                windowName = win.name ?? win.id;
                windows.Add(new JObject { ["name"] = windowName, ["title"] = win.contentPane?.GetChild("frame")?.asCom?.GetChild("title")?.text ?? "" });
            }
            bool inWindow = filterWindow.Length == 0 || string.Equals(filterWindow, windowName, StringComparison.OrdinalIgnoreCase);
            string text = DisplayText(obj);
            bool interactive = obj is GButton || obj is GTextInput || obj is GComboBox || obj is GSlider || obj.draggable || !obj.onClick.isEmpty || !obj.onRightClick.isEmpty || !obj.onTouchBegin.isEmpty || !obj.onTouchEnd.isEmpty || !obj.onRollOver.isEmpty;
            bool scrollable = obj is GComponent scroll && scroll.scrollPane != null;
            Rect rect = obj.LocalToGlobal(new Rect(0, 0, obj.width, obj.height));
            bool inside = rect.width > 0 && rect.height > 0 && rect.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
            if (inWindow && inside && (interactive || scrollable || !string.IsNullOrWhiteSpace(text)) && (query.Length == 0 || (text + " " + obj.name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string id = snapshotVersion + ":" + handles.Count;
                handles[id] = obj;
                var entry = new JObject { ["ref"] = id, ["window"] = windowName, ["name"] = obj.name ?? "", ["type"] = obj.GetType().Name,
                    ["text"] = Clip(Plain(text), 1800), ["enabled"] = Enabled(obj), ["clickable"] = interactive, ["scrollable"] = scrollable,
                    ["x"] = Math.Round(rect.x), ["y"] = Math.Round(rect.y), ["width"] = Math.Round(rect.width), ["height"] = Math.Round(rect.height) };
                if (obj is GTextInput textInput) { entry["editable"] = textInput.editable; entry["prompt"] = textInput.promptText; }
                if (obj is GComboBox combo) { entry["items"] = new JArray(combo.items); entry["value"] = combo.selectedIndex; }
                if (obj is GSlider slider) { entry["value"] = slider.value; entry["min"] = slider.min; entry["max"] = slider.max; }
                entries.Add(entry);
            }
            if (inWindow && inside && obj is GRichTextField rich)
                AddLinks(rich, windowName, query, entries);
            if (inWindow && inside) AddNativeText(obj.displayObject, obj, windowName, query, entries);
            if (obj is GComponent component)
                foreach (var child in component.GetChildren()) Walk(child, windowName, filterWindow, query, entries, windows, visited, depth + 1);
            // 邮件、网页模板等通过 GLoader 承载，不属于 GComponent.GetChildren()。
            if (obj is GLoader loader && loader.component != null)
                Walk(loader.component, windowName, filterWindow, query, entries, windows, visited, depth + 1);
        }

        private GObject Resolve(string id, bool requireEnabled = true)
        {
            if (!handles.TryGetValue(id, out var obj) || !Visible(obj)) throw new InvalidOperationException("这个界面引用已失效，请重新调用 read_ui。");
            if (requireEnabled && !Enabled(obj)) throw new InvalidOperationException("这个控件当前不可操作。");
            return obj;
        }

        private IEnumerator Click(JObject args, CancellationToken cancel)
        {
            string id = Required(args, "ref"); var obj = Resolve(id); string button = Required(args, "button"); int count = Number(args, "count", 1, 2);
            if (button != "left" && button != "right") throw new InvalidOperationException("button 只能为 left 或 right。");
            Focus(obj); yield return null;
            cancel.ThrowIfCancellationRequested();
            if (button == "right")
            {
                yield return MovePointer(obj, cancel);
                // 右键事件沿 FGUI 冒泡，让列表和原作命令分发器收到相同的事件。
                obj.displayObject.BubbleEvent("onRightClick", PointerEvent(obj, 1, count));
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var target = linkTargets.TryGetValue(id, out var link) ? link : obj.displayObject;
                    var point = target is SelectionShape shape && shape.rects.Count > 0 ? target.LocalToGlobal(shape.rects[0].center) : obj.LocalToGlobal(new Vector2(obj.width / 2, obj.height / 2));
                    yield return Pointer(point, point, false, cancel, target);
                }
            }
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private IEnumerator Drag(JObject args, CancellationToken cancel)
        {
            var obj = Resolve(Required(args, "ref")); Focus(obj); yield return null;
            int x = Number(args, "from_x", -1, 10000), y = Number(args, "from_y", -1, 10000);
            if (x > obj.width || y > obj.height) throw new InvalidOperationException("拖动起点必须位于指定控件内部。");
            var from = obj.LocalToGlobal(new Vector2(x < 0 ? obj.width / 2 : x, y < 0 ? obj.height / 2 : y));
            var to = new Vector2(Number(args, "to_x", 0, Screen.width), Number(args, "to_y", 0, Screen.height));
            yield return Pointer(from, to, true, cancel, obj.displayObject);
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private IEnumerator Pointer(Vector2 from, Vector2 to, bool drag, CancellationToken cancel, DisplayObject expected, float holdSeconds = 0)
        {
            var stage = Stage.inst; bool oldCustom = (bool)CustomInput.GetValue(stage); bool released = false;
            try
            {
                cancel.ThrowIfCancellationRequested();
                // Stage 在每次 LateUpdate 后清除自定义输入，必须在同一次提交中检查命中。
                stage.SetCustomInput(ToScreen(from), false);
                CheckHit(expected);
                stage.SetCustomInput(ToScreen(from), true); yield return null;
                float end = Time.unscaledTime + holdSeconds;
                while (Time.unscaledTime < end)
                {
                    cancel.ThrowIfCancellationRequested();
                    stage.SetCustomInput(ToScreen(from), true); yield return null;
                }
                if (drag)
                    for (int i = 1; i <= 10; i++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        stage.SetCustomInput(ToScreen(Vector2.Lerp(from, to, i / 10f)), true); yield return null;
                    }
                stage.SetCustomInput(ToScreen(to), false); yield return null;
                released = true;
            }
            finally
            {
                // 自定义指针只在本次操作期间生效，结束后立即还给玩家。
                try
                {
                    if (!released)
                    {
                        // ResetInputState 不发送 onTouchEnd，单独重置会让拆解长按继续计时。
                        stage.CancelClick(0); stage.SetCustomInput(ToScreen(to), false);
                        ProcessCustomInput.Invoke(stage, null);
                        GObject.draggingObject?.StopDrag(); stage.ResetInputState();
                    }
                }
                finally { CustomButton.SetValue(stage, false); CustomInput.SetValue(stage, oldCustom); }
            }
        }

        private IEnumerator Hold(JObject args, CancellationToken cancel)
        {
            var obj = Resolve(Required(args, "ref")); Focus(obj); yield return null;
            var point = obj.LocalToGlobal(new Vector2(obj.width / 2, obj.height / 2));
            yield return Pointer(point, point, false, cancel, obj.displayObject, Number(args, "milliseconds", 100, 5000) / 1000f);
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private static void CheckHit(DisplayObject expected)
        {
            // touchTarget 直接返回本次自定义位置的命中，不依赖按下前尚未分配的触摸编号。
            for (var hit = Stage.inst.touchTarget; hit != null; hit = hit.parent) if (hit == expected) return;
            throw new InvalidOperationException("这个控件的当前位置被遮挡或在滚动区域外，请先滚动或查看所属窗口。");
        }

        private IEnumerator MovePointer(GObject obj, CancellationToken cancel)
        {
            var stage = Stage.inst; bool oldCustom = (bool)CustomInput.GetValue(stage);
            try
            {
                stage.SetCustomInput(ToScreen(obj.LocalToGlobal(new Vector2(obj.width / 2, obj.height / 2))), false);
                cancel.ThrowIfCancellationRequested(); CheckHit(obj.displayObject); yield return null;
            }
            finally { CustomInput.SetValue(stage, oldCustom); }
        }

        private IEnumerator Hover(JObject args, CancellationToken cancel)
        {
            var obj = Resolve(Required(args, "ref")); Focus(obj); yield return null;
            var stage = Stage.inst; bool oldCustom = (bool)CustomInput.GetValue(stage);
            try
            {
                var point = ToScreen(obj.LocalToGlobal(new Vector2(obj.width / 2, obj.height / 2)));
                stage.SetCustomInput(point, false);
                cancel.ThrowIfCancellationRequested(); CheckHit(obj.displayObject);
                float end = Time.unscaledTime + 0.8f;
                while (Time.unscaledTime < end)
                {
                    cancel.ThrowIfCancellationRequested(); stage.SetCustomInput(point, false); yield return null;
                }
                owner.CompleteOperation(Snapshot("", "", 0));
            }
            finally { CustomInput.SetValue(stage, oldCustom); }
        }

        private IEnumerator Type(JObject args, CancellationToken cancel)
        {
            var field = Resolve(Required(args, "ref")) as GTextInput;
            if (field == null || !field.editable) throw new InvalidOperationException("目标不是可编辑输入框。");
            if (args["text"]?.Type != JTokenType.String || args["submit"]?.Type != JTokenType.Boolean) throw new InvalidOperationException("文字或提交参数无效。");
            Focus(field); GRoot.inst.focus = field; cancel.ThrowIfCancellationRequested();
            // ReplaceText 走原作字符校验、长度限制及 onChanged，而直接赋 text 会绕过输入限制。
            field.inputTextField.ReplaceText((string)args["text"]);
            if ((bool)args["submit"]) field.onSubmit.Call();
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private IEnumerator Scroll(JObject args, CancellationToken cancel)
        {
            var obj = Resolve(Required(args, "ref")); ScrollPane pane = null;
            for (GObject parent = obj; parent != null && pane == null; parent = Parent(parent)) pane = (parent as GComponent)?.scrollPane;
            if (pane == null) throw new InvalidOperationException("这个控件所在区域不能滚动。");
            Focus(obj); cancel.ThrowIfCancellationRequested();
            float pages = Number(args, "pages", 1, 5);
            switch (Required(args, "direction"))
            {
                case "up": pane.posY = Math.Max(0, pane.posY - pane.viewHeight * pages); break;
                case "down": pane.posY += pane.viewHeight * pages; break;
                case "left": pane.posX = Math.Max(0, pane.posX - pane.viewWidth * pages); break;
                case "right": pane.posX += pane.viewWidth * pages; break;
                default: throw new InvalidOperationException("滚动方向应为 up/down/left/right。");
            }
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private IEnumerator SetValue(JObject args, CancellationToken cancel)
        {
            var obj = Resolve(Required(args, "ref")); Focus(obj); cancel.ThrowIfCancellationRequested();
            if (args["value"]?.Type != JTokenType.Integer && args["value"]?.Type != JTokenType.Float) throw new InvalidOperationException("value 必须为数值。");
            double value = (double)args["value"];
            if (obj is GComboBox combo)
            {
                if (value != Math.Truncate(value) || value < 0 || value >= combo.items.Length) throw new InvalidOperationException("下拉选项序号超出范围。");
                combo.selectedIndex = (int)value; combo.onChanged.Call();
            }
            else if (obj is GSlider slider)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < slider.min || value > slider.max) throw new InvalidOperationException("滑块数值超出范围。");
                slider.value = value; slider.onChanged.Call();
            }
            else throw new InvalidOperationException("目标不是下拉框或滑块。");
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }

        private IEnumerator Advance(CancellationToken cancel)
        {
            owner.YieldToGame(); cancel.ThrowIfCancellationRequested(); owner.AdvanceGameDialogue();
            yield return null; yield return null;
            owner.CompleteOperation(Snapshot("", "", 0));
        }
        private IEnumerator Wait(int seconds, CancellationToken cancel)
        {
            float end = Time.unscaledTime + seconds;
            do { cancel.ThrowIfCancellationRequested(); yield return null; } while (Time.unscaledTime < end);
            owner.CompleteOperation(Snapshot("", "", 0));
        }
        private void Focus(GObject obj)
        {
            owner.YieldToGame();
            for (GObject parent = obj; parent != null; parent = Parent(parent))
                if (parent is Window window)
                {
                    var os = UnityEngine.Object.FindObjectOfType<OSManager>();
                    if (os != null && os.GetOpenWindows().Values.Contains(window)) os.SnapshotSelectWindow(window.name);
                    window.BringToFront(); break;
                }
        }
        private static Vector2 ToScreen(Vector2 point) => new Vector2(point.x, Stage.inst.size.y - point.y);
        private static InputEvent PointerEvent(GObject obj, int button, int count)
        {
            var point = obj.LocalToGlobal(new Vector2(obj.width / 2, obj.height / 2));
            var evt = new InputEvent();
            foreach (var pair in new Dictionary<string, object> { ["x"] = point.x, ["y"] = point.y, ["button"] = button, ["clickCount"] = count, ["touchId"] = 0 })
                typeof(InputEvent).GetProperty(pair.Key)?.GetSetMethod(true)?.Invoke(evt, new[] { pair.Value });
            return evt;
        }
        private static bool IsOwn(GObject obj)
        {
            for (var parent = obj; parent != null; parent = Parent(parent))
                if (parent.name != null && parent.name.StartsWith("InfantGodArchive_Chat", StringComparison.Ordinal)) return true;
            return false;
        }
        private static bool Visible(GObject obj)
        {
            if (obj == null || obj.isDisposed || obj.displayObject == null || !obj.onStage) return false;
            for (var parent = obj; parent != null; parent = Parent(parent)) if (!parent.visible || parent.isDisposed) return false;
            for (var display = obj.displayObject; display != null; display = display.parent) if (!display.visible || display.alpha <= 0) return false;
            return true;
        }
        private static bool Enabled(GObject obj)
        {
            for (var parent = obj; parent != null; parent = Parent(parent)) if (!parent.touchable || parent.grayed) return false;
            return true;
        }
        private static GObject Parent(GObject obj)
        {
            if (obj.parent != null) return obj.parent;
            for (var display = obj.displayObject?.parent; display != null; display = display.parent)
                if (display.gOwner != null && display.gOwner != obj) return display.gOwner;
            return null;
        }
        private static string DisplayText(GObject obj)
        {
            if (obj is GTextInput input) return input.displayAsPassword ? "[密码输入框]" : input.text ?? "";
            if (obj is GTextField field && RenderedText?.GetValue(field) is TextField rendered) return rendered.parsedText ?? Plain(field.text);
            return Plain(obj is GButton button ? button.title : obj.text);
        }
        private void AddLinks(GRichTextField rich, string windowName, string query, List<JObject> entries)
        {
            var field = rich.richTextField;
            for (int i = 0; i < field.htmlElementCount; i++)
            {
                var element = field.GetHtmlElementAt(i);
                if (element.type != FairyGUI.Utils.HtmlElementType.Link || element.htmlObject?.displayObject == null) continue;
                var target = element.htmlObject.displayObject;
                if (!target.visible || target.parent == null) continue;
                string href = element.GetString("href");
                if (query.Length > 0 && (href + " " + DisplayText(rich)).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string id = snapshotVersion + ":" + handles.Count; handles[id] = rich; linkTargets[id] = target;
                var rect = target.GetBounds(Stage.inst);
                entries.Add(new JObject { ["ref"] = id, ["window"] = windowName, ["type"] = "TextLink", ["text"] = element.text ?? href, ["link"] = href,
                    ["enabled"] = Enabled(rich), ["clickable"] = true, ["x"] = Math.Round(rect.x), ["y"] = Math.Round(rect.y), ["width"] = Math.Round(rect.width), ["height"] = Math.Round(rect.height) });
            }
        }
        private static void AddNativeText(DisplayObject display, GObject ownerObject, string windowName, string query, List<JObject> entries)
        {
            if (!display.visible || display.alpha <= 0 || display.gOwner != null && display.gOwner != ownerObject) return;
            if (display is GoWrapper wrapper && wrapper.wrapTarget != null)
                foreach (var behaviour in wrapper.wrapTarget.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (behaviour == null || !behaviour.isActiveAndEnabled || behaviour.GetType().Namespace != "TMPro") continue;
                    var property = behaviour.GetType().GetProperty("text");
                    string text = property?.GetValue(behaviour, null) as string;
                    if (string.IsNullOrWhiteSpace(text) || query.Length > 0 && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    entries.Add(new JObject { ["window"] = windowName, ["type"] = behaviour.GetType().Name, ["text"] = Clip(Plain(text), 8000), ["clickable"] = false });
                }
            if (display is Container container)
                foreach (var child in container.GetChildren()) AddNativeText(child, ownerObject, windowName, query, entries);
        }
        private static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[/?(?:color|size|font|b|i|u|url)(?:=[^\]]*)?\]", "");
    }
}
