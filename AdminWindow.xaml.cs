using System.Windows;
using System.Windows.Input;

namespace FattureViewer
{
    public partial class AdminWindow : Window
    {
        private const string AdminPassword = "1907";

        public bool ZipImportEnabled { get; private set; }

        public AdminWindow(bool zipImportEnabled)
        {
            InitializeComponent();
            ZipImportEnabled = zipImportEnabled;
            ZipImportCheckBox.IsChecked = zipImportEnabled;
            Loaded += (_, _) => AdminPasswordBox.Focus();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            TryLogin();
        }

        private void AdminPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                TryLogin();
        }

        private void TryLogin()
        {
            if (AdminPasswordBox.Password != AdminPassword)
            {
                LoginErrorText.Visibility = Visibility.Visible;
                AdminPasswordBox.SelectAll();
                AdminPasswordBox.Focus();
                return;
            }

            LoginPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ZipImportEnabled = ZipImportCheckBox.IsChecked == true;
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
