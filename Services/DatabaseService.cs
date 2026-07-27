using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly SQLiteConnection _db;

        public string StorageDirectory { get; }
        public string DatabasePath { get; }

        public DatabaseService(string? storageDirectory = null, bool migrateLegacyDatabase = true)
        {
            StorageDirectory = AppSettingsService.NormalizeStorageDirectory(
                storageDirectory ?? AppSettingsService.GetStorageDirectory());
            Directory.CreateDirectory(StorageDirectory);
            DatabasePath = Path.Combine(StorageDirectory, "fatture.db");

            _db = new SQLiteConnection(DatabasePath);
            _db.CreateTable<InvoiceData>();

            HydrateMissingContentFromLegacyPaths();
            if (migrateLegacyDatabase && !_db.Table<InvoiceData>().Any())
                ImportLegacyDatabase();
        }

        public void SaveInvoice(InvoiceData invoice)
        {
            var existing = _db.Table<InvoiceData>().FirstOrDefault(i => i.Id == invoice.Id || i.FileName == invoice.FileName);
            if (existing != null)
            {
                invoice.Id = existing.Id;
                if (invoice.FileContent == null || invoice.FileContent.Length == 0)
                    invoice.FileContent = existing.FileContent ?? Array.Empty<byte>();
                _db.Update(invoice);
            }
            else
            {
                _db.Insert(invoice);
            }
        }

        public void SaveInvoices(IEnumerable<InvoiceData> invoices)
        {
            _db.RunInTransaction(() =>
            {
                foreach (var invoice in invoices)
                {
                    SaveInvoice(invoice);
                }
            });
        }

        public List<InvoiceData> GetAllInvoices()
        {
            return GetInvoiceMetadata().OrderByDescending(i => i.Date).ToList();
        }

        public List<InvoiceData> SearchInvoices(int? year = null, int? month = null, int? day = null, string companyName = null)
        {
            IEnumerable<InvoiceData> result = GetInvoiceMetadata();

            if (year.HasValue && year.Value > 0)
                result = result.Where(i => i.Year == year.Value);
            if (month.HasValue && month.Value > 0)
                result = result.Where(i => i.Month == month.Value);
            if (day.HasValue && day.Value > 0)
                result = result.Where(i => i.Day == day.Value);

            if (!string.IsNullOrWhiteSpace(companyName))
            {
                string lowerQuery = companyName.ToLower();
                result = result.Where(i => !string.IsNullOrEmpty(i.SenderName) && i.SenderName.ToLower().Contains(lowerQuery));
            }

            return result.OrderByDescending(i => i.Date).ToList();
        }

        public byte[]? GetInvoiceContent(string invoiceId)
        {
            return _db.Table<InvoiceData>().FirstOrDefault(i => i.Id == invoiceId)?.FileContent;
        }

        public List<InvoiceData> GetAllInvoicesWithContent()
        {
            return _db.Table<InvoiceData>().ToList();
        }

        
        public void ClearAll()
        {
            _db.DeleteAll<InvoiceData>();
        }

        public void Dispose()
        {
            _db.Close();
            _db.Dispose();
        }

        private List<InvoiceData> GetInvoiceMetadata()
        {
            const string sql = @"SELECT Id, FileName, OriginalFilePath, Date, Year, Month, Day,
CompanyName, SenderName, RecipientCode, TotalAmount FROM InvoiceData";
            return _db.Query<InvoiceData>(sql);
        }

        private void HydrateMissingContentFromLegacyPaths()
        {
            foreach (InvoiceData invoice in _db.Query<InvoiceData>(
                         "SELECT * FROM InvoiceData WHERE FileContent IS NULL OR length(FileContent) = 0"))
            {
                TryHydrateFromOriginalPath(invoice);
            }
        }

        private void ImportLegacyDatabase()
        {
            string legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FattureViewer",
                "fatture.db");

            if (!File.Exists(legacyPath) || PathsEqual(legacyPath, DatabasePath))
                return;

            try
            {
                using var legacy = new SQLiteConnection(legacyPath);
                legacy.CreateTable<InvoiceData>();
                foreach (InvoiceData invoice in legacy.Table<InvoiceData>().ToList())
                {
                    if (invoice.FileContent == null || invoice.FileContent.Length == 0)
                        TryLoadFileContent(invoice);
                    if (invoice.FileContent != null && invoice.FileContent.Length > 0)
                        SaveInvoice(invoice);
                }
            }
            catch
            {
                // Un vecchio database non leggibile non deve impedire l'avvio.
            }
        }

        private void TryHydrateFromOriginalPath(InvoiceData invoice)
        {
            if (TryLoadFileContent(invoice))
                _db.Update(invoice);
        }

        private static bool TryLoadFileContent(InvoiceData invoice)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(invoice.OriginalFilePath) && File.Exists(invoice.OriginalFilePath))
                {
                    invoice.FileContent = File.ReadAllBytes(invoice.OriginalFilePath);
                    return invoice.FileContent.Length > 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
    }
}
