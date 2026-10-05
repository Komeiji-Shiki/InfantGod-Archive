using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Graywill.InfantGodCodex
{
    public sealed class Catalog
    {
        public int schemaVersion;
        public string title;
        public string version;
        public List<Section> sections = new List<Section>();
        public List<Entry> entries = new List<Entry>();
        public List<CGEntry> cg = new List<CGEntry>();
        public Dictionary<string, string> portraits = new Dictionary<string, string>();
    }

    public sealed class Section
    {
        public string id;
        public string title;
    }

    public sealed class Entry
    {
        public string id;
        public string section;
        public string title;
        public string exploredTitle;
        public string summary;
        public string body;
        public string exploredBody;
        public bool fullModeOnly;
        public bool alwaysVisible;
        public string source;
        public string portrait;
        public List<string> characters = new List<string>();
        public List<string> nodes = new List<string>();
        public List<string> unlockVariables = new List<string>();
        public List<string> cgKeys = new List<string>();
    }

    public sealed class CGEntry
    {
        public string id;
        public string title;
        public string description;
        public string preview;
        public bool developmentOnly;
        public List<string> unlockNodes = new List<string>();
        public List<string> unlockVariables = new List<string>();
    }

    public enum ReadMode
    {
        Unselected,
        Explored,
        Full
    }

    public static class Visibility
    {
        // 仅匹配原始节点名，节点组的基础标题不能证明每个变体都已访问。
        public static bool HasProof(IEnumerable<string> nodes, IEnumerable<string> variables, ProgressSnapshot progress)
        {
            if (!progress.IsReady) return false;
            if (nodes != null && nodes.Any(n => progress.Visited.Contains(n))) return true;
            return variables != null && variables.Any(v => progress.IsTrue(v));
        }

        public static bool CanSee(Entry entry, ReadMode mode, ProgressSnapshot progress)
        {
            if (mode == ReadMode.Full) return true;
            if (mode != ReadMode.Explored || entry.fullModeOnly) return false;
            if (entry.alwaysVisible) return true;
            if (string.IsNullOrWhiteSpace(entry.exploredBody)) return false;
            return HasProof(entry.nodes, entry.unlockVariables, progress);
        }

        public static bool CanSee(CGEntry entry, ReadMode mode, ProgressSnapshot progress)
        {
            if (mode == ReadMode.Full) return true;
            if (mode != ReadMode.Explored) return false;
            return progress.SeenCG.Contains(entry.id) || HasProof(entry.unlockNodes, entry.unlockVariables, progress);
        }

        public static string VisibleBody(Entry entry, ReadMode mode)
        {
            return mode == ReadMode.Explored ? entry.exploredBody ?? (entry.alwaysVisible ? entry.body ?? "" : "") : entry.body ?? "";
        }

        public static bool Matches(Entry entry, string query, ReadMode mode)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var text = VisibleTitle(entry, mode) + "\n" + VisibleBody(entry, mode);
            // 全剧透摘要不参与已探索模式的搜索。
            if (mode == ReadMode.Full) text += "\n" + entry.summary;
            return text.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string VisibleTitle(Entry entry, ReadMode mode)
        {
            return mode == ReadMode.Explored && !string.IsNullOrWhiteSpace(entry.exploredTitle) ? entry.exploredTitle : entry.title;
        }
    }

    public sealed class ProgressSnapshot
    {
        [JsonIgnore] public bool IsReady;
        [JsonIgnore] public HashSet<string> Visited = new HashSet<string>(StringComparer.Ordinal);
        [JsonIgnore] public HashSet<string> SeenCG = new HashSet<string>(StringComparer.Ordinal);
        public string format = "infantgod-progress-v1";
        public int schemaVersion = 1;
        public string exportedAt;
        public string source = "游戏内运行时";
        public int day;
        public int hour;
        public int minute;
        public int slotId;
        public List<string> visitedNodes = new List<string>();
        public Dictionary<string, object> variables = new Dictionary<string, object>(StringComparer.Ordinal);
        public List<string> seenCG = new List<string>();
        public CGEvidence cgEvidence = new CGEvidence();

        public bool IsTrue(string variable)
        {
            if (string.IsNullOrEmpty(variable)) return false;
            if (!variable.StartsWith("$", StringComparison.Ordinal)) variable = "$" + variable;
            object value;
            return variables.TryGetValue(variable, out value) && value is bool && (bool)value;
        }
    }

    public sealed class CGEvidence
    {
        public List<string> observedThisLoad = new List<string>();
        public List<string> recoveredFromCatalog = new List<string>();
    }
}
