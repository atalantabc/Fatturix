using System;
using System.IO;
using System.Text;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public sealed class InvoiceDocumentContent
    {
        public string Xml { get; init; } = "";
        public string Html { get; init; } = "";
    }

    public static class InvoiceDocumentService
    {
        public static InvoiceDocumentContent Render(
            InvoiceData invoice,
            byte[] fileContent)
        {
            if (fileContent == null || fileContent.Length == 0)
                throw new InvalidDataException(
                    "Il documento non è presente nel database.");

            string xml;
            if (invoice.FileName.EndsWith(
                    ".p7m",
                    StringComparison.OrdinalIgnoreCase))
            {
                xml = InvoiceParser.DecodeP7M(fileContent);
            }
            else
            {
                using var reader = new StreamReader(
                    new MemoryStream(fileContent),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                xml = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(xml))
                throw new InvalidDataException(
                    "Il contenuto della fattura non è leggibile.");

            return new InvoiceDocumentContent
            {
                Xml = xml,
                Html = HtmlGenerator.GenerateInvoiceHtml(invoice, xml)
            };
        }
    }
}
