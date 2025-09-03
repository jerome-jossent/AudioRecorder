using System.Diagnostics;
using System.IO;

namespace JJO_Tools// Enregistreur_vocal
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

            // commande Explorer /select,"<chemin>"
            string explorerCommand = $"explorer.exe";
            string arguments = $"/select,\"{path}\"";

            try
            {
                // Démarrer le processus
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerCommand,
                    Arguments = arguments,
                    UseShellExecute = true   // important sous .NET Core/5+/6+
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de l’ouverture d’Explorer : {ex.Message}");
            }

        }

    }
}
