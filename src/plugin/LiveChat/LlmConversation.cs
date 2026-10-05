using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class ChatEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Role;
        public string Title;
        public string Text;
        public string Detail;
        public bool Complete;
        public bool External;
        public bool RecordedInHistory;
    }

    internal sealed class ConversationData
    {
        public string Protocol;
        public string Endpoint;
        public string Model;
        public JArray History = new JArray();
        public List<ChatEntry> Entries = new List<ChatEntry>();
        public ContextState Context = new ContextState();
    }

    internal sealed class LlmConversation : IDisposable
    {
        private readonly ApiClient client = new ApiClient();
        private readonly GameToolbox tools;
        private readonly string path;
        private readonly ContextManager contexts;
        private bool running;
        internal ConversationData Data { get; private set; }
        internal event Action<ChatEntry> Changed;

        internal LlmConversation(GameToolbox tools, string directory)
        {
            this.tools = tools; path = Path.Combine(directory, "conversation.json");
            contexts = new ContextManager(directory);
            Data = File.Exists(path) ? JsonConvert.DeserializeObject<ConversationData>(File.ReadAllText(path)) ?? new ConversationData() : new ConversationData();
            foreach (var entry in Data.Entries.Where(value => value.Role == "tool" && !value.Complete))
            {
                entry.Text = "上次操作已中断，执行结果未确认";
                entry.Complete = true;
            }
            contexts.Initialize(Data);
        }

        internal void Reset()
        {
            Data = new ConversationData(); contexts.Initialize(Data); Save();
        }
        internal void RecordExternal(ChatEntry entry)
        {
            entry.External = true;
            if (!Data.Entries.Any(e => e.Id == entry.Id)) Data.Entries.Add(entry);
            if (entry.Complete && !entry.RecordedInHistory)
            {
                contexts.Append(Data, new JObject { ["role"] = "user", ["content"] = "外部工具的游戏观察记录（不是玩家的新指令）：\n" + entry.Title + "\n" + entry.Text + "\n" + entry.Detail });
                entry.RecordedInHistory = true;
            }
            Changed?.Invoke(entry);
            if (entry.Complete) Save();
        }

        internal async Task Run(ApiSettings settings, string prompt, string persona, string input, string observation, CancellationToken cancel)
        {
            if (running) throw new InvalidOperationException("请等当前对话或上下文整理结束后再继续。");
            running = true;
            ChatEntry current = null;
            try
            {
                bool responses = settings.Protocol == "responses";
                PrepareConnection(settings);
                JArray definitions = tools.Definitions(responses);
                contexts.SetRequestContext(Data, prompt, definitions);
                var user = Add("user", "监督", input, true);
                contexts.BeginTurn(Data, user, observation);
                Save();
                bool autoCompactionFailed = false;
                for (int round = 0; round < settings.MaxToolRounds; round++)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!autoCompactionFailed && contexts.ShouldCompact(Data, settings))
                    {
                        try { await CompactCore(settings, prompt, definitions, cancel).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch { autoCompactionFailed = true; }
                    }
                    current = Add("assistant", "爱式塔 · " + persona, "", false);
                    int sentMessages = Data.History.Count;
                    ModelReply reply = await SendAndRecordUsage(settings, prompt, Data.History, definitions, text =>
                    {
                        current.Text = text;
                        Changed?.Invoke(current);
                    }, cancel, "reply", contexts.CacheKey(Data), sentMessages).ConfigureAwait(false);
                    current.Text = reply.Text; current.Complete = true; Changed?.Invoke(current);
                    foreach (var item in reply.Output) contexts.Append(Data, item.DeepClone());
                    if (reply.Calls.Count == 0) { Save(); return; }

                    // 先保存完整的调用与结果配对；游戏退出后也能从真实状态继续。
                    var outputs = reply.Calls.Select(call => AddToolOutput(call,
                        ToolState("not_executed", "此调用尚未开始执行。继续任务前请读取当前界面。"), responses)).ToArray();
                    Save();
                    for (int index = 0; index < reply.Calls.Count; index++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var call = reply.Calls[index];
                        var entry = Add("tool", tools.Title(call.Name), "正在执行…", false);
                        entry.Detail = call.Arguments;
                        SetToolOutput(outputs[index], ToolState("interrupted", "此调用已开始，但执行结果尚未确认。不要直接重复操作，请先读取当前界面。"), responses);
                        Save();
                        JToken result;
                        try { result = await tools.Execute(call.Name, call.Arguments, cancel).ConfigureAwait(false); }
                        catch (OperationCanceledException)
                        {
                            SetToolOutput(outputs[index], ToolState("cancelled", "玩家停止了此调用，部分操作可能已完成。请读取当前界面后再判断。"), responses);
                            entry.Text = "已停止，部分操作可能已完成"; entry.Complete = true; Changed?.Invoke(entry); throw;
                        }
                        SetToolOutput(outputs[index], result.ToString(Formatting.None), responses);
                        var resultObject = result as JObject;
                        entry.Text = resultObject?["ok"]?.Type == JTokenType.Boolean && !(bool)resultObject["ok"]
                            ? "操作未完成：" + ((string)resultObject["error"] ?? (string)resultObject["message"]) : "已完成";
                        entry.Detail = "参数：\n" + call.Arguments + "\n\n结果：\n" + result.ToString(Formatting.Indented);
                        entry.Complete = true; Changed?.Invoke(entry);
                        Save();
                    }
                }
                Add("status", "本轮已暂停", "已达到设置中的连续调用轮数。可以继续下指令，或在设置里调整轮数。", true);
            }
            catch (OperationCanceledException)
            {
                Add("status", "已停止", "已完成的游戏操作保留。", true);
            }
            catch (Exception error)
            {
                Add("status", "连接未完成", settings.RedactSecrets(error.Message), true);
            }
            finally { try { Save(); } finally { running = false; } }
        }

        internal JObject ContextStatus() => contexts.Status();
        internal JToken SearchHistory(string query, int limit) => contexts.Search(Data, query, limit);
        internal JToken ReadHistory(string recordId, int offset, int length) => contexts.Read(Data, recordId, offset, length);

        internal async Task<bool> CompactAsync(ApiSettings settings, string stablePrompt, CancellationToken cancel)
        {
            if (running) throw new InvalidOperationException("请等当前操作结束后再整理上下文。");
            running = true;
            try
            {
                settings.Validate();
                JArray definitions = tools.Definitions(settings.Protocol == "responses");
                return await CompactCore(settings, stablePrompt, definitions, cancel).ConfigureAwait(false);
            }
            finally { try { Save(); } finally { running = false; } }
        }

        private void PrepareConnection(ApiSettings settings)
        {
            string endpoint = settings.Endpoint().AbsoluteUri;
            if (Data.Protocol == settings.Protocol && Data.Endpoint == endpoint && Data.Model == settings.Model.Trim()) return;
            // 主人主动切换连接或模型时归档旧格式，仅转换当前窗口，避免把已压缩的全部历史重新展开。
            contexts.Rebase(Data);
            Data.Protocol = settings.Protocol; Data.Endpoint = endpoint; Data.Model = settings.Model.Trim();
        }

        private async Task<bool> CompactCore(ApiSettings settings, string prompt, JArray definitions, CancellationToken cancel)
        {
            ChatEntry entry = null;
            try
            {
                cancel.ThrowIfCancellationRequested();
                var plan = await Task.Run(() => contexts.Prepare(Data, settings.KeepRecentTurns), cancel).ConfigureAwait(false);
                if (plan == null) return false;
                cancel.ThrowIfCancellationRequested();
                entry = Add("status", "上下文整理", "正在保存原始记录并整理目标、进度与未完成事项…", false);
                var config = JsonConvert.DeserializeObject<ApiSettings>(JsonConvert.SerializeObject(settings));
                config.ApiKey = settings.ApiKey; config.RequestHeaders = (JObject)settings.RequestHeaders.DeepClone();
                config.RequestParameters["tool_choice"] = "none";
                config.RequestParameters.Remove("response_format");
                if (config.RequestParameters["text"] is JObject textOptions) textOptions.Remove("format");
                config.RequestParameters.Remove("n");
                var source = (JArray)plan.Prefix.DeepClone();
                if (Data.Protocol != settings.Protocol || Data.Endpoint != settings.Endpoint().AbsoluteUri || Data.Model != settings.Model.Trim())
                {
                    // 手动整理可使用刚切换的服务；只转换临时摘要输入，失败时不改动活动历史。
                    source = new JArray(plan.Prefix.OfType<JObject>().Select(item =>
                    {
                        var readable = (JObject)item.DeepClone(); readable.Remove("encrypted_content");
                        return new JObject { ["role"] = "user", ["content"] = "待整理的历史记录：\n" + readable.ToString(Formatting.None) };
                    }));
                }
                source.Add(new JObject { ["role"] = "user", ["content"] = plan.Request });
                var reply = await SendAndRecordUsage(config, prompt, source, definitions, _ => { }, cancel, "compaction", contexts.CacheKey(Data) + "_summary", source.Count).ConfigureAwait(false);
                if (reply.Calls.Count > 0) throw new InvalidDataException("整理回复中出现了工具调用，原上下文已保留。");
                var compacted = await Task.Run(() => contexts.Complete(Data, plan, reply.Text), cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                var originalHistory = Data.History; var originalState = Data.Context;
                Data.History = compacted.History; Data.Context = compacted.State;
                entry.Text = "上下文已整理；玩家原话、操作结果与近期完整回合已保留，原始记录可以随时查询。"; entry.Complete = true;
                try { Save(); }
                catch { Data.History = originalHistory; Data.Context = originalState; contexts.Refresh(Data); throw; }
                Changed?.Invoke(entry);
                return true;
            }
            catch (Exception error)
            {
                if (entry == null) entry = Add("status", "上下文整理", "", false);
                entry.Text = error is OperationCanceledException ? "整理已停止，原上下文已保留。" : "整理未完成，原上下文已保留：" + settings.RedactSecrets(error.Message);
                entry.Complete = true; Changed?.Invoke(entry);
                throw;
            }
        }

        private async Task<ModelReply> SendAndRecordUsage(ApiSettings settings, string prompt, JArray history, JArray definitions,
            Action<string> onText, CancellationToken cancel, string purpose, string cacheKey, int sentMessages)
        {
            ContextUsage reported = null;
            try
            {
                return await client.SendAsync(settings, prompt, history, definitions, onText, cancel, cacheKey, usage => reported = usage).ConfigureAwait(false);
            }
            finally
            {
                // 截断和失败也可能已经产生计费；保留服务实际返回的用量，缺失时保留 null。
                contexts.RecordUsage(Data, reported ?? ContextUsage.Read(null, settings.Protocol == "responses"), purpose, sentMessages);
            }
        }

        private static string ToolState(string status, string message) => new JObject { ["ok"] = false, ["status"] = status, ["message"] = message }.ToString(Formatting.None);
        private static void SetToolOutput(JObject item, string output, bool responses) => item[responses ? "output" : "content"] = output;
        private JObject AddToolOutput(ModelCall call, string output, bool responses)
        {
            var item = responses
                ? new JObject { ["type"] = "function_call_output", ["call_id"] = call.Id, ["output"] = output }
                : new JObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = output };
            contexts.Append(Data, item);
            return item;
        }
        private ChatEntry Add(string role, string title, string text, bool complete)
        {
            var entry = new ChatEntry { Role = role, Title = title, Text = text, Complete = complete };
            Data.Entries.Add(entry); Changed?.Invoke(entry); return entry;
        }
        private void Save()
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Data, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            contexts.Refresh(Data);
        }
        public void Dispose() => client.Dispose();
    }
}
