using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FattureViewer.Services;

namespace FattureViewer
{
    public partial class ProfileConfigurationWindow : Window
    {
        private bool _updating;

        public IReadOnlyList<AppProfileKind> SelectedProfiles { get; private set; } =
            new List<AppProfileKind>();

        public ProfileConfigurationWindow(
            IEnumerable<AppProfileKind>? selectedProfiles = null,
            bool initialSetup = false)
        {
            InitializeComponent();
            if (!initialSetup)
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _updating = true;
            var selected = (selectedProfiles ?? Enumerable.Empty<AppProfileKind>())
                .ToHashSet();
            OmtCheckBox.IsChecked = selected.Contains(AppProfileKind.Omt);
            CCaseCheckBox.IsChecked = selected.Contains(AppProfileKind.CCase);
            FuchsCheckBox.IsChecked = selected.Contains(AppProfileKind.Fuchs);
            _updating = false;
            ApplyCompatibilityRules();

            if (initialSetup)
            {
                HeadingText.Text = "Prima configurazione";
                CancelButton.Content = "Esci";
            }
        }

        private void StandardProfileChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
                return;
            if ((OmtCheckBox.IsChecked == true || CCaseCheckBox.IsChecked == true) &&
                FuchsCheckBox.IsChecked == true)
            {
                _updating = true;
                FuchsCheckBox.IsChecked = false;
                _updating = false;
            }
            ApplyCompatibilityRules();
        }

        private void FuchsProfileChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
                return;
            if (FuchsCheckBox.IsChecked == true)
            {
                _updating = true;
                OmtCheckBox.IsChecked = false;
                CCaseCheckBox.IsChecked = false;
                _updating = false;
            }
            ApplyCompatibilityRules();
        }

        private void ApplyCompatibilityRules()
        {
            bool fuchsOnly = FuchsCheckBox.IsChecked == true;
            OmtCheckBox.IsEnabled = !fuchsOnly;
            CCaseCheckBox.IsEnabled = !fuchsOnly;
            FuchsCheckBox.IsEnabled = fuchsOnly ||
                                      (OmtCheckBox.IsChecked != true &&
                                       CCaseCheckBox.IsChecked != true);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = new List<AppProfileKind>();
            if (OmtCheckBox.IsChecked == true)
                selected.Add(AppProfileKind.Omt);
            if (CCaseCheckBox.IsChecked == true)
                selected.Add(AppProfileKind.CCase);
            if (FuchsCheckBox.IsChecked == true)
                selected.Add(AppProfileKind.Fuchs);

            if (!ProfileConfigurationService.IsValidCombination(selected))
            {
                ValidationText.Visibility = Visibility.Visible;
                return;
            }

            SelectedProfiles = selected;
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
