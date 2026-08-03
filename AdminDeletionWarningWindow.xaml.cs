using System.Windows;

namespace FattureViewer
{
    public partial class AdminDeletionWarningWindow : Window
    {
        public AdminDeletionWarningWindow()
        {
            InitializeComponent();
        }

        private void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
