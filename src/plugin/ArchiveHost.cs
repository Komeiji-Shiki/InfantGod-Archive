using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using InfantGod.Core;
using InfantGod.Core.OSSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.SceneManagement;
using InfantGod.Dialogue;
using InfantGod.UI.WindowSystem.Windows;
using Newtonsoft.Json;
using UnityEngine;

namespace Graywill.InfantGodCodex
{
    public sealed class ArchiveHost : MonoBehaviour
    {
        internal static ArchiveHost Instance;
        private static ManualLogSource hostLog;
        private static ConfigFile hostConfig;
        private static string packagePath;
        private ManualLogSource Logger => hostLog;
        internal ConfigEntry<KeyCode> OpenKey;
        private Harmony inputHooks;
        private RuntimeAccess runtime;
        private Catalog catalog;
        private CGViewer viewer;
        private GameIntegration integration;
        private WebHostBridge webHost;
        private GUIStyle noticeStyle;
        private Font noticeFont;
        private float nextRefresh;
        private int lastBlockedFrame = -1;
        internal string DataPath { get; private set; }
        internal string Notice { get; private set; }
        internal float NoticeUntil { get; private set; }
        internal bool IsOpen { get; private set; }
        internal bool IsAvailable { get; private set; }
        internal GameIntegration Integration => integration;
        private bool CaptureGameInput => IsOpen && integration != null && integration.WindowFocused;

        internal static void Configure(ManualLogSource log, ConfigFile config, string path)
        {
            hostLog = log;
            hostConfig = config;
            packagePath = path;
        }

        private void Awake()
        {
            Instance = this;
            DataPath = packagePath;
            OpenKey = hostConfig.Bind("界面", "打开快捷键", KeyCode.F8, "打开或关闭资料终端。");
            try
            {
                var file = Path.Combine(DataPath, "catalog.json");
                catalog = JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(file));
                if (catalog == null || catalog.schemaVersion != 1) throw new InvalidDataException("catalog.json 版本不受支持。");
                runtime = new RuntimeAccess();
                viewer = new CGViewer();
                runtime.Loaded += HandleLoaded;
                inputHooks = new Harmony("graywill.infantgod.archive.input");
                PatchInput(typeof(LiveStreamSpeakerManager), "UpdateAdvanceHotKey");
                PatchInput(typeof(LiveStreamSpeakerManager), "TryToggleDanmakuInputByHotKey");
                PatchInput(typeof(LiveStreamSpeakerManager), "TrySendDanmakuByEnter");
                PatchInput(typeof(OSManager), "UpdateEscapeShortcut");
                PatchInput(typeof(SaveSystem), "HandleQuickSaveInput");
                PatchInput(typeof(GameDialogueManager), "TriggerModReloadFromHotkey");
                PatchInput(typeof(PVShortcutManager), "Update");
                integration = new GameIntegration(this);
                webHost = new WebHostBridge(this, catalog, runtime, viewer);
                ShowNotice("资料终端已载入。按 " + OpenKey.Value + " 打开。", 8);
                Logger.LogInfo("幼神资料终端载入完成：" + catalog.entries.Count + " 条资料，" + catalog.cg.Count + " 个 CG。");
            }
            catch (Exception error)
            {
                Logger.LogError(error);
                ShowNotice("资料终端未能载入，请查看 BepInEx/LogOutput.log。", 15);
            }
        }

        private void PatchInput(Type type, string name)
        {
            var method = AccessTools.Method(type, name);
            if (method == null)
            {
                Logger.LogWarning("当前游戏版本缺少输入入口：" + type.Name + "." + name);
                return;
            }
            inputHooks.Patch(method, prefix: new HarmonyMethod(typeof(ArchiveHost), nameof(AllowGameInput)));
        }

        private static bool AllowGameInput()
        {
            // 浏览资料时，键盘不能同时点击主线选项或触发游戏快捷键。
            return Instance == null || (!Instance.CaptureGameInput && Time.frameCount != Instance.lastBlockedFrame && !Input.GetKeyDown(Instance.OpenKey.Value));
        }

        private void Update()
        {
            if (runtime == null || integration == null) return;
            if (IsAvailable)
            {
                if (Input.GetKeyDown(OpenKey.Value))
                {
                    SetOpen(!IsOpen || !integration.WindowShowing);
                }
                if (Input.GetKeyDown(KeyCode.Escape) && CaptureGameInput) SetOpen(false);
                runtime.ObserveCG();
                if (IsOpen && Time.unscaledTime >= nextRefresh) RefreshProgress();
            }
            // 管道消息只在 Unity 的更新循环中执行游戏操作。
            webHost?.Tick();
            // 入口资源初始化在核心更新之后，入口尚未准备好时快捷键仍可使用。
            integration.Tick();
        }

        private void LateUpdate()
        {
            if (IsOpen && integration.WindowShowing && viewer != null)
            {
                viewer.Tick();
                webHost?.CaptureCGFrame();
            }
        }

        private void OnGUI()
        {
            if (!IsAvailable) return;
            if (Time.unscaledTime >= NoticeUntil || webHost != null && webHost.IsReady && IsOpen) return;
            // 正文只由 WebView2 显示，宿主尚未就绪时保留状态提示。
            if (noticeStyle == null)
            {
                noticeFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimSun" }, 18);
                noticeStyle = new GUIStyle(GUI.skin.label) { font = noticeFont, fontSize = 18, wordWrap = true, padding = new RectOffset(12, 12, 8, 8) };
                noticeStyle.normal.textColor = new Color32(229, 225, 181, 255);
            }
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;
            float width = Mathf.Min(Screen.width - 32, 920);
            float height = noticeStyle.CalcHeight(new GUIContent(Notice), width);
            var toast = new Rect(16, Screen.height - height - 16, width, height);
            GUI.color = new Color32(42, 48, 46, 255);
            GUI.DrawTexture(toast, Texture2D.whiteTexture);
            GUI.color = new Color32(245, 141, 60, 255);
            GUI.DrawTexture(new Rect(toast.x, toast.y, toast.width, 3), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(toast, Notice, noticeStyle);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        internal void SetOpen(bool value)
        {
            if (value && !IsAvailable) return;
            lastBlockedFrame = Time.frameCount;
            IsOpen = value;
            if (value)
            {
                RefreshProgress(true);
                integration?.OpenWindow();
                webHost?.EnsureStarted();
            }
            else
            {
                if (webHost != null) webHost.CloseCG();
                else viewer?.Dispose();
                integration?.CloseWindow();
            }
        }

        internal void SetAvailable(bool value)
        {
            IsAvailable = value;
            if (!value && IsOpen) SetOpen(false);
        }

        private void HandleLoaded()
        {
            integration?.ClearBackgroundPreview();
            webHost?.CloseCG();
            webHost?.SendProgress(true);
            nextRefresh = 0;
        }

        internal void RefreshProgress(bool force = false)
        {
            runtime.Refresh(catalog);
            nextRefresh = Time.unscaledTime + 1.5f;
            webHost?.SendProgress(force);
        }

        internal void ExportProgress()
        {
            try
            {
                RefreshProgress(true);
                string file = Path.Combine(DataPath, "progress.json");
                File.WriteAllText(file, JsonConvert.SerializeObject(runtime.Current, Formatting.Indented), new UTF8Encoding(false));
                ShowNotice("进度已导出：progress.json。", 8);
            }
            catch (Exception error) { ReportError(error, "进度导出失败：" + error.Message); }
        }

        internal void ShowNotice(string text, float seconds = 6)
        {
            Notice = text;
            NoticeUntil = Time.unscaledTime + seconds;
            webHost?.SendNotice(text);
        }

        internal void ReportError(Exception error, string message)
        {
            Logger.LogError(error);
            ShowNotice(message, 12);
        }

        private void OnDestroy()
        {
            webHost?.Dispose();
            integration?.Dispose();
            if (runtime != null)
            {
                runtime.Loaded -= HandleLoaded;
                runtime.Dispose();
            }
            viewer?.Dispose();
            if (noticeFont != null) UnityEngine.Object.Destroy(noticeFont);
            inputHooks?.UnpatchSelf();
            Instance = null;
        }

    }
}
