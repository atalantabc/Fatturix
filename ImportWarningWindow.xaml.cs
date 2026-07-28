using System.Windows;

namespace FattureViewer
{
    public partial class ImportWarningWindow : Window
    {
        public ImportWarningWindow(string message)
        {
            InitializeComponent();
            WarningText.Text = message;
        }

        private void ImportAnywayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
