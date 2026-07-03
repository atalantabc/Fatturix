using System.Windows;
using FattureViewer.ViewModels;

namespace FattureViewer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();
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
    }
}
