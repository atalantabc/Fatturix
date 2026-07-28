using System;
using System.Threading.Tasks;
using System.Windows;
using FattureViewer.Services;
using FattureViewer.ViewModels;

namespace FattureViewer
{
    public partial class MainWindow : Window
    {
        private bool _updateCheckStarted;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!Services.PasswordService.IsPasswordSet())
            {
                var pwdWin = new PasswordWindow(PasswordMode.Setup);
                pwdWin.Owner = this;
                pwdWin.ShowDialog();
            }

            if (!_updateCheckStarted)
            {
                _updateCheckStarted = true;
                await CheckForUpdatesAsync();
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                UpdateInfo? update = await UpdateService.CheckForUpdateAsync();
                if (update == null || !IsVisible)
                    return;

                var updateWindow = new UpdateWindow(update)
                {
                    Owner = this
                };
                updateWindow.ShowDialog();
            }
            catch
            {
                // L'assenza di rete o di GitHub non deve rallentare né interrompere l'avvio.
            }
        }

        private void TreeView_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is InvoiceNode invoiceNode)
            {
                if (this.DataContext is MainViewModel vm)
                {
                    vm.SelectedInvoice = invoiceNode.Data;
                }
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
