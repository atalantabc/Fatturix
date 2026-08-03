using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedDeletionTarget = e.NewValue;
                if (e.NewValue is InvoiceNode invoiceNode)
                    vm.SelectedInvoice = invoiceNode.Data;
            }
        }

        private void TreeView_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not TreeView treeView ||
                e.OriginalSource is not DependencyObject source)
                return;
            if (ItemsControl.ContainerFromElement(treeView, source) is
                TreeViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
            else if (DataContext is MainViewModel vm)
            {
                vm.SelectedDeletionTarget = null;
            }
        }

        private void InvoiceList_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not ListBox listBox ||
                e.OriginalSource is not DependencyObject source)
                return;
            if (ItemsControl.ContainerFromElement(listBox, source) is
                ListBoxItem item)
            {
                item.IsSelected = true;
                if (DataContext is MainViewModel vm)
                    vm.SelectedDeletionTarget = item.DataContext;
            }
            else if (DataContext is MainViewModel vm)
            {
                vm.SelectedDeletionTarget = null;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
