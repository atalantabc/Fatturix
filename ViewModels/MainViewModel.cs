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
        private LastImportService _lastImportService;
        private readonly SessionDatabaseService _sessionDatabase;
        private readonly Dictionary<InvoiceSection, List<InvoiceData>> _invoiceCache = new();
        private readonly Dictionary<InvoiceSection, ObservableCollection<YearNode>> _treeCache = new();
        private bool _disposed;
        private object? _selectedDeletionTarget;

        public MainViewModel()
        {
            _supplierDatabase = new DatabaseService();
            _customerDatabase = new DatabaseService(
                _supplierDatabase.StorageDirectory,
                migrateLegacyDatabase: false,
                databaseFileName: CustomerDatabaseFileName,
                hydrateLegacyContent: false);
            _lastImportService = new LastImportService(
                _supplierDatabase.StorageDirectory);
            _sessionDatabase = new SessionDatabaseService();

            ImportCommand = new RelayCommand(ExecuteImport);
            ImportDatabaseCommand = new RelayCommand(ExecuteImportDatabase);
            ClearCommand = new RelayCommand(ExecuteClear);
            SetPathCommand = new RelayCommand(ExecuteSetPath);
            SetDatabasePathCommand = new RelayCommand(ExecuteSetDatabasePath);
            AdminCommand = new RelayCommand(ExecuteAdmin);
            VersionCommand = new RelayCommand(ExecuteShowVersion);
            RollbackImportCommand = new RelayCommand(ExecuteRollbackImport);
            DeleteSelectionCommand = new RelayCommand(ExecuteDeleteSelection);
            DeleteAllSectionCommand = new RelayCommand(ExecuteDeleteAllSection);
            IsAdminDeletionEnabled =
                AppSettingsService.IsAdminDeletionEnabled();

            RefreshInvoiceCache(InvoiceSection.Suppliers);
            RefreshInvoiceCache(InvoiceSection.Customers);
            FilterInvoices();
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
                if (_isFlatSearch == value)
                    return;
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
                SelectedDeletionTarget = null;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SearchCompany));
                OnPropertyChanged(nameof(FilterYear));
                OnPropertyChanged(nameof(FilterMonth));
                OnPropertyChanged(nameof(SelectedInvoice));
                OnPropertyChanged(nameof(ImportButtonText));
                OnPropertyChanged(nameof(ImportDatabaseMenuText));
                OnPropertyChanged(nameof(ClearDatabaseMenuText));
                OnPropertyChanged(nameof(SearchLabel));
                OnPropertyChanged(nameof(DeleteAllSectionMenuText));
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
        public ICommand RollbackImportCommand { get; }
        public ICommand DeleteSelectionCommand { get; }
        public ICommand DeleteAllSectionCommand { get; }

        public bool IsAdminDeletionEnabled { get; private set; }

        public object? SelectedDeletionTarget
        {
            get => _selectedDeletionTarget;
            set
            {
                _selectedDeletionTarget = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DeleteSelectionMenuText));
                OnPropertyChanged(nameof(HasDeletionSelection));
            }
        }

        public bool HasDeletionSelection =>
            GetDeletionIds(SelectedDeletionTarget).Count > 0;

        public string DeleteSelectionMenuText =>
            SelectedDeletionTarget switch
            {
                YearNode year => $"Elimina anno {year.Name}",
                MonthNode month => $"Elimina mese {month.Name}",
                InvoiceNode => "Elimina fattura",
                InvoiceData => "Elimina fattura",
                _ => "Elimina selezione"
            };

        public string DeleteAllSectionMenuText =>
            $"Elimina tutte le fatture {ActiveSection.ToStorageValue()}";

        private InvoiceSection ActiveSection =>
            SelectedSectionIndex == 1
                ? InvoiceSection.Customers
                : InvoiceSection.Suppliers;

        private DatabaseService ActivePermanentDatabase =>
            ActiveSection == InvoiceSection.Customers
                ? _customerDatabase
                : _supplierDatabase;

        private void LoadInvoices(bool refresh = false)
        {
            if (refresh || !_invoiceCache.ContainsKey(ActiveSection))
                RefreshInvoiceCache(ActiveSection);
            FilterInvoices();
        }

        private void RefreshInvoiceCache(InvoiceSection section)
        {
            DatabaseService database = section == InvoiceSection.Customers
                ? _customerDatabase
                : _supplierDatabase;
            List<InvoiceData> invoices = database.GetAllInvoices();
            foreach (InvoiceData invoice in invoices)
            {
                invoice.IsTemporary = false;
                PrepareForDisplay(invoice, section);
            }

            List<InvoiceData> temporary = _sessionDatabase.GetInvoices(section);
            foreach (InvoiceData invoice in temporary)
                PrepareForDisplay(invoice, section);
            invoices.AddRange(temporary);
            _invoiceCache[section] = invoices
                .OrderByDescending(invoice => invoice.Date)
                .ToList();
            _treeCache[section] = CreateTree(_invoiceCache[section]);
        }

        private void FilterInvoices()
        {
            if (!_invoiceCache.TryGetValue(ActiveSection, out List<InvoiceData>? invoices))
            {
                RefreshInvoiceCache(ActiveSection);
                invoices = _invoiceCache[ActiveSection];
            }

            List<InvoiceData> result = FilterCachedInvoices(
                invoices,
                FilterYear,
                FilterMonth,
                SearchCompany);
            Invoices = new ObservableCollection<InvoiceData>(result);
            OnPropertyChanged(nameof(Invoices));
            BuildTree(result);
        }

        public static List<InvoiceData> FilterCachedInvoices(
            IEnumerable<InvoiceData> invoices,
            int? year,
            int? month,
            string? companyName)
        {
            IEnumerable<InvoiceData> filtered = invoices;
            if (year is > 0)
                filtered = filtered.Where(invoice => invoice.Year == year.Value);
            if (month is > 0)
                filtered = filtered.Where(invoice => invoice.Month == month.Value);
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                filtered = filtered.Where(invoice =>
                    invoice.DisplayPartyName.Contains(
                        companyName,
                        StringComparison.OrdinalIgnoreCase));
            }
            return filtered.ToList();
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
            IsFlatSearch = ShouldUseFlatSearch(SearchCompany, FilterMonth);
            FlatSearchResults = IsFlatSearch
                ? Invoices
                : new ObservableCollection<InvoiceData>();
            OnPropertyChanged(nameof(FlatSearchResults));

            if (IsFlatSearch)
            {
                return;
            }

            bool unfiltered =
                string.IsNullOrWhiteSpace(SearchCompany) &&
                !FilterYear.HasValue &&
                !FilterMonth.HasValue;
            if (unfiltered &&
                _treeCache.TryGetValue(ActiveSection, out ObservableCollection<YearNode>? cachedTree))
            {
                InvoiceTree = cachedTree;
                OnPropertyChanged(nameof(InvoiceTree));
                return;
            }

            InvoiceTree = CreateTree(invoices);
            OnPropertyChanged(nameof(InvoiceTree));
        }

        private static ObservableCollection<YearNode> CreateTree(
            IEnumerable<InvoiceData> invoices)
        {
            var tree = new ObservableCollection<YearNode>();
            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;
            foreach (IGrouping<int, InvoiceData> yearGroup in invoices
                         .GroupBy(invoice => invoice.Date.Year)
                         .OrderByDescending(group => group.Key))
            {
                var yearNode = new YearNode
                {
                    Name = yearGroup.Key.ToString(),
                    Year = yearGroup.Key,
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
                        Year = yearGroup.Key,
                        Month = monthGroup.Key,
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
            return tree;
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
            ImportReviewResult review = ReviewInvoices(
                invoices,
                InvoiceSection.Suppliers);
            List<InvoiceData>? approvedInvoices =
                ImportSafetyService.ResolveImport(invoices, review);
            if (approvedInvoices == null || approvedInvoices.Count == 0)
                return;
            invoices = approvedInvoices;

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

            List<string> extractedFiles = new();
            List<string> archiveFiles = new();
            DatabaseImportChanges? databaseChanges = null;
            string archivedZipPath = string.Empty;
            string archivePeriodDirectory = Path.Combine(
                archiveDirectory,
                $"{periodWindow.ImportYear}-{periodWindow.ImportMonth}");
            try
            {
                string importedZipPath = ExtractionEngine.GetAvailablePath(
                    Path.Combine(
                        externalPassiveDirectory,
                        Path.GetFileName(zipPath)));
                if (review.Choice == ImportReviewChoice.All)
                    File.Copy(zipPath, importedZipPath);
                else
                    CreateInvoiceZip(importedZipPath, invoices);

                string extractDirectory = ExtractionEngine.ProcessZip(
                    importedZipPath,
                    string.Empty,
                    externalPassiveDirectory,
                    out extractedFiles);

                databaseChanges =
                    _supplierDatabase.SaveInvoicesForRollback(invoices);
                CopyExtractedFilesToArchive(
                    extractedFiles,
                    extractDirectory,
                    archiveDirectory,
                    periodWindow.ImportYear,
                    periodWindow.ImportMonth,
                    archiveFiles);
                archivedZipPath = ArchiveZip(
                    importedZipPath,
                    archiveDirectory,
                    periodWindow.ImportYear,
                    periodWindow.ImportMonth);

                _lastImportService.Save(new LastImportRecord
                {
                    Section = InvoiceSection.Suppliers,
                    DatabaseChanges = databaseChanges,
                    ImportedFileNames = invoices
                        .Select(invoice => invoice.FileName)
                        .ToList(),
                    Year = periodWindow.ImportYear,
                    Month = periodWindow.ImportMonth,
                    ZipFileName = Path.GetFileName(zipPath),
                    PassiveRoot = externalPassiveDirectory,
                    ArchiveRoot = archiveDirectory,
                    ArchivePeriodDirectory = archivePeriodDirectory,
                    ArchivedZipPath = archivedZipPath,
                    PassiveFiles = extractedFiles,
                    ArchiveFiles = archiveFiles
                });
            }
            catch (Exception ex)
            {
                if (databaseChanges != null)
                {
                    try
                    {
                        LastImportService.Rollback(
                            new LastImportRecord
                            {
                                Section = InvoiceSection.Suppliers,
                                DatabaseChanges = databaseChanges,
                                Year = periodWindow.ImportYear,
                                Month = periodWindow.ImportMonth,
                                PassiveRoot = externalPassiveDirectory,
                                ArchiveRoot = archiveDirectory,
                                ArchivePeriodDirectory = archivePeriodDirectory,
                                ArchivedZipPath = archivedZipPath,
                                PassiveFiles = extractedFiles,
                                ArchiveFiles = archiveFiles
                            },
                            InvoiceSection.Suppliers,
                            _supplierDatabase);
                    }
                    catch
                    {
                        // Registro non sostituito: nessuna vecchia importazione viene persa.
                    }
                }
                MessageBox.Show(
                    "Errore durante l'importazione Fornitori:\n\n" + ex.Message,
                    "Errore importazione",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            LoadInvoices(refresh: true);
            ShowImportCompleted(invoices.Count, "Fornitori", temporary: false);
        }

        private void ImportCustomerZip()
        {
            string? zipPath = SelectZip("Importa ZIP Clienti");
            if (zipPath == null)
                return;

            List<InvoiceData> invoices = ReadDatabaseImportFiles(new[] { zipPath });
            PrepareImportedInvoices(invoices, InvoiceSection.Customers);
            ImportReviewResult review = ReviewInvoices(
                invoices,
                InvoiceSection.Customers);
            List<InvoiceData>? approvedInvoices =
                ImportSafetyService.ResolveImport(invoices, review);
            if (approvedInvoices == null || approvedInvoices.Count == 0)
                return;
            invoices = approvedInvoices;

            if (IsSessionNoSaveEnabled)
                SaveTemporaryImport(InvoiceSection.Customers, invoices);
            else
            {
                SavePermanentDatabaseImport(
                    InvoiceSection.Customers,
                    invoices,
                    "Clienti");
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
            ImportReviewResult review = ReviewInvoices(invoices, section);
            List<InvoiceData>? approvedInvoices =
                ImportSafetyService.ResolveImport(invoices, review);
            if (approvedInvoices == null || approvedInvoices.Count == 0)
                return;
            invoices = approvedInvoices;

            if (IsSessionNoSaveEnabled)
                SaveTemporaryImport(section, invoices);
            else
            {
                SavePermanentDatabaseImport(
                    section,
                    invoices,
                    section == InvoiceSection.Customers
                        ? "Clienti"
                        : "Fornitori");
            }
        }

        private void SavePermanentDatabaseImport(
            InvoiceSection section,
            List<InvoiceData> invoices,
            string sectionName)
        {
            DatabaseService database = section == InvoiceSection.Customers
                ? _customerDatabase
                : _supplierDatabase;
            DatabaseImportChanges changes =
                database.SaveInvoicesForRollback(invoices);
            try
            {
                _lastImportService.Save(new LastImportRecord
                {
                    Section = section,
                    DatabaseChanges = changes,
                    ImportedFileNames = invoices
                        .Select(invoice => invoice.FileName)
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                database.RollbackImport(changes);
                MessageBox.Show(
                    "Importazione annullata: impossibile salvare il registro di ripristino.\n\n" +
                    ex.Message,
                    "Errore importazione",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            LoadInvoices(refresh: true);
            ShowImportCompleted(invoices.Count, sectionName, temporary: false);
        }

        private ImportReviewResult ReviewInvoices(
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
                return new ImportReviewResult
                {
                    Choice = ImportReviewChoice.Cancel
                };
            }

            List<InvoiceData> suspicious =
                ImportSafetyService.GetSuspiciousInvoices(
                    invoices,
                    section);
            if (suspicious.Count == 0)
            {
                return new ImportReviewResult
                {
                    Choice = ImportReviewChoice.All
                };
            }

            var warning = new ImportWarningWindow(
                invoices,
                suspicious,
                section)
            {
                Owner = Application.Current?.MainWindow
            };
            warning.ShowDialog();
            return warning.Result;
        }

        private void SaveTemporaryImport(
            InvoiceSection section,
            List<InvoiceData> invoices)
        {
            _sessionDatabase.SaveInvoices(section, invoices);
            LoadInvoices(refresh: true);
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
                LastImportRecord? lastImport = _lastImportService.Load();
                LastImportService oldLastImportService = _lastImportService;
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

                var newLastImportService = new LastImportService(newDirectory);
                if (lastImport != null)
                    newLastImportService.Save(lastImport);

                AppSettingsService.SetStorageDirectory(newDirectory);
                _supplierDatabase.Dispose();
                _customerDatabase.Dispose();
                _supplierDatabase = newSupplierDatabase;
                _customerDatabase = newCustomerDatabase;
                _lastImportService = newLastImportService;
                newSupplierDatabase = null;
                newCustomerDatabase = null;
                oldLastImportService.Clear();
                _invoiceCache.Clear();
                _treeCache.Clear();
                RefreshInvoiceCache(InvoiceSection.Suppliers);
                RefreshInvoiceCache(InvoiceSection.Customers);
                FilterInvoices();

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
                IsSessionNoSaveEnabled,
                IsAdminDeletionEnabled)
            {
                Owner = Application.Current?.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                AppSettingsService.SetZipImportEnabled(window.ZipImportEnabled);
                IsSessionNoSaveEnabled = window.SessionNoSaveEnabled;
                AppSettingsService.SetAdminDeletionEnabled(
                    window.AdminDeletionEnabled);
                IsAdminDeletionEnabled = window.AdminDeletionEnabled;
                OnPropertyChanged(nameof(IsAdminDeletionEnabled));
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

        public static void CreateInvoiceZip(
            string destinationPath,
            IEnumerable<InvoiceData> invoices)
        {
            var usedNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            using ZipArchive archive = ZipFile.Open(
                destinationPath,
                ZipArchiveMode.Create);
            foreach (InvoiceData invoice in invoices)
            {
                string originalName = Path.GetFileName(invoice.FileName);
                if (string.IsNullOrWhiteSpace(originalName))
                    originalName = "fattura.xml";

                string entryName = originalName;
                string stem = Path.GetFileNameWithoutExtension(originalName);
                string extension = Path.GetExtension(originalName);
                int suffix = 2;
                while (!usedNames.Add(entryName))
                    entryName = $"{stem}_{suffix++}{extension}";

                ZipArchiveEntry entry = archive.CreateEntry(
                    entryName,
                    CompressionLevel.Optimal);
                using Stream output = entry.Open();
                output.Write(
                    invoice.FileContent,
                    0,
                    invoice.FileContent.Length);
            }
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
            int month,
            ICollection<string>? copiedFiles = null)
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
                string copiedPath = ExtractionEngine.GetAvailablePath(targetPath);
                File.Copy(file, copiedPath);
                copiedFiles?.Add(copiedPath);
            }
        }

        public static string ArchiveZip(
            string zipPath,
            string archiveDirectory,
            int year,
            int month)
        {
            string archiveName = $"{year}-{month}.zip";
            string archivePath = ExtractionEngine.GetAvailablePath(
                Path.Combine(archiveDirectory, archiveName));
            File.Move(zipPath, archivePath);
            return archivePath;
        }

        private void ExecuteRollbackImport(object obj)
        {
            LastImportRecord? record;
            try
            {
                record = _lastImportService.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Annullamento non disponibile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (record == null)
            {
                MessageBox.Show(
                    "Non esiste un'importazione annullabile.",
                    "Annulla ultima importazione",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            if (record.Section != ActiveSection)
            {
                MessageBox.Show(
                    $"L'ultima importazione appartiene alla sezione {record.Section.ToStorageValue()}.",
                    "Sezione diversa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var passwordWindow = new PasswordWindow(PasswordMode.Rollback)
            {
                Owner = Application.Current?.MainWindow
            };
            if (passwordWindow.ShowDialog() != true)
                return;

            string period = record.Section == InvoiceSection.Suppliers &&
                            record.Year > 0
                ? $"\nPeriodo archivio: {record.Month:D2}/{record.Year}."
                : string.Empty;
            MessageBoxResult confirmation = MessageBox.Show(
                $"Annullare l'ultima importazione {record.Section.ToStorageValue()} " +
                $"di {record.ImportedFileNames.Count} fatture?{period}\n\n" +
                "Verranno rimossi solamente i dati e i file registrati da quell'importazione.",
                "Conferma annullamento",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                LastImportService.Rollback(
                    record,
                    ActiveSection,
                    ActivePermanentDatabase);
                _lastImportService.Clear();
                SelectedInvoice = null;
                LoadInvoices(refresh: true);
                MessageBox.Show(
                    "Ultima importazione annullata.",
                    "Annullamento completato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Annullamento non completato. Il registro è stato conservato per un nuovo tentativo.\n\n" +
                    ex.Message,
                    "Errore annullamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static List<string> GetDeletionIds(
            object? target,
            IEnumerable<InvoiceData>? allPermanentInvoices = null)
        {
            IEnumerable<InvoiceData> invoices = target switch
            {
                YearNode year when allPermanentInvoices != null && year.Year > 0 =>
                    allPermanentInvoices.Where(invoice =>
                        invoice.Year == year.Year),
                MonthNode month when allPermanentInvoices != null &&
                                         month.Year > 0 &&
                                         month.Month > 0 =>
                    allPermanentInvoices.Where(invoice =>
                        invoice.Year == month.Year &&
                        invoice.Month == month.Month),
                InvoiceData invoice => new[] { invoice },
                InvoiceNode invoiceNode => new[] { invoiceNode.Data },
                MonthNode month => month.Invoices.Select(node => node.Data),
                YearNode year => year.Months
                    .SelectMany(month => month.Invoices)
                    .Select(node => node.Data),
                _ => Enumerable.Empty<InvoiceData>()
            };

            return invoices
                .Where(invoice => !invoice.IsTemporary)
                .Select(invoice => invoice.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private void ExecuteDeleteSelection(object obj)
        {
            DeletePermanentInvoices(
                GetDeletionIds(
                    SelectedDeletionTarget,
                    ActivePermanentDatabase.GetAllInvoices()),
                DeleteSelectionMenuText);
        }

        private void ExecuteDeleteAllSection(object obj)
        {
            DeletePermanentInvoices(
                ActivePermanentDatabase.GetAllInvoices()
                    .Select(invoice => invoice.Id)
                    .ToList(),
                DeleteAllSectionMenuText);
        }

        private void DeletePermanentInvoices(
            IReadOnlyCollection<string> invoiceIds,
            string description)
        {
            if (!IsAdminDeletionEnabled)
                return;
            if (invoiceIds.Count == 0)
            {
                MessageBox.Show(
                    "La selezione non contiene fatture permanenti da eliminare.",
                    "Nessuna fattura",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                $"ATTENZIONE: {description}?\n\n" +
                $"Verranno eliminate definitivamente {invoiceIds.Count} fatture dal database " +
                $"{ActiveSection.ToStorageValue()}.\n" +
                "ZIP e file nell'Archivio non verranno cancellati.\n\n" +
                "L'operazione non può essere annullata. Continuare?",
                "Eliminazione amministratore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;

            int deleted = ActivePermanentDatabase.DeleteInvoices(invoiceIds);
            try
            {
                if (_lastImportService.Load()?.Section == ActiveSection)
                    _lastImportService.Clear();
            }
            catch
            {
                _lastImportService.Clear();
            }

            SelectedDeletionTarget = null;
            SelectedInvoice = null;
            LoadInvoices(refresh: true);
            MessageBox.Show(
                $"Eliminate {deleted} fatture dal database.",
                "Eliminazione completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            try
            {
                if (_lastImportService.Load()?.Section == ActiveSection)
                    _lastImportService.Clear();
            }
            catch
            {
                // Svuotamento già eseguito: registro corrotto non può ripristinare dati.
                _lastImportService.Clear();
            }
            SelectedInvoice = null;
            LoadInvoices(refresh: true);
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
                InvoiceDocumentContent content = InvoiceDocumentService.Render(
                    SelectedInvoice,
                    bytes ?? Array.Empty<byte>());
                InvoiceXmlContent = content.Xml;
                InvoiceHtmlContent = content.Html;
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
