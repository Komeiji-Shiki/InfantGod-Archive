using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Graywill.InfantGodInstaller
{
    internal sealed class Installation
    {
        internal string Status;
        internal string Version;
        internal bool CanRemove { get { return Status == "Installed" || Status == "Installing"; } }

        internal static Installation Read(string root)
        {
            var result = new Installation();
            if (root == null) return result;
            string file = Path.Combine(root, ".InfantGodArchive-install.json");
            if (!File.Exists(file)) return result;
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
            object value;
            if (data.TryGetValue("status", out value)) result.Status = Convert.ToString(value);
            if (data.TryGetValue("version", out value)) result.Version = Convert.ToString(value);
            return result;
        }
    }

    internal sealed class InstallSession : IDisposable
    {
        private string temporaryRoot;

        internal async Task<int> Run(string gameRoot, bool uninstall, Action<string> log)
        {
            return await Task.Run(() =>
            {
                EnsurePackage();
                string script = Path.Combine(temporaryRoot, uninstall ? "Uninstall-Mod.ps1" : "Install-Mod.ps1");
                // 编码完整命令，目录中的空格和单引号不会被当作 PowerShell 语法。
                string command = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); "
                    + "$OutputEncoding = [Console]::OutputEncoding; $ProgressPreference = 'SilentlyContinue'; $ErrorActionPreference = 'Stop'; "
                    + "try { & '" + script.Replace("'", "''") + "' -GamePath '" + gameRoot.Replace("'", "''")
                    + "'; exit 0 } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                var start = new ProcessStartInfo(powershell)
                {
                    Arguments = "-NoLogo -NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = temporaryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = new Process { StartInfo = start })
                {
                    process.OutputDataReceived += (sender, eventArgs) => { if (eventArgs.Data != null) log(eventArgs.Data); };
                    process.ErrorDataReceived += (sender, eventArgs) => { if (eventArgs.Data != null) log(eventArgs.Data); };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            });
        }

        private void EnsurePackage()
        {
            if (temporaryRoot != null) return;
            string root = Path.Combine(Path.GetTempPath(), "InfantGodArchive-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            temporaryRoot = root;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("InfantGodArchive.Payload.zip"))
            using (var package = new ZipArchive(resource, ZipArchiveMode.Read))
            {
                string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                foreach (var entry in package.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包文件路径无效。");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target);
                }
            }
        }

        internal static bool CanWrite(string gameRoot)
        {
            string probe = Path.Combine(gameRoot, ".InfantGodArchive-write-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool Elevate(string gameRoot, bool uninstall)
        {
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--game-path \"" + gameRoot.TrimEnd('\\') + "\" --action " + (uninstall ? "uninstall" : "install")
            };
            try { Process.Start(start); return true; }
            catch (System.ComponentModel.Win32Exception error)
            {
                if (error.NativeErrorCode == 1223) return false;
                throw;
            }
        }

        public void Dispose()
        {
            if (temporaryRoot == null) return;
            // 只清理由本次安装器创建的临时目录，游戏备份与进度不在这里。
            try { Directory.Delete(temporaryRoot, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
