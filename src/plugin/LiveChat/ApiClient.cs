using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class ModelReply
    {
        internal string Text;
        internal JArray Output;
        internal ContextUsage Usage;
        internal List<ModelCall> Calls = new List<ModelCall>();
    }

    internal sealed class ModelCall
    {
        internal string Id;
        internal string Name;
        internal string Arguments;
    }

    internal sealed class ApiClient : IDisposable
    {
        private readonly HttpClient http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        internal async Task<ModelReply> SendAsync(ApiSettings settings, string instructions, JArray history,
            JArray definitions, Action<string> onText, CancellationToken cancel, string cacheKey = null, Action<ContextUsage> onUsage = null)
        {
            bool responses = settings.Protocol == "responses";
            var body = (JObject)settings.RequestParameters.DeepClone();
            body["model"] = settings.Model.Trim(); body["stream"] = true; body["tools"] = definitions; body["parallel_tool_calls"] = false;
            if (settings.SendPromptCacheKey && body.Property("prompt_cache_key") == null && !string.IsNullOrEmpty(cacheKey)) body["prompt_cache_key"] = cacheKey;
            if (responses)
            {
                body["instructions"] = instructions;
                body["input"] = history;
                body["store"] = false;
                var include = body["include"] as JArray ?? new JArray();
                if (!include.Any(value => (string)value == "reasoning.encrypted_content")) include.Add("reasoning.encrypted_content");
                body["include"] = include;
            }
            else
            {
                var messages = new JArray(new JObject { ["role"] = "system", ["content"] = instructions });
                foreach (var item in history) messages.Add(item.DeepClone());
                body["messages"] = messages;
                if (settings.RequestStreamUsage)
                {
                    var streamOptions = body["stream_options"] as JObject ?? new JObject();
                    if (streamOptions.Property("include_usage") == null) streamOptions["include_usage"] = true;
                    body["stream_options"] = streamOptions;
                }
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint()))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                foreach (var header in settings.RequestHeaders.Properties())
                {
                    request.Headers.Remove(header.Name);
                    if (!request.Headers.TryAddWithoutValidation(header.Name, (string)header.Value))
                        throw new InvalidOperationException("无法设置请求头：" + header.Name);
                }
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    using (timeout.Token.Register(() => response.Dispose()))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            throw new InvalidOperationException("API 返回 " + (int)response.StatusCode + "：" + ErrorMessage(error));
                        }
                        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                        {
                            string raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            timeout.Token.ThrowIfCancellationRequested();
                            var json = JObject.Parse(raw);
                            onUsage?.Invoke(ContextUsage.Read(json["usage"] as JObject, responses));
                            // 非流式兼容服务的截断文本同样保留在界面上，但不能执行其中的工具。
                            onText(ResponseText(json, responses));
                            return Decode(json, responses);
                        }
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var assembler = new StreamReply(responses, onText, onUsage);
                            var data = new StringBuilder();
                            while (true)
                            {
                                timeout.Token.ThrowIfCancellationRequested();
                                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) break;
                                if (line.Length == 0)
                                {
                                    if (data.Length > 0) { assembler.Add(data.ToString()); data.Clear(); }
                                    if (assembler.Done) break;
                                }
                                else if (line.StartsWith("data:", StringComparison.Ordinal))
                                {
                                    if (data.Length > 0) data.Append('\n');
                                    data.Append(line.Substring(5).TrimStart());
                                }
                            }
                            if (data.Length > 0) assembler.Add(data.ToString());
                            timeout.Token.ThrowIfCancellationRequested();
                            return assembler.Finish();
                        }
                    }
                }
                catch (Exception error) when (timeout.IsCancellationRequested)
                {
                    // 读取响应体时取消会表现为流已关闭，统一还原真正的停止原因。
                    cancel.ThrowIfCancellationRequested();
                    throw new TimeoutException("API 请求超过设置的 " + settings.TimeoutSeconds + " 秒，已停止本轮回复。", error);
                }
            }
        }

        internal static ModelReply Decode(JObject json, bool responses)
        {
            if (HasError(json)) throw new InvalidOperationException(ErrorMessage(json.ToString()));
            var result = new ModelReply { Usage = ContextUsage.Read(json["usage"] as JObject, responses) };
            if (responses)
            {
                if ((string)json["status"] != "completed") throw new InvalidOperationException(IncompleteMessage((string)(json["incomplete_details"] as JObject)?["reason"] ?? (string)json["status"]));
                result.Output = json["output"] as JArray ?? throw new InvalidDataException("API 没有返回完整的 output。");
                result.Text = ResponseText(json, true);
                foreach (var item in result.Output.OfType<JObject>().Where(i => (string)i["type"] == "function_call"))
                {
                    if (item["status"]?.Type == JTokenType.String && (string)item["status"] != "completed")
                        throw new InvalidDataException("API 返回的工具调用尚未完成，本轮操作未执行。");
                    result.Calls.Add(new ModelCall { Id = (string)item["call_id"], Name = (string)item["name"], Arguments = (string)item["arguments"] });
                }
            }
            else
            {
                var choice = FirstChoice(json);
                string finish = (string)choice?["finish_reason"];
                if (finish != "stop" && finish != "tool_calls") throw new InvalidOperationException(IncompleteMessage(finish));
                var message = choice?["message"] as JObject;
                if (message == null) throw new InvalidDataException("API 没有返回聊天消息。");
                result.Output = new JArray(message.DeepClone());
                result.Text = ResponseText(json, false);
                foreach (var item in message["tool_calls"] as JArray ?? new JArray())
                    result.Calls.Add(new ModelCall { Id = (string)item["id"], Name = (string)item["function"]?["name"], Arguments = (string)item["function"]?["arguments"] });
                if (result.Calls.Count > 0 && finish != "tool_calls") throw new InvalidDataException("API 未确认工具调用完成，本轮操作未执行。");
                if (finish == "tool_calls" && result.Calls.Count == 0) throw new InvalidDataException("API 未返回所声明的工具调用。");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var call in result.Calls)
                if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name) || string.IsNullOrWhiteSpace(call.Arguments) || !ids.Add(call.Id))
                    throw new InvalidDataException("API 返回的工具调用缺少完整参数或包含重复编号，本轮操作未执行。");
            return result;
        }

        private static JObject FirstChoice(JObject json) => (json["choices"] as JArray)?.OfType<JObject>().FirstOrDefault(choice => ((int?)choice["index"] ?? 0) == 0);
        private static bool HasError(JObject json) => json["error"] != null && json["error"].Type != JTokenType.Null;

        private static string ResponseText(JObject json, bool responses)
        {
            if (responses)
                return string.Concat((json["output"] as JArray ?? new JArray()).OfType<JObject>().Where(item => (string)item["type"] == "message")
                    .SelectMany(item => item["content"] as JArray ?? new JArray()).OfType<JObject>()
                    .Select(item => (string)item["text"] ?? (string)item["refusal"] ?? ""));
            var message = FirstChoice(json)?["message"];
            return (message?["content"]?.Type == JTokenType.String ? (string)message["content"] : "")
                + (message?["refusal"]?.Type == JTokenType.String ? (string)message["refusal"] : "");
        }

        private static string IncompleteMessage(string reason)
        {
            if (reason == "length" || reason == "max_output_tokens") return "模型达到输出 Token 上限，回复未完成，本轮工具调用未执行。可以在设置中提高上限后继续。";
            if (reason == "content_filter") return "服务的内容过滤中止了回复，本轮工具调用未执行。";
            return "API 未确认回复完成，本轮工具调用未执行。";
        }

        private static string ErrorMessage(string body)
        {
            try
            {
                var json = JObject.Parse(body);
                return (string)(json["error"] as JObject)?["message"] ?? (json["error"]?.Type == JTokenType.String ? (string)json["error"] : null)
                    ?? (string)json["message"] ?? "请求未完成。";
            }
            catch (JsonException) { return "服务返回了无法解析的错误响应。"; }
        }

        public void Dispose() => http.Dispose();

        private sealed class StreamReply
        {
            private readonly bool responses;
            private readonly Action<string> notify;
            private readonly Action<ContextUsage> notifyUsage;
            private readonly StringBuilder text = new StringBuilder();
            private readonly StringBuilder reasoning = new StringBuilder();
            private readonly StringBuilder refusal = new StringBuilder();
            private readonly SortedDictionary<int, JObject> calls = new SortedDictionary<int, JObject>();
            private JObject finalResponse;
            private string finishReason;
            private JObject usage;
            internal bool Done { get; private set; }

            internal StreamReply(bool responses, Action<string> notify, Action<ContextUsage> notifyUsage) { this.responses = responses; this.notify = notify; this.notifyUsage = notifyUsage; }

            internal void Add(string data)
            {
                if (data == "[DONE]") { Done = true; return; }
                var json = JObject.Parse(data);
                if (HasError(json)) throw new InvalidOperationException(ErrorMessage(data));
                if (json["usage"] is JObject reportedUsage)
                {
                    usage = (JObject)reportedUsage.DeepClone(); notifyUsage?.Invoke(ContextUsage.Read(reportedUsage, responses));
                }
                if (responses)
                {
                    if ((json["response"] as JObject)?["usage"] is JObject responseUsage) notifyUsage?.Invoke(ContextUsage.Read(responseUsage, true));
                    string type = (string)json["type"];
                    if (type == "response.output_text.delta" || type == "response.refusal.delta") { text.Append((string)json["delta"]); notify(text.ToString()); }
                    else if (type == "response.completed") { finalResponse = json["response"] as JObject; Done = true; }
                    else if (type == "response.failed" || type == "response.incomplete" || type == "error")
                    {
                        var response = json["response"] as JObject;
                        if (response != null)
                        {
                            string partial = ResponseText(response, true);
                            if (partial.Length > 0) notify(partial);
                            if (HasError(response)) throw new InvalidOperationException(ErrorMessage(response.ToString()));
                            throw new InvalidOperationException(IncompleteMessage((string)(response["incomplete_details"] as JObject)?["reason"]));
                        }
                        throw new InvalidOperationException(ErrorMessage(data));
                    }
                    return;
                }
                var choice = FirstChoice(json);
                if (choice == null) return;
                if (finishReason != null) return;
                var delta = choice["delta"] as JObject;
                if (delta != null)
                {
                    if (delta["content"]?.Type == JTokenType.String) { text.Append((string)delta["content"]); notify(text.ToString() + refusal); }
                    if (delta["refusal"]?.Type == JTokenType.String) { refusal.Append((string)delta["refusal"]); notify(text.ToString() + refusal); }
                    if (delta["reasoning_content"]?.Type == JTokenType.String) reasoning.Append((string)delta["reasoning_content"]);
                    foreach (var item in delta["tool_calls"] as JArray ?? new JArray())
                    {
                        int index = (int?)item["index"] ?? -1;
                        if (index < 0) throw new InvalidDataException("API 流中的工具调用缺少序号，本轮操作未执行。");
                        if (!calls.TryGetValue(index, out var call)) calls[index] = call = new JObject { ["type"] = "function", ["function"] = new JObject { ["name"] = "", ["arguments"] = "" } };
                        if (item["id"]?.Type == JTokenType.String)
                        {
                            if (call["id"] != null && (string)call["id"] != (string)item["id"]) throw new InvalidDataException("API 流中的工具调用编号不一致，本轮操作未执行。");
                            call["id"] = item["id"].DeepClone();
                        }
                        var function = call["function"];
                        if (item["function"]?["name"] != null) function["name"] = (string)function["name"] + (string)item["function"]["name"];
                        if (item["function"]?["arguments"] != null) function["arguments"] = (string)function["arguments"] + (string)item["function"]["arguments"];
                        // 部分兼容服务把续接签名放在工具项中，原样带回后续请求。
                        foreach (var property in ((JObject)item).Properties().Where(p => p.Name != "index" && p.Name != "id" && p.Name != "function")) call[property.Name] = property.Value.DeepClone();
                    }
                }
                // Chat 的 usage 通常在 finish_reason 之后独立发送，等到 [DONE] 再结束读取。
                if (choice["finish_reason"]?.Type == JTokenType.String) finishReason = (string)choice["finish_reason"];
            }

            internal ModelReply Finish()
            {
                if (responses)
                {
                    if (finalResponse == null) throw new IOException("API 连接在回复完成前断开。");
                    // 保留完整 output，包括 reasoning 与 encrypted_content，供下一轮工具结果续接。
                    return Decode(finalResponse, true);
                }
                if (finishReason == null) throw new IOException("API 连接在回复完成前断开，未收到完整的结束状态。");
                var message = new JObject { ["role"] = "assistant", ["content"] = text.ToString() };
                if (reasoning.Length > 0) message["reasoning_content"] = reasoning.ToString();
                if (refusal.Length > 0) message["refusal"] = refusal.ToString();
                if (calls.Count > 0) message["tool_calls"] = new JArray(calls.Values);
                return Decode(new JObject { ["choices"] = new JArray(new JObject { ["message"] = message, ["finish_reason"] = finishReason }), ["usage"] = usage }, false);
            }
        }
    }
}
