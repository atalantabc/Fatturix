using System.Diagnostics;
using System.Reflection;

namespace FattureViewerInstaller
{
    internal static class Program
    {
        private const string ApplicationFileName = "FattureViewer.exe";

        private static int Main(string[] args)
        {
            bool silent = HasArgument(args, "--silent");
            bool restart = HasArgument(args, "--restart");
            bool noShortcuts = HasArgument(args, "--no-shortcuts");
            int? parentProcessId = GetIntegerArgument(args, "--parent-pid");
            string? customInstallDirectory =
                GetStringArgument(args, "--install-dir");
            string installDirectory = customInstallDirectory ??
                                      Path.Combine(
                                          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "FattureViewer");
            string applicationPath = Path.Combine(installDirectory, ApplicationFileName);
            bool isUpdate = File.Exists(applicationPath);
            bool succeeded = false;

            if (!silent)
                PrintHeader(isUpdate);

            try
            {
                WaitForApplicationToClose(
                    parentProcessId,
                    silent,
                    waitForAnyApplicationInstance:
                        customInstallDirectory == null);
                Directory.CreateDirectory(installDirectory);

                if (!silent)
                {
                    Console.WriteLine(isUpdate
                        ? "Aggiornamento dell'applicazione in corso..."
                        : "Installazione dell'applicazione in corso...");
                    Console.WriteLine($"Destinazione: {applicationPath}");
                }

                InstallApplicationFile(applicationPath, silent);
                if (!noShortcuts)
                    EnsureShortcuts(applicationPath, silent);
                succeeded = true;

                if (!silent)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine();
                    Console.WriteLine("---------------------------------------------");
                    Console.WriteLine(isUpdate
                        ? " Aggiornamento completato con successo! "
                        : " Installazione completata con successo! ");
                    Console.WriteLine("---------------------------------------------");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine();
                    Console.WriteLine(isUpdate
                        ? "ERRORE DURANTE L'AGGIORNAMENTO:"
                        : "ERRORE DURANTE L'INSTALLAZIONE:");
                    Console.WriteLine(ex.Message);
                    Console.ResetColor();
                }
            }
            finally
            {
                if (restart && File.Exists(applicationPath))
                    StartApplication(applicationPath);
            }

            if (!silent)
            {
                Console.WriteLine();
                Console.WriteLine("Premi un tasto qualsiasi per uscire...");
                Console.ReadKey();
            }

            return succeeded ? 0 : 1;
        }

        private static void PrintHeader(bool isUpdate)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version ??
                              new Version(0, 0, 0);
            string action = isUpdate ? "AGGIORNAMENTO" : "INSTALLAZIONE";
            Console.Title = $"{action} DI FATTUREVIEWER";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=============================================");
            Console.WriteLine($"       {action} FATTUREVIEWER v{version.ToString(3)}");
            Console.WriteLine("=============================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void WaitForApplicationToClose(
            int? parentProcessId,
            bool silent,
            bool waitForAnyApplicationInstance)
        {
            if (parentProcessId.HasValue)
            {
                try
                {
                    using Process parent = Process.GetProcessById(parentProcessId.Value);
                    parent.WaitForExit();
                }
                catch (ArgumentException)
                {
                    // Il processo è già terminato.
                }
                return;
            }

            if (!waitForAnyApplicationInstance)
                return;

            Process[] runningApplications = Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(ApplicationFileName));
            if (runningApplications.Length == 0)
                return;

            if (!silent)
                Console.WriteLine("Chiudi FattureViewer per continuare con l'aggiornamento...");

            foreach (Process process in runningApplications)
            {
                using (process)
                    process.WaitForExit();
            }
        }

        private static void InstallApplicationFile(string applicationPath, bool silent)
        {
            string newPath = applicationPath + ".new";
            string backupPath = applicationPath + ".old";
            DeleteIfExists(newPath);
            DeleteIfExists(backupPath);

            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? resource = assembly.GetManifestResourceStream(
                "FattureViewerInstaller.FattureViewer.exe");
            if (resource == null)
                throw new InvalidOperationException(
                    "File dell'applicazione non trovato all'interno dell'installer.");

            using (var destination = new FileStream(newPath, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                int lastPercent = -1;

                while ((read = resource.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destination.Write(buffer, 0, read);
                    written += read;

                    if (!silent)
                    {
                        int percent = (int)(written * 100 / resource.Length);
                        if (percent != lastPercent)
                        {
                            PrintProgress(percent);
                            lastPercent = percent;
                        }
                    }
                }

                destination.Flush(true);
            }

            if (!silent)
                Console.WriteLine();

            bool hadExistingApplication = File.Exists(applicationPath);
            try
            {
                if (hadExistingApplication)
                    File.Move(applicationPath, backupPath);

                File.Move(newPath, applicationPath);
                DeleteIfExists(backupPath);
            }
            catch
            {
                DeleteIfExists(newPath);
                if (!File.Exists(applicationPath) && File.Exists(backupPath))
                    File.Move(backupPath, applicationPath);
                throw;
            }
        }

        private static void PrintProgress(int percent)
        {
            Console.Write($"\rProgresso: {percent}% [");
            int completed = percent / 4;
            Console.Write(new string('=', completed));
            Console.Write(new string(' ', 25 - completed));
            Console.Write("]");
        }

        private static void EnsureShortcuts(string applicationPath, bool silent)
        {
            string desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "FattureViewer.lnk");
            if (!File.Exists(desktopShortcut))
                CreateShortcut(applicationPath, desktopShortcut, silent);

            string startMenuDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
            if (Directory.Exists(startMenuDirectory))
            {
                string startMenuShortcut = Path.Combine(startMenuDirectory, "FattureViewer.lnk");
                if (!File.Exists(startMenuShortcut))
                    CreateShortcut(applicationPath, startMenuShortcut, silent);
            }
        }

        private static void CreateShortcut(string targetPath, string shortcutPath, bool silent)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    return;

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = "Visualizzatore di Fatture Elettroniche";
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Console.WriteLine(
                        $"Avviso: impossibile creare il collegamento " +
                        $"'{Path.GetFileName(shortcutPath)}': {ex.Message}");
                }
            }
        }

        private static void StartApplication(string applicationPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = applicationPath,
                    WorkingDirectory = Path.GetDirectoryName(applicationPath)!,
                    UseShellExecute = true
                });
            }
            catch
            {
                // L'aggiornamento è concluso; l'utente può avviare l'app dal collegamento.
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            return args.Any(argument =>
                argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static int? GetIntegerArgument(string[] args, string name)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[index + 1], out int value))
                    return value;
            }
            return null;
        }

        private static string? GetStringArgument(string[] args, string name)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(args[index + 1]))
                    return Path.GetFullPath(args[index + 1]);
            }
            return null;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
