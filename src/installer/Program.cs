using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("幼神资料终端安装器")]
[assembly: AssemblyProduct("InfantGod Archive")]
[assembly: AssemblyCompany("Komeiji-Shiki / Graywill")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace Graywill.InfantGodInstaller
{
    internal static class Program
    {
        internal const string Version = "1.1.0";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string gamePath = null;
            string action = null;
            for (int index = 0; index + 1 < args.Length; index++)
            {
                if (args[index] == "--game-path") gamePath = args[++index];
                else if (args[index] == "--action") action = args[++index];
            }
            try { Application.Run(new InstallerForm(gamePath, action)); }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "资料终端安装器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { InstallerTheme.Dispose(); }
        }
    }
}
