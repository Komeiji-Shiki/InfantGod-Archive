using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using UnityEngine;
using static Graywill.InfantGodCodex.LiveChat.NativeChatWidgets;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class NativeChatPanel : IDisposable
    {
        private readonly LiveChatController owner;
        private readonly List<ChatEntry> entries = new List<ChatEntry>();
        private Window window;
        private GComponent pane;
        private GButton entry;
        private GComponent panel;
        private GGraph background;
        private GTextField heading;
        private GTextField status;
        private GList history;
        private GTextInput input;
        private GButton send;
        private GButton stop;
        private GButton config;
        private GButton clear;
        private GButton back;
        private bool dirty;
        private bool hasLayout;
        private float lastWidth;
        private float lastHeight;
        private float lastRefresh;
        private readonly HashSet<string> expandedTools = new HashSet<string>();

        internal NativeChatPanel(LiveChatController owner, IEnumerable<ChatEntry> initial)
        {
            this.owner = owner; ReplaceEntries(initial);
        }
        internal bool IsOpen => window != null && !window.isDisposed && window.isShowing && panel != null && !panel.isDisposed && panel.visible;
        internal bool InputFocused
        {
            get
            {
                for (GObject focused = GRoot.inst?.focus; focused != null; focused = focused.parent)
                    if (focused == panel) return true;
                return false;
            }
        }

        internal void Attach(Window value)
        {
            DisposeUi(); window = value; pane = window?.contentPane;
            if (pane == null || pane.isDisposed) return;
            entry = Button("自由交谈 · F9", owner.Toggle);
            entry.name = "InfantGodArchive_ChatEntry";
            pane.AddChild(entry);
            panel = new GComponent { name = "InfantGodArchive_ChatPanel", opaque = true, visible = false };
            pane.AddChild(panel);
            background = new GGraph { touchable = false }; panel.AddChild(background);
            heading = Label("爱式塔 / 自由交谈", 20, Ink); panel.AddChild(heading);
            status = Label("填写 API 设置后，就可以打字交流了。", 14, Muted); panel.AddChild(status);
            config = Button("连接设置", () => ShowSettings("")); panel.AddChild(config);
            clear = Button("新对话", owner.NewConversation); panel.AddChild(clear);
            back = Button("返回剧情", owner.Toggle); panel.AddChild(back);

            // 使用原作对话记录中的列表，沿用它已有的滚动、裁切和像素滚动条。
            var logTemplate = UIPackage.CreateObject("LiveStreamWindow", "DialogueLog").asCom;
            history = logTemplate.GetChild("List_LogItem").asList;
            logTemplate.RemoveChild(history);
            logTemplate.Dispose();
            history.name = "InfantGodArchive_ChatHistory";
            history.RemoveChildrenToPool();
            history.itemRenderer = RenderItem;
            history.onClickItem.Add(OnItemClick);
            panel.AddChild(history);
            history.SetVirtual();
            input = Input("输入想说的话，Enter 发送", false);
            input.name = "InfantGodArchive_ChatInput";
            input.onSubmit.Add(Send);
            panel.AddChild(input);
            send = Button("发送", Send); panel.AddChild(send);
            stop = Button("停止", owner.Stop); panel.AddChild(stop);
            hasLayout = false; Layout(); dirty = true;
        }

        internal void Tick(bool busy)
        {
            if (pane == null || pane.isDisposed) return;
            if (!hasLayout || pane.width != lastWidth || pane.height != lastHeight) Layout();
            Text(entry, busy ? "交谈中 · F9" : "自由交谈 · F9");
            stop.enabled = busy;
            Text(send, busy ? "发送新指令" : "发送");
            if (dirty && Time.unscaledTime - lastRefresh >= 0.12f)
            {
                dirty = false; lastRefresh = Time.unscaledTime;
                bool follow = history.scrollPane == null || history.scrollPane.isBottomMost;
                history.numItems = entries.Count;
                history.RefreshVirtualList();
                if (follow) history.scrollPane?.ScrollBottom(false);
            }
        }

        internal void Open()
        {
            if (panel == null || panel.isDisposed) return;
            panel.visible = true; dirty = true;
            if (string.IsNullOrWhiteSpace(owner.Settings.BaseUrl) || string.IsNullOrWhiteSpace(owner.Settings.Model)) ShowSettings("");
            else GRoot.inst.focus = input;
        }
        internal void Close()
        {
            if (InputFocused) GRoot.inst.focus = null;
            if (panel != null && !panel.isDisposed) panel.visible = false;
        }
        internal void SetStatus(string value) { if (status != null && !status.isDisposed) status.text = value; }

        internal void UpdateEntry(ChatEntry value)
        {
            var entryValue = entries.FirstOrDefault(e => e.Id == value.Id);
            if (entryValue == null) { entryValue = new ChatEntry { Id = value.Id }; entries.Add(entryValue); }
            entryValue.Role = value.Role; entryValue.Title = value.Title; entryValue.Text = value.Text; entryValue.Detail = value.Detail; entryValue.Complete = value.Complete;
            if (value.Role == "tool") SetStatus(value.Title + (value.Complete ? " · " + value.Text : "…"));
            dirty = true;
        }
        internal void ReplaceEntries(IEnumerable<ChatEntry> values)
        {
            entries.Clear(); expandedTools.Clear();
            foreach (var value in values) UpdateEntry(value);
            dirty = true;
        }
        private void Send()
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text)) return;
            if (owner.Submit(input.text)) input.text = "";
        }

        private void RenderItem(int index, GObject value)
        {
            if (index < 0 || index >= entries.Count) return;
            var data = entries[index]; var row = value.asCom; row.data = data.Id;
            float width = Math.Max(100, history.width - 24);
            row.SetSize(width, 80);
            foreach (var child in row.GetChildren()) child.visible = false;
            var title = row.GetChild("title") as GRichTextField;
            var speaker = row.GetChild("InfantGodArchive_ChatCaption") as GTextField;
            if (speaker == null) { speaker = Label("", 14, Muted); speaker.name = "InfantGodArchive_ChatCaption"; row.AddChild(speaker); }
            speaker.visible = true; speaker.SetXY(8, 7); speaker.SetSize(width - 16, 22);
            speaker.text = data.Title + (data.Role == "tool" ? "  ·  点击查看操作详情" : "");
            var format = speaker.textFormat; format.color = data.Role == "user" ? new Color32(151, 77, 33, 255) : Muted; speaker.textFormat = format;
            if (title != null)
            {
                title.visible = true; title.UBBEnabled = false; title.templateVars = null;
                title.autoSize = AutoSizeType.Height; title.SetXY(8, 31); title.width = width - 20;
                format = title.textFormat; format.font = UIConfig.defaultFont; format.size = 18; format.color = Ink; title.textFormat = format;
                string message = data.Text;
                if (string.IsNullOrEmpty(message)) message = data.Complete ? "" : "…";
                if (data.Role == "tool" && expandedTools.Contains(data.Id)) message += "\n\n" + GameToolbox.Clip(data.Detail, 9000);
                title.text = message;
                row.height = Math.Max(65, title.y + title.textHeight + 15);
            }
        }
        private void OnItemClick(EventContext context)
        {
            var row = context.data as GObject; string id = row?.data as string;
            if (id == null || entries.FirstOrDefault(e => e.Id == id)?.Role != "tool") return;
            if (!expandedTools.Add(id)) expandedTools.Remove(id);
            dirty = true;
        }

        internal void ShowSettings(string message) => owner.OpenSettings(message);

        private void Layout()
        {
            if (pane == null || pane.isDisposed) return;
            lastWidth = pane.width; lastHeight = pane.height; hasLayout = true;
            SetButton(entry, Math.Max(16, pane.width - 320), 8, 158, 30);
            var nativeLine = pane.GetChild("Panel_LinePresenter");
            float top = Math.Max(60, Math.Min(nativeLine?.y ?? pane.height * 0.48f, pane.height * 0.48f));
            float width = Math.Max(340, pane.width - 32), height = Math.Max(240, pane.height - top - 16);
            panel.SetXY(16, top); panel.SetSize(width, height);
            background.DrawRect(width, height, 2, Ink, Paper);
            heading.SetXY(16, 12); heading.SetSize(Math.Max(120, width - 380), 29);
            SetButton(config, width - 326, 10, 100, 30); SetButton(clear, width - 218, 10, 92, 30); SetButton(back, width - 118, 10, 102, 30);
            status.SetXY(16, 45); status.SetSize(width - 32, 24);
            history.SetXY(12, 76); history.SetSize(width - 24, height - 139);
            input.SetXY(16, height - 49); input.SetSize(width - 242, 34);
            SetButton(send, width - 218, height - 49, 118, 34); SetButton(stop, width - 92, height - 49, 76, 34);

            dirty = true;
        }

        private void DisposeUi()
        {
            if (entry != null && !entry.isDisposed) entry.Dispose();
            if (panel != null && !panel.isDisposed) panel.Dispose();
            entry = null; panel = null; history = null; input = null; pane = null; window = null;
        }
        public void Dispose() => DisposeUi();
    }
}
