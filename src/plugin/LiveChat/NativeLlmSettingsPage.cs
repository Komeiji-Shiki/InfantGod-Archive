using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core.OSSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Graywill.InfantGodCodex.LiveChat.NativeChatWidgets;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class NativeLlmSettingsPage : IDisposable
    {
        private const string PageName = "InfantGodArchive_LLM";
        private readonly LiveChatController owner;
        private readonly List<Binding> bindings = new List<Binding>();
        private float nextProbe;
        private bool openPending;

        internal NativeLlmSettingsPage(LiveChatController owner) { this.owner = owner; }
        internal bool InputFocused => bindings.Any(b => b.InputFocused);
        internal bool IsOpen => bindings.Any(b => b.IsOpen);
        internal bool NativeSettingsVisible => bindings.Any(b => !b.Panel.isDisposed && b.Panel.onStage && AncestorsVisible(b.Panel));

        internal void Tick()
        {
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + 0.5f;
                bindings.RemoveAll(b => b.Panel.isDisposed);
                if (GRoot.inst != null) Find(GRoot.inst);
                foreach (var ui in UnityEngine.Object.FindObjectsOfType<UIPanel>())
                    if (ui.ui != null && !ui.ui.isDisposed) Find(ui.ui);
            }
            foreach (var binding in bindings) binding.Layout();
            if (openPending)
            {
                var shown = bindings.FirstOrDefault(b => b.Panel.onStage && AncestorsVisible(b.Panel));
                if (shown != null) { shown.Open(); openPending = false; }
            }
        }
        private void Find(GComponent parent)
        {
            if (parent.isDisposed) return;
            if (parent.GetType().FullName == "MainMenu.UI_Settings")
            {
                if (!bindings.Any(b => b.Panel == parent)) bindings.Add(new Binding(owner, parent));
                return;
            }
            foreach (var child in parent.GetChildren()) if (child is GComponent component) Find(component);
        }
        internal void Open()
        {
            owner.StopForSettings();
            var os = UnityEngine.Object.FindObjectOfType<OSManager>();
            if (os != null) AccessTools.Method(typeof(OSManager), "SetWorkspaceSettingsVisible").Invoke(os, new object[] { true });
            openPending = true; nextProbe = 0; Tick();
        }
        private static bool AncestorsVisible(GObject obj)
        {
            for (var parent = obj; parent != null; parent = parent.parent) if (!parent.visible) return false;
            return true;
        }
        public void Dispose() { foreach (var binding in bindings) binding.Dispose(); bindings.Clear(); }

        private sealed class Binding : IDisposable
        {
            internal readonly GComponent Panel;
            private readonly LiveChatController owner;
            private readonly Controller pages;
            private readonly GButton tab;
            private readonly List<GButton> nativeTabs;
            private readonly GComponent view;
            private readonly GGraph background;
            private readonly GList scroll;
            private readonly GComponent form;
            private readonly GTextField title;
            private readonly GComponent basic;
            private readonly GComponent advanced;
            private readonly GComponent mcpArea;
            private readonly GComponent contextArea;
            private readonly GButton switchPage;
            private readonly GButton mcpTab;
            private readonly GButton contextTab;
            private readonly GButton cacheToggle;
            private readonly GButton usageToggle;
            private readonly GButton compact;
            private readonly GTextInput compactThreshold;
            private readonly GTextInput recentTurns;
            private readonly GTextField contextStatus;
            private readonly GButton mcpToggle;
            private readonly GTextField mcpStatus;
            private readonly GButton protocol;
            private readonly GButton persona;
            private readonly GButton save;
            private readonly GButton back;
            private readonly GTextField status;
            private readonly GTextInput url;
            private readonly GTextInput key;
            private readonly GTextInput model;
            private readonly GTextInput temperature;
            private readonly GTextInput maxTokens;
            private readonly GTextInput rounds;
            private readonly GTextInput timeout;
            private readonly GTextInput extra;
            private readonly GTextInput parameters;
            private readonly GTextInput headers;
            private string chosenProtocol;
            private string chosenPersona;
            private string previousPage;
            private int section;
            private bool chosenMcp;
            private bool chosenCache;
            private bool chosenUsage;
            private bool lastVisible;
            private float lastWidth = -1;
            private float lastHeight = -1;
            private readonly EventCallback0 onPageChanged;

            internal bool IsOpen => !Panel.isDisposed && Panel.onStage && AncestorsVisible(Panel) && pages.selectedPage == PageName;
            internal bool InputFocused
            {
                get { for (var focused = GRoot.inst?.focus; focused != null; focused = focused.parent) if (focused == view) return true; return false; }
            }

            internal Binding(LiveChatController owner, GComponent panel)
            {
                this.owner = owner; Panel = panel; pages = panel.GetController("Case");
                nativeTabs = FindNativeTabs(panel).ToList();
                pages.AddPage(PageName);
                tab = UIPackage.CreateObjectFromURL(nativeTabs[0].resourceURL).asButton;
                tab.name = "InfantGodArchive_ChatSettingsTab"; tab.title = "LLM";
                tab.onClick.Add(Open); panel.AddChild(tab);
                view = new GComponent { name = "InfantGodArchive_ChatSettingsPage", opaque = true, visible = false }; panel.AddChild(view);
                background = new GGraph { touchable = false }; view.AddChild(background);
                title = Label("爱式塔自由交谈", 25, Ink); title.SetXY(24, 17); title.SetSize(380, 35); view.AddChild(title);
                switchPage = Button("高级参数", () => { section = section == 0 ? 1 : 0; ShowSection(); }); view.AddChild(switchPage);
                mcpTab = Button("本机 MCP", () => { section = 2; ShowSection(); }); view.AddChild(mcpTab);
                contextTab = Button("上下文", () => { section = 3; ShowSection(); }); view.AddChild(contextTab);
                // 复用原作列表的裁切和像素滚动条，表单保持原字号，空间不足时自然滚动。
                if (UIPackage.GetByName("LiveStreamWindow") == null) UIPackage.AddPackage("FairyGUI/UI/LiveStreamWindow");
                var logTemplate = UIPackage.CreateObject("LiveStreamWindow", "DialogueLog").asCom;
                scroll = logTemplate.GetChild("List_LogItem").asList;
                logTemplate.RemoveChild(scroll); logTemplate.Dispose(); scroll.RemoveChildrenToPool();
                scroll.name = "InfantGodArchive_ChatSettingsScroll";
                scroll.layout = ListLayoutType.SingleColumn; scroll.selectionMode = ListSelectionMode.None; scroll.autoResizeItem = false;
                view.AddChild(scroll);
                form = new GComponent(); scroll.AddChild(form);
                basic = new GComponent { name = "InfantGodArchive_ChatBasic" }; form.AddChild(basic);
                advanced = new GComponent { name = "InfantGodArchive_ChatAdvanced", visible = false }; form.AddChild(advanced);
                mcpArea = new GComponent { name = "InfantGodArchive_ChatMcp", visible = false }; form.AddChild(mcpArea);
                contextArea = new GComponent { name = "InfantGodArchive_ChatContext", visible = false }; form.AddChild(contextArea);
                url = Field(basic, "API 地址", "https://服务地址/v1", 0);
                model = Field(basic, "模型名称", "服务提供的模型 ID", 51);
                key = Field(basic, "API Key", "本地服务不需要时可以留空", 102, true);
                protocol = Button("", () => { chosenProtocol = chosenProtocol == "responses" ? "chat-completions" : "responses"; UpdateButtons(); }); basic.AddChild(protocol);
                persona = Button("", () => { var names = new[] { "current", "Empathy", "Tech", "Troll", "Kitsch" }; chosenPersona = names[(Array.IndexOf(names, chosenPersona) + 1) % names.Length]; UpdateButtons(); }); basic.AddChild(persona);
                temperature = Field(basic, "Temperature", "留空使用服务默认值", 207);
                maxTokens = Field(basic, "回复 Token 上限", "留空使用服务默认值", 258);
                rounds = Field(basic, "连续调用轮数", "48", 309);
                timeout = Field(basic, "请求超时（秒）", "300", 360);
                extra = Area(advanced, "角色补充设定", "希望爱式塔记住的语气、偏好或额外要求。", 0, 94, false);
                parameters = Area(advanced, "其他请求参数 JSON", "例如 {\"top_p\":0.9}；推理强度等模型专有参数也可在这里填写。", 134, 105, false);
                headers = Area(advanced, "自定义请求头 JSON", "例如 {\"X-Custom-Header\":\"value\"}", 279, 85, true);
                var mcpIntro = Label("让本机的 Codex 或其他 MCP 客户端操作这台游戏电脑。\n使用标准输入输出连接；游戏运行时接受工具调用。", 19, Ink);
                mcpIntro.SetXY(0, 0); mcpIntro.SetSize(882, 85); mcpArea.AddChild(mcpIntro);
                mcpToggle = Button("", () => { chosenMcp = !chosenMcp; UpdateButtons(); }); mcpArea.AddChild(mcpToggle); SetButton(mcpToggle, 0, 99, 330, 40);
                mcpStatus = Label("", 17, Muted); mcpStatus.SetXY(0, 153); mcpStatus.SetSize(882, 50); mcpArea.AddChild(mcpStatus);
                var codex = Button("复制 Codex 接入命令", () => { GUIUtility.systemCopyBuffer = owner.McpCommand; status.text = "已复制 Codex 命令。"; }); mcpArea.AddChild(codex); SetButton(codex, 0, 221, 360, 42);
                var config = Button("复制通用 MCP 配置", () => { GUIUtility.systemCopyBuffer = owner.McpConfig; status.text = "已复制通用 MCP 配置。"; }); mcpArea.AddChild(config); SetButton(config, 379, 221, 360, 42);
                var help = Label("启用后点击右下角保存。外部工具操作会记入自由交谈窗口，\n按 Esc 或聊天窗口中的“停止”可以中止当前操作。", 17, Ink);
                help.SetXY(0, 289); help.SetSize(882, 93); mcpArea.AddChild(help);
                compactThreshold = Field(contextArea, "自动压缩阈值", "Token 数；0 表示仅手动压缩", 0);
                recentTurns = Field(contextArea, "保留最近回合", "完整保留的近期对话回合数", 55);
                cacheToggle = Button("", () => { chosenCache = !chosenCache; UpdateButtons(); }); contextArea.AddChild(cacheToggle); SetButton(cacheToggle, 0, 112, 434, 36);
                usageToggle = Button("", () => { chosenUsage = !chosenUsage; UpdateButtons(); }); contextArea.AddChild(usageToggle); SetButton(usageToggle, 448, 112, 434, 36);
                contextStatus = Label("", 17, Ink); contextStatus.SetXY(0, 166); contextStatus.SetSize(882, 177); contextArea.AddChild(contextStatus);
                compact = Button("现在整理上下文", owner.CompactContext); contextArea.AddChild(compact); SetButton(compact, 0, 353, 260, 38);
                status = Label("", 15, Muted); view.AddChild(status);
                save = Button("保存设置", Save); view.AddChild(save);
                back = Button("返回原设置", () => pages.selectedPage = previousPage ?? pages.GetPageName(0)); view.AddChild(back);
                onPageChanged = Changed; pages.onChanged.Add(onPageChanged); Layout();
            }

            internal void Open()
            {
                if (pages.selectedPage != PageName) previousPage = pages.selectedPage;
                pages.selectedPage = PageName;
            }
            private void Changed()
            {
                view.visible = pages.selectedPage == PageName;
                tab.selected = view.visible;
                if (view.visible) { owner.StopForSettings(); Fill(); }
            }
            private void Fill()
            {
                var value = owner.Settings;
                url.text = value.BaseUrl; model.text = value.Model; key.text = value.ApiKey;
                chosenProtocol = value.Protocol; chosenPersona = value.Persona;
                chosenMcp = value.McpEnabled;
                chosenCache = value.SendPromptCacheKey; chosenUsage = value.RequestStreamUsage;
                compactThreshold.text = value.AutoCompactTokenThreshold.ToString(); recentTurns.text = value.KeepRecentTurns.ToString();
                var request = (JObject)value.RequestParameters.DeepClone();
                temperature.text = request["temperature"]?.ToString() ?? ""; request.Remove("temperature");
                string tokenKey = chosenProtocol == "responses" ? "max_output_tokens" : "max_tokens";
                maxTokens.text = request[tokenKey]?.ToString() ?? ""; request.Remove(tokenKey);
                rounds.text = value.MaxToolRounds.ToString(); timeout.text = value.TimeoutSeconds.ToString();
                extra.text = value.ExtraInstructions; parameters.text = request.Count == 0 ? "" : request.ToString(Formatting.Indented);
                headers.text = value.RequestHeaders.Count == 0 ? "" : value.RequestHeaders.ToString(Formatting.None);
                status.text = "保存后，在聊天窗口按 F9 与爱式塔自由交谈。";
                UpdateButtons(); ShowSection();
            }
            private void UpdateButtons()
            {
                Text(protocol, "协议：" + (chosenProtocol == "responses" ? "Responses" : "Chat Completions"));
                Text(persona, "人格：" + (chosenPersona == "current" ? "跟随原作" : LiveChatController.PersonaName(chosenPersona)));
                Text(mcpToggle, "本机 MCP：" + (chosenMcp ? "开启" : "关闭") + "（保存后生效）");
                Text(cacheToggle, "发送固定缓存标识：" + (chosenCache ? "开启" : "关闭"));
                Text(usageToggle, "请求 Chat 流式用量：" + (chosenUsage ? "开启" : "关闭"));
            }
            private void ShowSection()
            {
                advanced.visible = section == 1; basic.visible = section == 0; mcpArea.visible = section == 2; contextArea.visible = section == 3;
                Text(switchPage, section != 0 ? "基础参数" : "高级参数");
                scroll.scrollPane?.ScrollTop(false);
            }
            private void Save()
            {
                try
                {
                    var request = string.IsNullOrWhiteSpace(parameters.text) ? new JObject() : JObject.Parse(parameters.text);
                    var requestHeaders = string.IsNullOrWhiteSpace(headers.text) ? new JObject() : JObject.Parse(headers.text);
                    if (!string.IsNullOrWhiteSpace(temperature.text))
                    {
                        if (!double.TryParse(temperature.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidOperationException("Temperature 请填写数值或留空。");
                        request["temperature"] = number;
                    }
                    if (!string.IsNullOrWhiteSpace(maxTokens.text))
                    {
                        if (!int.TryParse(maxTokens.text, out var limit) || limit < 1) throw new InvalidOperationException("Token 上限请填写正整数或留空。");
                        request[chosenProtocol == "responses" ? "max_output_tokens" : "max_tokens"] = limit;
                    }
                    if (!int.TryParse(rounds.text, out var count) || !int.TryParse(timeout.text, out var seconds)) throw new InvalidOperationException("调用轮数和超时请填写整数。");
                    if (!int.TryParse(compactThreshold.text, out var threshold) || !int.TryParse(recentTurns.text, out var recent)) throw new InvalidOperationException("上下文阈值与保留回合数请填写整数。");
                    owner.SaveSettings(new ApiSettings { BaseUrl = url.text.Trim(), ApiKey = key.text.Trim(), Model = model.text.Trim(), Protocol = chosenProtocol, Persona = chosenPersona,
                        ExtraInstructions = extra.text, MaxToolRounds = count, TimeoutSeconds = seconds, RequestParameters = request, RequestHeaders = requestHeaders, McpEnabled = chosenMcp,
                        SendPromptCacheKey = chosenCache, RequestStreamUsage = chosenUsage, AutoCompactTokenThreshold = threshold, KeepRecentTurns = recent });
                    status.text = "已保存。返回聊天窗口后即可发送消息。";
                }
                catch (Exception error) { status.text = error.GetBaseException().Message; }
            }
            internal void Layout()
            {
                if (Panel.isDisposed) return;
                mcpStatus.text = owner.McpStatus;
                compact.enabled = !owner.Busy;
                if (section == 3 && IsOpen)
                {
                    var info = owner.ContextStatus();
                    contextStatus.text = "活动消息：" + Value(info["active_messages"]) + "  ·  预计输入 Token：" + Value(info["estimated_tokens"])
                        + "\n最近请求输入：" + Value(info["input_tokens"]) + "  ·  缓存命中：" + Value(info["cached_tokens"]) + "  ·  输出：" + Value(info["output_tokens"])
                        + "\n压缩次数：" + Value(info["compactions"]) + "  ·  原始归档：" + Value(info["archive_count"])
                        + "\n保留玩家要求：" + Value(info["preserved_player_requests"]) + "  ·  保留操作记录：" + Value(info["preserved_operations"])
                        + "\n" + owner.ContextOperationStatus;
                }
                bool visible = Panel.onStage && AncestorsVisible(Panel);
                if (visible && !lastVisible && pages.selectedPage == PageName) Fill();
                lastVisible = visible;
                Rect header = LayoutEntryTab();
                if (lastWidth == Panel.width && lastHeight == Panel.height && view.y == header.yMax + header.height / 3) return;
                lastWidth = Panel.width; lastHeight = Panel.height;
                float margin = nativeTabs[0].TransformRect(new Rect(0, 0, nativeTabs[0].width, nativeTabs[0].height), Panel).xMin;
                float scale = header.height / 48;
                float top = header.yMax + header.height / 3;
                var returnButton = Panel.GetChild("Btn_ReturntoHub");
                float bottom = returnButton != null ? returnButton.y - header.height / 4 : Panel.height - margin / 2;
                float width = (Panel.width - margin * 2) / scale, height = Math.Max(210, (bottom - top) / scale);
                view.SetSize(width, height); view.SetScale(scale, scale); view.SetXY(margin, top);
                background.DrawRect(width, height, 0, Ink, Paper);
                title.SetSize(Math.Max(100, width - 450), 35);
                scroll.SetXY(24, 76); scroll.SetSize(width - 48, Math.Max(60, height - 155));
                float fieldWidth = scroll.width - 24;
                form.SetSize(fieldWidth, 411);
                foreach (var area in new[] { basic, advanced, mcpArea, contextArea })
                {
                    area.SetXY(0, 0); area.SetSize(fieldWidth, 411);
                    foreach (var child in area.GetChildren())
                        if (child is GTextInput || child is GTextField) child.width = Math.Max(60, fieldWidth - child.x);
                }
                SetButton(switchPage, width - 144, 18, 120, 32);
                SetButton(mcpTab, width - 278, 18, 122, 32);
                SetButton(contextTab, width - 396, 18, 106, 32);
                float half = (fieldWidth - 14) / 2;
                SetButton(protocol, 0, 156, half, 36); SetButton(persona, half + 14, 156, half, 36);
                SetButton(cacheToggle, 0, 112, half, 36); SetButton(usageToggle, half + 14, 112, half, 36);
                status.SetXY(24, height - 62); status.SetSize(width - 354, 47);
                SetButton(save, width - 292, height - 57, 122, 36); SetButton(back, width - 158, height - 57, 134, 36);
            }
            private static IEnumerable<GButton> FindNativeTabs(GComponent parent)
            {
                foreach (var child in parent.GetChildren())
                {
                    if (child is GButton button && button.GetType().Name == "UI_Btn_Settings" && !button.name.StartsWith("InfantGod", StringComparison.Ordinal)) yield return button;
                    else if (child is GComponent component)
                        foreach (var nested in FindNativeTabs(component)) yield return nested;
                }
            }
            private Rect LayoutEntryTab()
            {
                var row = nativeTabs.Where(button => !button.isDisposed)
                    .Select(button => new { Button = button, Bounds = button.TransformRect(new Rect(0, 0, button.width, button.height), Panel) })
                    .OrderBy(item => item.Bounds.xMin).ToArray();
                if (row.Length == 0) return new Rect(0, 0, 384, 96);
                var last = row[row.Length - 1];
                float gap = row.Length > 1 ? Math.Max(0, last.Bounds.xMin - row[row.Length - 2].Bounds.xMax) : 0;
                float start = last.Bounds.xMax + gap;
                // 右侧接原生第四、第五格，放不下时两个新页签一起换行，保持原字号。
                float right = Panel.width - Math.Max(row[0].Bounds.xMin, gap);
                bool secondRow = start + last.Bounds.width * 2 + gap > right;
                float x = secondRow ? row[0].Bounds.xMin : start;
                float y = secondRow ? last.Bounds.yMax + gap : last.Bounds.yMin;
                tab.SetXY(x, y); tab.SetSize(last.Bounds.width, last.Bounds.height);
                return new Rect(x, y, last.Bounds.width, last.Bounds.height);
            }
            private static string Value(JToken value) => value == null || value.Type == JTokenType.Null ? "未返回" : value.ToString();
            private static GTextInput Field(GComponent parent, string caption, string prompt, float y, bool password = false)
            {
                var label = Label(caption, 17, Ink); label.SetXY(0, y + 6); label.SetSize(170, 30); parent.AddChild(label);
                var input = Input(prompt, password); input.SetXY(184, y); input.SetSize(698, 36); parent.AddChild(input); return input;
            }
            private static GTextInput Area(GComponent parent, string caption, string prompt, float y, float height, bool password)
            {
                var label = Label(caption, 17, Ink); label.SetXY(0, y); label.SetSize(882, 26); parent.AddChild(label);
                var input = Input(prompt, password, !password); input.SetXY(0, y + 30); input.SetSize(882, height); parent.AddChild(input); return input;
            }
            public void Dispose()
            {
                if (Panel.isDisposed) return;
                if (pages.selectedPage == PageName) pages.selectedPage = previousPage ?? pages.GetPageName(0);
                pages.onChanged.Remove(onPageChanged); pages.RemovePage(PageName);
                view.Dispose(); tab.Dispose();
            }
        }
    }
}
