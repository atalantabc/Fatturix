using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using FattureViewer.Models;
using FattureViewer.Services;
using Microsoft.Win32;

namespace FattureViewer.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string SettingsRegistryPath = @"HKEY_CURRENT_USER\Software\FattureViewer";
        private const string PassiveDirectoryValue = "FatturePassiveDirectory";
        private const string CustomerDatabaseFileName = "fattureClienti.db";

        private DatabaseService _supplierDatabase;
        private DatabaseService _customerDatabase;
        private readonly SessionDatabaseService _sessionDatabase;
        private bool _disposed;

        public MainViewModel()
        {
            _supplierDatabase = new DatabaseService();
            _customerDatabase = new DatabaseService(
                _supplierDatabase.StorageDirectory,
                migrateLegacyDatabase: false,
                databaseFileName: CustomerDatabaseFileName,
                hydrateLegacyContent: false);
            _sessionDatabase = new SessionDatabaseService();

            ImportCommand = new RelayCommand(ExecuteImport);
            ImportDatabaseCommand = new RelayCommand(ExecuteImportDatabase);
            ClearCommand = new RelayCommand(ExecuteClear);
            SetPathCommand = new RelayCommand(ExecuteSetPath);
            SetDatabasePathCommand = new RelayCommand(ExecuteSetDatabasePath);
            AdminCommand = new RelayCommand(ExecuteAdmin);
            VersionCommand = new RelayCommand(ExecuteShowVersion);

            LoadInvoices();
        }

        public ObservableCollection<InvoiceData> Invoices { get; private set; } =
            new ObservableCollection<InvoiceData>();

        public ObservableCollection<YearNode> InvoiceTree { get; private set; } =
            new ObservableCollection<YearNode>();

        public ObservableCollection<InvoiceData> FlatSearchResults { get; private set; } =
            new ObservableCollection<InvoiceData>();

        private bool _isFlatSearch;
        public bool IsFlatSearch
        {
            get => _isFlatSearch;
            private set
            {
                _isFlatSearch = value;
                OnPropertyChanged();
            }
        }

        private InvoiceData? _selectedInvoice;
        public InvoiceData? SelectedInvoice
        {
            get => _selectedInvoice;
            set
            {
                _selectedInvoice = value;
                OnPropertyChanged();
                LoadInvoiceContent();
            }
        }

        private string _invoiceHtmlContent =
            "<html><body><p>Nessuna fattura selezionata.</p></body></html>";
        public string InvoiceHtmlContent
        {
            get => _invoiceHtmlContent;
            private set
            {
                _invoiceHtmlContent = value;
                OnPropertyChanged();
            }
        }

        private string _invoiceXmlContent = "Nessuna fattura selezionata.";
        public string InvoiceXmlContent
        {
            get => _invoiceXmlContent;
            private set
            {
                _invoiceXmlContent = value;
                OnPropertyChanged();
            }
        }

        private string _searchCompany = string.Empty;
        public string SearchCompany
        {
            get => _searchCompany;
            set
            {
                _searchCompany = value ?? string.Empty;
                OnPropertyChanged();
                FilterInvoices();
            }
        }

        private int? _filterYear;
        public int? FilterYear
        {
            get => _filterYear;
            set
            {
                _filterYear = value;
                OnPropertyChanged();
                FilterInvoices();
            }
        }

        private int? _filterMonth;
        public int? FilterMonth
        {
            get => _filterMonth;
            set
            {
                _filterMonth = value;
                OnPropertyChanged();
                FilterInvoices();
            }
        }

        private int _selectedSectionIndex;
        public int SelectedSectionIndex
        {
            get => _selectedSectionIndex;
            set
            {
                int normalized = value == 1 ? 1 : 0;
                if (_selectedSectionIndex == normalized)
                    return;

                _selectedSectionIndex = normalized;
                _searchCompany = string.Empty;
                _filterYear = null;
                _filterMonth = null;
                _selectedInvoice = null;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SearchCompany));
                OnPropertyChanged(nameof(FilterYear));
                OnPropertyChanged(nameof(FilterMonth));
                OnPropertyChanged(nameof(SelectedInvoice));
                OnPropertyChanged(nameof(ImportButtonText));
                OnPropertyChanged(nameof(ImportDatabaseMenuText));
                OnPropertyChanged(nameof(ClearDatabaseMenuText));
                OnPropertyChanged(nameof(SearchLabel));
                LoadInvoiceContent();
                LoadInvoices();
            }
        }

        private bool _isSessionNoSaveEnabled;
        public bool IsSessionNoSaveEnabled
        {
            get => _isSessionNoSaveEnabled;
            private set
            {
                if (_isSessionNoSaveEnabled == value)
                    return;
                _isSessionNoSaveEnabled = value;
                OnPropertyChanged();
            }
        }

        public string ImportButtonText =>
            ActiveSection == InvoiceSection.Customers
                ? "Importa ZIP Clienti"
                : "Importa ZIP Fornitori";

        public string ImportDatabaseMenuText =>
            ActiveSection == InvoiceSection.Customers
                ? "Importa nel database Clienti..."
                : "Importa nel database Fornitori...";

        public string ClearDatabaseMenuText =>
            ActiveSection == InvoiceSection.Customers
                ? "Svuota database Clienti"
                : "Svuota database Fornitori";

        public string SearchLabel =>
            ActiveSection == InvoiceSection.Customers
                ? "Cerca Cliente:"
                : "Cerca Fornitore:";

        public string VersionMenuText =>
            $"Versione {GetApplicationVersion()}";

        public ICommand ImportCommand { get; }
        public ICommand ImportDatabaseCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand SetPathCommand { get; }
        public ICommand SetDatabasePathCommand { get; }
        public ICommand AdminCommand { get; }
        public ICommand VersionCommand { get; }

        private InvoiceSection ActiveSection =>
            SelectedSectionIndex == 1
                ? InvoiceSection.Customers
                : InvoiceSection.Suppliers;

        private DatabaseService ActivePermanentDatabase =>
            ActiveSection == InvoiceSection.Customers
                ? _customerDatabase
                : _supplierDatabase;

        private void LoadInvoices()
        {
            FilterInvoices();
        }

        private void FilterInvoices()
        {
            List<InvoiceData> invoices = ActivePermanentDatabase.GetAllInvoices();
            foreach (InvoiceData invoice in invoices)
            {
                invoice.IsTemporary = false;
                PrepareForDisplay(invoice, ActiveSection);
            }

            List<InvoiceData> temporary = _sessionDatabase.GetInvoices(ActiveSection);
            foreach (InvoiceData invoice in temporary)
                PrepareForDisplay(invoice, ActiveSection);
            invoices.AddRange(temporary);

            IEnumerable<InvoiceData> filtered = invoices;
            if (FilterYear is > 0)
                filtered = filtered.Where(invoice => invoice.Year == FilterYear.Value);
            if (FilterMonth is > 0)
                filtered = filtered.Where(invoice => invoice.Month == FilterMonth.Value);
            if (!string.IsNullOrWhiteSpace(SearchCompany))
            {
                filtered = filtered.Where(invoice =>
                    invoice.DisplayPartyName.Contains(
                        SearchCompany,
                        StringComparison.OrdinalIgnoreCase));
            }

            List<InvoiceData> result = filtered
                .OrderByDescending(invoice => invoice.Date)
                .ToList();
            Invoices = new ObservableCollection<InvoiceData>(result);
            OnPropertyChanged(nameof(Invoices));
            BuildTree(result);
        }

        private static void PrepareForDisplay(
            InvoiceData invoice,
            InvoiceSection section)
        {
            invoice.DisplayPartyName = section == InvoiceSection.Customers
                ? GetRecipientName(invoice)
                : invoice.SenderName;
            if (string.IsNullOrWhiteSpace(invoice.DisplayPartyName))
                invoice.DisplayPartyName = "Soggetto non indicato";
        }

        private void BuildTree(List<InvoiceData> invoices)
        {
            var tree = new ObservableCollection<YearNode>();
            IsFlatSearch = ShouldUseFlatSearch(SearchCompany, FilterMonth);
            FlatSearchResults = new ObservableCollection<InvoiceData>(
                IsFlatSearch
                    ? invoices.OrderByDescending(invoice => invoice.Date)
                    : Enumerable.Empty<InvoiceData>());
            OnPropertyChanged(nameof(FlatSearchResults));

            if (IsFlatSearch)
            {
                InvoiceTree = tree;
                OnPropertyChanged(nameof(InvoiceTree));
                return;
            }

            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;
            foreach (IGrouping<int, InvoiceData> yearGroup in invoices
                         .GroupBy(invoice => invoice.Date.Year)
                         .OrderByDescending(group => group.Key))
            {
                var yearNode = new YearNode
                {
                    Name = yearGroup.Key.ToString(),
                    IsExpanded = yearGroup.Key == currentYear
                };

                foreach (IGrouping<int, InvoiceData> monthGroup in yearGroup
                             .GroupBy(invoice => invoice.Date.Month)
                             .OrderByDescending(group => group.Key))
                {
                    string monthName = new DateTime(2000, monthGroup.Key, 1)
                        .ToString(
                            "MMMM",
                            new System.Globalization.CultureInfo("it-IT"));
                    monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

                    var monthNode = new MonthNode
                    {
                        Name = monthName,
                        IsExpanded =
                            yearGroup.Key == currentYear &&
                            monthGroup.Key == currentMonth
                    };

                    foreach (InvoiceData invoice in monthGroup.OrderBy(item => item.Date))
                    {
                        string temporaryLabel = invoice.IsTemporary
                            ? " • Temporanea"
                            : string.Empty;
                        monthNode.Invoices.Add(new InvoiceNode
                        {
                            Name =
                                $"{invoice.Date:dd/MM} - " +
                                $"{invoice.DisplayPartyName}{temporaryLabel}",
                            Data = invoice
                        });
                    }
                    yearNode.Months.Add(monthNode);
                }
                tree.Add(yearNode);
            }

            InvoiceTree = tree;
            OnPropertyChanged(nameof(InvoiceTree));
        }

        public static bool ShouldUseFlatSearch(string? companyName, int? month)
        {
            return !string.IsNullOrWhiteSpace(companyName) && !month.HasValue;
        }

        private void ExecuteSetPath(object obj)
        {
            string selectedPath = Microsoft.VisualBasic.Interaction.InputBox(
                "Inserisci il percorso della cartella Fatture Passive. Verrà salvato e usato per le importazioni Fornitori:",
                "Imposta percorso Fatture Passive",
                GetSavedPassiveDirectory());
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            if (!TryGetPassiveDirectories(selectedPath, out string passiveDir, out _))
            {
                MessageBox.Show(
                    "Questa cartella non è una cartella Fatture Passive valida.",
                    "Errore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                Registry.SetValue(
                    SettingsRegistryPath,
                    PassiveDirectoryValue,
                    passiveDir);
                MessageBox.Show(
                    $"Percorso Fornitori salvato:\n\n{passiveDir}",
                    "Percorso salvato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Errore durante il salvataggio del percorso: " + ex.Message,
                    "Errore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExecuteImport(object obj)
        {
            if (!EnsureImportsEnabled())
                return;

            if (ActiveSection == InvoiceSection.Customers)
                ImportCustomerZip();
            else
                ImportSupplierZip();
        }

        private void ImportSupplierZip()
        {
            string externalPassiveDirectory = string.Empty;
            string archiveDirectory = string.Empty;
            if (!IsSessionNoSaveEnabled)
            {
                string selectedPassiveDirectory = GetSavedPassiveDirectory();
                if (string.IsNullOrWhiteSpace(selectedPassiveDirectory))
                {
                    MessageBox.Show(
                        "Imposta prima il percorso Fatture Passive dal menu con i tre puntini.",
                        "Percorso mancante",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!TryGetPassiveDirectories(
                        selectedPassiveDirectory,
                        out externalPassiveDirectory,
                        out archiveDirectory))
                {
                    MessageBox.Show(
                        "Questa cartella non è una cartella Fatture Passive valida.",
                        "Errore",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            string? zipPath = SelectZip("Importa ZIP Fornitori");
            if (zipPath == null)
                return;

            List<InvoiceData> invoices = ReadDatabaseImportFiles(new[] { zipPath });
            PrepareImportedInvoices(invoices, InvoiceSection.Suppliers);
            if (!ValidateInvoicesAndConfirm(
                    invoices,
                    InvoiceSection.Suppliers))
                return;

            if (IsSessionNoSaveEnabled)
            {
                SaveTemporaryImport(InvoiceSection.Suppliers, invoices);
                return;
            }

            var periodWindow = new ImportPeriodWindow
            {
                Owner = Application.Current?.MainWindow
            };
            if (periodWindow.ShowDialog() != true)
                return;

            try
            {
                string importedZipPath = ExtractionEngine.GetAvailablePath(
                    Path.Combine(
                        externalPassiveDirectory,
                        Path.GetFileName(zipPath)));
                File.Copy(zipPath, importedZipPath);

                string extractDirectory = ExtractionEngine.ProcessZip(
                    importedZipPath,
                    string.Empty,
                    externalPassiveDirectory,
                    out List<string> extractedFiles);

                _supplierDatabase.SaveInvoices(invoices);
                CopyExtractedFilesToArchive(
                    extractedFiles,
                    extractDirectory,
                    archiveDirectory,
                    periodWindow.ImportYear,
                    periodWindow.ImportMonth);
                ArchiveZip(
                    importedZipPath,
                    archiveDirectory,
                    periodWindow.ImportYear,
                    periodWindow.ImportMonth);

                LoadInvoices();
                ShowImportCompleted(invoices.Count, "Fornitori", temporary: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Errore durante l'importazione Fornitori:\n\n" + ex.Message,
                    "Errore importazione",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ImportCustomerZip()
        {
            string? zipPath = SelectZip("Importa ZIP Clienti");
            if (zipPath == null)
                return;

            List<InvoiceData> invoices = ReadDatabaseImportFiles(new[] { zipPath });
            PrepareImportedInvoices(invoices, InvoiceSection.Customers);
            if (!ValidateInvoicesAndConfirm(invoices, InvoiceSection.Customers))
                return;

            if (IsSessionNoSaveEnabled)
                SaveTemporaryImport(InvoiceSection.Customers, invoices);
            else
            {
                _customerDatabase.SaveInvoices(invoices);
                LoadInvoices();
                ShowImportCompleted(invoices.Count, "Clienti", temporary: false);
            }
        }

        private void ExecuteImportDatabase(object obj)
        {
            if (!EnsureImportsEnabled())
                return;

            InvoiceSection section = ActiveSection;
            var dialog = new OpenFileDialog
            {
                Filter =
                    "Fatture e archivi ZIP (*.xml;*.p7m;*.zip)|*.xml;*.p7m;*.zip",
                Multiselect = true,
                Title = section == InvoiceSection.Customers
                    ? "Importa nel database Clienti"
                    : "Importa nel database Fornitori"
            };
            if (dialog.ShowDialog() != true)
                return;

            List<InvoiceData> invoices = ReadDatabaseImportFiles(dialog.FileNames);
            PrepareImportedInvoices(invoices, section);
            if (!ValidateInvoicesAndConfirm(invoices, section))
                return;

            if (IsSessionNoSaveEnabled)
                SaveTemporaryImport(section, invoices);
            else
            {
                ActivePermanentDatabase.SaveInvoices(invoices);
                LoadInvoices();
                ShowImportCompleted(
                    invoices.Count,
                    section == InvoiceSection.Customers
                        ? "Clienti"
                        : "Fornitori",
                    temporary: false);
            }
        }

        private bool ValidateInvoicesAndConfirm(
            List<InvoiceData> invoices,
            InvoiceSection section)
        {
            if (invoices.Count == 0)
            {
                MessageBox.Show(
                    "Nello ZIP non sono state trovate fatture XML o P7M valide.",
                    "Nessuna fattura",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            bool warningRequired = ImportSafetyService.RequiresWarning(
                invoices,
                section);
            if (!warningRequired)
                return true;

            string message = section == InvoiceSection.Suppliers
                ? "Il sistema ha rilevato che queste fatture sembrano essere fatture emesse verso clienti. Sei sicuro di volerle importare nella sezione Fornitori?"
                : "Il sistema ha rilevato che queste fatture sembrano essere fatture ricevute da fornitori. Sei sicuro di volerle importare nella sezione Clienti?";
            var warning = new ImportWarningWindow(message)
            {
                Owner = Application.Current?.MainWindow
            };
            bool importAnyway = warning.ShowDialog() == true;
            return ImportSafetyService.CanContinueAfterWarning(
                warningRequired,
                importAnyway);
        }

        private void SaveTemporaryImport(
            InvoiceSection section,
            List<InvoiceData> invoices)
        {
            _sessionDatabase.SaveInvoices(section, invoices);
            LoadInvoices();
            ShowImportCompleted(
                invoices.Count,
                section == InvoiceSection.Customers ? "Clienti" : "Fornitori",
                temporary: true);
        }

        private static void PrepareImportedInvoices(
            IEnumerable<InvoiceData> invoices,
            InvoiceSection section)
        {
            foreach (InvoiceData invoice in invoices)
            {
                invoice.Section = section.ToStorageValue();
                invoice.OriginalFilePath = string.Empty;
            }
        }

        private static string? SelectZip(string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Archivi ZIP (*.zip)|*.zip",
                Title = title
            };
            return dialog.ShowDialog() == true
                ? dialog.FileName
                : null;
        }

        private static void ShowImportCompleted(
            int count,
            string sectionName,
            bool temporary)
        {
            string detail = temporary
                ? "Le fatture sono temporanee e verranno eliminate alla chiusura dell'app."
                : "Le fatture sono state salvate nel database dedicato.";
            MessageBox.Show(
                $"Importate {count} fatture nella sezione {sectionName}.\n\n{detail}",
                temporary
                    ? "Importazione temporanea completata"
                    : "Importazione completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ExecuteSetDatabasePath(object obj)
        {
            string selectedPath = Microsoft.VisualBasic.Interaction.InputBox(
                "Inserisci la cartella principale dei database. Sono supportati anche percorsi di rete, ad esempio \\\\SERVER\\Fatture:",
                "Imposta percorso database",
                _supplierDatabase.StorageDirectory);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            DatabaseService? newSupplierDatabase = null;
            DatabaseService? newCustomerDatabase = null;
            try
            {
                string newDirectory =
                    AppSettingsService.NormalizeStorageDirectory(selectedPath);
                if (string.Equals(
                        newDirectory,
                        _supplierDatabase.StorageDirectory,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                newSupplierDatabase = new DatabaseService(
                    newDirectory,
                    migrateLegacyDatabase: false);
                newCustomerDatabase = new DatabaseService(
                    newDirectory,
                    migrateLegacyDatabase: false,
                    databaseFileName: CustomerDatabaseFileName,
                    hydrateLegacyContent: false);

                newSupplierDatabase.SaveInvoices(
                    _supplierDatabase.GetAllInvoicesWithContent());
                newCustomerDatabase.SaveInvoices(
                    _customerDatabase.GetAllInvoicesWithContent());

                AppSettingsService.SetStorageDirectory(newDirectory);
                _supplierDatabase.Dispose();
                _customerDatabase.Dispose();
                _supplierDatabase = newSupplierDatabase;
                _customerDatabase = newCustomerDatabase;
                newSupplierDatabase = null;
                newCustomerDatabase = null;
                LoadInvoices();

                MessageBox.Show(
                    "Database Fornitori e Clienti copiati e percorso salvato:\n\n" +
                    newDirectory,
                    "Percorso database",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Impossibile usare il percorso indicato. Verifica che la cartella locale o di rete sia raggiungibile e scrivibile.\n\n" +
                    ex.Message,
                    "Errore percorso database",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                newSupplierDatabase?.Dispose();
                newCustomerDatabase?.Dispose();
            }
        }

        private void ExecuteAdmin(object obj)
        {
            var window = new AdminWindow(
                AppSettingsService.IsZipImportEnabled(),
                IsSessionNoSaveEnabled)
            {
                Owner = Application.Current?.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                AppSettingsService.SetZipImportEnabled(window.ZipImportEnabled);
                IsSessionNoSaveEnabled = window.SessionNoSaveEnabled;
            }
        }

        private void ExecuteShowVersion(object obj)
        {
            MessageBox.Show(
                $"FattureViewer\nVersione {GetApplicationVersion()}",
                "Informazioni versione",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static string GetApplicationVersion()
        {
            Version version = Assembly.GetEntryAssembly()?.GetName().Version ??
                              new Version(0, 0, 0);
            return version.ToString(3);
        }

        private static InvoiceData? CreateStoredInvoice(string filePath)
        {
            try
            {
                return CreateStoredInvoice(
                    Path.GetFileName(filePath),
                    File.ReadAllBytes(filePath));
            }
            catch
            {
                return null;
            }
        }

        private static InvoiceData? CreateStoredInvoice(
            string fileName,
            byte[] content)
        {
            InvoiceData invoice = InvoiceParser.ParseInvoice(fileName, content);
            if (!invoice.IsValidInvoice)
                return null;

            invoice.FileContent = content;
            invoice.OriginalFilePath = string.Empty;
            return invoice;
        }

        public static List<InvoiceData> ReadDatabaseImportFiles(
            IEnumerable<string> filePaths)
        {
            const long maxInvoiceBytes = 50L * 1024 * 1024;
            var invoices = new List<InvoiceData>();

            foreach (string filePath in filePaths)
            {
                try
                {
                    if (!filePath.EndsWith(
                            ".zip",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (new FileInfo(filePath).Length <= maxInvoiceBytes)
                        {
                            InvoiceData? invoice = CreateStoredInvoice(filePath);
                            if (invoice != null)
                                invoices.Add(invoice);
                        }
                        continue;
                    }

                    using ZipArchive archive = ZipFile.OpenRead(filePath);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) ||
                            entry.Length <= 0 ||
                            entry.Length > maxInvoiceBytes ||
                            (!entry.Name.EndsWith(
                                 ".xml",
                                 StringComparison.OrdinalIgnoreCase) &&
                             !entry.Name.EndsWith(
                                 ".p7m",
                                 StringComparison.OrdinalIgnoreCase)))
                            continue;

                        using Stream source = entry.Open();
                        using var content = new MemoryStream((int)entry.Length);
                        source.CopyTo(content);
                        InvoiceData? invoice = CreateStoredInvoice(
                            entry.Name,
                            content.ToArray());
                        if (invoice != null)
                            invoices.Add(invoice);
                    }
                }
                catch
                {
                    // Un file non valido non interrompe gli altri.
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
            return Registry.GetValue(
                       SettingsRegistryPath,
                       PassiveDirectoryValue,
                       "") as string ??
                   string.Empty;
        }

        public static bool TryGetPassiveDirectories(
            string selectedPath,
            out string passiveDirectory,
            out string archiveDirectory)
        {
            passiveDirectory = string.Empty;
            archiveDirectory = string.Empty;
            if (string.IsNullOrWhiteSpace(selectedPath))
                return false;

            try
            {
                passiveDirectory = Path.GetFullPath(selectedPath.Trim());
                archiveDirectory = Path.Combine(
                    passiveDirectory,
                    "Archivio");
                return Directory.Exists(passiveDirectory) &&
                       Directory.Exists(archiveDirectory);
            }
            catch
            {
                passiveDirectory = string.Empty;
                archiveDirectory = string.Empty;
                return false;
            }
        }

        public static void CopyExtractedFilesToArchive(
            IEnumerable<string> files,
            string extractDirectory,
            string archiveDirectory,
            int year,
            int month)
        {
            string targetDirectory = Path.Combine(
                archiveDirectory,
                $"{year}-{month}");
            Directory.CreateDirectory(targetDirectory);

            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(
                    extractDirectory,
                    file);
                string targetPath = Path.Combine(
                    targetDirectory,
                    relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, ExtractionEngine.GetAvailablePath(targetPath));
            }
        }

        public static void ArchiveZip(
            string zipPath,
            string archiveDirectory,
            int year,
            int month)
        {
            string archiveName = $"{year}-{month}.zip";
            string archivePath = ExtractionEngine.GetAvailablePath(
                Path.Combine(archiveDirectory, archiveName));
            File.Move(zipPath, archivePath);
        }

        private void ExecuteClear(object obj)
        {
            var passwordWindow = new PasswordWindow(PasswordMode.Verify)
            {
                Owner = Application.Current?.MainWindow
            };
            if (passwordWindow.ShowDialog() != true)
                return;

            ActivePermanentDatabase.ClearAll();
            SelectedInvoice = null;
            LoadInvoices();
        }

        private void LoadInvoiceContent()
        {
            if (SelectedInvoice == null)
            {
                InvoiceXmlContent = "Nessuna fattura selezionata.";
                InvoiceHtmlContent =
                    "<html><body><p>Nessuna fattura selezionata.</p></body></html>";
                return;
            }

            try
            {
                byte[]? bytes = SelectedInvoice.IsTemporary
                    ? _sessionDatabase.GetInvoiceContent(SelectedInvoice.Id)
                    : ActivePermanentDatabase.GetInvoiceContent(
                        SelectedInvoice.Id);
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException(
                        "Il documento non è presente nel database.");

                string rawContent = SelectedInvoice.FileName.EndsWith(
                    ".p7m",
                    StringComparison.OrdinalIgnoreCase)
                    ? InvoiceParser.DecodeP7M(bytes)
                    : System.Text.Encoding.UTF8.GetString(bytes);

                InvoiceXmlContent = rawContent;
                InvoiceHtmlContent = HtmlGenerator.GenerateInvoiceHtml(
                    SelectedInvoice,
                    rawContent);
            }
            catch (Exception ex)
            {
                InvoiceXmlContent = "Errore: " + ex.Message;
                InvoiceHtmlContent =
                    $"<html><body><h2 style='color:red'>Errore</h2><p>{ex.Message}</p></body></html>";
            }
        }

        private static string GetRecipientName(InvoiceData invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.RecipientName)
                ? invoice.CompanyName
                : invoice.RecipientName;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _supplierDatabase.Dispose();
            _customerDatabase.Dispose();
            _sessionDatabase.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
