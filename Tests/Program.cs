using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FattureViewer.Services;
using FattureViewer.ViewModels;
using FattureViewer.Models;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
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
