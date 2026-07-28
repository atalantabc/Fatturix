using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FattureViewer.Services;
using FattureViewer.ViewModels;
using FattureViewer.Models;
using SQLite;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
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
    string fiveInvoicesZip = CreateInvoiceZip(
        v4TestRoot,
        "solo-cinque.zip",
        5,
        omtName,
        omtVat,
        "CLIENTE TEST",
        "IT10987654321");

    var supplierInvoices = MainViewModel.ReadDatabaseImportFiles(new[] { supplierZip });
    var customerInvoices = MainViewModel.ReadDatabaseImportFiles(new[] { customerZip });
    var misplacedCustomers = MainViewModel.ReadDatabaseImportFiles(new[] { misplacedCustomerZip });
    var misplacedSuppliers = MainViewModel.ReadDatabaseImportFiles(new[] { misplacedSupplierZip });
    var onlyFive = MainViewModel.ReadDatabaseImportFiles(new[] { fiveInvoicesZip });

    Assert(supplierInvoices.Count == 1, "Lo ZIP Fornitori corretto non viene letto.");
    Assert(customerInvoices.Count == 1, "Lo ZIP Clienti corretto non viene letto.");
    Assert(supplierInvoices[0].SenderName == "FORNITORE TEST", "La sezione Fornitori non usa il cedente.");
    Assert(customerInvoices[0].RecipientName == "CLIENTE TEST", "La sezione Clienti non usa il destinatario.");
    Assert(supplierInvoices[0].SenderVatNumber == "IT12345678901", "Partita IVA cedente non letta.");
    Assert(customerInvoices[0].RecipientVatNumber == "IT10987654321", "Partita IVA destinatario non letta.");

    Assert(
        ImportSafetyService.RequiresWarning(misplacedCustomers, InvoiceSection.Suppliers),
        "Sei fatture Clienti nei Fornitori non attivano l'avviso.");
    Assert(
        ImportSafetyService.RequiresWarning(misplacedSuppliers, InvoiceSection.Customers),
        "Sei fatture Fornitori nei Clienti non attivano l'avviso.");
    Assert(
        !ImportSafetyService.RequiresWarning(onlyFive, InvoiceSection.Suppliers),
        "L'avviso scatta con sole cinque fatture.");
    Assert(
        !ImportSafetyService.CanContinueAfterWarning(true, false),
        "Annulla non interrompe l'importazione.");
    Assert(
        ImportSafetyService.CanContinueAfterWarning(true, true),
        "Importa comunque non consente l'importazione.");

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
    Console.WriteLine("OK (database e ZIP interno); test ZIP archivio saltato: campioni non disponibili");
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
