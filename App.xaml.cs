using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using FattureViewer.Services;

namespace FattureViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            IReadOnlyList<AppProfileKind>? profiles =
                ProfileConfigurationService.GetOrMigrate();
            if (profiles == null)
            {
                var configurationWindow = new ProfileConfigurationWindow(
                    initialSetup: true);
                if (configurationWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                profiles = configurationWindow.SelectedProfiles;
                ProfileConfigurationService.Save(profiles);
                TrySyncShortcuts(profiles);
            }

            if (!profiles.Contains(AppProfileService.Current.Kind))
            {
                RestartWithProfile(
                    ProfileConfigurationService.GetPreferredProfile(profiles));
                Shutdown();
                return;
            }

            MainWindow = new MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }

        private static void RestartWithProfile(AppProfileKind profile)
        {
            string applicationPath = Environment.ProcessPath ?? string.Empty;
            if (!File.Exists(applicationPath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = applicationPath,
                Arguments = $"--profile {ProfileConfigurationService.ProfileId(profile)}",
                WorkingDirectory = Path.GetDirectoryName(applicationPath) ?? string.Empty,
                UseShellExecute = true
            });
        }

        private static void TrySyncShortcuts(IEnumerable<AppProfileKind> profiles)
        {
            try
            {
                ProfileShortcutService.Sync(profiles);
            }
            catch
            {
                // La configurazione resta valida anche se Windows non aggiorna i collegamenti.
            }
        }
    }
}
