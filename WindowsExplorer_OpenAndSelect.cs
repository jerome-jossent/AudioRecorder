using System.Diagnostics;
using System.IO;

namespace JJO_Tools
{
    public class WindowsExplorer_OpenAndSelect
    {
        internal static void OpenAndSelect(FileInfo file)
        {
            OpenAndSelect(file.FullName);
        }

        internal static void OpenAndSelect(string path)
        {
            if (!File.Exists(path)) return;

            string explorerCommand = $"explorer.exe";
            string arguments = $"/select,\"{path}\"";

            try
            {
                // Démarrer le processus
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerCommand,
                    Arguments = arguments,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de l’ouverture d’Explorer : {ex.Message}");
            }
        }
    }
}