using System;
using System.IO;
using System.Reflection;

namespace FattureViewerInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Installazione di FattureViewer";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=============================================");
            Console.WriteLine("     INSTALLAZIONE DI FATTUREVIEWER v3.1.1   ");
            Console.WriteLine("=============================================");
            Console.ResetColor();
            Console.WriteLine();
            
            try
            {
                // Install to AppData/Local/FattureViewer (so it doesn't require admin privileges)
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FattureViewer");
                if (!Directory.Exists(installDir))
                {
                    Directory.CreateDirectory(installDir);
                }

                string exePath = Path.Combine(installDir, "FattureViewer.exe");
                Console.WriteLine("Estrazione del file eseguibile in corso...");
                Console.WriteLine($"Destinazione: {exePath}");

                // Extract the embedded raw exe
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream? resourceStream = assembly.GetManifestResourceStream("FattureViewerInstaller.FattureViewer.exe"))
                {
                    if (resourceStream == null)
                    {
                        throw new Exception("File sorgente dell'applicazione non trovato all'interno dell'installer.");
                    }
                    
                    // Display progress bar/spinner
                    using (FileStream fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[81920];
                        long totalBytes = resourceStream.Length;
                        long bytesWritten = 0;
                        int read;
                        
                        while ((read = resourceStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fileStream.Write(buffer, 0, read);
                            bytesWritten += read;
                            
                            int percent = (int)((bytesWritten * 100) / totalBytes);
                            Console.Write($"\rProgresso: {percent}% [");
                            int progressBars = percent / 4;
                            Console.Write(new string('=', progressBars));
                            Console.Write(new string(' ', 25 - progressBars));
                            Console.Write("]");
                        }
                    }
                }
                Console.WriteLine();
                Console.WriteLine();

                Console.WriteLine("Creazione collegamento sul Desktop...");
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(exePath, Path.Combine(desktopPath, "FattureViewer.lnk"));

                Console.WriteLine("Creazione collegamento nel menu Start...");
                string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                if (Directory.Exists(startMenuPath))
                {
                    CreateShortcut(exePath, Path.Combine(startMenuPath, "FattureViewer.lnk"));
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine(" Installazione completata con successo! ");
                Console.WriteLine(" Ora puoi avviare FattureViewer dal Desktop!  ");
                Console.WriteLine("---------------------------------------------");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("ERRORE DURANTE L'INSTALLAZIONE:");
                Console.WriteLine(ex.Message);
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Premi un tasto qualsiasi per uscire...");
            Console.ReadKey();
        }

        static void CreateShortcut(string targetPath, string shortcutPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = targetPath;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                        shortcut.Description = "Visualizzatore di Fatture Elettroniche";
                        shortcut.IconLocation = targetPath + ",0";
                        shortcut.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Avviso: Impossibile creare il collegamento '{Path.GetFileName(shortcutPath)}': {ex.Message}");
            }
        }
    }
}
