using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Xml.Linq;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class InvoiceParser
    {
        public static InvoiceData ParseInvoice(string filePath)
        {
            try
            {
                return ParseInvoice(Path.GetFileName(filePath), File.ReadAllBytes(filePath), filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read {filePath}: {ex.Message}");
                return new InvoiceData { FileName = Path.GetFileName(filePath), OriginalFilePath = filePath };
            }
        }

        public static InvoiceData ParseInvoice(string fileName, byte[] fileBytes)
        {
            return ParseInvoice(fileName, fileBytes, string.Empty);
        }

        private static InvoiceData ParseInvoice(string fileName, byte[] fileBytes, string originalFilePath)
        {
            var data = new InvoiceData { FileName = fileName, OriginalFilePath = originalFilePath };

            try
            {
                string xmlContent;

                if (fileName.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
                {
                    xmlContent = DecodeP7M(fileBytes);
                }
                else
                {
                    xmlContent = DecodeXmlBytes(fileBytes);
                }

                // Parse XML
                XDocument doc = XDocument.Parse(xmlContent);
                if (!string.Equals(doc.Root?.Name.LocalName, "FatturaElettronica", StringComparison.OrdinalIgnoreCase))
                    return data;

                // Find Receiver (CessionarioCommittente)
                var header = Find(doc, "FatturaElettronicaHeader");
                if (header != null)
                {
                    data.RecipientCode = Find(header, "CodiceDestinatario")?.Value ?? "";
                    var receiver = Find(Find(header, "CessionarioCommittente"), "Anagrafica");
                    if (receiver != null)
                    {
                        var denom = Find(receiver, "Denominazione")?.Value;
                        if (!string.IsNullOrEmpty(denom))
                        {
                            data.CompanyName = denom;
                            data.RecipientName = denom;
                        }
                        else
                        {
                            var nome = Find(receiver, "Nome")?.Value;
                            var cognome = Find(receiver, "Cognome")?.Value;
                            data.CompanyName = $"{nome} {cognome}".Trim();
                            data.RecipientName = data.CompanyName;
                        }
                    }

                    XElement? recipientParty = Find(header, "CessionarioCommittente");
                    data.RecipientVatNumber = ReadVatNumber(recipientParty);

                    XElement? senderParty = Find(header, "CedentePrestatore");
                    var sender = Find(senderParty, "Anagrafica");
                    if (sender != null)
                    {
                        var denom = Find(sender, "Denominazione")?.Value;
                        if (!string.IsNullOrEmpty(denom))
                            data.SenderName = denom;
                        else
                            data.SenderName = $"{Find(sender, "Nome")?.Value} {Find(sender, "Cognome")?.Value}".Trim();
                    }
                    data.SenderVatNumber = ReadVatNumber(senderParty);
                }

                // Find Date
                var body = Find(doc, "FatturaElettronicaBody");
                if (body != null)
                {
                    var datiGenerali = Find(body, "DatiGeneraliDocumento");
                    if (datiGenerali != null)
                    {
                        data.InvoiceNumber =
                            Find(datiGenerali, "Numero")?.Value?.Trim() ?? "";
                        var dateStr = Find(datiGenerali, "Data")?.Value;
                        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate) ||
                            DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            data.Date = parsedDate;
                            data.Year = parsedDate.Year;
                            data.Month = parsedDate.Month;
                            data.Day = parsedDate.Day;
                        }

                        var totalStr = Find(datiGenerali, "ImportoTotaleDocumento")?.Value;
                        if (decimal.TryParse(totalStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal total))
                        {
                            data.TotalAmount = total;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse {fileName}: {ex.Message}");
            }

            return data;
        }

        public static string DecodeP7M(byte[] fileBytes)
        {
            if (fileBytes.Length > 0 && fileBytes[0] != 0x30)
            {
                try
                {
                    fileBytes = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(fileBytes));
                }
                catch (FormatException) { }
            }
            try
            {
                var signedCms = new SignedCms();
                signedCms.Decode(fileBytes);
                return DecodeXmlBytes(signedCms.ContentInfo.Content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignedCms failed: {ex.Message}. Trying fallback...");
                // Fallback: search for XML byte patterns
                byte[] startPattern1 = System.Text.Encoding.UTF8.GetBytes("<?xml");
                byte[] startPattern2 = System.Text.Encoding.UTF8.GetBytes("<p:FatturaElettronica");
                byte[] startPattern3 = System.Text.Encoding.UTF8.GetBytes("<FatturaElettronica");
                byte[] endPattern1 = System.Text.Encoding.UTF8.GetBytes("</p:FatturaElettronica>");
                byte[] endPattern2 = System.Text.Encoding.UTF8.GetBytes("</FatturaElettronica>");

                int startIndex = FindPattern(fileBytes, startPattern1);
                if (startIndex == -1) startIndex = FindPattern(fileBytes, startPattern2);
                if (startIndex == -1) startIndex = FindPattern(fileBytes, startPattern3);

                if (startIndex != -1)
                {
                    int endIndex = FindPattern(fileBytes, endPattern1, startIndex);
                    int endPatternLen = endPattern1.Length;

                    if (endIndex == -1) 
                    {
                        endIndex = FindPattern(fileBytes, endPattern2, startIndex);
                        endPatternLen = endPattern2.Length;
                    }

                    if (endIndex != -1)
                    {
                        // Find the closing '>'
                        int closeBracket = FindPattern(fileBytes, new byte[] { (byte)'>' }, endIndex + endPatternLen - 1);
                        if (closeBracket != -1)
                        {
                            int length = closeBracket - startIndex + 1;
                            byte[] xmlBytes = new byte[length];
                            Array.Copy(fileBytes, startIndex, xmlBytes, 0, length);
                            return DecodeXmlBytes(xmlBytes);
                        }
                    }
                }
                return string.Empty;
            }
        }

        private static XElement? Find(XContainer? parent, string localName)
        {
            return parent?.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadVatNumber(XContainer? party)
        {
            XElement? vat = Find(party, "IdFiscaleIVA");
            if (vat == null)
                return string.Empty;

            string country = Find(vat, "IdPaese")?.Value?.Trim() ?? string.Empty;
            string code = Find(vat, "IdCodice")?.Value?.Trim() ?? string.Empty;
            return (country + code).ToUpperInvariant();
        }

        private static string DecodeXmlBytes(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static int FindPattern(byte[] data, byte[] pattern, int startIndex = 0)
        {
            if (pattern.Length == 0 || data.Length == 0 || data.Length < pattern.Length) return -1;
            for (int i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
