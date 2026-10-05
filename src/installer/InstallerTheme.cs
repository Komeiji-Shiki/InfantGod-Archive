using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Graywill.InfantGodInstaller
{
    internal static class InstallerTheme
    {
        internal static readonly Color Paper = Color.FromArgb(238, 235, 198);
        internal static readonly Color Panel = Color.FromArgb(247, 243, 212);
        internal static readonly Color Ink = Color.FromArgb(52, 57, 56);
        internal static readonly Color Muted = Color.FromArgb(105, 109, 94);
        internal static readonly Color Accent = Color.FromArgb(251, 139, 53);
        private static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        private static IntPtr fontBytes;
        internal static readonly Bitmap Portrait;

        static InstallerTheme()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var compressed = assembly.GetManifestResourceStream("InfantGodArchive.PixelFont.gz"))
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            using (var memory = new MemoryStream())
            {
                gzip.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                fontBytes = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, fontBytes, bytes.Length);
                Fonts.AddMemoryFont(fontBytes, bytes.Length);
            }
            using (var image = assembly.GetManifestResourceStream("InfantGodArchive.AistaltTech.png"))
            using (var bitmap = new Bitmap(image)) Portrait = new Bitmap(bitmap);
        }

        internal static Font Pixel(float pixels) { return new Font(Fonts.Families[0], pixels, FontStyle.Regular, GraphicsUnit.Pixel); }

        internal static void Dispose()
        {
            Portrait.Dispose();
            Fonts.Dispose();
            Marshal.FreeCoTaskMem(fontBytes);
            fontBytes = IntPtr.Zero;
        }
    }

    internal sealed class PixelButton : Button
    {
        private bool hover;
        private bool pressed;

        internal PixelButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs args) { base.OnMouseDown(args); pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs args) { base.OnMouseUp(args); pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics graphics = args.Graphics;
            graphics.Clear(Parent.BackColor);
            int offset = pressed ? 2 : 0;
            Rectangle face = new Rectangle(offset, offset, Width - 4, Height - 4);
            using (var shadow = new SolidBrush(InstallerTheme.Ink)) graphics.FillRectangle(shadow, 3, 3, Width - 3, Height - 3);
            Color fill = !Enabled ? Color.FromArgb(218, 216, 185) : (hover && BackColor != InstallerTheme.Accent ? Color.FromArgb(255, 248, 207) : BackColor);
            using (var background = new SolidBrush(fill)) graphics.FillRectangle(background, face);
            using (var border = new Pen(InstallerTheme.Ink, 2)) graphics.DrawRectangle(border, face.X + 1, face.Y + 1, face.Width - 2, face.Height - 2);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using (var ink = new SolidBrush(Enabled ? ForeColor : InstallerTheme.Muted))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(Text, Font, ink, face, format);
            if (Focused && ShowFocusCues)
            using (var focus = new Pen(InstallerTheme.Accent, 2)) graphics.DrawRectangle(focus, 5, 5, Width - 14, Height - 14);
        }
    }

    internal sealed class PortraitPanel : Panel
    {
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            args.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            args.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            int size = Math.Min(Width, Height) - 8;
            args.Graphics.DrawImage(InstallerTheme.Portrait, new Rectangle(4, 4, size, size));
            using (var border = new Pen(InstallerTheme.Ink, 2)) args.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        }
    }

    internal sealed class ChromeButton : Button
    {
        internal bool IsClose;
        private bool hover;

        internal ChromeButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Dock = DockStyle.Right;
            Width = 44;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(hover ? InstallerTheme.Accent : InstallerTheme.Ink);
            int centerX = Width / 2;
            int centerY = Height / 2;
            int radius = Math.Max(5, Height / 7);
            // 两个标题按钮共用几何中心，避免字符字体的基线差异。
            using (var stroke = new Pen(InstallerTheme.Paper, 2))
            {
                if (IsClose)
                {
                    args.Graphics.DrawLine(stroke, centerX - radius, centerY - radius, centerX + radius, centerY + radius);
                    args.Graphics.DrawLine(stroke, centerX - radius, centerY + radius, centerX + radius, centerY - radius);
                }
                else args.Graphics.DrawLine(stroke, centerX - radius, centerY, centerX + radius, centerY);
            }
        }
    }
}
