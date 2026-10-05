using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Graywill.InfantGod.WebHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < args.Length; i += 2) values[args[i]] = args[i + 1];
            if (!values.ContainsKey("--pipe") || !values.ContainsKey("--parent") || !values.ContainsKey("--html")) return;
            IntPtr parent = new IntPtr(long.Parse(values["--parent"]));
            uint parentPid;
            Native.GetWindowThreadProcessId(parent, out parentPid);
            if (!Native.IsWindow(parent) || (values.ContainsKey("--pid") && parentPid != uint.Parse(values["--pid"]))) return;
            try { Native.SetThreadDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(parent)); }
            catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string profile = values.ContainsKey("--profile") ? values["--profile"] : Path.Combine(Path.GetDirectoryName(values["--html"]), "webhost-profile");
            Process game;
            try { game = Process.GetProcessById((int)parentPid); }
            catch (ArgumentException) { return; }
            using (game)
            {
                // 嵌入的子窗口可能先被系统销毁，进程退出监听不依赖这个窗口的消息循环。
                game.Exited += delegate { Environment.Exit(0); };
                game.EnableRaisingEvents = true;
                if (game.HasExited) return;
                Application.Run(new ArchiveForm(parent, parentPid, values["--pipe"], values["--html"], profile));
            }
        }
    }

    internal sealed class ArchiveForm : Form
    {
        private readonly IntPtr gameWindow;
        private readonly uint gamePid;
        private readonly string pipeName;
        private readonly string htmlPath;
        private readonly string profilePath;
        private readonly WebView2 browser;
        private readonly System.Windows.Forms.Timer updateTimer;
        private readonly object writerLock = new object();
        private NamedPipeClientStream pipe;
        private StreamWriter writer;
        private string pendingLayout;
        private string pendingProgress;
        private string pendingCG;
        private string pendingNotice;
        private Dictionary<string, object> layout;
        private string lastProgress;
        private bool started;
        private bool pageReady;
        private bool ending;
        private Rectangle appliedBounds = Rectangle.Empty;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 24000000 };
        private static readonly HashSet<string> Actions = new HashSet<string>(StringComparer.Ordinal)
        { "ready", "refreshProgress", "exportProgress", "toggle", "close", "setMode", "cgOpen", "cgPlay", "cgPause", "cgZoom", "cgClose" };

        internal ArchiveForm(IntPtr parent, uint pid, string pipeId, string file, string profile)
        {
            gameWindow = parent;
            gamePid = pid;
            pipeName = pipeId;
            htmlPath = Path.GetFullPath(file);
            profilePath = Path.GetFullPath(profile);
            Directory.CreateDirectory(profilePath);
            Text = "幼神资料终端 · 网页视图";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(238, 235, 198);
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);
            browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(browser);
            updateTimer = new System.Windows.Forms.Timer { Interval = 16 };
            updateTimer.Tick += UpdateFrame;
            FormClosed += delegate { Stop(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Parent = gameWindow;
                parameters.Style = Native.WS_CHILD | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN;
                parameters.ExStyle = Native.WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (started) return;
            started = true;
            Native.SetParent(Handle, gameWindow);
            Hide();
            updateTimer.Start();
            Task.Run((Func<Task>)ReadPipe);
            StartBrowser();
        }

        private async void StartBrowser()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profilePath, null);
                await browser.EnsureCoreWebView2Async(environment);
                CoreWebView2 core = browser.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
                {
                    Uri uri;
                    if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out uri)) { args.Cancel = true; return; }
                    if (uri.IsFile && string.Equals(Path.GetFullPath(uri.LocalPath), htmlPath, StringComparison.OrdinalIgnoreCase)) return;
                    args.Cancel = true;
                    OpenExternal(uri);
                };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                    Uri uri;
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out uri)) OpenExternal(uri);
                };
                core.WebMessageReceived += ReceiveWebMessage;
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (!args.IsSuccess) { Log("页面打开失败：" + args.WebErrorStatus); return; }
                    pageReady = true;
                    if (lastProgress != null) core.PostWebMessageAsJson(lastProgress);
                    ApplyLayout();
                    SendAction("ready");
                    Log("HTML 资料页已就绪。");
                };
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
                {
                    Log("WebView2 进程异常：" + args.ProcessFailedKind);
                };
                await core.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('keydown',function(e){if(e.key==='F8'){e.preventDefault();e.stopImmediatePropagation();window.chrome.webview.postMessage({type:'action',action:'toggle'});}},true);");
                core.Navigate(new Uri(htmlPath).AbsoluteUri + "?embedded=1");
            }
            catch (Exception error)
            {
                Log(error.ToString());
                Send(new Dictionary<string, object> { { "type", "action" }, { "action", "hostError" }, { "message", "网页视图启动失败。请确认已安装 Microsoft Edge WebView2 Runtime。" } });
                Controls.Clear();
                var message = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei", 15), Text = "网页视图启动失败\n\n请安装 Microsoft Edge WebView2 Runtime 后重新打开资料终端。" };
                Controls.Add(message);
                pageReady = true;
                ApplyLayout();
            }
        }

        private async Task ReadPipe()
        {
            try
            {
                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await Task.Run(delegate { pipe.Connect(15000); });
                lock (writerLock) writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                SendAction("ready");
                using (var reader = new StreamReader(pipe, Encoding.UTF8, true, 65536, true))
                {
                    while (!ending && pipe.IsConnected)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null) break;
                        var data = new JavaScriptSerializer { MaxJsonLength = 24000000 }.Deserialize<Dictionary<string, object>>(line);
                        string type = Value<string>(data, "type", "");
                        // 布局和动画只保留最新一帧，避免窗口缩放时积压过期画面。
                        if (type == "layout") Interlocked.Exchange(ref pendingLayout, line);
                        else if (type == "progress") Interlocked.Exchange(ref pendingProgress, line);
                        else if (type == "cgFrame") Interlocked.Exchange(ref pendingCG, line);
                        else if (type == "notice") Interlocked.Exchange(ref pendingNotice, line);
                        else if (type == "shutdown") break;
                    }
                }
            }
            catch (Exception error) { if (!ending) Log("IPC：" + error.Message); }
            if (!ending && IsHandleCreated) BeginInvoke((Action)Close);
        }

        private void UpdateFrame(object sender, EventArgs args)
        {
            if (ending) return;
            uint pid;
            Native.GetWindowThreadProcessId(gameWindow, out pid);
            if (!Native.IsWindow(gameWindow) || pid != gamePid) { Close(); return; }
            string value = Interlocked.Exchange(ref pendingLayout, null);
            if (value != null) layout = serializer.Deserialize<Dictionary<string, object>>(value);
            ApplyLayout();
            value = Interlocked.Exchange(ref pendingProgress, null);
            if (value != null) { lastProgress = value; PostToPage(value); }
            value = Interlocked.Exchange(ref pendingCG, null);
            if (value != null) PostToPage(value);
            value = Interlocked.Exchange(ref pendingNotice, null);
            if (value != null) PostToPage(value);
        }

        private void ApplyLayout()
        {
            if (layout == null) return;
            bool show = pageReady && Value<bool>(layout, "visible", false) && Value<bool>(layout, "focused", true) && !Native.IsIconic(gameWindow);
            object rawBounds;
            if (!layout.TryGetValue("bounds", out rawBounds)) show = false;
            if (!show) { if (Visible) Hide(); return; }
            var bounds = rawBounds as Dictionary<string, object>;
            if (bounds == null) return;
            Native.RECT parent;
            Native.GetClientRect(gameWindow, out parent);
            int left = (int)Math.Round(Number(bounds, "x") * (parent.Right - parent.Left));
            int top = (int)Math.Round(Number(bounds, "y") * (parent.Bottom - parent.Top));
            int width = (int)Math.Round(Number(bounds, "width") * (parent.Right - parent.Left));
            int height = (int)Math.Round(Number(bounds, "height") * (parent.Bottom - parent.Top));
            if (width < 1 || height < 1) { if (Visible) Hide(); return; }
            if (!Visible) Show();
            Rectangle rectangle = new Rectangle(left, top, width, height);
            if (rectangle != appliedBounds)
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, left, top, width, height, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
                appliedBounds = rectangle;
            }
        }

        private void PostToPage(string json)
        {
            if (pageReady && browser.CoreWebView2 != null) browser.CoreWebView2.PostWebMessageAsJson(json);
        }

        private void ReceiveWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var message = serializer.Deserialize<Dictionary<string, object>>(args.WebMessageAsJson);
                string action = Value<string>(message, "action", "");
                if (Value<string>(message, "type", "") == "action" && Actions.Contains(action)) Send(message);
            }
            catch (Exception error) { Log("页面消息：" + error.Message); }
        }

        private void SendAction(string action) { Send(new Dictionary<string, object> { { "type", "action" }, { "action", action } }); }
        private void Send(Dictionary<string, object> value)
        {
            try { lock (writerLock) { if (writer != null) writer.WriteLine(new JavaScriptSerializer().Serialize(value)); } }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        private static T Value<T>(Dictionary<string, object> data, string key, T fallback)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value is T ? (T)value : fallback;
        }
        private static double Number(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        private void OpenExternal(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        private void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(profilePath, "webhost.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { }
        }
        private void Stop()
        {
            if (ending) return;
            ending = true;
            updateTimer.Stop();
            lock (writerLock) { if (writer != null) writer.Dispose();writer = null; }
            if (pipe != null) pipe.Dispose();
            browser.Dispose();
        }
    }

    internal static class Native
    {
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_CLIPSIBLINGS = 0x04000000;
        internal const int WS_CLIPCHILDREN = 0x02000000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_NOZORDER = 0x0004;
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
