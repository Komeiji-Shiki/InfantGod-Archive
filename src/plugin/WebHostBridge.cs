using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    internal sealed class WebHostBridge : IDisposable
    {
        private readonly ArchiveHost owner;
        private readonly Catalog catalog;
        private readonly RuntimeAccess runtime;
        private readonly CGViewer viewer;
        private readonly ConcurrentQueue<Incoming> incoming = new ConcurrentQueue<Incoming>();
        private Session session;
        private ReadMode mode = ReadMode.Unselected;
        private bool disposed;
        private bool lastVisible;
        private bool lastFocused;
        private bool hasLayout;
        private Rect lastBounds;
        private CGEntry currentCG;
        private Texture2D readback;
        private bool hasCGFrame;
        private float nextCGFrame;
        internal bool IsReady => session != null && session.Ready && !session.Closing;

        private sealed class Session
        {
            internal NamedPipeServerStream Pipe;
            internal Process Process;
            internal readonly object Sync = new object();
            internal readonly AutoResetEvent Signal = new AutoResetEvent(false);
            internal readonly Queue<object> Controls = new Queue<object>();
            internal object Layout;
            internal ProgressMessage Progress;
            internal FrameMessage Frame;
            internal string ProgressSignature;
            internal volatile bool Connected;
            internal volatile bool Ready;
            internal volatile bool Closing;
        }

        private sealed class Incoming
        {
            internal Session Session;
            internal JObject Message;
            internal Exception Error;
        }

        private sealed class ProgressMessage
        {
            internal bool Ready;
            internal bool Force;
            internal ProgressSnapshot Data;
        }

        private sealed class FrameMessage
        {
            internal string Id;
            internal byte[] PNG;
            internal string[] Animations;
            internal string Animation;
            internal bool Paused;
        }

        private delegate bool WindowCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        internal WebHostBridge(ArchiveHost owner, Catalog catalog, RuntimeAccess runtime, CGViewer viewer)
        {
            this.owner = owner;
            this.catalog = catalog;
            this.runtime = runtime;
            this.viewer = viewer;
        }

        internal void EnsureStarted()
        {
            if (disposed || session != null && !session.Closing) return;
            Session next = null;
            try
            {
                string executable = Path.Combine(owner.DataPath, "webhost", "Graywill.InfantGod.WebHost.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("没有找到网页终端程序。", executable);
                using (Process game = Process.GetCurrentProcess())
                {
                    IntPtr parent = game.MainWindowHandle;
                    if (parent == IntPtr.Zero)
                        EnumWindows((hwnd, unused) =>
                        {
                            uint pid;
                            GetWindowThreadProcessId(hwnd, out pid);
                            if (pid != game.Id || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero) return true;
                            parent = hwnd;
                            return false;
                        }, IntPtr.Zero);
                    if (parent == IntPtr.Zero) throw new InvalidOperationException("没有找到游戏的主窗口。");
                    string pipeName = "Graywill.InfantGodArchive." + game.Id + "." + Guid.NewGuid().ToString("N");
                    next = new Session { Pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous) };
                    session = next;
                    mode = ReadMode.Unselected;
                    hasLayout = false;
                    Session captured = next;
                    Task.Run(() => ReadLoop(captured));
                    var start = new ProcessStartInfo(executable)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        Arguments = "--pipe " + Quote(pipeName) + " --parent " + parent.ToInt64().ToString(CultureInfo.InvariantCulture)
                            + " --pid " + game.Id.ToString(CultureInfo.InvariantCulture)
                            + " --html " + Quote(Path.Combine(owner.DataPath, "archive.html"))
                            + " --profile " + Quote(Path.Combine(owner.DataPath, "webhost-profile"))
                    };
                    next.Process = Process.Start(start);
                }
            }
            catch (Exception error)
            {
                if (next != null) { next.Closing = true; next.Pipe.Dispose(); }
                owner.ReportError(error, "网页终端未能启动：" + error.Message);
            }
        }

        private static string Quote(string text) { return "\"" + text.Replace("\"", "\\\"") + "\""; }

        private void ReadLoop(Session current)
        {
            Task writer = null;
            try
            {
                current.Pipe.WaitForConnection();
                current.Connected = true;
                writer = Task.Run(() => WriteLoop(current));
                using (var reader = new StreamReader(current.Pipe, new UTF8Encoding(false), false, 8192, true))
                {
                    string line;
                    while (!current.Closing && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line.Length > 16384) continue;
                        JObject message = JObject.Parse(line);
                        if ((string)message["type"] == "action") incoming.Enqueue(new Incoming { Session = current, Message = message });
                    }
                }
                if (!current.Closing) incoming.Enqueue(new Incoming { Session = current, Error = new EndOfStreamException("网页终端的连接已结束。") });
            }
            catch (Exception error)
            {
                if (!current.Closing) incoming.Enqueue(new Incoming { Session = current, Error = error });
            }
            finally
            {
                current.Ready = false;
                current.Closing = true;
                current.Signal.Set();
                Action cleanup = () => { current.Pipe.Dispose(); current.Signal.Dispose(); current.Process?.Dispose(); };
                if (writer == null) cleanup();
                else writer.ContinueWith(unused => cleanup());
            }
        }

        private void WriteLoop(Session current)
        {
            try
            {
                using (var writer = new StreamWriter(current.Pipe, new UTF8Encoding(false), 8192, true) { AutoFlush = true })
                    while (true)
                    {
                        current.Signal.WaitOne();
                        object[] controls;
                        object layout;
                        ProgressMessage progress;
                        FrameMessage frame;
                        bool closing;
                        lock (current.Sync)
                        {
                            controls = current.Controls.ToArray();
                            current.Controls.Clear();
                            layout = current.Layout; current.Layout = null;
                            progress = current.Progress; current.Progress = null;
                            frame = current.Frame; current.Frame = null;
                            closing = current.Closing;
                        }
                        foreach (object control in controls) WriteJSON(writer, control);
                        if (layout != null) WriteJSON(writer, layout);
                        if (progress != null) WriteProgress(writer, current, progress);
                        if (frame != null) WriteJSON(writer, new
                        {
                            type = "cgFrame", id = frame.Id, image = "data:image/png;base64," + Convert.ToBase64String(frame.PNG),
                            animations = frame.Animations, currentAnimation = frame.Animation, paused = frame.Paused
                        });
                        if (closing) break;
                    }
            }
            catch (Exception error)
            {
                if (!current.Closing) incoming.Enqueue(new Incoming { Session = current, Error = error });
                current.Closing = true;
            }
            finally { current.Pipe.Dispose(); }
        }

        private static void WriteJSON(StreamWriter writer, object value) { writer.WriteLine(JsonConvert.SerializeObject(value, Formatting.None)); }

        private static void WriteProgress(StreamWriter writer, Session current, ProgressMessage progress)
        {
            // 进度副本已在 Unity 线程生成，较大的 JSON 比较与编码放到管道线程。
            JObject data = progress.Ready && progress.Data != null ? JObject.FromObject(progress.Data) : null;
            JToken exportedAt = data?["exportedAt"];
            data?.Remove("exportedAt");
            string signature = progress.Ready + ":" + (data == null ? "null" : data.ToString(Formatting.None));
            if (!progress.Force && signature == current.ProgressSignature) return;
            current.ProgressSignature = signature;
            if (data != null) data["exportedAt"] = exportedAt;
            WriteJSON(writer, new { type = "progress", ready = progress.Ready, data });
        }

        internal void Tick()
        {
            Incoming item;
            while (incoming.TryDequeue(out item))
            {
                if (item.Session != session) continue;
                if (item.Error != null)
                {
                    CloseCG();
                    owner.ReportError(item.Error, "网页终端连接已中断：" + item.Error.Message);
                    continue;
                }
                try { HandleAction(item.Message); }
                catch (Exception error) { owner.ReportError(error, "网页操作未完成：" + error.Message); }
            }
            bool visible = owner.IsAvailable && owner.IsOpen && owner.Integration.WindowShowing;
            if (!visible && currentCG != null) CloseCG();
            if (currentCG != null && !Visibility.CanSee(currentCG, mode, runtime.Current)) CloseCG();
            SendLayout(false);
        }

        private void HandleAction(JObject message)
        {
            string action = (string)message["action"];
            if (action == "ready")
            {
                session.Ready = true;
                SendLayout(true);
                owner.RefreshProgress(true);
                if (Time.unscaledTime < owner.NoticeUntil) SendNotice(owner.Notice);
                return;
            }
            if (action == "hostError")
            {
                string messageText = (string)message["message"];
                owner.ReportError(new InvalidOperationException(messageText), "网页终端未能载入：" + messageText);
                return;
            }
            if (!owner.IsAvailable) return;
            switch (action)
            {
                case "refreshProgress": owner.RefreshProgress(true); break;
                case "exportProgress": owner.ExportProgress(); break;
                case "toggle": owner.SetOpen(!owner.IsOpen || !owner.Integration.WindowShowing); break;
                case "close": owner.SetOpen(false); break;
                case "setMode":
                    string choice = (string)message["mode"];
                    if (choice != "all" && choice != "explored") return;
                    mode = choice == "all" ? ReadMode.Full : ReadMode.Explored;
                    if (currentCG != null && !Visibility.CanSee(currentCG, mode, runtime.Current)) CloseCG();
                    break;
                case "cgOpen": OpenCG((string)message["id"], (string)message["animation"]); break;
                case "cgPlay":
                    string name = (string)message["name"];
                    if (currentCG == null || !viewer.Animations.Contains(name)) return;
                    viewer.Play(name); hasCGFrame = false; nextCGFrame = 0;
                    break;
                case "cgPause":
                    if (currentCG == null || message["paused"]?.Type != JTokenType.Boolean) return;
                    viewer.Paused = (bool)message["paused"]; hasCGFrame = false; nextCGFrame = 0;
                    break;
                case "cgZoom":
                    if (currentCG == null || (message["value"]?.Type != JTokenType.Float && message["value"]?.Type != JTokenType.Integer)) return;
                    float zoom = (float)message["value"];
                    if (float.IsNaN(zoom) || float.IsInfinity(zoom)) return;
                    viewer.Zoom = Mathf.Clamp(zoom, 0.5f, 2.5f); hasCGFrame = false; nextCGFrame = 0;
                    break;
                case "cgClose": CloseCG(); break;
            }
        }

        private void SendLayout(bool force)
        {
            Session current = session;
            if (current == null || !current.Ready || current.Closing) return;
            bool visible = owner.IsAvailable && owner.IsOpen && owner.Integration.WindowShowing;
            bool focused = visible && owner.Integration.WindowFocused;
            Rect client = visible ? owner.Integration.ClientBounds : Rect.zero;
            var bounds = new Rect(client.x / Screen.width, client.y / Screen.height, client.width / Screen.width, client.height / Screen.height);
            if (!force && hasLayout && visible == lastVisible && focused == lastFocused && bounds == lastBounds) return;
            hasLayout = true;
            lastVisible = visible; lastFocused = focused; lastBounds = bounds;
            lock (current.Sync) current.Layout = new { type = "layout", visible, focused, bounds = new { x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height } };
            current.Signal.Set();
        }

        internal void SendProgress(bool force)
        {
            Session current = session;
            if (current == null || !current.Ready || current.Closing) return;
            lock (current.Sync)
            {
                force |= current.Progress != null && current.Progress.Force;
                current.Progress = new ProgressMessage { Ready = runtime.Current.IsReady, Data = runtime.Current, Force = force };
            }
            current.Signal.Set();
        }

        internal void SendNotice(string text)
        {
            Session current = session;
            if (current == null || !current.Ready || current.Closing) return;
            lock (current.Sync) current.Controls.Enqueue(new { type = "notice", text });
            current.Signal.Set();
        }

        private void OpenCG(string id, string animation)
        {
            if (!owner.IsOpen || !owner.Integration.WindowShowing) return;
            CGEntry entry = catalog.cg.FirstOrDefault(c => c.id == id);
            if (entry == null || !Visibility.CanSee(entry, mode, runtime.Current)) return;
            CloseCG();
            try { viewer.Open(entry, owner.DataPath); }
            catch { viewer.Dispose(); throw; }
            currentCG = entry;
            if (!string.IsNullOrEmpty(animation) && viewer.Animations.Contains(animation)) viewer.Play(animation);
            hasCGFrame = false;
            nextCGFrame = 0;
        }

        internal void CloseCG()
        {
            currentCG = null;
            hasCGFrame = false;
            viewer.Dispose();
            Session current = session;
            if (current == null || !current.Ready || current.Closing) return;
            lock (current.Sync)
            {
                current.Frame = null;
                current.Controls.Enqueue(new { type = "cgFrame", id = (string)null, image = (string)null, animations = new string[0], currentAnimation = (string)null, paused = false });
            }
            current.Signal.Set();
        }

        internal void CaptureCGFrame()
        {
            Session current = session;
            if (currentCG == null || current == null || !current.Ready || current.Closing || !viewer.IsReady || Time.unscaledTime < nextCGFrame) return;
            Texture texture = viewer.Texture;
            if (hasCGFrame && (viewer.Paused || texture is Texture2D)) return;
            nextCGFrame = Time.unscaledTime + 0.12f;
            try
            {
                byte[] png;
                if (texture is Texture2D) png = ImageConversion.EncodeToPNG((Texture2D)texture);
                else
                {
                    var target = texture as RenderTexture;
                    if (target == null) return;
                    if (readback == null || readback.width != target.width || readback.height != target.height)
                    {
                        if (readback != null) UnityEngine.Object.Destroy(readback);
                        readback = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                    }
                    RenderTexture previous = RenderTexture.active;
                    try { RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); }
                    finally { RenderTexture.active = previous; }
                    png = ImageConversion.EncodeToPNG(readback);
                }
                // 替换未发送的旧帧，后台慢于画面时也不会积累图片队列。
                lock (current.Sync) current.Frame = new FrameMessage { Id = viewer.Id, PNG = png, Animations = viewer.Animations, Animation = viewer.CurrentAnimation, Paused = viewer.Paused };
                hasCGFrame = true;
                current.Signal.Set();
            }
            catch (Exception error) { CloseCG(); owner.ReportError(error, "CG 画面传输失败：" + error.Message); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseCG();
            if (readback != null) UnityEngine.Object.Destroy(readback);
            readback = null;
            Session current = session;
            if (current == null || current.Closing) return;
            lock (current.Sync)
            {
                current.Controls.Enqueue(new { type = "shutdown" });
                current.Layout = null; current.Progress = null; current.Frame = null;
                current.Closing = true;
            }
            if (current.Connected) current.Signal.Set();
            else current.Pipe.Dispose();
        }
    }
}
