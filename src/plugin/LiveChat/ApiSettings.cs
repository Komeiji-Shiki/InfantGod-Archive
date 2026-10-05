using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class ApiSettings
    {
        public string BaseUrl = "";
        public string Model = "";
        public string Protocol = "chat-completions";
        public string Persona = "current";
        public string ExtraInstructions = "";
        public int MaxToolRounds = 48;
        public int TimeoutSeconds = 300;
        public bool McpEnabled;
        public bool SendPromptCacheKey = true;
        public bool RequestStreamUsage = true;
        public int AutoCompactTokenThreshold;
        public int KeepRecentTurns = 4;
        public string ProtectedApiKey = "";
        public string ProtectedHeaders = "";
        public JObject RequestParameters = new JObject();
        [JsonIgnore] public string ApiKey = "";
        [JsonIgnore] public JObject RequestHeaders = new JObject();

        internal static ApiSettings Load(string path)
        {
            if (!File.Exists(path)) return new ApiSettings();
            var value = JsonConvert.DeserializeObject<ApiSettings>(File.ReadAllText(path)) ?? new ApiSettings();
            if (!string.IsNullOrEmpty(value.ProtectedApiKey)) value.ApiKey = SecretStorage.Read(value.ProtectedApiKey);
            if (!string.IsNullOrEmpty(value.ProtectedHeaders)) value.RequestHeaders = JObject.Parse(SecretStorage.Read(value.ProtectedHeaders));
            return value;
        }

        internal void Save(string path)
        {
            Validate(false);
            ProtectedApiKey = string.IsNullOrEmpty(ApiKey) ? "" : SecretStorage.Write(ApiKey);
            ProtectedHeaders = RequestHeaders.Count == 0 ? "" : SecretStorage.Write(RequestHeaders.ToString(Formatting.None));
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
        }

        internal void Validate(bool requireApi = true)
        {
            Uri address;
            if ((requireApi || !string.IsNullOrWhiteSpace(BaseUrl)) && (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out address) || (address.Scheme != "http" && address.Scheme != "https")))
                throw new InvalidOperationException("请填写有效的 API 地址。");
            if (requireApi && string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("请填写模型名称。");
            if (Protocol != "chat-completions" && Protocol != "responses") throw new InvalidOperationException("请选择 API 协议。");
            if (MaxToolRounds < 1 || MaxToolRounds > 256) throw new InvalidOperationException("连续调用轮数应在 1～256 之间。");
            if (TimeoutSeconds < 15 || TimeoutSeconds > 1800) throw new InvalidOperationException("请求超时应在 15～1800 秒之间。");
            if (AutoCompactTokenThreshold < 0) throw new InvalidOperationException("自动压缩阈值不能为负数；0 表示关闭。");
            if (KeepRecentTurns < 1 || KeepRecentTurns > 100) throw new InvalidOperationException("近期保留回合数应在 1～100 之间。");
            if (RequestParameters == null || RequestHeaders == null) throw new InvalidOperationException("额外参数和请求头应为 JSON 对象。");
            foreach (var property in RequestParameters.Properties())
                if (Array.IndexOf(new[] { "model", "messages", "input", "instructions", "tools", "stream", "store", "parallel_tool_calls" }, property.Name) >= 0)
                    throw new InvalidOperationException(property.Name + " 由聊天功能生成，请在对应设置项中修改。");
            if (Protocol == "responses" && RequestParameters["include"] != null &&
                (!(RequestParameters["include"] is JArray include) || include.Any(value => value.Type != JTokenType.String)))
                throw new InvalidOperationException("include 应为字符串数组。");
            foreach (var property in RequestHeaders.Properties())
                if (property.Value.Type != JTokenType.String || property.Name.IndexOfAny(new[] { '\r', '\n' }) >= 0 || ((string)property.Value).IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new InvalidOperationException("请求头应为不含换行的文本值。");
        }

        internal string RedactSecrets(string message)
        {
            // 服务的错误响应可能回显认证信息，不把这些内容写入聊天记录。
            if (!string.IsNullOrWhiteSpace(ApiKey)) message = message.Replace(ApiKey.Trim(), "[已隐藏]");
            foreach (var header in RequestHeaders.Properties())
            {
                string value = header.Value.Type == JTokenType.String ? (string)header.Value : null;
                if (!string.IsNullOrWhiteSpace(value)) message = message.Replace(value, "[已隐藏]");
            }
            return message;
        }

        internal Uri Endpoint()
        {
            Validate();
            var builder = new UriBuilder(BaseUrl.Trim());
            string suffix = Protocol == "responses" ? "/responses" : "/chat/completions";
            string path = builder.Path.TrimEnd('/');
            foreach (string knownSuffix in new[] { "/chat/completions", "/responses" })
                if (path.EndsWith(knownSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    // 填写完整端点后切换协议时，替换末尾端点，不重复追加路径。
                    builder.Path = path.Substring(0, path.Length - knownSuffix.Length) + suffix;
                    return builder.Uri;
                }
            builder.Path = (path.Length == 0 ? "/v1" : path) + suffix;
            return builder.Uri;
        }
    }

    internal static class SecretStorage
    {
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob data, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr data);

        internal static string Write(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
        internal static string Read(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                bool success = protect
                    ? CryptProtectData(ref input, "InfantGod LiveChat", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new InvalidOperationException("无法保存或读取 API Key，请重新填写。Windows 错误：" + Marshal.GetLastWin32Error());
                var bytes = new byte[output.Size];
                Marshal.Copy(output.Data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
