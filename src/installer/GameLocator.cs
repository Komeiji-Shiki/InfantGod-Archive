using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Graywill.InfantGodInstaller
{
    internal static class GameLocator
    {
        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                path = Path.GetFullPath(path.Trim().Trim('"'));
                if (File.Exists(path)) path = Path.GetDirectoryName(path);
                if (File.Exists(Path.Combine(path, "Aistalt.exe"))) return path;
                string build = Path.Combine(path, "Build");
                return File.Exists(Path.Combine(build, "Aistalt.exe")) ? build : null;
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        internal static List<string> FindGames()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 安装器也可以直接放在游戏或其 Mods 子目录，逐级查找即可识别。
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                AddGame(found, directory.FullName);
            var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key != null) AddRoot(steamRoots, key.GetValue("SteamPath") as string);
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (var key = hive.OpenSubKey(@"Software\Valve\Steam"))
                if (key != null) AddRoot(steamRoots, key.GetValue("InstallPath") as string);
            AddRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (string root in steamRoots.ToArray())
            {
                string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraries)) continue;
                foreach (string library in Values(File.ReadAllText(libraries), "path")) AddRoot(steamRoots, library);
            }
            foreach (string root in steamRoots)
            {
                string apps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                    foreach (string folder in Values(File.ReadAllText(manifest), "installdir"))
                        AddGame(found, Path.Combine(apps, "common", folder));
            }
            return found.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> Values(string text, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                yield return match.Groups[1].Value.Replace(@"\\", @"\").Replace("\\\"", "\"");
        }

        private static void AddRoot(HashSet<string> roots, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
        }

        private static void AddGame(HashSet<string> games, string path)
        {
            string resolved = Normalize(path);
            if (resolved != null) games.Add(resolved);
        }

        internal static bool HasWebViewRuntime()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            using (var registry = RegistryKey.OpenBaseKey(hive, view))
            using (var clients = registry.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients"))
            {
                if (clients == null) continue;
                foreach (string child in clients.GetSubKeyNames())
                using (var item = clients.OpenSubKey(child))
                {
                    string name = Convert.ToString(item.GetValue("name"));
                    string version = Convert.ToString(item.GetValue("pv"));
                    if (name.IndexOf("WebView2", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(version) && version != "0.0.0.0") return true;
                }
            }
            return false;
        }
    }
}
