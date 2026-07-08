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
using System.Windows;

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
                            Name = $"{invoice.Date:dd/MM} - {invoice.SenderName}",
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
            openFileDialog.Filter = "Zip files (*.zip)|*.zip";
            if (openFileDialog.ShowDialog() == true)
            {
                string zipPath = openFileDialog.FileName;
                if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return;

                var periodWindow = new ImportPeriodWindow { Owner = Application.Current?.MainWindow };
                if (periodWindow.ShowDialog() != true)
                    return;
                
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                string configContent = EnsureConfigFile(configPath);

                var rules = ConfigParser.Parse(configContent);
                string zipWorkDir = ExtractionEngine.GetConfiguredPath(rules, RuleAction.ZipWorkDir, "ZipWork");
                string archiveDir = ExtractionEngine.GetConfiguredPath(rules, RuleAction.ArchiveDir, "Archivio");
                string passiveDir = ExtractionEngine.GetConfiguredPath(rules, RuleAction.PassiveDir, "Fatture_Passive");
                Directory.CreateDirectory(zipWorkDir);
                Directory.CreateDirectory(archiveDir);
                Directory.CreateDirectory(passiveDir);

                string localZipPath = ExtractionEngine.GetAvailablePath(Path.Combine(zipWorkDir, Path.GetFileName(zipPath)));
                File.Copy(zipPath, localZipPath);

                string extractDir = ExtractionEngine.ProcessZip(localZipPath, configContent, periodWindow.ImportYear, periodWindow.ImportMonth);
                var newInvoices = ExtractionEngine.GetInvoiceFiles(extractDir)
                                                  .Select(f => InvoiceParser.ParseInvoice(f))
                                                  .Where(i => i.IsValidInvoice)
                                                  .Select(i => ApplyImportPeriod(i, periodWindow.ImportYear, periodWindow.ImportMonth))
                                                  .ToList();

                if (!rules.Any(r => r.Action == RuleAction.Copy))
                {
                    foreach (var invoice in newInvoices)
                    {
                        string destName = $"{periodWindow.ImportYear}_{periodWindow.ImportMonth:D2}_{invoice.FileName}";
                        File.Copy(invoice.OriginalFilePath, ExtractionEngine.GetAvailablePath(Path.Combine(passiveDir, destName)));
                    }
                }

                _dbService.SaveInvoices(newInvoices);
                ArchiveZip(zipPath, archiveDir, periodWindow.ImportYear, periodWindow.ImportMonth);
                if (File.Exists(localZipPath))
                    File.Delete(localZipPath);

                LoadInvoices();
            }
        }

        private static string EnsureConfigFile(string configPath)
        {
            if (File.Exists(configPath))
                return File.ReadAllText(configPath);

            string content = GetDefaultConfig();
            File.WriteAllText(configPath, content);
            return content;
        }

        private static string GetDefaultConfig()
        {
            return "EXTRACT_DIR \"Estratti\"\r\n" +
                   "PASSIVE_DIR \"Fatture_Passive\"\r\n" +
                   "ARCHIVE_DIR \"Archivio\"\r\n" +
                   "ZIP_WORK_DIR \"ZipWork\"\r\n" +
                   "COPY \"*.xml\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"\r\n" +
                   "COPY \"*.p7m\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"";
        }

        public static InvoiceData ApplyImportPeriod(InvoiceData invoice, int year, int month)
        {
            invoice.Date = new DateTime(year, month, 1);
            invoice.Year = year;
            invoice.Month = month;
            invoice.Day = 1;
            return invoice;
        }

        private static void ArchiveZip(string zipPath, string archiveDir, int year, int month)
        {
            string archiveName = $"{year}_{month:D2}.zip";
            string archivePath = ExtractionEngine.GetAvailablePath(Path.Combine(archiveDir, archiveName));
            File.Move(zipPath, archivePath);
        }

        private void ExecuteClear(object obj)
        {
            var pwdWin = new PasswordWindow(PasswordMode.Verify);
            if (pwdWin.ShowDialog() == true)
            {
                // Clear the Database
                _dbService.ClearAll();

                // Clear the physical files in EXTRACT_DIR
                try
                {
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                    if (File.Exists(configPath))
                    {
                        string configContent = File.ReadAllText(configPath);
                        var rules = ConfigParser.Parse(configContent);
                        var extractRule = rules.FirstOrDefault(r => r.Action == RuleAction.ExtractDir);
                        string extractDir = extractRule?.SourceOrPath;

                        if (!string.IsNullOrEmpty(extractDir) && Directory.Exists(extractDir))
                        {
                            var di = new DirectoryInfo(extractDir);
                            foreach (FileInfo file in di.GetFiles())
                            {
                                file.Delete();
                            }
                            foreach (DirectoryInfo dir in di.GetDirectories())
                            {
                                dir.Delete(true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Errore durante la pulizia dei file: " + ex.Message);
                }

                // Reset UI
                SelectedInvoice = null;
                InvoiceXmlContent = "";
                InvoiceHtmlContent = "";
                LoadInvoices();
            }
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
