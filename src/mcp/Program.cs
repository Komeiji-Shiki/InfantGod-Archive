using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Graywill.InfantGodMcp
{
    internal static class Program
    {
        private static readonly object OutputLock = new object();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> Pending = new ConcurrentDictionary<string, CancellationTokenSource>();
        private static readonly CancellationTokenSource Shutdown = new CancellationTokenSource();
        private static string pluginRoot;
        private static string clientName = "MCP 客户端";
        private static bool initializeReceived;
        private static bool initialized;

        private static int Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
            pluginRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string call = null, arguments = null, output = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string option = args[i];
                    if (option == "--help")
                    {
                        Console.Error.WriteLine("InfantGod.Mcp.exe [--plugin-dir <游戏插件目录>]\n"
                            + "默认通过标准输入输出提供《幼神》游戏电脑 MCP 工具。\n"
                            + "InfantGod.Mcp.exe [--plugin-dir <游戏插件目录>] --call <工具名> [--arguments <JSON对象>] [--output <文件>]\n"
                            + "单次调用复用同一游戏连接，arguments 默认为 {}，结果 JSON 写入 stdout 或指定文件。\n"
                            + "失败信息写入 stderr 并返回非零退出码；截图返回包含 PNG 数据的 JSON。请先在游戏设置中启用本机 MCP。");
                        return 0;
                    }
                    if (option != "--plugin-dir" && option != "--call" && option != "--arguments" && option != "--output")
                        throw new ArgumentException("未知参数：" + option);
                    if (++i >= args.Length) throw new ArgumentException(option + " 缺少参数值。");
                    if (option == "--plugin-dir") pluginRoot = Path.GetFullPath(args[i]);
                    else if (option == "--call") call = args[i];
                    else if (option == "--arguments") arguments = args[i];
                    else output = Path.GetFullPath(args[i]);
                }
                if (call == null && (arguments != null || output != null)) throw new ArgumentException("--arguments 和 --output 需要与 --call 一起使用。");
            }
            catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); return 2; }
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; SafeCancel(Shutdown); };
            if (call != null) return CallOnce(call, arguments ?? "{}", output);
            try
            {
                string line;
                while (!Shutdown.IsCancellationRequested && (line = Console.ReadLine()) != null)
                {
                    Dictionary<string, object> request;
                    try { request = Json().Deserialize<Dictionary<string, object>>(line); }
                    catch { Error(null, -32700, "JSON 格式无效。"); continue; }
                    object id = Value(request, "id");
                    bool hasId = request != null && request.ContainsKey("id");
                    if (request == null || StringValue(request, "jsonrpc") != "2.0" || !(Value(request, "method") is string)
                        || hasId && !(id is string || id is int || id is long || id is decimal || id is double))
                    { Error(null, -32600, "JSON-RPC 请求无效。"); continue; }
                    string method = StringValue(request, "method");
                    if (request.ContainsKey("params") && !(Value(request, "params") is Dictionary<string, object>))
                    { if (hasId) Error(id, -32602, "params 必须为对象。"); continue; }
                    var parameters = Value(request, "params") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    if (!hasId && method == "notifications/initialized") { initialized = initializeReceived; continue; }
                    if (!hasId && method == "notifications/cancelled")
                    {
                        CancellationTokenSource cancel;
                        if (Pending.TryGetValue(IdKey(Value(parameters, "requestId")), out cancel)) SafeCancel(cancel);
                        continue;
                    }
                    if (!hasId) continue;
                    var ignored = Handle(request, parameters);
                }
                return 0;
            }
            catch (IOException) { return 0; }
            finally
            {
                SafeCancel(Shutdown); foreach (var cancel in Pending.Values) SafeCancel(cancel);
            }
        }

        private static int CallOnce(string name, string arguments, string output)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("--call 必须填写工具名称。");
                var parameters = Json().DeserializeObject(arguments) as Dictionary<string, object>;
                if (parameters == null) throw new ArgumentException("--arguments 必须为 JSON 对象。");
                clientName = "本机命令行";
                var response = GameRequest("tools/call", name, parameters, Shutdown.Token).GetAwaiter().GetResult();
                object result = Value(response, "result");
                string json = Json().Serialize(result);
                // 单次入口输出原始工具结果，截图也保留原始图像字段供程序读取。
                if (output == null) Console.WriteLine(json);
                else File.WriteAllText(output, json, new UTF8Encoding(false));
                var obj = result as Dictionary<string, object>;
                if (obj != null && Value(obj, "ok") is bool && !(bool)Value(obj, "ok"))
                {
                    Console.Error.WriteLine(Value(obj, "error") ?? Value(obj, "message") ?? "游戏没有接受这次操作。");
                    return 1;
                }
                return 0;
            }
            catch (OperationCanceledException) { Console.Error.WriteLine("操作已停止。"); return 130; }
            catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); return 1; }
        }

        private static async Task Handle(Dictionary<string, object> request, Dictionary<string, object> parameters)
        {
            object id = Value(request, "id"); string method = StringValue(request, "method");
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token))
            {
                string key = IdKey(id);
                if (!Pending.TryAdd(key, cancel)) { Error(id, -32600, "请求 ID 已在使用中。"); return; }
                try
                {
                    if (method == "initialize")
                    {
                        if (initializeReceived) { Error(id, -32600, "此 MCP 连接已经初始化。"); return; }
                        if (!(Value(parameters, "protocolVersion") is string) || !(Value(parameters, "clientInfo") is Dictionary<string, object>)
                            || !(Value(parameters, "capabilities") is Dictionary<string, object>))
                        { Error(id, -32602, "initialize 缺少 protocolVersion、clientInfo 或 capabilities。"); return; }
                        string requested = StringValue(parameters, "protocolVersion");
                        string negotiated = Array.IndexOf(new[] { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" }, requested) >= 0 ? requested : "2025-11-25";
                        var info = Value(parameters, "clientInfo") as Dictionary<string, object>;
                        if (info != null) clientName = StringValue(info, "name");
                        initializeReceived = true;
                        Result(id, new { protocolVersion = negotiated, capabilities = new { tools = new { listChanged = false } },
                            serverInfo = new { name = "infantgod-computer", version = "1.1.0" },
                            instructions = "操作正在运行的《幼神》游戏电脑。先读取界面，使用最新 ref 调用工具。游戏内玩家可以随时停止外部操作。" });
                        return;
                    }
                    if (method == "ping") { Result(id, new { }); return; }
                    if (!initialized) { Error(id, -32002, "请先初始化 MCP 连接。"); return; }
                    if (method == "tools/list")
                    {
                        string cache = Path.Combine(pluginRoot, "mcp-tools.json");
                        object listed = File.Exists(cache) ? new { tools = Json().DeserializeObject(File.ReadAllText(cache, Encoding.UTF8)) }
                            : (object)await GameRequest("tools/list", null, null, cancel.Token).ConfigureAwait(false);
                        cancel.Token.ThrowIfCancellationRequested(); Result(id, listed);
                        return;
                    }
                    if (method == "tools/call")
                    {
                        string name = StringValue(parameters, "name");
                        if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey("arguments") && !(Value(parameters, "arguments") is Dictionary<string, object>))
                        { Error(id, -32602, "tools/call 需要工具名称及对象形式的 arguments。"); return; }
                        var result = await GameRequest("tools/call", name, Value(parameters, "arguments") ?? new Dictionary<string, object>(), cancel.Token).ConfigureAwait(false);
                        cancel.Token.ThrowIfCancellationRequested();
                        object raw = Value(result, "result");
                        var obj = raw as Dictionary<string, object>;
                        bool isError = obj != null && Value(obj, "ok") is bool && !(bool)Value(obj, "ok");
                        if (obj != null && Value(obj, "_image") is bool && (bool)Value(obj, "_image"))
                            Result(id, new { content = new object[] { new { type = "image", mimeType = StringValue(obj, "mimeType"), data = StringValue(obj, "data") } }, isError = false });
                        else Result(id, new { content = new[] { new { type = "text", text = Json().Serialize(raw) } }, isError = isError });
                        return;
                    }
                    Error(id, -32601, "不支持的方法：" + method);
                }
                // MCP 取消通知无需响应，被取消的请求也不再发送迟到结果。
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    if (cancel.IsCancellationRequested) return;
                    if (method == "tools/call") Result(id, new { content = new[] { new { type = "text", text = error.GetBaseException().Message } }, isError = true });
                    else Error(id, -32000, error.GetBaseException().Message);
                }
                finally { CancellationTokenSource removed; Pending.TryRemove(key, out removed); }
            }
        }

        private static async Task<Dictionary<string, object>> GameRequest(string method, string name, object arguments, CancellationToken cancel)
        {
            string endpoint = Path.Combine(pluginRoot, "mcp-endpoint.json");
            if (!File.Exists(endpoint)) throw new InvalidOperationException("请先启动幼神，并在“设置 → LLM → 本机 MCP”中启用连接。");
            var configuration = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(endpoint, Encoding.UTF8));
            using (var pipe = new NamedPipeClientStream(".", StringValue(configuration, "pipe"), PipeDirection.InOut, PipeOptions.Asynchronous))
            using (cancel.Register(() => pipe.Dispose()))
            {
                try
                {
                    await pipe.ConnectAsync(5000, cancel).ConfigureAwait(false);
                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                    {
                        await writer.WriteLineAsync(Json().Serialize(new { token = StringValue(configuration, "token"), method = method, name = name, arguments = arguments, client = clientName })).ConfigureAwait(false);
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        cancel.ThrowIfCancellationRequested();
                        if (line == null) throw new IOException("游戏连接已结束，请确认游戏仍在运行。");
                        var result = Json().Deserialize<Dictionary<string, object>>(line);
                        if (result == null) throw new IOException("游戏返回了无效结果。");
                        if (result.ContainsKey("error")) throw new InvalidOperationException(StringValue(result, "error"));
                        return result;
                    }
                }
                catch (Exception) { cancel.ThrowIfCancellationRequested(); throw; }
            }
        }

        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 }; }
        private static void SafeCancel(CancellationTokenSource cancel)
        {
            // 请求可以在通知抵达的同时完成并释放，取消应当保持幂等。
            try { cancel.Cancel(); } catch (ObjectDisposedException) { }
        }
        private static object Value(Dictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) ? value : null; }
        private static string StringValue(Dictionary<string, object> obj, string key) { return Value(obj, key) as string ?? ""; }
        private static string IdKey(object id) { return Json().Serialize(id); }
        private static void Result(object id, object result) { Write(new { jsonrpc = "2.0", id = id, result = result }); }
        private static void Error(object id, int code, string message) { Write(new { jsonrpc = "2.0", id = id, error = new { code = code, message = message } }); }
        private static void Write(object value) { lock (OutputLock) { Console.WriteLine(Json().Serialize(value)); Console.Out.Flush(); } }
    }
}
