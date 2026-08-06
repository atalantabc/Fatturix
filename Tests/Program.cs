using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using FattureViewer;
using FattureViewer.Services;
using FattureViewer.ViewModels;
using FattureViewer.Models;
using SQLite;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

AppProfileDefinition omtProfile = AppProfileService.ParseProfile(
    new[] { "FattureViewer.exe", "--profile", "omt" });
AppProfileDefinition ccaseProfile = AppProfileService.ParseProfile(
    new[] { "FattureViewer.exe", "--profile", "ccase" });
AppProfileDefinition fuchsProfile = AppProfileService.ParseProfile(
    new[] { "FattureViewer.exe", "--profile", "fuchs" });
Assert(
    omtProfile.Kind == AppProfileKind.Omt &&
    omtProfile.SupportsPassiveArchive &&
    ccaseProfile.Kind == AppProfileKind.CCase &&
    !ccaseProfile.SupportsPassiveArchive &&
    fuchsProfile.Kind == AppProfileKind.Fuchs &&
    !fuchsProfile.SupportsPassiveArchive,
    "I profili OMT, C.CASE e Fuchs non applicano le funzioni previste.");
Assert(
    omtProfile.CompanyVatNumber == "IT02745400164" &&
    ccaseProfile.CompanyVatNumber == "IT03415270168" &&
    fuchsProfile.CompanyVatNumber == "IT04127340166",
    "Le Partite IVA dei profili non sono configurate correttamente.");
Assert(
    !string.Equals(
        AppProfileService.GetPasswordFilePath(omtProfile),
        AppProfileService.GetPasswordFilePath(ccaseProfile),
        StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(
        AppProfileService.GetPasswordFilePath(ccaseProfile),
        AppProfileService.GetPasswordFilePath(fuchsProfile),
        StringComparison.OrdinalIgnoreCase),
    "Due profili condividono la password database.");
Assert(
    !string.Equals(
        AppSettingsService.GetDefaultStorageDirectory(
            ccaseProfile,
            InvoiceSection.Suppliers),
        AppSettingsService.GetDefaultStorageDirectory(
            ccaseProfile,
            InvoiceSection.Customers),
        StringComparison.OrdinalIgnoreCase),
    "I database C.CASE Fornitori e Clienti usano la stessa cartella.");
Assert(
    !string.Equals(
        AppSettingsService.GetDefaultStorageDirectory(
            ccaseProfile,
            InvoiceSection.Suppliers),
        AppSettingsService.GetDefaultStorageDirectory(
            fuchsProfile,
            InvoiceSection.Suppliers),
        StringComparison.OrdinalIgnoreCase) &&
    AppSettingsService.GetStorageOwner(
        fuchsProfile,
        InvoiceSection.Customers) == "FUCHS-CLIENTI",
    "C.CASE e Fuchs condividono cartelle o proprietÃ  del database.");
Assert(
    ProfileConfigurationService.IsValidCombination(
        new[] { AppProfileKind.Omt }) &&
    ProfileConfigurationService.IsValidCombination(
        new[] { AppProfileKind.CCase }) &&
    ProfileConfigurationService.IsValidCombination(
        new[] { AppProfileKind.Omt, AppProfileKind.CCase }) &&
    ProfileConfigurationService.IsValidCombination(
        new[] { AppProfileKind.Fuchs }) &&
    !ProfileConfigurationService.IsValidCombination(
        Array.Empty<AppProfileKind>()) &&
    !ProfileConfigurationService.IsValidCombination(
        new[] { AppProfileKind.Fuchs, AppProfileKind.Omt }),
    "Le regole di compatibilitÃ  dei profili non sono rispettate.");
Assert(
    ProfileConfigurationService.DetectExistingProfiles(true, true, false)
        .SequenceEqual(new[] { AppProfileKind.Omt, AppProfileKind.CCase }) &&
    ProfileConfigurationService.DetectExistingProfiles(false, false, true)
        .SequenceEqual(new[] { AppProfileKind.Fuchs }),
    "La migrazione automatica dei profili esistenti Ã¨ errata.");

string profileIsolationRoot = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerProfileIsolation_" + Guid.NewGuid().ToString("N"));
try
{
    AppSettingsService.EnsureStorageOwnership(profileIsolationRoot, "OMT");
    bool collisionBlocked = false;
    try
    {
        AppSettingsService.EnsureStorageOwnership(
            profileIsolationRoot,
            "C.CASE-FORNITORI");
    }
    catch (InvalidOperationException)
    {
        collisionBlocked = true;
    }
    Assert(
        collisionBlocked,
        "Una cartella database OMT può essere assegnata anche a C.CASE.");
}
finally
{
    if (Directory.Exists(profileIsolationRoot))
        Directory.Delete(profileIsolationRoot, true);
}

static byte[] CreateInvoiceXml(
    string fileNumber,
    string senderName,
    string senderVat,
    string recipientName,
    string recipientVat)
{
    string SenderCountry(string vat) =>
        vat.StartsWith("IT", StringComparison.OrdinalIgnoreCase) ? "IT" : "";
    string SenderCode(string vat) =>
        vat.StartsWith("IT", StringComparison.OrdinalIgnoreCase) ? vat.Substring(2) : vat;

    string xml =
        "<FatturaElettronica>" +
        "<FatturaElettronicaHeader>" +
        "<CedentePrestatore><DatiAnagrafici>" +
        $"<IdFiscaleIVA><IdPaese>{SenderCountry(senderVat)}</IdPaese><IdCodice>{SenderCode(senderVat)}</IdCodice></IdFiscaleIVA>" +
        $"<Anagrafica><Denominazione>{senderName}</Denominazione></Anagrafica>" +
        "</DatiAnagrafici></CedentePrestatore>" +
        "<CessionarioCommittente><DatiAnagrafici>" +
        $"<IdFiscaleIVA><IdPaese>{SenderCountry(recipientVat)}</IdPaese><IdCodice>{SenderCode(recipientVat)}</IdCodice></IdFiscaleIVA>" +
        $"<Anagrafica><Denominazione>{recipientName}</Denominazione></Anagrafica>" +
        "</DatiAnagrafici></CessionarioCommittente>" +
        "<DatiTrasmissione><CodiceDestinatario>ABC1234</CodiceDestinatario></DatiTrasmissione>" +
        "</FatturaElettronicaHeader>" +
        "<FatturaElettronicaBody><DatiGenerali><DatiGeneraliDocumento>" +
        $"<TipoDocumento>TD01</TipoDocumento><Divisa>EUR</Divisa><Data>2026-07-28</Data><Numero>{fileNumber}</Numero>" +
        "<ImportoTotaleDocumento>100.00</ImportoTotaleDocumento>" +
        "</DatiGeneraliDocumento></DatiGenerali></FatturaElettronicaBody>" +
        "</FatturaElettronica>";
    return System.Text.Encoding.UTF8.GetBytes(xml);
}

static string CreateInvoiceZip(
    string directory,
    string name,
    int count,
    string senderName,
    string senderVat,
    string recipientName,
    string recipientVat)
{
    string path = Path.Combine(directory, name);
    using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
    for (int index = 0; index < count; index++)
    {
        byte[] xml = CreateInvoiceXml(
            (index + 1).ToString(),
            senderName,
            senderVat,
            recipientName,
            recipientVat);
        using Stream output = archive.CreateEntry($"fattura-{index + 1}.xml").Open();
        output.Write(xml, 0, xml.Length);
    }
    return path;
}

static InvoiceData TestInvoice(
    string id,
    string fileName,
    string sender,
    InvoiceSection section)
{
    return new InvoiceData
    {
        Id = id,
        FileName = fileName,
        Date = new DateTime(2026, 8, 3),
        Year = 2026,
        Month = 8,
        Day = 3,
        SenderName = sender,
        Section = section.ToStorageValue(),
        FileContent = System.Text.Encoding.UTF8.GetBytes(sender)
    };
}

string startupOptimizationRoot = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerStartupTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(startupOptimizationRoot);
try
{
    using (var seed = new DatabaseService(
               startupOptimizationRoot,
               migrateLegacyDatabase: false,
               databaseFileName: "startup.db",
               hydrateLegacyContent: false))
    {
        seed.SaveInvoice(new InvoiceData
        {
            Id = "backfill-test",
            FileName = "backfill.xml",
            FileContent = CreateInvoiceXml(
                "A 7/2026",
                "FORNITORE",
                "IT12345678901",
                "OMT",
                omtProfile.CompanyVatNumber),
            InvoiceNumber = string.Empty
        });
    }

    using (var firstOpen = new DatabaseService(
               startupOptimizationRoot,
               migrateLegacyDatabase: false,
               databaseFileName: "startup.db"))
    {
        Assert(
            firstOpen.GetAllInvoices().Single().InvoiceNumber == "A 7/2026",
            "Il recupero iniziale dei numeri fattura non funziona.");
    }

    using var metadataDatabase = new SQLiteConnection(
        Path.Combine(startupOptimizationRoot, "startup.db"));
    Assert(
        metadataDatabase.ExecuteScalar<string>(
            "SELECT [Value] FROM AppMetadata WHERE [Key] = ?",
            "InvoiceNumberBackfillVersion") == "1",
        "L'ottimizzazione di avvio non memorizza il completamento del recupero.");
}
finally
{
    if (Directory.Exists(startupOptimizationRoot))
        Directory.Delete(startupOptimizationRoot, true);
}

Exception? nestedSelectionError = null;
var nestedSelectionThread = new Thread(() =>
{
    try
    {
        var yearItem = new TreeViewItem();
        var monthItem = new TreeViewItem();
        var invoiceItem = new TreeViewItem();
        var monthText = new TextBlock { Text = "Agosto" };
        var invoiceText = new TextBlock { Text = "Fattura" };
        monthItem.Header = monthText;
        invoiceItem.Header = invoiceText;
        monthItem.Items.Add(invoiceItem);
        yearItem.Items.Add(monthItem);

        Assert(
            ReferenceEquals(
                MainWindow.FindNearestAncestor<TreeViewItem>(monthText),
                monthItem),
            "Clic destro sul mese risale erroneamente al nodo anno.");
        Assert(
            ReferenceEquals(
                MainWindow.FindNearestAncestor<TreeViewItem>(invoiceText),
                invoiceItem),
            "Clic destro sulla fattura risale erroneamente al nodo anno.");
    }
    catch (Exception ex)
    {
        nestedSelectionError = ex;
    }
});
nestedSelectionThread.SetApartmentState(ApartmentState.STA);
nestedSelectionThread.Start();
nestedSelectionThread.Join();
if (nestedSelectionError != null)
    throw nestedSelectionError;

var viewModelStartupTimer = Stopwatch.StartNew();
using (var startupViewModel = new MainViewModel())
{
    viewModelStartupTimer.Stop();
    object? deferredSupplierDatabase = typeof(MainViewModel)
        .GetField(
            "_supplierDatabase",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
        ?.GetValue(startupViewModel);
    Assert(
        startupViewModel.IsInitialLoading && deferredSupplierDatabase == null,
        "Il database Fornitori viene ancora aperto prima della visualizzazione della finestra.");
}
Assert(
    viewModelStartupTimer.Elapsed < TimeSpan.FromMilliseconds(250),
    $"Il costruttore iniziale blocca ancora l'avvio: {viewModelStartupTimer.ElapsedMilliseconds} ms.");

var cachedInvoices = Enumerable.Range(0, 25000)
    .Select(index => new InvoiceData
    {
        Date = new DateTime(2026, index % 12 + 1, 1),
        Year = 2026,
        Month = index % 12 + 1,
        DisplayPartyName = index % 10 == 0 ? "CLIENTE VELOCE" : "ALTRO"
    })
    .OrderByDescending(invoice => invoice.Date)
    .ToList();
var searchTimer = Stopwatch.StartNew();
for (int index = 0; index < 20; index++)
{
    List<InvoiceData> matches = MainViewModel.FilterCachedInvoices(
        cachedInvoices,
        2026,
        null,
        "veloce");
    Assert(matches.Count == 2500, "La ricerca in cache restituisce risultati errati.");
}
searchTimer.Stop();
Assert(
    searchTimer.Elapsed < TimeSpan.FromSeconds(2),
    $"La ricerca in memoria è troppo lenta: {searchTimer.ElapsedMilliseconds} ms.");

var invoiceNumberInvoices = new List<InvoiceData>
{
    new()
    {
        Id = "cliente-100",
        DisplayPartyName = "CLIENTE ALFA",
        InvoiceNumber = "100/2026"
    },
    new()
    {
        Id = "fornitore-100",
        DisplayPartyName = "FORNITORE BETA",
        InvoiceNumber = "100"
    },
    new()
    {
        Id = "cliente-200",
        DisplayPartyName = "CLIENTE ALFA",
        InvoiceNumber = "200"
    }
};
Assert(
    MainViewModel.FilterCachedInvoices(
            invoiceNumberInvoices,
            null,
            null,
            null,
            "100")
        .Select(invoice => invoice.Id)
        .OrderBy(id => id)
        .SequenceEqual(new[] { "fornitore-100" }),
    "Ricerca numero fattura senza cliente non usa una corrispondenza esatta.");
Assert(
    MainViewModel.FilterCachedInvoices(
            invoiceNumberInvoices,
            null,
            null,
            "CLIENTE ALFA",
            "100")
        .Count == 0,
    "Ricerca combinata azienda e numero fattura accetta corrispondenze parziali.");
var exactInvoiceNumberInvoices = new List<InvoiceData>
{
    new() { Id = "fattura-7", InvoiceNumber = "7" },
    new() { Id = "fattura-97", InvoiceNumber = "97" },
    new() { Id = "fattura-107", InvoiceNumber = "107" },
    new() { Id = "fattura-127", InvoiceNumber = "127" },
    new() { Id = "fattura-57", InvoiceNumber = "57" }
};
Assert(
    MainViewModel.FilterCachedInvoices(
            exactInvoiceNumberInvoices,
            null,
            null,
            null,
            " 7 ")
        .Select(invoice => invoice.Id)
        .SequenceEqual(new[] { "fattura-7" }),
    "La fattura 7 trova anche 97, 107, 127 o 57.");
Assert(
    MainViewModel.IsInvoiceNumberSearchAllowed(
        InvoiceSection.Customers,
        null),
    "Numero fattura Clienti bloccato senza nome cliente.");
Assert(
    !MainViewModel.IsInvoiceNumberSearchAllowed(
        InvoiceSection.Suppliers,
        null) &&
    MainViewModel.IsInvoiceNumberSearchAllowed(
        InvoiceSection.Suppliers,
        "FORNITORE BETA"),
    "Blocco numero fattura Fornitori non dipende dal nome fornitore.");
Assert(
    MainViewModel.ShouldUseFlatSearch("", null, "100"),
    "Ricerca per solo numero fattura non usa la lista risultati.");

var supplierPrefixInvoices = new List<InvoiceData>
{
    new() { Id = "blu-7", DisplayPartyName = "Pinco Pallino Blu", InvoiceNumber = "7" },
    new() { Id = "rosso-7", DisplayPartyName = "Pinco Pallino Rosso", InvoiceNumber = "7" },
    new() { Id = "verde-8", DisplayPartyName = "Pinco Pallino Verde", InvoiceNumber = "8" },
    new() { Id = "altro-7", DisplayPartyName = "Altro Pinco", InvoiceNumber = "7" }
};
List<InvoiceData> supplierPrefixMatches = MainViewModel.FilterCachedInvoices(
    supplierPrefixInvoices,
    null,
    null,
    "Pinco",
    "7",
    companyNameStartsWith: true);
Assert(
    supplierPrefixMatches.Select(invoice => invoice.Id)
        .SequenceEqual(new[] { "blu-7", "rosso-7" }),
    "Ricerca Fornitori non combina prefisso azienda e numero esatto.");
Assert(
    MainViewModel.FilterCachedInvoices(
            supplierPrefixInvoices,
            null,
            null,
            "Pinco Pallino Blu",
            "7",
            companyNameStartsWith: true)
        .Single().Id == "blu-7",
    "Ricerca Fornitori con nome completo e numero non trova la fattura.");
Assert(
    MainViewModel.ShouldGroupSupplierSearchResults(
        InvoiceSection.Suppliers,
        "Pinco",
        "7",
        supplierPrefixMatches),
    "Più fornitori con lo stesso numero non vengono raggruppati.");
Assert(
    !MainViewModel.ShouldGroupSupplierSearchResults(
        InvoiceSection.Suppliers,
        "Pinco Pallino Blu",
        "7",
        supplierPrefixMatches.Take(1)),
    "Un solo fornitore viene raggruppato inutilmente.");

DateTime julySuggestion = ImportPeriodWindow.GetSuggestedPeriod(
    new DateTime(2026, 8, 15));
Assert(
    julySuggestion.Year == 2026 && julySuggestion.Month == 7,
    "Agosto non suggerisce luglio per l'archiviazione Fornitori.");
DateTime decemberSuggestion = ImportPeriodWindow.GetSuggestedPeriod(
    new DateTime(2026, 1, 15));
Assert(
    decemberSuggestion.Year == 2025 && decemberSuggestion.Month == 12,
    "Gennaio non suggerisce dicembre dell'anno precedente.");

string releasesJson =
    "[" +
    "{\"tag_name\":\"v3.4.0\"," +
    "\"body\":\"Versione da non distribuire automaticamente.\\n(N.U)   \"," +
    "\"draft\":false,\"prerelease\":false,\"assets\":[{" +
    "\"name\":\"FattureViewerInstaller-3.4.0.exe\"," +
    "\"browser_download_url\":\"https://example.test/3.4.0.exe\"," +
    "\"digest\":\"sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}]}," +
    "{\"tag_name\":\"v3.3.0\"," +
    "\"body\":\"Aggiornamento automatico consentito.\"," +
    "\"draft\":false,\"prerelease\":false,\"assets\":[{" +
    "\"name\":\"FattureViewerInstaller-3.3.0.exe\"," +
    "\"browser_download_url\":\"https://example.test/3.3.0.exe\"," +
    "\"digest\":\"sha256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\"}]}" +
    "]";
var selectedUpdate = UpdateService.SelectEligibleRelease(releasesJson, new Version(3, 2, 0));
Assert(selectedUpdate?.Version == new Version(3, 3, 0, 0), "La release (N.U) non viene ignorata.");
Assert(
    UpdateService.SelectEligibleRelease(releasesJson, new Version(3, 3, 0)) == null,
    "Viene proposto un aggiornamento già installato.");
Assert(UpdateService.IsNoUpdateDescription("Test\r\n(N.U)  "), "Il marcatore (N.U) finale non viene riconosciuto.");
Assert(!UpdateService.IsNoUpdateDescription("Test (N.U) altro"), "Il marcatore (N.U) non finale viene applicato per errore.");

string v4TestRoot = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerV4Tests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(v4TestRoot);
try
{
    const string omtName = ImportSafetyService.ReferenceCompanyName;
    const string omtVat = ImportSafetyService.ReferenceVatNumber;
    string supplierZip = CreateInvoiceZip(
        v4TestRoot,
        "fornitori-corretti.zip",
        1,
        "FORNITORE TEST",
        "IT12345678901",
        omtName,
        omtVat);
    string customerZip = CreateInvoiceZip(
        v4TestRoot,
        "clienti-corretti.zip",
        1,
        omtName,
        omtVat,
        "CLIENTE TEST",
        "IT10987654321");
    string misplacedCustomerZip = CreateInvoiceZip(
        v4TestRoot,
        "clienti-in-fornitori.zip",
        6,
        omtName,
        omtVat,
        "CLIENTE TEST",
        "IT10987654321");
    string misplacedSupplierZip = CreateInvoiceZip(
        v4TestRoot,
        "fornitori-in-clienti.zip",
        6,
        "FORNITORE TEST",
        "IT12345678901",
        omtName,
        omtVat);
    var supplierInvoices = MainViewModel.ReadDatabaseImportFiles(new[] { supplierZip });
    var customerInvoices = MainViewModel.ReadDatabaseImportFiles(new[] { customerZip });
    var misplacedCustomers = MainViewModel.ReadDatabaseImportFiles(new[] { misplacedCustomerZip });
    var misplacedSuppliers = MainViewModel.ReadDatabaseImportFiles(new[] { misplacedSupplierZip });

    Assert(supplierInvoices.Count == 1, "Lo ZIP Fornitori corretto non viene letto.");
    Assert(customerInvoices.Count == 1, "Lo ZIP Clienti corretto non viene letto.");
    Assert(supplierInvoices[0].SenderName == "FORNITORE TEST", "La sezione Fornitori non usa il cedente.");
    Assert(customerInvoices[0].RecipientName == "CLIENTE TEST", "La sezione Clienti non usa il destinatario.");
    Assert(supplierInvoices[0].SenderVatNumber == "IT12345678901", "Partita IVA cedente non letta.");
    Assert(customerInvoices[0].RecipientVatNumber == "IT10987654321", "Partita IVA destinatario non letta.");
    Assert(supplierInvoices[0].InvoiceNumber == "1", "Numero fattura non letto.");

    List<InvoiceData> oneSupplierSuspicious =
        ImportSafetyService.GetSuspiciousInvoices(
            misplacedSuppliers.Take(1),
            InvoiceSection.Customers);
    Assert(
        oneSupplierSuspicious.Count == 1,
        "Una singola fattura Fornitore nei Clienti non viene rilevata.");
    Assert(
        ImportSafetyService.GetWarningMessage(
            1,
            InvoiceSection.Customers) ==
        "È stata trovata 1 fattura che sembra essere una fattura Fornitore." &&
        ImportSafetyService.GetWarningMessage(
            6,
            InvoiceSection.Suppliers) ==
        "Sono state trovate 6 fatture che sembrano essere fatture Clienti.",
        "Il messaggio singolare/plurale non è corretto.");
    Assert(
        ImportSafetyService.GetSuspiciousInvoices(
            misplacedCustomers,
            InvoiceSection.Suppliers).Count == 6,
        "Più fatture Clienti nei Fornitori non vengono rilevate.");
    Assert(
        ImportSafetyService.GetSuspiciousInvoices(
            misplacedSuppliers,
            InvoiceSection.Customers).Count == 6,
        "Più fatture Fornitori nei Clienti non vengono rilevate.");
    Assert(
        ImportSafetyService.IsSuspicious(
            new InvoiceData
            {
                RecipientVatNumber = "IT02745400164"
            },
            InvoiceSection.Customers),
        "Clienti: la P.IVA IT02745400164 come destinatario non apre il controllo.");
    Assert(
        ImportSafetyService.IsSuspicious(
            new InvoiceData
            {
                SenderVatNumber = "IT02745400164"
            },
            InvoiceSection.Suppliers),
        "Fornitori: la P.IVA IT02745400164 come mittente non apre il controllo.");
    Assert(
        !ImportSafetyService.IsSuspicious(
            new InvoiceData
            {
                SenderVatNumber = "IT02745400164",
                RecipientVatNumber = "IT99999999999"
            },
            InvoiceSection.Customers) &&
        !ImportSafetyService.IsSuspicious(
            new InvoiceData
            {
                SenderVatNumber = "IT99999999999",
                RecipientVatNumber = "IT02745400164"
            },
            InvoiceSection.Suppliers),
        "Il controllo usa il lato sbagliato della fattura.");
    Assert(
        ImportSafetyService.GetSuspiciousInvoices(
            supplierInvoices,
            InvoiceSection.Suppliers).Count == 0 &&
        ImportSafetyService.GetSuspiciousInvoices(
            customerInvoices,
            InvoiceSection.Customers).Count == 0,
        "Fatture nella sezione corretta vengono segnalate.");
    Assert(
        ImportSafetyService.IsReferenceParty(
            "02745400164",
            "nome differente") &&
        !ImportSafetyService.IsReferenceParty(
            "",
            omtName),
        "Il controllo non usa la P.IVA come identificativo principale.");
    Assert(
        !ImportSafetyService.IsReferenceParty(
            "IT99999999999",
            omtName),
        "Una P.IVA diversa viene ignorata nonostante sia il riferimento principale.");
    foreach (string companyVat in new[]
             {
                 omtProfile.CompanyVatNumber,
                 ccaseProfile.CompanyVatNumber,
                 fuchsProfile.CompanyVatNumber
             })
    {
        Assert(
            ImportSafetyService.IsSuspicious(
                new InvoiceData { RecipientVatNumber = companyVat },
                InvoiceSection.Customers,
                companyVat) &&
            ImportSafetyService.IsSuspicious(
                new InvoiceData { SenderVatNumber = companyVat },
                InvoiceSection.Suppliers,
                companyVat),
            $"Allarme P.IVA non funzionante per {companyVat}.");
    }
    Assert(
        ImportSafetyService.IsReferenceVat(
            "034 1527 0168",
            ccaseProfile.CompanyVatNumber) &&
        ImportSafetyService.IsReferenceVat(
            "IT-04127340166",
            fuchsProfile.CompanyVatNumber),
        "L'allarme non normalizza prefisso, spazi o separatori della P.IVA.");

    List<InvoiceData> mixedInvoices = misplacedCustomers
        .Concat(supplierInvoices)
        .ToList();
    List<InvoiceData>? importAll = ImportSafetyService.ResolveImport(
        mixedInvoices,
        new ImportReviewResult { Choice = ImportReviewChoice.All });
    Assert(
        importAll?.Count == mixedInvoices.Count,
        "Importa tutte non conserva tutte le fatture.");

    string[] selectedIds =
    {
        misplacedCustomers[0].Id,
        misplacedCustomers[2].Id
    };
    List<InvoiceData>? importSelected = ImportSafetyService.ResolveImport(
        mixedInvoices,
        new ImportReviewResult
        {
            Choice = ImportReviewChoice.Selected,
            SelectedInvoiceIds = selectedIds
        });
    Assert(
        importSelected?.Select(invoice => invoice.Id)
            .SequenceEqual(selectedIds) == true,
        "Importa selezionate non rispetta le checkbox.");
    Assert(
        ImportSafetyService.ResolveImport(
            mixedInvoices,
            new ImportReviewResult
            {
                Choice = ImportReviewChoice.Cancel
            }) == null,
        "Annulla non interrompe completamente l'importazione.");

    InvoiceDocumentContent preview = InvoiceDocumentService.Render(
        misplacedCustomers[0],
        misplacedCustomers[0].FileContent);
    Assert(
        preview.Xml.Contains("<Numero>1</Numero>", StringComparison.Ordinal) &&
        preview.Html.Contains("CLIENTE TEST", StringComparison.Ordinal),
        "La preview interna non renderizza completamente la fattura.");
    var reviewItem = new ImportReviewItem(misplacedCustomers[0])
    {
        IsSelected = false
    };
    InvoiceDocumentService.Render(
        reviewItem.Invoice,
        reviewItem.Invoice.FileContent);
    Assert(
        !reviewItem.IsSelected,
        "La selezione viene persa aprendo e chiudendo la preview.");

    string selectedZip = Path.Combine(v4TestRoot, "solo-selezionate.zip");
    MainViewModel.CreateInvoiceZip(selectedZip, importSelected!);
    using (ZipArchive selectedArchive = ZipFile.OpenRead(selectedZip))
    {
        Assert(
            selectedArchive.Entries.Count == 2,
            "Lo ZIP Fornitori filtrato contiene fatture non selezionate.");
    }

    string supplierWorkflowDirectory = Path.Combine(v4TestRoot, "FlussoFornitori");
    string supplierArchiveDirectory = Path.Combine(supplierWorkflowDirectory, "Archivio");
    Directory.CreateDirectory(supplierWorkflowDirectory);
    Directory.CreateDirectory(supplierArchiveDirectory);
    string supplierWorkingZip = Path.Combine(supplierWorkflowDirectory, "fornitori-da-importare.zip");
    File.Copy(supplierZip, supplierWorkingZip);
    string supplierExtractDirectory = ExtractionEngine.ProcessZip(
        supplierWorkingZip,
        string.Empty,
        supplierWorkflowDirectory,
        out var supplierExtractedFiles);
    MainViewModel.CopyExtractedFilesToArchive(
        supplierExtractedFiles,
        supplierExtractDirectory,
        supplierArchiveDirectory,
        2026,
        7);
    MainViewModel.ArchiveZip(
        supplierWorkingZip,
        supplierArchiveDirectory,
        2026,
        7);
    Assert(
        Directory.GetFiles(
            Path.Combine(supplierArchiveDirectory, "2026-7"),
            "*.xml",
            SearchOption.AllDirectories).Length == 1,
        "Il flusso Fornitori non archivia gli XML per anno e mese.");
    Assert(
        File.Exists(Path.Combine(supplierArchiveDirectory, "2026-7.zip")) &&
        !File.Exists(supplierWorkingZip),
        "Il flusso Fornitori non archivia lo ZIP originale.");

    using var supplierDatabase = new DatabaseService(
        v4TestRoot,
        migrateLegacyDatabase: false,
        databaseFileName: "fatture.db",
        hydrateLegacyContent: false);
    using var customerDatabase = new DatabaseService(
        v4TestRoot,
        migrateLegacyDatabase: false,
        databaseFileName: "fattureClienti.db",
        hydrateLegacyContent: false);

    supplierInvoices.ForEach(invoice => invoice.Section = InvoiceSection.Suppliers.ToStorageValue());
    customerInvoices.ForEach(invoice => invoice.Section = InvoiceSection.Customers.ToStorageValue());
    supplierDatabase.SaveInvoices(supplierInvoices);
    customerDatabase.SaveInvoices(customerInvoices);
    Assert(supplierDatabase.GetAllInvoices().Count == 1, "Database Fornitori non popolato.");
    Assert(customerDatabase.GetAllInvoices().Count == 1, "Database Clienti non popolato.");
    Assert(
        !string.Equals(supplierDatabase.DatabasePath, customerDatabase.DatabasePath, StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(customerDatabase.DatabasePath) == "fattureClienti.db",
        "Il database Clienti non è separato o non si chiama fattureClienti.db.");

    string[] filesBeforeCustomerRead = Directory.GetFiles(v4TestRoot, "*", SearchOption.AllDirectories);
    MainViewModel.ReadDatabaseImportFiles(new[] { customerZip });
    string[] filesAfterCustomerRead = Directory.GetFiles(v4TestRoot, "*", SearchOption.AllDirectories);
    Assert(
        filesBeforeCustomerRead.OrderBy(path => path).SequenceEqual(filesAfterCustomerRead.OrderBy(path => path)),
        "La lettura Clienti ha copiato, spostato o creato file permanenti.");

    int permanentSupplierCount = supplierDatabase.GetAllInvoices().Count;
    int permanentCustomerCount = customerDatabase.GetAllInvoices().Count;
    string[] archiveDirectoriesBeforeSession = Directory.GetDirectories(
        v4TestRoot,
        "Archivio",
        SearchOption.AllDirectories);
    string sessionDirectory = Path.Combine(v4TestRoot, "SessioneTest");
    string sessionDatabasePath;
    using (var session = new SessionDatabaseService(sessionDirectory))
    {
        sessionDatabasePath = session.DatabasePath;
        session.SaveInvoices(InvoiceSection.Suppliers, supplierInvoices);
        session.SaveInvoices(InvoiceSection.Customers, customerInvoices);

        Assert(session.GetInvoices(InvoiceSection.Suppliers).Single().IsTemporary, "Fattura Fornitore temporanea non marcata.");
        Assert(session.GetInvoices(InvoiceSection.Customers).Single().IsTemporary, "Fattura Cliente temporanea non marcata.");
        Assert(
            supplierDatabase.GetAllInvoices().Count == permanentSupplierCount &&
            customerDatabase.GetAllInvoices().Count == permanentCustomerCount,
            "La modalità temporanea ha modificato un database reale.");
        Assert(File.Exists(supplierZip) && File.Exists(customerZip), "La modalità temporanea ha spostato uno ZIP.");
        Assert(
            archiveDirectoriesBeforeSession
                .OrderBy(path => path)
                .SequenceEqual(
                    Directory.GetDirectories(
                            v4TestRoot,
                            "Archivio",
                            SearchOption.AllDirectories)
                        .OrderBy(path => path)),
            "La modalità temporanea ha creato un nuovo archivio.");
        Assert(session.IsSafeSessionDatabasePath(sessionDatabasePath), "Database sessione non riconosciuto come sicuro.");
        Assert(
            !session.IsSafeSessionDatabasePath(supplierDatabase.DatabasePath),
            "Il database reale potrebbe essere eliminato come temporaneo.");
    }
    Assert(!File.Exists(sessionDatabasePath), "Database temporaneo non eliminato alla chiusura.");
    Assert(!Directory.Exists(sessionDirectory), "Cartella temporanea non eliminata alla chiusura.");

    string reopenedSessionPath;
    using (var reopenedSession = new SessionDatabaseService(sessionDirectory))
    {
        reopenedSessionPath = reopenedSession.DatabasePath;
        Assert(
            reopenedSession.GetInvoices(InvoiceSection.Suppliers).Count == 0 &&
            reopenedSession.GetInvoices(InvoiceSection.Customers).Count == 0,
            "Le fatture temporanee ricompaiono al riavvio.");
    }
    Assert(!File.Exists(reopenedSessionPath), "Secondo database temporaneo non eliminato.");
}
finally
{
    if (Directory.Exists(v4TestRoot))
        Directory.Delete(v4TestRoot, true);
}

string rollbackTestRoot = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerRollbackTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(rollbackTestRoot);
try
{
    string databaseDirectory = Path.Combine(rollbackTestRoot, "Database");
    string passiveRoot = Path.Combine(rollbackTestRoot, "FatturePassiveTest");
    string archiveRoot = Path.Combine(passiveRoot, "Archivio");
    string periodDirectory = Path.Combine(archiveRoot, "2025-7");
    Directory.CreateDirectory(periodDirectory);

    using var rollbackDatabase = new DatabaseService(
        databaseDirectory,
        migrateLegacyDatabase: false,
        databaseFileName: "rollback.db",
        hydrateLegacyContent: false);
    rollbackDatabase.SaveInvoice(TestInvoice(
        "preesistente",
        "stesso-nome.xml",
        "VALORE ORIGINALE",
        InvoiceSection.Customers));

    DatabaseImportChanges customerChanges =
        rollbackDatabase.SaveInvoicesForRollback(new[]
        {
            TestInvoice(
                "nuovo-cliente",
                "nuovo-cliente.xml",
                "NUOVO CLIENTE",
                InvoiceSection.Customers),
            TestInvoice(
                "id-casuale-importazione",
                "stesso-nome.xml",
                "VALORE SOVRASCRITTO",
                InvoiceSection.Customers)
        });
    Assert(
        rollbackDatabase.GetAllInvoices().Count == 2 &&
        rollbackDatabase.GetAllInvoices().Any(invoice =>
            invoice.SenderName == "VALORE SOVRASCRITTO"),
        "Preparazione rollback Clienti errata.");

    var lastImportStore = new LastImportService(databaseDirectory);
    var customerRecord = new LastImportRecord
    {
        Section = InvoiceSection.Customers,
        DatabaseChanges = customerChanges,
        ImportedFileNames = new()
        {
            "nuovo-cliente.xml",
            "stesso-nome.xml"
        }
    };
    lastImportStore.Save(customerRecord);
    LastImportRecord reopenedRecord =
        new LastImportService(databaseDirectory).Load()
        ?? throw new Exception("Registro rollback non persistente.");
    Assert(
        reopenedRecord.Section == InvoiceSection.Customers &&
        reopenedRecord.ImportedFileNames.Count == 2,
        "Registro ultima importazione perso dopo riapertura.");
    Assert(
        !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(
                Path.Combine(databaseDirectory, "last-import.dat")))
            .Contains("nuovo-cliente.xml", StringComparison.Ordinal),
        "Registro ultima importazione non protetto.");

    string unrelatedCustomerFile = Path.Combine(
        rollbackTestRoot,
        "cliente-da-non-toccare.txt");
    File.WriteAllText(unrelatedCustomerFile, "resta");
    LastImportService.Rollback(
        reopenedRecord,
        InvoiceSection.Customers,
        rollbackDatabase);
    List<InvoiceData> customerAfterRollback =
        rollbackDatabase.GetAllInvoicesWithContent();
    Assert(
        customerAfterRollback.Count == 1 &&
        customerAfterRollback[0].Id == "preesistente" &&
        customerAfterRollback[0].SenderName == "VALORE ORIGINALE" &&
        System.Text.Encoding.UTF8.GetString(
            customerAfterRollback[0].FileContent) == "VALORE ORIGINALE",
        "Rollback Clienti non elimina solo il nuovo record o non ripristina quello sovrascritto.");
    Assert(
        File.ReadAllText(unrelatedCustomerFile) == "resta",
        "Rollback Clienti ha toccato un file estraneo.");

    DatabaseImportChanges firstChanges =
        rollbackDatabase.SaveInvoicesForRollback(new[]
        {
            TestInvoice(
                "prima-importazione",
                "prima.xml",
                "PRIMA",
                InvoiceSection.Suppliers)
        });
    lastImportStore.Save(new LastImportRecord
    {
        Section = InvoiceSection.Suppliers,
        DatabaseChanges = firstChanges,
        ImportedFileNames = new() { "prima.xml" }
    });
    DatabaseImportChanges latestChanges =
        rollbackDatabase.SaveInvoicesForRollback(new[]
        {
            TestInvoice(
                "ultima-importazione",
                "ultima.xml",
                "ULTIMA",
                InvoiceSection.Suppliers)
        });

    string passiveTrackedDirectory = Path.Combine(passiveRoot, "sotto-cartella");
    Directory.CreateDirectory(passiveTrackedDirectory);
    string passiveTracked = Path.Combine(passiveTrackedDirectory, "ultima.xml");
    string passiveUntracked = Path.Combine(passiveRoot, "non-toccare.xml");
    string archiveTracked = Path.Combine(periodDirectory, "ultima.xml");
    string archiveUntracked = Path.Combine(periodDirectory, "non-toccare.xml");
    string archivedZip = Path.Combine(archiveRoot, "2025-7.zip");
    File.WriteAllText(passiveTracked, "elimina");
    File.WriteAllText(passiveUntracked, "resta");
    File.WriteAllText(archiveTracked, "elimina");
    File.WriteAllText(archiveUntracked, "resta");
    File.WriteAllText(archivedZip, "elimina");

    var supplierRecord = new LastImportRecord
    {
        Section = InvoiceSection.Suppliers,
        DatabaseChanges = latestChanges,
        ImportedFileNames = new() { "ultima.xml" },
        Year = 2025,
        Month = 7,
        ZipFileName = "fornitori.zip",
        PassiveRoot = passiveRoot,
        ArchiveRoot = archiveRoot,
        ArchivePeriodDirectory = periodDirectory,
        ArchivedZipPath = archivedZip,
        PassiveFiles = new() { passiveTracked },
        ArchiveFiles = new() { archiveTracked }
    };
    lastImportStore.Save(supplierRecord);
    Assert(
        new LastImportService(databaseDirectory).Load()?.ImportedFileNames
            .Single() == "ultima.xml",
        "Nuova importazione non sostituisce la precedente come unico rollback.");
    LastImportService.Rollback(
        supplierRecord,
        InvoiceSection.Suppliers,
        rollbackDatabase);
    Assert(
        rollbackDatabase.GetAllInvoices().Any(invoice =>
            invoice.Id == "prima-importazione") &&
        !rollbackDatabase.GetAllInvoices().Any(invoice =>
            invoice.Id == "ultima-importazione"),
        "Rollback Fornitori ha toccato la penultima importazione.");
    Assert(
        !File.Exists(passiveTracked) &&
        !Directory.Exists(passiveTrackedDirectory) &&
        !File.Exists(archiveTracked) &&
        !File.Exists(archivedZip),
        "Rollback Fornitori non elimina i file registrati.");
    Assert(
        File.ReadAllText(passiveUntracked) == "resta" &&
        File.ReadAllText(archiveUntracked) == "resta" &&
        Directory.Exists(periodDirectory),
        "Rollback Fornitori ha eliminato file o cartelle non appartenenti all'ultima importazione.");

    string emptyPeriodDirectory = Path.Combine(archiveRoot, "2025-8");
    Directory.CreateDirectory(emptyPeriodDirectory);
    string onlyArchiveFile = Path.Combine(emptyPeriodDirectory, "solo-ultima.xml");
    string onlyArchiveZip = Path.Combine(archiveRoot, "2025-8.zip");
    File.WriteAllText(onlyArchiveFile, "elimina");
    File.WriteAllText(onlyArchiveZip, "elimina");
    DatabaseImportChanges emptyPeriodChanges =
        rollbackDatabase.SaveInvoicesForRollback(new[]
        {
            TestInvoice(
                "periodo-vuoto",
                "solo-ultima.xml",
                "PERIODO VUOTO",
                InvoiceSection.Suppliers)
        });
    LastImportService.Rollback(
        new LastImportRecord
        {
            Section = InvoiceSection.Suppliers,
            DatabaseChanges = emptyPeriodChanges,
            ImportedFileNames = new() { "solo-ultima.xml" },
            Year = 2025,
            Month = 8,
            PassiveRoot = passiveRoot,
            ArchiveRoot = archiveRoot,
            ArchivePeriodDirectory = emptyPeriodDirectory,
            ArchivedZipPath = onlyArchiveZip,
            ArchiveFiles = new() { onlyArchiveFile }
        },
        InvoiceSection.Suppliers,
        rollbackDatabase);
    Assert(
        !Directory.Exists(emptyPeriodDirectory) &&
        Directory.Exists(passiveRoot) &&
        Directory.Exists(archiveRoot),
        "La cartella periodo vuota non viene rimossa oppure è stata rimossa una radice.");

    DatabaseImportChanges unsafeChanges =
        rollbackDatabase.SaveInvoicesForRollback(new[]
        {
            TestInvoice(
                "importazione-non-sicura",
                "non-sicura.xml",
                "NON SICURA",
                InvoiceSection.Suppliers)
        });
    string safeCandidate = Path.Combine(passiveRoot, "candidato.xml");
    string outsideCandidate = Path.Combine(rollbackTestRoot, "fuori-radice.xml");
    File.WriteAllText(safeCandidate, "resta");
    File.WriteAllText(outsideCandidate, "resta");
    var unsafeRecord = new LastImportRecord
    {
        Section = InvoiceSection.Suppliers,
        DatabaseChanges = unsafeChanges,
        ImportedFileNames = new() { "non-sicura.xml" },
        Year = 2025,
        Month = 7,
        PassiveRoot = passiveRoot,
        ArchiveRoot = archiveRoot,
        ArchivePeriodDirectory = periodDirectory,
        PassiveFiles = new() { safeCandidate, outsideCandidate }
    };
    bool unsafeRejected = false;
    try
    {
        LastImportService.Rollback(
            unsafeRecord,
            InvoiceSection.Suppliers,
            rollbackDatabase);
    }
    catch (InvalidDataException)
    {
        unsafeRejected = true;
    }
    Assert(unsafeRejected, "Percorso esterno alla radice non rifiutato.");
    Assert(
        rollbackDatabase.GetAllInvoices().Any(invoice =>
            invoice.Id == "importazione-non-sicura") &&
        File.Exists(safeCandidate) &&
        File.Exists(outsideCandidate),
        "Validazione sicurezza avvenuta dopo una cancellazione.");

    bool wrongSectionRejected = false;
    try
    {
        LastImportService.Rollback(
            unsafeRecord,
            InvoiceSection.Customers,
            rollbackDatabase);
    }
    catch (InvalidOperationException)
    {
        wrongSectionRejected = true;
    }
    Assert(
        wrongSectionRejected &&
        rollbackDatabase.GetAllInvoices().Any(invoice =>
            invoice.Id == "importazione-non-sicura"),
        "Rollback avviato dalla sezione errata.");

    lastImportStore.Save(customerRecord);
    string protectedRecordPath = Path.Combine(
        databaseDirectory,
        "last-import.dat");
    byte[] corruptedRecord = File.ReadAllBytes(protectedRecordPath);
    corruptedRecord[corruptedRecord.Length / 2] ^= 0x5A;
    File.WriteAllBytes(protectedRecordPath, corruptedRecord);
    bool corruptedRejected = false;
    try
    {
        new LastImportService(databaseDirectory).Load();
    }
    catch (InvalidDataException)
    {
        corruptedRejected = true;
    }
    Assert(corruptedRejected, "Registro rollback alterato non rifiutato.");
    lastImportStore.Clear();
    Assert(
        new LastImportService(databaseDirectory).Load() == null,
        "Registro ultima importazione non eliminato.");
}
finally
{
    if (Directory.Exists(rollbackTestRoot))
        Directory.Delete(rollbackTestRoot, true);
}

string deletionTestRoot = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerAdminDeletionTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(deletionTestRoot);
try
{
    using var deletionDatabase = new DatabaseService(
        deletionTestRoot,
        migrateLegacyDatabase: false,
        databaseFileName: "eliminazione.db",
        hydrateLegacyContent: false);
    InvoiceData januaryOne = TestInvoice(
        "gennaio-1",
        "gennaio-1.xml",
        "GENNAIO 1",
        InvoiceSection.Customers);
    InvoiceData januaryTwo = TestInvoice(
        "gennaio-2",
        "gennaio-2.xml",
        "GENNAIO 2",
        InvoiceSection.Customers);
    InvoiceData february = TestInvoice(
        "febbraio",
        "febbraio.xml",
        "FEBBRAIO",
        InvoiceSection.Customers);
    InvoiceData nextYear = TestInvoice(
        "anno-successivo",
        "anno-successivo.xml",
        "ANNO SUCCESSIVO",
        InvoiceSection.Customers);
    januaryOne.Date = januaryTwo.Date = new DateTime(2025, 1, 10);
    januaryOne.Year = januaryTwo.Year = 2025;
    januaryOne.Month = januaryTwo.Month = 1;
    february.Date = new DateTime(2025, 2, 10);
    february.Year = 2025;
    february.Month = 2;
    nextYear.Date = new DateTime(2026, 1, 10);
    deletionDatabase.SaveInvoices(new[]
    {
        januaryOne,
        januaryTwo,
        february,
        nextYear
    });

    var januaryNode = new MonthNode
    {
        Name = "Gennaio",
        Year = 2025,
        Month = 1
    };
    januaryNode.Invoices.Add(new InvoiceNode { Data = januaryOne });
    januaryNode.Invoices.Add(new InvoiceNode { Data = januaryTwo });
    januaryNode.Invoices.Add(new InvoiceNode
    {
        Data = TestInvoice(
            "temporanea",
            "temporanea.xml",
            "TEMPORANEA",
            InvoiceSection.Customers)
    });
    januaryNode.Invoices.Last().Data.IsTemporary = true;
    var februaryNode = new MonthNode
    {
        Name = "Febbraio",
        Year = 2025,
        Month = 2
    };
    februaryNode.Invoices.Add(new InvoiceNode { Data = february });
    var yearNode = new YearNode { Name = "2025", Year = 2025 };
    yearNode.Months.Add(januaryNode);
    yearNode.Months.Add(februaryNode);

    Assert(
        MainViewModel.GetDeletionIds(januaryNode)
            .OrderBy(id => id)
            .SequenceEqual(new[] { "gennaio-1", "gennaio-2" }),
        "Selezione mese include fatture temporanee o fatture di altri mesi.");
    Assert(
        MainViewModel.GetDeletionIds(yearNode).Count == 3,
        "Selezione anno non include esattamente le fatture permanenti dell'anno.");
    var filteredJanuaryNode = new MonthNode
    {
        Name = "Gennaio",
        Year = 2025,
        Month = 1
    };
    filteredJanuaryNode.Invoices.Add(new InvoiceNode { Data = januaryOne });
    Assert(
        MainViewModel.GetDeletionIds(
                filteredJanuaryNode,
                deletionDatabase.GetAllInvoices())
            .OrderBy(id => id)
            .SequenceEqual(new[] { "gennaio-1", "gennaio-2" }),
        "Filtro attivo limita erroneamente l'eliminazione dell'intero mese.");
    Assert(
        MainViewModel.GetDeletionIds(januaryOne).Single() == "gennaio-1",
        "Selezione singola fattura errata.");

    string untouchedFile = Path.Combine(deletionTestRoot, "non-toccare.xml");
    File.WriteAllText(untouchedFile, "resta");
    int deletedMonth = deletionDatabase.DeleteInvoices(
        MainViewModel.GetDeletionIds(januaryNode));
    Assert(
        deletedMonth == 2 &&
        deletionDatabase.GetAllInvoices().Select(invoice => invoice.Id)
            .OrderBy(id => id)
            .SequenceEqual(new[] { "anno-successivo", "febbraio" }),
        "Eliminazione mese ha cancellato record estranei.");
    Assert(
        File.ReadAllText(untouchedFile) == "resta",
        "Eliminazione amministratore ha toccato file esterni al database.");

    int deletedSingle = deletionDatabase.DeleteInvoices(new[] { "febbraio" });
    Assert(
        deletedSingle == 1 &&
        deletionDatabase.GetAllInvoices().Single().Id == "anno-successivo",
        "Eliminazione singola fattura errata.");
    deletionDatabase.ClearAll();
    Assert(
        deletionDatabase.GetAllInvoices().Count == 0,
        "Eliminazione intera sezione non svuota il database.");
}
finally
{
    if (Directory.Exists(deletionTestRoot))
        Directory.Delete(deletionTestRoot, true);
}

string migrationTestDirectory = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerMigrationTest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(migrationTestDirectory);
try
{
    string migrationDatabasePath = Path.Combine(migrationTestDirectory, "fatture.db");
    using (var legacy = new SQLiteConnection(migrationDatabasePath))
    {
        legacy.Execute(
            "CREATE TABLE InvoiceData (" +
            "Id varchar primary key, FileName varchar, OriginalFilePath varchar, " +
            "FileContent blob, Date datetime, Year integer, Month integer, Day integer, " +
            "CompanyName varchar, SenderName varchar, RecipientCode varchar, TotalAmount decimal)");
        legacy.Execute(
            "INSERT INTO InvoiceData " +
            "(Id, FileName, OriginalFilePath, FileContent, Date, Year, Month, Day, CompanyName, SenderName, RecipientCode, TotalAmount) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "legacy-v3",
            "legacy.xml",
            "",
            Array.Empty<byte>(),
            new DateTime(2026, 7, 28),
            2026,
            7,
            28,
            "O.M.T.",
            "FORNITORE LEGACY",
            "ABC1234",
            100m);
    }

    using var migrated = new DatabaseService(
        migrationTestDirectory,
        migrateLegacyDatabase: false,
        databaseFileName: "fatture.db",
        hydrateLegacyContent: false);
    InvoiceData migratedInvoice = migrated.GetAllInvoices().Single();
    Assert(
        migratedInvoice.Id == "legacy-v3" &&
        migratedInvoice.SenderName == "FORNITORE LEGACY",
        "La migrazione 3.x → 4.0 non conserva le fatture esistenti.");
}
finally
{
    if (Directory.Exists(migrationTestDirectory))
        Directory.Delete(migrationTestDirectory, true);
}

string invoiceNumberBackfillDirectory = Path.Combine(
    Path.GetTempPath(),
    "FattureViewerInvoiceNumberBackfill_" + Guid.NewGuid().ToString("N"));
try
{
    byte[] versatileNumberXml = CreateInvoiceXml(
        " FT 7 / 2026 A ",
        "FORNITORE FORMATI",
        "IT01234567890",
        "O.M.T.",
        "IT02745400164");
    using (var database = new DatabaseService(
               invoiceNumberBackfillDirectory,
               migrateLegacyDatabase: false))
    {
        database.SaveInvoice(new InvoiceData
        {
            Id = "numero-da-ricostruire",
            FileName = "fattura-formati.xml",
            Date = new DateTime(2026, 7, 28),
            Year = 2026,
            Month = 7,
            Day = 28,
            SenderName = "FORNITORE FORMATI",
            InvoiceNumber = "",
            FileContent = versatileNumberXml
        });
    }

    using var reopened = new DatabaseService(
        invoiceNumberBackfillDirectory,
        migrateLegacyDatabase: false);
    Assert(
        reopened.GetAllInvoices().Single().InvoiceNumber == "FT 7 / 2026 A",
        "Il numero fattura mancante non viene ricostruito dal contenuto XML conservando spazi interni e simboli.");
    Assert(
        InvoiceParser.ParseInvoice("fattura-formati.xml", versatileNumberXml).InvoiceNumber ==
        "FT 7 / 2026 A",
        "Il parser non legge numeri fattura alfanumerici con spazi e separatori.");
}
finally
{
    if (Directory.Exists(invoiceNumberBackfillDirectory))
        Directory.Delete(invoiceNumberBackfillDirectory, true);
}

string databaseTestDir = Path.Combine(Path.GetTempPath(), "FattureViewerDatabaseTest_" + Guid.NewGuid().ToString("N"));
try
{
    byte[] expectedContent = System.Text.Encoding.UTF8.GetBytes("<FatturaElettronica />");
    using var database = new DatabaseService(databaseTestDir, migrateLegacyDatabase: false);
    database.SaveInvoice(new InvoiceData
    {
        Id = "database-test",
        FileName = "test.xml",
        Date = new DateTime(2026, 7, 13),
        Year = 2026,
        Month = 7,
        Day = 13,
        SenderName = "Test",
        FileContent = expectedContent
    });
    Assert(database.GetAllInvoices().Single().FileContent.Length == 0, "La lista metadati carica inutilmente il BLOB.");
    Assert(database.GetInvoiceContent("database-test")!.SequenceEqual(expectedContent), "Il documento non viene letto dal database.");

    byte[] invoiceXml = System.Text.Encoding.UTF8.GetBytes(
        "<FatturaElettronica><FatturaElettronicaHeader><CedentePrestatore><DatiAnagrafici><Anagrafica><Denominazione>Test ZIP</Denominazione></Anagrafica></DatiAnagrafici></CedentePrestatore></FatturaElettronicaHeader><FatturaElettronicaBody><DatiGenerali><DatiGeneraliDocumento><Data>2026-07-13</Data></DatiGeneraliDocumento></DatiGenerali></FatturaElettronicaBody></FatturaElettronica>");
    string databaseZip = Path.Combine(databaseTestDir, "fatture.zip");
    using (ZipArchive zip = ZipFile.Open(databaseZip, ZipArchiveMode.Create))
    {
        using Stream output = zip.CreateEntry("cartella/fattura.xml").Open();
        output.Write(invoiceXml, 0, invoiceXml.Length);
    }
    var zipInvoices = MainViewModel.ReadDatabaseImportFiles(new[] { databaseZip });
    Assert(zipInvoices.Count == 1, "Lo ZIP non importa la fattura nel database.");
    Assert(zipInvoices[0].FileContent.SequenceEqual(invoiceXml), "Il contenuto estratto dallo ZIP non viene conservato nel BLOB.");
}
finally
{
    if (Directory.Exists(databaseTestDir))
        Directory.Delete(databaseTestDir, true);
}

string? testRoot = Environment.GetEnvironmentVariable("FATTURE_TEST_DIR");
if (string.IsNullOrWhiteSpace(testRoot) || !Directory.Exists(testRoot))
    testRoot = @"D:\DocumentiD\ProgammaFattureTestFiles";
if (!Directory.Exists(testRoot))
{
    Console.WriteLine("OK (database, rollback ed eliminazione ADMIN isolati); test ZIP archivio saltato: campioni non disponibili");
    return;
}
string sampleZip = Directory.EnumerateFiles(testRoot, "*.zip", SearchOption.AllDirectories)
                            .FirstOrDefault(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                                      .Any(part => part.Equals("Backup", StringComparison.OrdinalIgnoreCase)) &&
                                                 !Path.GetFileName(f).StartsWith("Backup", StringComparison.OrdinalIgnoreCase)) ?? "";
Assert(!string.IsNullOrEmpty(sampleZip) && File.Exists(sampleZip), "File ZIP di test non trovato: " + sampleZip);

string tempRoot = Path.Combine(Path.GetTempPath(), "FattureViewerTests_" + Guid.NewGuid().ToString("N"));
string extractDir = Path.Combine(tempRoot, "Estratti");
string passiveDir = Path.Combine(tempRoot, "Fatture_Passive");
string archiveDir = Path.Combine(extractDir, "Archivio");
try
{
    Directory.CreateDirectory(tempRoot);
    Directory.CreateDirectory(extractDir);
    Directory.CreateDirectory(archiveDir);
    string existingFile = Path.Combine(extractDir, "preesistente.txt");
    File.WriteAllText(existingFile, "da conservare");
    string config = $"EXTRACT_DIR \"{Path.Combine(tempRoot, "PercorsoDaIgnorare")}\"\r\n" +
                    $"PASSIVE_DIR \"{passiveDir}\"\r\n" +
                    $"COPY \"*.xml\" TO \"{passiveDir}\" RENAME \"{{year}}_{{month}}_{{filename}}\"\r\n" +
                    $"COPY \"*.p7m\" TO \"{passiveDir}\" RENAME \"{{year}}_{{month}}_{{filename}}\"";

    Assert(MainViewModel.TryGetPassiveDirectories(extractDir, out string selectedPassiveDir, out string selectedArchiveDir), "La cartella Fatture Passive valida non viene riconosciuta.");
    Assert(Path.GetFullPath(selectedArchiveDir) == Path.GetFullPath(archiveDir), "La cartella Archivio non viene risolta dal percorso inserito.");
    Assert(!MainViewModel.TryGetPassiveDirectories(tempRoot, out _, out _), "Una cartella senza Archivio e' stata accettata.");
    Assert(!Directory.Exists(Path.Combine(tempRoot, "Archivio")), "Il controllo ha creato una cartella Archivio non richiesta.");
    Assert(MainViewModel.ShouldUseFlatSearch("Wuerth", null), "Ricerca azienda senza mese non usa lista piatta.");
    Assert(!MainViewModel.ShouldUseFlatSearch("Wuerth", 6), "Ricerca con mese non deve usare lista piatta.");
    Assert(!MainViewModel.ShouldUseFlatSearch("", null), "Ricerca vuota non deve usare lista piatta.");
    string runDir = ExtractionEngine.ProcessZip(sampleZip, config, selectedPassiveDir, out var extractedFiles);
    var files = ExtractionEngine.GetInvoiceFiles(runDir).ToList();
    var invoices = files.Select(InvoiceParser.ParseInvoice)
                        .Where(i => i.IsValidInvoice)
                        .ToList();
    var invoice = InvoiceParser.ParseInvoice(extractedFiles.First(f => Path.GetFileName(f).Equals("IT00125230219_NGXXA.xml", StringComparison.OrdinalIgnoreCase)));
    MainViewModel.CopyExtractedFilesToArchive(extractedFiles, extractDir, archiveDir, 2026, 6);
    string archivePeriodDir = Path.Combine(archiveDir, "2026-6");
    Assert(invoice.IsValidInvoice, "La fattura XML non viene riconosciuta come valida.");
    Assert(invoice.Year == 2026 && invoice.Month == 5 && invoice.Day == 31, "Data fattura letta male.");
    Assert(invoice.SenderName == "Wuerth S.r.l", "Il nome fattura deve essere il mittente.");


    Assert(Path.GetFullPath(runDir) == Path.GetFullPath(extractDir), "Lo ZIP non viene estratto direttamente nella cartella configurata.");
    Assert(File.ReadAllText(existingFile) == "da conservare", "Un file preesistente e' stato modificato durante l'estrazione.");
    Assert(extractedFiles.Count > 0, "Nessun file estratto dallo ZIP.");
    Assert(extractedFiles.Any(f => f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase)), "I P7M non vengono estratti per l'archiviazione.");
    Assert(files.All(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase)), "E' stato caricato un file non autorizzato.");
    Assert(files.Any(f => f.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase)), "I P7M non vengono caricati come fatture.");
    Assert(!Directory.GetDirectories(extractDir).Any(d => Path.GetFileName(d).StartsWith(Path.GetFileNameWithoutExtension(sampleZip) + "_", StringComparison.Ordinal)), "E' stata creata una sottocartella extra per lo ZIP.");
    Assert(Directory.Exists(archivePeriodDir), "La cartella Archivio\\2026-6 non e' stata creata.");
    Assert(Directory.GetFiles(archivePeriodDir, "*", SearchOption.AllDirectories).Length == extractedFiles.Count, "Non tutti i file estratti sono stati copiati in Archivio\\2026-6.");
    Assert(Directory.GetFiles(archivePeriodDir, "*.p7m", SearchOption.AllDirectories).Any(), "I P7M non vengono spostati nella cartella archivio.");
    Assert(files.All(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("Backup", StringComparison.OrdinalIgnoreCase))), "Sono stati processati file da Backup.");
    Assert(invoices.Count > 0, "Nessuna fattura valida trovata nello ZIP.");
    Assert(invoices.All(i => i.Year > 1 && i.Month > 0 && i.Day > 0), "Catalogazione interna fattura non valida.");
    Assert(Directory.GetFiles(passiveDir).Any(f => Path.GetFileName(f).StartsWith(invoices[0].Year + "_" + invoices[0].Month.ToString("D2") + "_", StringComparison.Ordinal)), "Le fatture copiate non usano anno/mese reale.");
    Assert(Directory.GetFiles(passiveDir, "*.p7m").Any(), "I P7M non vengono copiati nella cartella interna.");

    string missingList = Path.Combine(testRoot, "NN va.txt");
    string oldPassiveDir = Path.Combine(testRoot, "FatturePassive OLD");
    string base64P7m = Path.Combine(oldPassiveDir, "tmp", "IT03632460485_B6I5B.xml.p7m");
    if (File.Exists(base64P7m))
        Assert(InvoiceParser.ParseInvoice(base64P7m).IsValidInvoice, "P7M Base64 ancora saltato.");

    string complexXmlPath = Path.Combine(oldPassiveDir, "IT10988541008_1UMUC.xml");
    if (File.Exists(complexXmlPath))
    {
        var complexInvoice = InvoiceParser.ParseInvoice(complexXmlPath);
        string complexHtml = HtmlGenerator.GenerateInvoiceHtml(complexInvoice, File.ReadAllText(complexXmlPath));
        Assert(complexInvoice.RecipientCode == "SUBM70N", "Codice destinatario non mappato.");
        foreach (string expected in new[]
        {
            "TRZGNT65A29M147U", "109871/TE", "Bolletta servizio E.E.", "ONERI DI SISTEMA",
            "3.960,95", "SUBM70N", "CNE-32234", "01-02-2025"
        })
        {
            Assert(complexHtml.Contains(expected, StringComparison.Ordinal), "Campo XML complesso assente dall'HTML: " + expected);
        }
    }

    string sequenceXmlPath = Path.Combine(oldPassiveDir, "IT016417907022026T_05Wf0.xml");
    if (File.Exists(sequenceXmlPath))
    {
        var sequenceInvoice = InvoiceParser.ParseInvoice(sequenceXmlPath);
        string sequenceHtml = HtmlGenerator.GenerateInvoiceHtml(sequenceInvoice, File.ReadAllText(sequenceXmlPath));
        int ddtPosition = sequenceHtml.IndexOf("Ddt nr. 166-2026", StringComparison.Ordinal);
        int orderPosition = sequenceHtml.IndexOf("Ordine Cl. num. 47-2026", StringComparison.Ordinal);
        int itemPosition = sequenceHtml.IndexOf("DISTANZIALE", StringComparison.Ordinal);
        Assert(ddtPosition >= 0 && ddtPosition < orderPosition && orderPosition < itemPosition, "Riferimenti e articoli non rispettano la sequenza XML.");
        Assert(sequenceHtml.Contains("<tr class='reference-row'>", StringComparison.Ordinal), "Riga riferimento non dedicata.");
        string referenceRow = sequenceHtml.Substring(sequenceHtml.IndexOf("<tr class='reference-row'>", StringComparison.Ordinal));
        referenceRow = referenceRow.Substring(0, referenceRow.IndexOf("</tr>", StringComparison.Ordinal));
        Assert(!referenceRow.Contains("0,00", StringComparison.Ordinal), "Riga riferimento contiene valori numerici.");
        Assert(sequenceHtml.Contains(">NR</td>", StringComparison.Ordinal), "Unita di misura non visualizzata.");
        Assert(!sequenceHtml.Contains(">RIFERIMENTI<", StringComparison.Ordinal), "Riferimenti ancora separati dagli articoli.");
    }

    string discountXmlPath = Path.Combine(oldPassiveDir, "IT016417907022026h_05v6Q.xml");
    if (File.Exists(discountXmlPath))
    {
        var discountInvoice = InvoiceParser.ParseInvoice(discountXmlPath);
        string discountHtml = HtmlGenerator.GenerateInvoiceHtml(discountInvoice, File.ReadAllText(discountXmlPath));
        Assert(discountHtml.Contains("-10,00%", StringComparison.Ordinal), "Sconto riga non visualizzato.");
        Assert(discountHtml.Contains(">NR</td>", StringComparison.Ordinal), "UM riga scontata non visualizzata.");
    }

    if (File.Exists(missingList) && Directory.Exists(oldPassiveDir))
    {
        foreach (string name in File.ReadAllLines(missingList).Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            var missingInvoice = InvoiceParser.ParseInvoice(Path.Combine(oldPassiveDir, name.Trim()));
            Assert(missingInvoice.IsValidInvoice, "Fattura segnalata ancora saltata: " + name);
        }
    }
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, true);
}

Console.WriteLine("OK");
