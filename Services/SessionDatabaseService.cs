using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public sealed class SessionDatabaseService : IDisposable
    {
        private const string DatabasePrefix = "fattureSessioneTemporanea_";
        private readonly DatabaseService _database;
        private bool _disposed;

        public string SessionDirectory { get; }
        public string DatabasePath => _database.DatabasePath;

        public SessionDatabaseService(string? sessionDirectory = null)
        {
            SessionDirectory = Path.GetFullPath(sessionDirectory ?? Path.Combine(
                Path.GetTempPath(),
                "FattureViewer",
                "Session"));
            Directory.CreateDirectory(SessionDirectory);

            string fileName =
                $"{DatabasePrefix}{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}.db";
            _database = new DatabaseService(
                SessionDirectory,
                migrateLegacyDatabase: false,
                databaseFileName: fileName,
                hydrateLegacyContent: false);
        }

        public void SaveInvoices(InvoiceSection section, IEnumerable<InvoiceData> invoices)
        {
            List<InvoiceData> prepared = invoices.ToList();
            foreach (InvoiceData invoice in prepared)
            {
                invoice.Section = section.ToStorageValue();
                invoice.IsTemporary = true;
            }
            _database.SaveInvoices(prepared);
        }

        public List<InvoiceData> GetInvoices(InvoiceSection section)
        {
            string sectionValue = section.ToStorageValue();
            List<InvoiceData> invoices = _database.GetAllInvoices()
                .Where(invoice => string.Equals(
                    invoice.Section,
                    sectionValue,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (InvoiceData invoice in invoices)
                invoice.IsTemporary = true;
            return invoices;
        }

        public byte[]? GetInvoiceContent(string invoiceId)
        {
            return _database.GetInvoiceContent(invoiceId);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            string databasePath = _database.DatabasePath;
            _database.Dispose();

            foreach (string path in new[]
                     {
                         databasePath,
                         databasePath + "-journal",
                         databasePath + "-shm",
                         databasePath + "-wal"
                     })
            {
                if (IsSafeSessionDatabasePath(path) && File.Exists(path))
                    File.Delete(path);
            }

            if (Directory.Exists(SessionDirectory) &&
                !Directory.EnumerateFileSystemEntries(SessionDirectory).Any())
                Directory.Delete(SessionDirectory);
        }

        public bool IsSafeSessionDatabasePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            return string.Equals(
                       directory,
                       SessionDirectory,
                       StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith(DatabasePrefix, StringComparison.Ordinal) &&
                   (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".db-journal", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase));
        }
    }
}
