using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FattureViewer.Services
{
    public static class ProfileShortcutService
    {
        public static void Sync(IEnumerable<AppProfileKind> enabledProfiles)
        {
            string applicationPath = Environment.ProcessPath ?? string.Empty;
            if (!applicationPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(applicationPath))
                return;

            AppProfileKind[] enabled = enabledProfiles.Distinct().ToArray();
            SyncDirectory(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                applicationPath,
                enabled);
            SyncDirectory(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs"),
                applicationPath,
                enabled);
        }

        private static void SyncDirectory(
            string directory,
            string applicationPath,
            IReadOnlyCollection<AppProfileKind> enabledProfiles)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (AppProfileKind profile in Enum.GetValues<AppProfileKind>())
            {
                string shortcutPath = Path.Combine(directory, ShortcutName(profile));
                if (!enabledProfiles.Contains(profile))
                {
                    if (File.Exists(shortcutPath))
                        File.Delete(shortcutPath);
                    continue;
                }
                CreateShortcut(applicationPath, shortcutPath, profile);
            }

            string legacyShortcut = Path.Combine(directory, "FattureViewer.lnk");
            if (File.Exists(legacyShortcut))
                File.Delete(legacyShortcut);
        }

        private static string ShortcutName(AppProfileKind profile) => profile switch
        {
            AppProfileKind.CCase => "FattureViewer C.CASE.lnk",
            AppProfileKind.Fuchs => "FattureViewer Fuchs.lnk",
            _ => "FattureViewer OMT.lnk"
        };

        private static void CreateShortcut(
            string applicationPath,
            string shortcutPath,
            AppProfileKind profile)
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = shellType == null ? null : Activator.CreateInstance(shellType);
            if (shell == null)
                return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = applicationPath;
            shortcut.Arguments = $"--profile {ProfileConfigurationService.ProfileId(profile)}";
            shortcut.WorkingDirectory = Path.GetDirectoryName(applicationPath);
            shortcut.Description =
                $"FattureViewer - Profilo {AppProfileService.Create(profile).DisplayName}";
            shortcut.IconLocation = applicationPath + ",0";
            shortcut.Save();
        }
    }
}
