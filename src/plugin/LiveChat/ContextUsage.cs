using System;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex.LiveChat
{
    internal sealed class ContextUsage
    {
        public string RecordedAt = DateTime.UtcNow.ToString("o");
        public string Purpose;
        public int Epoch;
        public int HistoryMessages;
        public long? InputTokens;
        public long? OutputTokens;
        public long? CachedInputTokens;
        public long? CacheWriteInputTokens;
        public JObject ProviderUsage;

        internal static ContextUsage Read(JObject usage, bool responses)
        {
            var details = usage?[responses ? "input_tokens_details" : "prompt_tokens_details"] as JObject;
            return new ContextUsage
            {
                InputTokens = Number(usage?[responses ? "input_tokens" : "prompt_tokens"]),
                OutputTokens = Number(usage?[responses ? "output_tokens" : "completion_tokens"]),
                CachedInputTokens = Number(details?["cached_tokens"]),
                CacheWriteInputTokens = Number(details?["cache_write_tokens"]),
                ProviderUsage = usage == null ? null : (JObject)usage.DeepClone()
            };
        }

        private static long? Number(JToken value) => value?.Type == JTokenType.Integer ? (long?)value : null;
    }
}
