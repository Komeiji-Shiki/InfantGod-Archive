using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class LocalMcpBridge : IDisposable
    {
        private readonly LiveChatController owner;
        private readonly string root;
        private readonly JArray definitions;
        private readonly ConcurrentDictionary<NamedPipeServerStream, byte> clients = new ConcurrentDictionary<NamedPipeServerStream, byte>();
        private CancellationTokenSource stop;
        private string secret;
        private string pipeName;
        internal string Status { get; private set; } = "未启用";
        internal string Executable => Path.Combine(root, "mcp", "InfantGod.Mcp.exe");
        internal string CodexCommand => "codex mcp add infantgod -- '" + Executable.Replace("'", "''") + "'";
        internal string ClientConfig => new JObject { ["mcpServers"] = new JObject { ["infantgod"] = new JObject { ["command"] = Executable, ["args"] = new JArray() } } }.ToString(Formatting.Indented);

        internal LocalMcpBridge(LiveChatController owner, string root, JArray definitions)
        {
            this.owner = owner; this.root = root; this.definitions = definitions;
            File.WriteAllText(Path.Combine(root, "mcp-tools.json"), definitions.ToString(Formatting.Indented), new UTF8Encoding(false));
        }

        internal void SetEnabled(bool enabled)
        {
            if (enabled == (stop != null)) return;
            if (!enabled) { Dispose(); return; }
            secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            pipeName = "InfantGodArchive.Mcp." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N");
            stop = new CancellationTokenSource();
            File.WriteAllText(Path.Combine(root, "mcp-endpoint.json"), new JObject { ["pipe"] = pipeName, ["token"] = secret, ["pid"] = Process.GetCurrentProcess().Id }.ToString(Formatting.None), new UTF8Encoding(false));
            Status = "已启用，可接受本机工具调用";
            _ = Accept(stop.Token, pipeName, secret);
        }

        private async Task Accept(CancellationToken cancel, string name, string token)
        {
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    clients.TryAdd(pipe, 0);
                    try { await pipe.WaitForConnectionAsync(cancel).ConfigureAwait(false); }
                    catch { clients.TryRemove(pipe, out _); pipe.Dispose(); throw; }
                    _ = Handle(pipe, token, cancel);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception error)
            {
                if (!cancel.IsCancellationRequested) owner.Post(() =>
                {
                    if (cancel.IsCancellationRequested) return;
                    Dispose(); Status = "本机连接未能启动：" + error.Message;
                });
            }
        }

        private async Task Handle(NamedPipeServerStream pipe, string token, CancellationToken serverCancel)
        {
            using (pipe)
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(serverCancel))
            using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
            using (serverCancel.Register(() => pipe.Dispose()))
            {
                try
                {
                    string first = await ReadRequest(reader, cancel.Token).ConfigureAwait(false);
                    if (first == null) return;
                    var request = JObject.Parse(first);
                    if (!string.Equals((string)request["token"], token, StringComparison.Ordinal)) throw new InvalidOperationException("本机连接令牌无效，请重新连接。");
                    string method = (string)request["method"];
                    if (method == "tools/list") { await writer.WriteLineAsync(new JObject { ["tools"] = definitions }.ToString(Formatting.None)).ConfigureAwait(false); return; }
                    if (method != "tools/call") throw new InvalidOperationException("不支持的游戏连接请求。");
                    if (request["name"]?.Type != JTokenType.String || request["arguments"] != null && request["arguments"].Type != JTokenType.Object)
                        throw new InvalidOperationException("工具名称或参数格式无效。");
                    string source = GameToolbox.Clip((string)request["client"] ?? "MCP", 80);
                    owner.Post(() => { if (!serverCancel.IsCancellationRequested) Status = "最近调用：" + source; });
                    var call = owner.ExecuteExternal((string)request["name"], (request["arguments"] ?? new JObject()).ToString(Formatting.None), source, cancel.Token);
                    // 每条连接只处理一个请求，随后任何额外字节或 EOF 都视为取消。
                    var disconnected = reader.ReadAsync(new char[1], 0, 1);
                    if (await Task.WhenAny(call, disconnected).ConfigureAwait(false) == disconnected)
                    {
                        cancel.Cancel();
                        try { await call.ConfigureAwait(false); } catch (OperationCanceledException) { }
                        return;
                    }
                    JToken result = await call.ConfigureAwait(false);
                    await writer.WriteLineAsync(new JObject { ["result"] = result }.ToString(Formatting.None)).ConfigureAwait(false);
                    writer.Dispose();
                    pipe.Dispose();
                    try { await disconnected.ConfigureAwait(false); } catch (IOException) { } catch (ObjectDisposedException) { }
                }
                catch (Exception error)
                {
                    try { if (pipe.IsConnected) await writer.WriteLineAsync(new JObject { ["error"] = error.GetBaseException().Message }.ToString(Formatting.None)).ConfigureAwait(false); }
                    catch (IOException) { } catch (ObjectDisposedException) { }
                }
                finally { clients.TryRemove(pipe, out _); }
            }
        }

        private static async Task<string> ReadRequest(StreamReader reader, CancellationToken cancel)
        {
            var message = new StringBuilder(); var buffer = new char[4096];
            while (true)
            {
                int count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                if (count == 0) return message.Length == 0 ? null : throw new IOException("本机请求未完整结束。");
                int end = Array.IndexOf(buffer, '\n', 0, count);
                message.Append(buffer, 0, end >= 0 ? end : count);
                if (message.Length > 1024 * 1024) throw new InvalidOperationException("本机请求超过 1 MiB 限制。");
                if (end >= 0)
                {
                    if (end != count - 1) throw new InvalidOperationException("每条游戏连接只能发送一个请求。");
                    return message.ToString();
                }
            }
        }

        public void Dispose()
        {
            var cancellation = stop; stop = null;
            if (cancellation == null) return;
            cancellation.Cancel();
            foreach (var pipe in clients.Keys) pipe.Dispose();
            clients.Clear(); cancellation.Dispose();
            string path = Path.Combine(root, "mcp-endpoint.json");
            if (File.Exists(path)) File.Delete(path);
            Status = "未启用";
        }
    }
}
