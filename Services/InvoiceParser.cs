using System;
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
            var data = new InvoiceData { FileName = Path.GetFileName(filePath), OriginalFilePath = filePath };

            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string xmlContent;

                if (filePath.EndsWith(".p7m", StringComparison.OrdinalIgnoreCase))
                {
                    xmlContent = DecodeP7M(fileBytes);
                }
                else
                {
                    xmlContent = System.Text.Encoding.UTF8.GetString(fileBytes);
                }

                // Parse XML
                XDocument doc = XDocument.Parse(xmlContent);
                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Find Receiver (CessionarioCommittente)
                var header = doc.Descendants(ns + "FatturaElettronicaHeader").FirstOrDefault();
                if (header != null)
                {
                    var receiver = header.Element(ns + "CessionarioCommittente")?
                                         .Element(ns + "DatiAnagrafici")?
                                         .Element(ns + "Anagrafica");
                    if (receiver != null)
                    {
                        var denom = receiver.Element(ns + "Denominazione")?.Value;
                        if (!string.IsNullOrEmpty(denom))
                        {
                            data.CompanyName = denom;
                        }
                        else
                        {
                            var nome = receiver.Element(ns + "Nome")?.Value;
                            var cognome = receiver.Element(ns + "Cognome")?.Value;
                            data.CompanyName = $"{nome} {cognome}".Trim();
                        }
                    }

                    var sender = header.Element(ns + "CedentePrestatore")?
                                       .Element(ns + "DatiAnagrafici")?
                                       .Element(ns + "Anagrafica");
                    if (sender != null)
                    {
                        var denom = sender.Element(ns + "Denominazione")?.Value;
                        if (!string.IsNullOrEmpty(denom))
                            data.SenderName = denom;
                        else
                            data.SenderName = $"{sender.Element(ns + "Nome")?.Value} {sender.Element(ns + "Cognome")?.Value}".Trim();
                    }
                }

                // Find Date
                var body = doc.Descendants(ns + "FatturaElettronicaBody").FirstOrDefault();
                if (body != null)
                {
                    var datiGenerali = body.Element(ns + "DatiGenerali")?
                                           .Element(ns + "DatiGeneraliDocumento");
                    if (datiGenerali != null)
                    {
                        var dateStr = datiGenerali.Element(ns + "Data")?.Value;
                        if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                        {
                            data.Date = parsedDate;
                            data.Year = parsedDate.Year;
                            data.Month = parsedDate.Month;
                            data.Day = parsedDate.Day;
                        }

                        var totalStr = datiGenerali.Element(ns + "ImportoTotaleDocumento")?.Value;
                        if (decimal.TryParse(totalStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal total))
                        {
                            data.TotalAmount = total;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse {filePath}: {ex.Message}");
            }

            return data;
        }

        public static string DecodeP7M(byte[] fileBytes)
        {
            try
            {
                var signedCms = new SignedCms();
                signedCms.Decode(fileBytes);
                return System.Text.Encoding.UTF8.GetString(signedCms.ContentInfo.Content);
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
                            return System.Text.Encoding.UTF8.GetString(xmlBytes);
                        }
                    }
                }
                return string.Empty;
            }
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
