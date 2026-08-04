using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FattureViewer.Services;

namespace FattureViewer
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _update;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _installing;

        public UpdateWindow(UpdateInfo update)
        {
            InitializeComponent();
            _update = update;
            VersionText.Text =
                $"Versione installata: {UpdateService.CurrentVersion.ToString(3)}    " +
                $"Nuova versione: {update.Version.ToString(3)}";
            ReleaseNotesText.Text = update.ReleaseNotes;
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateService.EnsureOtherProfilesClosed();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Chiudi l'altro profilo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _installing = true;
            InstallButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Download dell'aggiornamento...";

            try
            {
                string installerPath = UpdateService.GetInstallerDownloadPath(_update);
                var progress = new Progress<double>(value =>
                {
                    DownloadProgress.Value = value;
                    StatusText.Text = $"Download dell'aggiornamento... {value:0}%";
                });

                await UpdateService.DownloadInstallerAsync(
                    _update,
                    installerPath,
                    progress,
                    _cancellation.Token);

                StatusText.Text = "Installazione dell'aggiornamento...";
                await Task.Delay(250);
                UpdateService.StartInstaller(installerPath);
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                _installing = false;
                InstallButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                MessageBox.Show(
                    "Non è stato possibile installare l'aggiornamento.\n\n" + ex.Message,
                    "Errore aggiornamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (_installing)
                _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}
