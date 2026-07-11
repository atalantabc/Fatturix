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
        private const string SettingsRegistryPath = @"HKEY_CURRENT_USER\Software\FattureViewer";
        private const string PassiveDirectoryValue = "FatturePassiveDirectory";
        private readonly DatabaseService _dbService;

        public MainViewModel()
        {
            _dbService = new DatabaseService();
            LoadInvoices();
            
            ImportCommand = new RelayCommand(ExecuteImport);
            ClearCommand = new RelayCommand(ExecuteClear);
            SetPathCommand = new RelayCommand(ExecuteSetPath);
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
        private ObservableCollection<InvoiceData> _flatSearchResults;
        public ObservableCollection<InvoiceData> FlatSearchResults
        {
            get => _flatSearchResults;
            set { _flatSearchResults = value; OnPropertyChanged(); }
        }

        private bool _isFlatSearch;
        public bool IsFlatSearch
        {
            get => _isFlatSearch;
            set { _isFlatSearch = value; OnPropertyChanged(); }
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
        public ICommand SetPathCommand { get; }

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
            IsFlatSearch = ShouldUseFlatSearch(SearchCompany, FilterMonth);
            FlatSearchResults = new ObservableCollection<InvoiceData>(
                IsFlatSearch ? list.OrderByDescending(i => i.Date) : Enumerable.Empty<InvoiceData>());
            if (IsFlatSearch)
            {
                InvoiceTree = tree;
                return;
            }
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
        public static bool ShouldUseFlatSearch(string companyName, int? month)
        {
            return !string.IsNullOrWhiteSpace(companyName) && !month.HasValue;
        }

        private void ExecuteSetPath(object obj)
        {
            string selectedPath = Microsoft.VisualBasic.Interaction.InputBox(
                "Inserisci il percorso della cartella Fatture Passive. Verrà salvato e usato per tutte le importazioni:",
                "Imposta percorso Fatture Passive",
                GetSavedPassiveDirectory());
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            if (!TryGetPassiveDirectories(selectedPath, out string passiveDir, out _))
            {
                MessageBox.Show("Questa cartella data non è la cartella fatture passive", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Registry.SetValue(SettingsRegistryPath, PassiveDirectoryValue, passiveDir);
                MessageBox.Show(
                    $"Percorso salvato e usato per tutte le importazioni:\n\n{passiveDir}\n\nPuoi cambiarlo in qualsiasi momento da 'Imposta percorso Fatture Passive'.",
                    "Percorso salvato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il salvataggio del percorso: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteImport(object obj)
        {
            string selectedPassiveDir = GetSavedPassiveDirectory();
            if (string.IsNullOrWhiteSpace(selectedPassiveDir))
            {
                MessageBox.Show("Imposta prima il percorso Fatture Passive dal menu con i tre puntini.", "Percorso mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryGetPassiveDirectories(selectedPassiveDir, out string externalPassiveDir, out string archiveDir))
            {
                MessageBox.Show("Questa cartella data non è la cartella fatture passive", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
                string internalPassiveDir = ExtractionEngine.GetConfiguredPath(rules, RuleAction.PassiveDir, "Fatture_Passive");
                Directory.CreateDirectory(internalPassiveDir);

                string importedZipPath = ExtractionEngine.GetAvailablePath(Path.Combine(externalPassiveDir, Path.GetFileName(zipPath)));
                File.Copy(zipPath, importedZipPath);

                string extractDir = ExtractionEngine.ProcessZip(importedZipPath, configContent, externalPassiveDir, out var extractedFiles);
                var newInvoices = extractedFiles
                                                  .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
                                                  .Select(f => InvoiceParser.ParseInvoice(f))
                                                  .Where(i => i.IsValidInvoice)
                                                  .ToList();

                if (!rules.Any(r => r.Action == RuleAction.Copy))
                {
                    foreach (var invoice in newInvoices)
                    {
                        string destName = $"{periodWindow.ImportYear}_{periodWindow.ImportMonth:D2}_{invoice.FileName}";
                        File.Copy(invoice.OriginalFilePath, ExtractionEngine.GetAvailablePath(Path.Combine(internalPassiveDir, destName)));
                    }
                }

                _dbService.SaveInvoices(newInvoices);
                CopyExtractedFilesToArchive(extractedFiles, extractDir, archiveDir, periodWindow.ImportYear, periodWindow.ImportMonth);
                ArchiveZip(importedZipPath, archiveDir, periodWindow.ImportYear, periodWindow.ImportMonth);

                LoadInvoices();
            }
        }

        private static string EnsureConfigFile(string configPath)
        {
            string content = GetDefaultConfig();
            File.WriteAllText(configPath, content);
            return content;
        }

        private static string GetDefaultConfig()
        {
            return "PASSIVE_DIR \"Fatture_Passive\"\r\n" +
                   "COPY \"*.xml\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"\r\n" +
                   "COPY \"*.p7m\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"";
        }
        private static string GetSavedPassiveDirectory()
        {
            return Registry.GetValue(SettingsRegistryPath, PassiveDirectoryValue, "") as string ?? "";
        }


        public static bool TryGetPassiveDirectories(string selectedPath, out string passiveDir, out string archiveDir)
        {
            passiveDir = "";
            archiveDir = "";

            if (string.IsNullOrWhiteSpace(selectedPath))
                return false;

            try
            {
                passiveDir = Path.GetFullPath(selectedPath.Trim());
                archiveDir = Path.Combine(passiveDir, "Archivio");
                return Directory.Exists(passiveDir) && Directory.Exists(archiveDir);
            }
            catch
            {
                passiveDir = "";
                archiveDir = "";
                return false;
            }
        }

        public static void CopyExtractedFilesToArchive(System.Collections.Generic.IEnumerable<string> files, string extractDir, string archiveDir, int year, int month)
        {
            string targetDir = Path.Combine(archiveDir, $"{year}-{month}");
            Directory.CreateDirectory(targetDir);

            foreach (var file in files)
            {
                string relativePath = Path.GetRelativePath(extractDir, file);
                string targetPath = Path.Combine(targetDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, ExtractionEngine.GetAvailablePath(targetPath));
            }
        }

        private static void ArchiveZip(string zipPath, string archiveDir, int year, int month)
        {
            string archiveName = $"{year}-{month}.zip";
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
