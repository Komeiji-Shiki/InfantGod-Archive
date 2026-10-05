using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Graywill.InfantGodCodex
{
    [BepInPlugin("graywill.infantgod.archive", "幼神资料终端", "1.0.2")]
    [BepInProcess("Aistalt.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("资料终端引导已注册，等待游戏场景准备。");
            ArchiveHost.Configure(Logger, Config, Path.GetDirectoryName(Info.Location));
            ArchiveHostLifetime.Initialize(Logger);
        }
    }

    internal static class ArchiveHostLifetime
    {
        private static bool initialized;
        private static bool quitting;
        private static ManualLogSource log;

        internal static void Initialize(ManualLogSource logger)
        {
            if (initialized) return;
            initialized = true;
            log = logger;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnQuit;
            // 初始场景装入前，加载器创建的对象可能被引擎清理。
            // 使用静态场景事件，在初始装入后创建真实宿主。
            if (Time.frameCount > 0 && SceneManager.GetActiveScene().isLoaded) EnsureHost();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureHost();
        }

        private static void EnsureHost()
        {
            if (quitting || ArchiveHost.Instance != null) return;
            var host = new GameObject("BepInEx_InfantGodArchive");
            host.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ArchiveHost>();
            log.LogInfo("资料终端独立宿主已建立；场景=" + SceneManager.GetActiveScene().name + "，frame=" + Time.frameCount);
        }

        private static void OnQuit()
        {
            quitting = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= OnQuit;
        }
    }
}
