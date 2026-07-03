using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using FattureViewer.Models;
using FattureViewer.Services;
using System.IO;

namespace FattureViewer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _dbService;

        public MainViewModel()
        {
            _dbService = new DatabaseService();
            LoadInvoices();
            
            ImportCommand = new RelayCommand(ExecuteImport);
            ClearCommand = new RelayCommand(ExecuteClear);
            ConfigCommand = new RelayCommand(ExecuteConfig);
        }

        private ObservableCollection<InvoiceData> _invoices;
        public ObservableCollection<InvoiceData> Invoices
        {
            get => _invoices;
            set { _invoices = value; OnPropertyChanged(); }
        }

        private ObservableCollection<YearNode> _invoiceTree;
        public ObservableCollection<YearNode> InvoiceTree
        {
            get => _invoiceTree;
            set { _invoiceTree = value; OnPropertyChanged(); }
        }

        private InvoiceData _selectedInvoice;
        public InvoiceData SelectedInvoice
        {
            get => _selectedInvoice;
            set { _selectedInvoice = value; OnPropertyChanged(); LoadInvoiceContent(); }
        }

        private string _invoiceHtmlContent;
        public string InvoiceHtmlContent
        {
            get => _invoiceHtmlContent;
            set { _invoiceHtmlContent = value; OnPropertyChanged(); }
        }

        private string _invoiceXmlContent;
        public string InvoiceXmlContent
        {
            get => _invoiceXmlContent;
            set { _invoiceXmlContent = value; OnPropertyChanged(); }
        }

        // Filters
        private string _searchCompany;
        public string SearchCompany
        {
            get => _searchCompany;
            set { _searchCompany = value; OnPropertyChanged(); FilterInvoices(); }
        }

        private int? _filterYear;
        public int? FilterYear
        {
            get => _filterYear;
            set { _filterYear = value; OnPropertyChanged(); FilterInvoices(); }
        }

        private int? _filterMonth;
        public int? FilterMonth
        {
            get => _filterMonth;
            set { _filterMonth = value; OnPropertyChanged(); FilterInvoices(); }
        }

        public ICommand ImportCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ConfigCommand { get; }

        private void LoadInvoices()
        {
            var list = _dbService.GetAllInvoices();
            Invoices = new ObservableCollection<InvoiceData>(list);
            BuildTree(list);
        }

        private void FilterInvoices()
        {
            var list = _dbService.SearchInvoices(FilterYear, FilterMonth, null, SearchCompany);
            Invoices = new ObservableCollection<InvoiceData>(list);
            BuildTree(list);
        }

        private void BuildTree(System.Collections.Generic.List<InvoiceData> list)
        {
            var tree = new ObservableCollection<YearNode>();
            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;

            var groupedByYear = list.GroupBy(i => i.Date.Year).OrderByDescending(g => g.Key);
            foreach (var yearGroup in groupedByYear)
            {
                var yearNode = new YearNode
                {
                    Name = yearGroup.Key.ToString(),
                    IsExpanded = (yearGroup.Key == currentYear)
                };

                var groupedByMonth = yearGroup.GroupBy(i => i.Date.Month).OrderByDescending(g => g.Key);
                foreach (var monthGroup in groupedByMonth)
                {
                    string monthName = new DateTime(2000, monthGroup.Key, 1).ToString("MMMM", new System.Globalization.CultureInfo("it-IT"));
                    monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

                    var monthNode = new MonthNode
                    {
                        Name = monthName,
                        IsExpanded = (yearGroup.Key == currentYear && monthGroup.Key == currentMonth)
                    };

                    foreach (var invoice in monthGroup.OrderBy(i => i.Date))
                    {
                        monthNode.Invoices.Add(new InvoiceNode
                        {
                            Name = $"{invoice.Date:dd/MM} - {invoice.CompanyName}",
                            Data = invoice
                        });
                    }
                    yearNode.Months.Add(monthNode);
                }
                tree.Add(yearNode);
            }
            InvoiceTree = tree;
        }

        private void ExecuteConfig(object obj)
        {
            var configWin = new ConfigWindow();
            configWin.ShowDialog();
        }

        private void ExecuteImport(object obj)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                string zipPath = openFileDialog.FileName;
                
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                string configContent = "EXTRACT_DIR \"C:\\Temp\\FattureEstratte\"";
                if (File.Exists(configPath))
                {
                    configContent = File.ReadAllText(configPath);
                }

                // Process Zip
                ExtractionEngine.ProcessZip(zipPath, configContent);

                var rules = ConfigParser.Parse(configContent);
                var extractRule = rules.FirstOrDefault(r => r.Action == RuleAction.ExtractDir);
                string extractDir = extractRule?.SourceOrPath;

                if (!string.IsNullOrEmpty(extractDir) && Directory.Exists(extractDir))
                {
                    var files = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories)
                                         .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || 
                                                     f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase));
                    
                    var newInvoices = files.Select(f => InvoiceParser.ParseInvoice(f)).ToList();
                    _dbService.SaveInvoices(newInvoices);
                    LoadInvoices();
                }
            }
        }

        private void ExecuteClear(object obj)
        {
            _dbService.ClearAll();
            LoadInvoices();
        }

        private void LoadInvoiceContent()
        {
            if (SelectedInvoice != null && File.Exists(SelectedInvoice.OriginalFilePath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(SelectedInvoice.OriginalFilePath);
                    string rawContent = "";
                    if (SelectedInvoice.OriginalFilePath.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
                    {
                        rawContent = InvoiceParser.DecodeP7M(bytes);
                    }
                    else
                    {
                        rawContent = System.Text.Encoding.UTF8.GetString(bytes);
                    }

                    InvoiceXmlContent = rawContent;
                    InvoiceHtmlContent = HtmlGenerator.GenerateInvoiceHtml(SelectedInvoice, rawContent);
                }
                catch (Exception ex)
                {
                    InvoiceXmlContent = "Errore: " + ex.Message;
                    InvoiceHtmlContent = $"<html><body><h2 style='color:red'>Errore</h2><p>{ex.Message}</p></body></html>";
                }
            }
            else
            {
                InvoiceXmlContent = "Nessuna fattura selezionata.";
                InvoiceHtmlContent = "<html><body><p>Nessuna fattura selezionata o file non trovato.</p></body></html>";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
