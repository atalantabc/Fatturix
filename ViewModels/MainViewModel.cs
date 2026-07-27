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
using System.IO.Compression;
using System.Windows;

namespace FattureViewer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private const string SettingsRegistryPath = @"HKEY_CURRENT_USER\Software\FattureViewer";
        private const string PassiveDirectoryValue = "FatturePassiveDirectory";
        private DatabaseService _dbService;

        public MainViewModel()
        {
            _dbService = new DatabaseService();
            LoadInvoices();
            
            ImportCommand = new RelayCommand(ExecuteImport);
            ImportDatabaseCommand = new RelayCommand(ExecuteImportDatabase);
            ClearCommand = new RelayCommand(ExecuteClear);
            SetPathCommand = new RelayCommand(ExecuteSetPath);
            SetDatabasePathCommand = new RelayCommand(ExecuteSetDatabasePath);
            AdminCommand = new RelayCommand(ExecuteAdmin);
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
        public ICommand ImportDatabaseCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand SetPathCommand { get; }
        public ICommand SetDatabasePathCommand { get; }
        public ICommand AdminCommand { get; }

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
            if (!EnsureImportsEnabled())
                return;

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
                
                string importedZipPath = ExtractionEngine.GetAvailablePath(Path.Combine(externalPassiveDir, Path.GetFileName(zipPath)));
                File.Copy(zipPath, importedZipPath);

                string extractDir = ExtractionEngine.ProcessZip(importedZipPath, string.Empty, externalPassiveDir, out var extractedFiles);
                var newInvoices = extractedFiles
                                                  .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                                              f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
                                                  .Select(CreateStoredInvoice)
                                                  .Where(i => i != null)
                                                  .Cast<InvoiceData>()
                                                  .ToList();

                _dbService.SaveInvoices(newInvoices);
                CopyExtractedFilesToArchive(extractedFiles, extractDir, archiveDir, periodWindow.ImportYear, periodWindow.ImportMonth);
                ArchiveZip(importedZipPath, archiveDir, periodWindow.ImportYear, periodWindow.ImportMonth);

                LoadInvoices();
            }
        }

        private void ExecuteImportDatabase(object obj)
        {
            if (!EnsureImportsEnabled())
                return;

            var dialog = new OpenFileDialog
            {
                Filter = "Fatture e archivi ZIP (*.xml;*.p7m;*.zip)|*.xml;*.p7m;*.zip",
                Multiselect = true,
                Title = "Importa nel database interno"
            };

            if (dialog.ShowDialog() != true)
                return;

            var invoices = ReadDatabaseImportFiles(dialog.FileNames);
            _dbService.SaveInvoices(invoices);
            LoadInvoices();

            MessageBox.Show(
                $"Importate {invoices.Count} fatture nel database interno. Nessun file è stato copiato nell'archivio ZIP.",
                "Importazione completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ExecuteSetDatabasePath(object obj)
        {
            string selectedPath = Microsoft.VisualBasic.Interaction.InputBox(
                "Inserisci la cartella del database interno. Sono supportati anche percorsi di rete, ad esempio \\\\SERVER\\Fatture:",
                "Imposta percorso database",
                _dbService.StorageDirectory);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            try
            {
                string newDirectory = AppSettingsService.NormalizeStorageDirectory(selectedPath);
                if (string.Equals(newDirectory, _dbService.StorageDirectory, StringComparison.OrdinalIgnoreCase))
                    return;

                var replacement = new DatabaseService(newDirectory, migrateLegacyDatabase: false);
                replacement.SaveInvoices(_dbService.GetAllInvoicesWithContent());
                AppSettingsService.SetStorageDirectory(newDirectory);

                _dbService.Dispose();
                _dbService = replacement;
                LoadInvoices();

                MessageBox.Show(
                    $"Database spostato e percorso salvato:\n\n{newDirectory}",
                    "Percorso database",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Impossibile usare il percorso indicato. Verifica che la cartella locale o di rete sia raggiungibile e scrivibile.\n\n" + ex.Message,
                    "Errore percorso database",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExecuteAdmin(object obj)
        {
            var window = new AdminWindow(AppSettingsService.IsZipImportEnabled())
            {
                Owner = Application.Current?.MainWindow
            };

            if (window.ShowDialog() == true)
                AppSettingsService.SetZipImportEnabled(window.ZipImportEnabled);
        }

        private static InvoiceData? CreateStoredInvoice(string filePath)
        {
            try
            {
                return CreateStoredInvoice(Path.GetFileName(filePath), File.ReadAllBytes(filePath));
            }
            catch
            {
                return null;
            }
        }

        private static InvoiceData? CreateStoredInvoice(string fileName, byte[] content)
        {
            InvoiceData invoice = InvoiceParser.ParseInvoice(fileName, content);
            if (!invoice.IsValidInvoice)
                return null;

            invoice.FileContent = content;
            invoice.OriginalFilePath = string.Empty;
            return invoice;
        }

        public static System.Collections.Generic.List<InvoiceData> ReadDatabaseImportFiles(
            System.Collections.Generic.IEnumerable<string> filePaths)
        {
            const long maxInvoiceBytes = 50L * 1024 * 1024;
            var invoices = new System.Collections.Generic.List<InvoiceData>();

            foreach (string filePath in filePaths)
            {
                try
                {
                    if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (new FileInfo(filePath).Length <= maxInvoiceBytes)
                        {
                            InvoiceData? invoice = CreateStoredInvoice(filePath);
                            if (invoice != null) invoices.Add(invoice);
                        }
                        continue;
                    }

                    using ZipArchive archive = ZipFile.OpenRead(filePath);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || entry.Length <= 0 || entry.Length > maxInvoiceBytes ||
                            (!entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                             !entry.Name.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase)))
                            continue;

                        using Stream source = entry.Open();
                        using var content = new MemoryStream((int)entry.Length);
                        source.CopyTo(content);
                        InvoiceData? invoice = CreateStoredInvoice(entry.Name, content.ToArray());
                        if (invoice != null) invoices.Add(invoice);
                    }
                }
                catch
                {
                    // Un file non valido non interrompe l'importazione degli altri.
                }
            }

            return invoices;
        }

        private static bool EnsureImportsEnabled()
        {
            if (AppSettingsService.IsZipImportEnabled())
                return true;

            MessageBox.Show(
                "Le importazioni sono state disattivate su questo computer.",
                "Importazioni disabilitate",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
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
            if (SelectedInvoice != null)
            {
                try
                {
                    byte[]? bytes = _dbService.GetInvoiceContent(SelectedInvoice.Id);
                    if (bytes == null || bytes.Length == 0)
                        throw new InvalidOperationException("Il documento non è presente nel database interno.");

                    string rawContent = "";
                    if (SelectedInvoice.FileName.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
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
                InvoiceHtmlContent = "<html><body><p>Nessuna fattura selezionata.</p></body></html>";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
