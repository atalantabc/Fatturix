using System;
using System.IO;
using System.Linq;
using FattureViewer.Services;
using FattureViewer.ViewModels;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string? testRoot = Environment.GetEnvironmentVariable("FATTURE_TEST_DIR");
if (string.IsNullOrWhiteSpace(testRoot) || !Directory.Exists(testRoot))
    testRoot = @"D:\DocumentiD\ProgammaFattureTestFiles";
string sampleXml = Path.Combine(testRoot, "FatturePassive", "IT00125230219_NGXXA.xml");
string sampleZip = Path.Combine(testRoot, "b97bb40da8e148edb4b1c231cb749a0d-f4z84okpq3.zip");
if (!File.Exists(sampleZip))
    sampleZip = Path.Combine(testRoot, "FatturePassive", "Archivio", "2024-1.zip");
Assert(File.Exists(sampleXml), "File XML di test non trovato.");
Assert(!string.IsNullOrEmpty(sampleZip) && File.Exists(sampleZip), "File ZIP di test non trovato: " + sampleZip);

var invoice = InvoiceParser.ParseInvoice(sampleXml);
Assert(invoice.IsValidInvoice, "La fattura XML non viene riconosciuta come valida.");
Assert(invoice.Year == 2026 && invoice.Month == 5 && invoice.Day == 31, "Data fattura letta male.");
Assert(invoice.SenderName == "Wuerth S.r.l", "Il nome fattura deve essere il mittente.");

string tempRoot = Path.Combine(Path.GetTempPath(), "FattureViewerTests_" + Guid.NewGuid().ToString("N"));
string extractDir = Path.Combine(tempRoot, "Estratti");
string passiveDir = Path.Combine(tempRoot, "Fatture_Passive");
try
{
    Directory.CreateDirectory(tempRoot);
    string config = $"EXTRACT_DIR \"{extractDir}\"\r\n" +
                    $"PASSIVE_DIR \"{passiveDir}\"\r\n" +
                    $"COPY \"*.xml\" TO \"{passiveDir}\" RENAME \"{{year}}_{{month}}_{{filename}}\"\r\n" +
                    $"COPY \"*.p7m\" TO \"{passiveDir}\" RENAME \"{{year}}_{{month}}_{{filename}}\"";

    string runDir = ExtractionEngine.ProcessZip(sampleZip, config, 2024, 4);
    var files = ExtractionEngine.GetInvoiceFiles(runDir).ToList();
    var invoices = files.Select(InvoiceParser.ParseInvoice)
                        .Where(i => i.IsValidInvoice)
                        .Select(i => MainViewModel.ApplyImportPeriod(i, 2024, 4))
                        .ToList();

    Assert(files.All(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("Backup", StringComparison.OrdinalIgnoreCase))), "Sono stati processati file da Backup.");
    Assert(invoices.Count > 0, "Nessuna fattura valida trovata nello ZIP.");
    Assert(invoices.All(i => i.Year == 2024 && i.Month == 4 && i.Day == 1), "Catalogazione manuale anno/mese non applicata.");
    Assert(Directory.GetFiles(passiveDir).Any(f => Path.GetFileName(f).StartsWith("2024_04_", StringComparison.Ordinal)), "Le fatture copiate non usano anno/mese manuali.");
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, true);
}

Console.WriteLine("OK");
