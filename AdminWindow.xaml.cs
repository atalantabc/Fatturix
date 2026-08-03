using System.Windows;
using System.Windows.Input;

namespace FattureViewer
{
    public partial class AdminWindow : Window
    {
        private const string AdminPassword = "1907";

        public bool ZipImportEnabled { get; private set; }
        public bool SessionNoSaveEnabled { get; private set; }
        public bool AdminDeletionEnabled { get; private set; }
        private bool _initializing = true;

        public AdminWindow(
            bool zipImportEnabled,
            bool sessionNoSaveEnabled = false,
            bool adminDeletionEnabled = false)
        {
            InitializeComponent();
            ZipImportEnabled = zipImportEnabled;
            SessionNoSaveEnabled = sessionNoSaveEnabled;
            AdminDeletionEnabled = adminDeletionEnabled;
            ZipImportCheckBox.IsChecked = zipImportEnabled;
            SessionNoSaveCheckBox.IsChecked = sessionNoSaveEnabled;
            AdminDeletionCheckBox.IsChecked = adminDeletionEnabled;
            _initializing = false;
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
            SessionNoSaveEnabled = SessionNoSaveCheckBox.IsChecked == true;
            AdminDeletionEnabled = AdminDeletionCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void AdminDeletionCheckBox_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (_initializing || AdminDeletionEnabled)
                return;

            var warning = new AdminDeletionWarningWindow
            {
                Owner = this
            };
            if (warning.ShowDialog() != true)
                AdminDeletionCheckBox.IsChecked = false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
