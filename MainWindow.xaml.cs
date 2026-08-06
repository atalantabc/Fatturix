using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FattureViewer.Services;
using FattureViewer.ViewModels;

namespace FattureViewer
{
    public partial class MainWindow : Window
    {
        private bool _updateCheckStarted;

        public MainWindow()
        {
            AppProfileService.ApplyWindowsIdentity();
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

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.EnsureProfileSetup();
                await viewModel.LoadInitialDataAsync();
            }

            if (!_updateCheckStarted && UpdateService.TryBecomeUpdateOwner())
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
            if (sender is not TreeView ||
                e.OriginalSource is not DependencyObject source)
                return;
            if (FindNearestAncestor<TreeViewItem>(source) is TreeViewItem item)
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
            if (sender is not ListBox ||
                e.OriginalSource is not DependencyObject source)
                return;
            if (FindNearestAncestor<ListBoxItem>(source) is ListBoxItem item)
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

        public static T? FindNearestAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;

                DependencyObject? visualParent = current is Visual
                    ? VisualTreeHelper.GetParent(current)
                    : null;
                current = visualParent ?? LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
