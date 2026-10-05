using System;
using FairyGUI;
using UnityEngine;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal static class NativeChatWidgets
    {
        internal static readonly Color Paper = new Color32(229, 225, 181, 255);
        internal static readonly Color Ink = new Color32(42, 48, 46, 255);
        internal static readonly Color Muted = new Color32(98, 108, 91, 255);

        internal static GTextField Label(string text, int size, Color color)
        {
            var result = new GTextField { text = text, touchable = false, singleLine = false };
            var format = result.textFormat; format.font = UIConfig.defaultFont; format.size = size; format.color = color; result.textFormat = format; return result;
        }
        internal static GTextInput Input(string prompt, bool password, bool multiline = false)
        {
            var result = new GTextInput { promptText = prompt, singleLine = !multiline, displayAsPassword = password, border = 1, borderColor = Ink, keyboardInput = true };
            var format = result.textFormat; format.font = UIConfig.defaultFont; format.size = 18; format.color = Ink; result.textFormat = format;
            result.inputTextField.backgroundColor = new Color32(244, 239, 201, 255);
            return result;
        }
        internal static GButton Button(string title, Action clicked)
        {
            var button = new GButton { name = "InfantGodArchive_ChatButton", opaque = true };
            var fill = new GGraph { name = "fill", touchable = false }; button.AddChild(fill);
            var label = Label(title, 16, Paper); label.name = "label"; label.align = AlignType.Center; label.verticalAlign = VertAlignType.Middle; button.AddChild(label);
            button.onClick.Add(context => { context.StopPropagation(); clicked(); });
            button.onRollOver.Add(() => fill.DrawRect(button.width, button.height, 1, Ink, Muted));
            button.onRollOut.Add(() => fill.DrawRect(button.width, button.height, 1, Ink, Ink));
            return button;
        }
        internal static void SetButton(GButton button, float x, float y, float width, float height)
        {
            button.SetXY(x, y); button.SetSize(width, height);
            button.GetChild("fill").asGraph.DrawRect(width, height, 1, Ink, Ink);
            button.GetChild("label").SetSize(width, height);
        }
        internal static void Text(GButton button, string value)
        {
            if (button == null || button.isDisposed) return;
            var label = button.GetChild("label"); if (label != null && label.text != value) label.text = value;
        }
    }
}
