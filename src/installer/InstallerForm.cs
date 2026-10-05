using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Graywill.InfantGodInstaller
{
    internal sealed class InstallerForm : Form
    {
        private readonly Color surface = InstallerTheme.Panel;
        private readonly Color muted = InstallerTheme.Muted;
        private readonly Color accent = InstallerTheme.Accent;
        private readonly ComboBox pathBox = new ComboBox();
        private readonly Button browse = new PixelButton();
        private readonly Button install = new PixelButton();
        private readonly Button uninstall = new PixelButton();
        private readonly Button launch = new PixelButton();
        private readonly Button detail = new PixelButton();
        private readonly Label statusTitle = new Label();
        private readonly Label statusText = new Label();
        private readonly Label runtimeLabel = new Label();
        private readonly Button runtimeDownload = new PixelButton();
        private readonly TextBox logBox = new TextBox();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private readonly InstallSession session = new InstallSession();
        private readonly string initialPath;
        private readonly string initialAction;
        private bool busy;
        private bool loading;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal InstallerForm(string gamePath, string action)
        {
            initialPath = gamePath;
            initialAction = action;
            Text = "幼神资料终端 · 安装器 " + Program.Version;
            BackColor = InstallerTheme.Paper;
            ForeColor = InstallerTheme.Ink;
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(950, 660);
            MinimumSize = new Size(950, 660);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(4);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;

            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(30, 25, 30, 20);
            layout.ColumnCount = 1;
            layout.RowCount = 9;
            foreach (int height in new[] { 28, 62, 46, 91, 142, 42, 68, 0 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            var titlebar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = InstallerTheme.Ink };
            var caption = MakeLabel("  OmniArchive.Setup  /  资料终端安装", 12, InstallerTheme.Paper);
            caption.Font = InstallerTheme.Pixel(18);
            caption.MouseDown += (sender, eventArgs) =>
            {
                if (eventArgs.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            };
            var close = new ChromeButton { IsClose = true, AccessibleName = "关闭" };
            close.Click += (sender, eventArgs) => Close();
            var minimize = new ChromeButton { AccessibleName = "最小化" };
            minimize.Click += (sender, eventArgs) => WindowState = FormWindowState.Minimized;
            titlebar.Controls.Add(caption);
            titlebar.Controls.Add(minimize);
            titlebar.Controls.Add(close);
            Controls.Add(titlebar);
            var hero = new Panel { Dock = DockStyle.Fill, BackColor = InstallerTheme.Ink, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 17) };
            var portrait = new PortraitPanel { Dock = DockStyle.Left, Width = 88, BackColor = InstallerTheme.Paper };
            var introduction = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20, 0, 0, 0) };
            introduction.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            introduction.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            introduction.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var brand = MakeLabel("OMNIARCHIVE  /  INSTALLATION " + Program.Version, 10, accent);
            brand.Font = InstallerTheme.Pixel(12);
            introduction.Controls.Add(brand, 0, 0);
            var headline = MakeLabel("资料终端安装器", 26, InstallerTheme.Paper);
            headline.Font = InstallerTheme.Pixel(36);
            introduction.Controls.Add(headline, 0, 1);
            introduction.Controls.Add(MakeLabel("监督，资料已经整理好了。现在把它装进游戏。", 10, Color.FromArgb(208, 212, 185)), 0, 2);
            hero.Controls.Add(introduction);
            hero.Controls.Add(portrait);
            layout.Controls.Add(hero, 0, 0);
            layout.SetRowSpan(hero, 3);

            var pathArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            pathArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            pathArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            pathArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            pathArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            pathArea.Controls.Add(MakeLabel("游戏目录", 10, ForeColor), 0, 0);
            pathBox.Dock = DockStyle.Fill;
            pathBox.BackColor = surface;
            pathBox.ForeColor = ForeColor;
            pathBox.FlatStyle = FlatStyle.Flat;
            pathBox.DropDownStyle = ComboBoxStyle.DropDown;
            pathBox.AccessibleName = "游戏安装目录";
            pathBox.TextChanged += (sender, eventArgs) => { if (!busy && !loading) RefreshState(); };
            pathArea.Controls.Add(pathBox, 0, 1);
            StyleButton(browse, "浏览…", false);
            browse.Click += Browse;
            pathArea.Controls.Add(browse, 1, 1);
            var pathHelp = MakeLabel("选择包含 Aistalt.exe 的 Build 文件夹，或从已发现的目录中选择。", 9, muted);
            pathArea.Controls.Add(pathHelp, 0, 2);
            pathArea.SetColumnSpan(pathHelp, 2);
            layout.Controls.Add(pathArea, 0, 3);

            var status = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(19, 15, 19, 12), Margin = new Padding(0, 8, 0, 7) };
            var stripe = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
            status.Controls.Add(stripe);
            status.Paint += (sender, eventArgs) => { using (var border = new Pen(InstallerTheme.Ink, 2)) eventArgs.Graphics.DrawRectangle(border, 1, 1, status.Width - 3, status.Height - 3); };
            statusTitle.Font = InstallerTheme.Pixel(24);
            statusTitle.UseCompatibleTextRendering = true;
            statusTitle.Dock = DockStyle.Top;
            statusTitle.Height = 34;
            statusText.Dock = DockStyle.Fill;
            statusText.ForeColor = muted;
            statusText.TextAlign = ContentAlignment.MiddleCenter;
            status.Controls.Add(statusText);
            status.Controls.Add(statusTitle);
            layout.Controls.Add(status, 0, 4);

            var runtime = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            runtime.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            runtime.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            runtimeLabel.Dock = DockStyle.Fill;
            runtimeLabel.ForeColor = muted;
            runtimeLabel.Font = new Font(Font.FontFamily, 9);
            runtimeLabel.TextAlign = ContentAlignment.MiddleLeft;
            runtime.Controls.Add(runtimeLabel, 0, 0);
            StyleButton(runtimeDownload, "获取网页运行组件", false);
            runtimeDownload.Click += (sender, eventArgs) => Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            runtime.Controls.Add(runtimeDownload, 1, 0);
            layout.Controls.Add(runtime, 0, 5);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 10) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27.5f));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27.5f));
            StyleButton(install, "安装资料终端", true);
            StyleButton(uninstall, "卸载并恢复文件", false);
            StyleButton(launch, "启动游戏", false);
            install.Click += async (sender, eventArgs) => await RunOperation(false);
            uninstall.Click += async (sender, eventArgs) => await RunOperation(true);
            launch.Click += (sender, eventArgs) =>
            {
                string root = GameLocator.Normalize(pathBox.Text);
                if (root != null) Process.Start(new ProcessStartInfo(Path.Combine(root, "Aistalt.exe")) { WorkingDirectory = root, UseShellExecute = true });
            };
            actions.Controls.Add(install, 0, 0);
            actions.Controls.Add(uninstall, 1, 0);
            actions.Controls.Add(launch, 2, 0);
            layout.Controls.Add(actions, 0, 6);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = InstallerTheme.Ink;
            logBox.ForeColor = InstallerTheme.Paper;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Microsoft YaHei UI", 9);
            logBox.Dock = DockStyle.Fill;
            logBox.Visible = false;
            layout.Controls.Add(logBox, 0, 7);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 9, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            footer.Controls.Add(MakeLabel("安装后：设置 → Mod 管理器 → 启用“幼神资料终端”\n进入游戏后按 F8，或点击桌面的“资料终端”。", 9, muted), 0, 0);
            StyleButton(detail, "详细信息", false);
            detail.Height = 35;
            detail.Dock = DockStyle.Top;
            detail.Click += (sender, eventArgs) => SetDetails(!logBox.Visible);
            footer.Controls.Add(detail, 1, 0);
            layout.Controls.Add(footer, 0, 8);
            Shown += async (sender, eventArgs) => await Initialize();
            FormClosing += (sender, eventArgs) => { if (busy) eventArgs.Cancel = true; };
            FormClosed += (sender, eventArgs) => session.Dispose();
        }

        private Label MakeLabel(string text, float size, Color color)
        {
            return new Label { Text = text, Font = new Font(Font.FontFamily, size), ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), UseCompatibleTextRendering = true };
        }

        private void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.Font = InstallerTheme.Pixel(24);
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 83, 75);
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.BackColor = primary ? accent : surface;
            button.ForeColor = ForeColor;
            button.Margin = new Padding(0, 0, 10, 0);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private async Task Initialize()
        {
            loading = true;
            SetBusy(true);
            statusTitle.Text = "正在查找游戏…";
            statusText.Text = "正在读取 Steam 的游戏目录。";
            try
            {
                var games = await Task.Run(() => GameLocator.FindGames());
                foreach (string game in games) pathBox.Items.Add(game);
                pathBox.Text = initialPath ?? games.FirstOrDefault() ?? "";
            }
            catch (Exception error) { AppendLog("自动查找未完成，可以手动选择游戏目录。" + error.Message); pathBox.Text = initialPath ?? ""; }
            bool runtime = GameLocator.HasWebViewRuntime();
            runtimeLabel.Text = runtime ? "网页运行组件：已安装 Microsoft Edge WebView2" : "网页运行组件：未检测到 WebView2，请通过右侧入口安装。";
            runtimeDownload.Visible = !runtime;
            loading = false;
            SetBusy(false);
            RefreshState();
            if (initialAction == "install" || initialAction == "uninstall") await RunOperation(initialAction == "uninstall");
        }

        private void RefreshState()
        {
            string root = GameLocator.Normalize(pathBox.Text);
            install.Enabled = root != null;
            launch.Enabled = root != null;
            uninstall.Enabled = false;
            install.Text = "安装资料终端";
            if (root == null)
            {
                statusTitle.Text = "请选择幼神的游戏目录";
                statusText.Text = "点击“浏览…”选择 Aistalt.exe，或填写游戏的 Build 文件夹路径。";
                return;
            }
            try
            {
                Installation state = Installation.Read(root);
                uninstall.Enabled = state.CanRemove;
                if (state.Status == "Installed")
                {
                    bool same = state.Version == Program.Version;
                    statusTitle.Text = "已安装资料终端 " + state.Version;
                    statusText.Text = same ? "文件已经安装完成。启动游戏，在 Mod 管理器中启用后即可使用。" : "可以更新到 " + Program.Version + "，首次安装前的原文件备份会继续保留。";
                    install.Text = same ? "已安装最新版本" : "更新到 " + Program.Version;
                    install.Enabled = !same;
                }
                else if (state.Status == "Installing")
                {
                    statusTitle.Text = "上一次安装尚未完成";
                    statusText.Text = "可以继续安装，也可以点击“卸载并恢复文件”撤回已执行的步骤。";
                    install.Text = "继续安装";
                }
                else
                {
                    statusTitle.Text = "已找到游戏，可以安装";
                    statusText.Text = "安装文件已包含在这个程序里。点击安装后，会备份需要覆盖的原文件。";
                }
            }
            catch (Exception error)
            {
                install.Enabled = false;
                statusTitle.Text = "无法读取已有安装记录";
                statusText.Text = error.Message;
            }
        }

        private void Browse(object sender, EventArgs eventArgs)
        {
            using (var dialog = new OpenFileDialog { Title = "选择幼神的 Aistalt.exe", Filter = "幼神游戏程序 (Aistalt.exe)|Aistalt.exe", CheckFileExists = true, FileName = "Aistalt.exe" })
            {
                string root = GameLocator.Normalize(pathBox.Text);
                if (root != null) dialog.InitialDirectory = root;
                if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private async Task RunOperation(bool remove)
        {
            if (busy) return;
            string root = GameLocator.Normalize(pathBox.Text);
            if (root == null) { RefreshState(); return; }
            logBox.Clear();
            try
            {
                using (var running = Process.GetProcessesByName("Aistalt").FirstOrDefault())
                    if (running != null)
                    {
                        statusTitle.Text = "请先退出幼神";
                        statusText.Text = "游戏仍在运行。退出游戏后，再点击“" + (remove ? "卸载并恢复文件" : "安装资料终端") + "”。";
                        return;
                    }
                if (!InstallSession.CanWrite(root))
                {
                    if (InstallSession.Elevate(root, remove)) Close();
                    else { statusTitle.Text = "未获得目录写入权限"; statusText.Text = "安装尚未开始。再次点击安装并允许 Windows 的权限请求即可继续。"; }
                    return;
                }
                SetBusy(true);
                statusTitle.Text = remove ? "正在卸载并恢复文件…" : "正在安装资料终端…";
                statusText.Text = remove ? "根据安装记录恢复原文件，保留导出的进度和配置。" : "正在解压安装文件、保存备份并复制到游戏目录。";
                int code = await session.Run(root, remove, AppendLog);
                SetBusy(false);
                RefreshState();
                if (code == 0)
                {
                    statusTitle.Text = remove ? "已卸载，原文件已恢复" : "安装完成，可以启动游戏了";
                    statusText.Text = remove ? "导出的进度、配置和备份仍然保留。" : "进入“设置 → Mod 管理器”启用“幼神资料终端”，然后按 F8 打开。";
                }
                else
                {
                    statusTitle.Text = remove ? "卸载未完成" : "安装未完成";
                    statusText.Text = "请查看下方的具体原因。已执行步骤的恢复信息保留在游戏目录。";
                    SetDetails(true);
                }
            }
            catch (Exception error)
            {
                SetBusy(false);
                RefreshState();
                statusTitle.Text = remove ? "卸载未完成" : "安装未完成";
                statusText.Text = error.Message;
                AppendLog(error.ToString());
                SetDetails(true);
            }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            pathBox.Enabled = !value;
            browse.Enabled = !value;
            install.Enabled = !value;
            uninstall.Enabled = !value;
            launch.Enabled = !value;
            UseWaitCursor = value;
        }

        private void AppendLog(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), line); return; }
            logBox.AppendText(line + Environment.NewLine);
        }

        private void SetDetails(bool value)
        {
            if (logBox.Visible == value) return;
            logBox.Visible = value;
            layout.RowStyles[7].Height = value ? 135 : 0;
            detail.Text = value ? "收起信息" : "详细信息";
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + (value ? 135 : -135));
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            base.OnPaintBackground(eventArgs);
            using (var dot = new SolidBrush(Color.FromArgb(189, 190, 158)))
                for (int y = 0; y < Height; y += 8)
                    for (int x = 0; x < Width; x += 8) eventArgs.Graphics.FillRectangle(dot, x, y, 1, 1);
            using (var border = new Pen(InstallerTheme.Ink, 4)) eventArgs.Graphics.DrawRectangle(border, 2, 2, ClientSize.Width - 4, ClientSize.Height - 4);
        }
    }
}
