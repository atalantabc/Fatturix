using System.Windows;
using FattureViewer.Services;

namespace FattureViewer
{
    public enum PasswordMode
    {
        Setup,
        Verify,
        Rollback
    }

    public partial class PasswordWindow : Window
    {
        private PasswordMode _mode;

        public PasswordWindow(PasswordMode mode)
        {
            InitializeComponent();
            _mode = mode;

            if (_mode == PasswordMode.Setup)
            {
                TitleText.Text = "Imposta Password";
                DescText.Text = "Inserisci una nuova password per proteggere lo svuotamento del database.";
                CancelButton.Visibility = Visibility.Collapsed; // Must set a password
                this.Closing += (s, e) => { if (DialogResult != true) e.Cancel = true; }; // Prevent closing without setting
            }
            else if (_mode == PasswordMode.Rollback)
            {
                TitleText.Text = "Annulla importazione";
                DescText.Text = "Inserisci la password usata per proteggere lo svuotamento del database.";
            }
            else
            {
                TitleText.Text = "Svuota Database";
                DescText.Text = "Inserisci la tua password per confermare la cancellazione completa.";
            }

            PwdBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string pwd = PwdBox.Password;

            if (_mode == PasswordMode.Setup)
            {
                if (string.IsNullOrWhiteSpace(pwd))
                {
                    ErrorText.Text = "La password non può essere vuota.";
                    ErrorText.Visibility = Visibility.Visible;
                    return;
                }

                PasswordService.SetPassword(pwd);
                DialogResult = true;
                Close();
            }
            else
            {
                if (PasswordService.VerifyPassword(pwd))
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ErrorText.Text = "Password errata!";
                    ErrorText.Visibility = Visibility.Visible;
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
