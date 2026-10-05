using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using InfantGod.Core;
using InfantGod.Core.SaveSystem;
using InfantGod.Dialogue;
using InfantGod.UI.WindowSystem.Windows;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    public sealed class RuntimeAccess : IDisposable
    {
        private AistaltVariableStorage storage;
        private GameTimeManager clock;
        private SaveSystem saves;
        private LiveStreamSpeakerManager speakers;
        private int slot = int.MinValue;
        private readonly HashSet<string> observedCG = new HashSet<string>(StringComparer.Ordinal);
        private static readonly FieldInfo CurrentCG = typeof(LiveStreamSpeakerManager).GetField("charactersByCG", BindingFlags.Instance | BindingFlags.NonPublic);
        public ProgressSnapshot Current { get; private set; } = new ProgressSnapshot();
        public event Action Loaded;

        public RuntimeAccess()
        {
            SaveSystem.OnGameLoaded += OnGameLoaded;
        }

        private void OnGameLoaded(int slotId, SaveData data)
        {
            // 回到旧档后，未来的目击记录不能继续作为当前档案的证据。
            observedCG.Clear();
            Current = new ProgressSnapshot();
            slot = slotId;
            Loaded?.Invoke();
        }

        public void ObserveCG()
        {
            if (speakers == null) speakers = UnityEngine.Object.FindObjectOfType<LiveStreamSpeakerManager>();
            if (speakers == null || CurrentCG == null) return;
            var active = CurrentCG.GetValue(speakers) as IDictionary;
            if (active == null) return;
            foreach (var key in active.Keys)
                if (key is string) observedCG.Add((string)key);
        }

        public ProgressSnapshot Refresh(Catalog catalog)
        {
            if (storage == null) storage = UnityEngine.Object.FindObjectOfType<AistaltVariableStorage>();
            if (clock == null) clock = UnityEngine.Object.FindObjectOfType<GameTimeManager>();
            if (saves == null) saves = UnityEngine.Object.FindObjectOfType<SaveSystem>();
            var next = new ProgressSnapshot();
            if (storage == null)
            {
                Current = next;
                return next;
            }
            int currentSlot = saves != null ? saves.GetActiveSlotId() : -1;
            if (slot != int.MinValue && slot != currentSlot) observedCG.Clear();
            slot = currentSlot;
            var all = storage.GetAllVariables();
            foreach (var item in all.Item1) next.variables[item.Key] = item.Value;
            foreach (var item in all.Item2) next.variables[item.Key] = item.Value;
            foreach (var item in all.Item3) next.variables[item.Key] = item.Value;
            next.Visited = new HashSet<string>(storage.GetVisitedNodes(), StringComparer.Ordinal);
            if (Current.IsReady && Current.Visited.Any(n => !next.Visited.Contains(n))) observedCG.Clear();
            next.visitedNodes = next.Visited.OrderBy(x => x, StringComparer.Ordinal).ToList();
            next.IsReady = true;
            next.slotId = currentSlot;
            next.day = clock != null ? clock.GetCurrentDay() : 0;
            next.hour = clock != null ? clock.GetCurrentHour() : 0;
            next.minute = clock != null ? clock.GetCurrentMinute() : 0;
            next.exportedAt = DateTime.UtcNow.ToString("o");
            next.cgEvidence.observedThisLoad = observedCG.OrderBy(x => x).ToList();
            if (catalog != null)
                next.cgEvidence.recoveredFromCatalog = catalog.cg.Where(c => Visibility.HasProof(c.unlockNodes, c.unlockVariables, next)).Select(c => c.id).Distinct().ToList();
            next.SeenCG = new HashSet<string>(next.cgEvidence.observedThisLoad.Concat(next.cgEvidence.recoveredFromCatalog), StringComparer.Ordinal);
            next.seenCG = next.SeenCG.OrderBy(x => x).ToList();
            Current = next;
            return next;
        }

        public void Dispose()
        {
            SaveSystem.OnGameLoaded -= OnGameLoaded;
            observedCG.Clear();
        }
    }
}
