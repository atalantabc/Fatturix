using System;
using System.Windows;

namespace FattureViewer
{
    public partial class ImportPeriodWindow : Window
    {
        public int ImportYear { get; private set; }
        public int ImportMonth { get; private set; }

        public ImportPeriodWindow()
        {
            InitializeComponent();
            ImportYear = DateTime.Now.Year;
            ImportMonth = DateTime.Now.Month;
            YearBox.Text = ImportYear.ToString();
            MonthBox.Text = ImportMonth.ToString("D2");
            YearBox.Focus();
            YearBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(YearBox.Text, out int year) || year < 2000 || year > 2099)
            {
                ShowError("Inserisci un anno valido.");
                return;
            }

            if (!int.TryParse(MonthBox.Text, out int month) || month < 1 || month > 12)
            {
                ShowError("Inserisci un mese da 1 a 12.");
                return;
            }

            ImportYear = year;
            ImportMonth = month;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
