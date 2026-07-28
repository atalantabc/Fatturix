using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using FattureViewer.Models;
using FattureViewer.Services;

namespace FattureViewer
{
    public sealed class ImportReviewItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ImportReviewItem(
            InvoiceData invoice,
            bool isSuspicious = false)
        {
            Invoice = invoice;
            IsSuspicious = isSuspicious;
        }

        public InvoiceData Invoice { get; }
        public bool IsSuspicious { get; }
        public string Status => IsSuspicious ? "Sospetta" : "Corretta";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ImportWarningWindow : Window
    {
        public ObservableCollection<ImportReviewItem> Items { get; }
        public ImportReviewResult Result { get; private set; } =
            new ImportReviewResult { Choice = ImportReviewChoice.Cancel };

        public ImportWarningWindow(
            IReadOnlyList<InvoiceData> allInvoices,
            IReadOnlyList<InvoiceData> suspiciousInvoices,
            InvoiceSection targetSection)
        {
            InitializeComponent();
            var suspiciousIds = new HashSet<string>(
                suspiciousInvoices.Select(invoice => invoice.Id));
            Items = new ObservableCollection<ImportReviewItem>(
                allInvoices
                    .OrderByDescending(invoice =>
                        suspiciousIds.Contains(invoice.Id))
                    .Select(invoice => new ImportReviewItem(
                        invoice,
                        suspiciousIds.Contains(invoice.Id))));
            foreach (ImportReviewItem item in Items)
                item.PropertyChanged += Item_PropertyChanged;
            DataContext = this;

            WarningText.Text = ImportSafetyService.GetWarningMessage(
                suspiciousInvoices.Count,
                targetSection);
            UpdateSelectionText();
        }

        public InvoicePreviewWindow CreatePreviewWindow(
            ImportReviewItem item)
        {
            return new InvoicePreviewWindow(item.Invoice)
            {
                Owner = this
            };
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ImportReviewItem item })
                CreatePreviewWindow(item).ShowDialog();
        }

        private void ImportAllButton_Click(object sender, RoutedEventArgs e)
        {
            Result = new ImportReviewResult
            {
                Choice = ImportReviewChoice.All
            };
            DialogResult = true;
        }

        private void ImportSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Result = new ImportReviewResult
            {
                Choice = ImportReviewChoice.Selected,
                SelectedInvoiceIds = Items
                    .Where(item => item.IsSelected)
                    .Select(item => item.Invoice.Id)
                    .ToArray()
            };
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = new ImportReviewResult
            {
                Choice = ImportReviewChoice.Cancel
            };
            DialogResult = false;
        }

        private void Item_PropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImportReviewItem.IsSelected))
                UpdateSelectionText();
        }

        private void UpdateSelectionText()
        {
            int selected = Items.Count(item => item.IsSelected);
            ImportSelectedButton.IsEnabled = selected > 0;
            SelectionText.Text = selected == 1
                ? "1 fattura selezionata"
                : $"{selected} fatture selezionate";
        }
    }
}
