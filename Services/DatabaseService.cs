using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public sealed class DatabaseImportChanges
    {
        public List<string> InsertedInvoiceIds { get; set; } = new();
        public List<InvoiceData> PreviousInvoices { get; set; } = new();
    }

    public class DatabaseService : IDisposable
    {
        private readonly SQLiteConnection _db;

        public string StorageDirectory { get; }
        public string DatabasePath { get; }

        public DatabaseService(
            string? storageDirectory = null,
            bool migrateLegacyDatabase = true,
            string databaseFileName = "fatture.db",
            bool hydrateLegacyContent = true)
        {
            StorageDirectory = AppSettingsService.NormalizeStorageDirectory(
                storageDirectory ?? AppSettingsService.GetStorageDirectory());
            Directory.CreateDirectory(StorageDirectory);
            DatabasePath = Path.Combine(StorageDirectory, Path.GetFileName(databaseFileName));

            _db = new SQLiteConnection(DatabasePath);
            _db.CreateTable<InvoiceData>();

            if (hydrateLegacyContent)
                HydrateMissingContentFromLegacyPaths();
            if (migrateLegacyDatabase && !_db.Table<InvoiceData>().Any())
                ImportLegacyDatabase();
            if (hydrateLegacyContent)
                BackfillMissingInvoiceNumbers();
        }

        public void SaveInvoice(InvoiceData invoice)
        {
            InvoiceData? existing = FindExistingInvoice(invoice);
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

        public DatabaseImportChanges SaveInvoicesForRollback(
            IEnumerable<InvoiceData> invoices)
        {
            var changes = new DatabaseImportChanges();
            var insertedIds = new HashSet<string>(StringComparer.Ordinal);
            var previousIds = new HashSet<string>(StringComparer.Ordinal);

            _db.RunInTransaction(() =>
            {
                foreach (InvoiceData invoice in invoices)
                {
                    InvoiceData? existing = FindExistingInvoice(invoice);
                    if (existing == null)
                    {
                        _db.Insert(invoice);
                        insertedIds.Add(invoice.Id);
                        changes.InsertedInvoiceIds.Add(invoice.Id);
                        continue;
                    }

                    if (!insertedIds.Contains(existing.Id) &&
                        previousIds.Add(existing.Id))
                        changes.PreviousInvoices.Add(existing);

                    invoice.Id = existing.Id;
                    if (invoice.FileContent == null || invoice.FileContent.Length == 0)
                        invoice.FileContent = existing.FileContent ?? Array.Empty<byte>();
                    _db.Update(invoice);
                }
            });

            return changes;
        }

        public void RollbackImport(DatabaseImportChanges changes)
        {
            _db.RunInTransaction(() =>
            {
                foreach (string id in changes.InsertedInvoiceIds.Distinct())
                    _db.Delete<InvoiceData>(id);
                foreach (InvoiceData invoice in changes.PreviousInvoices
                             .GroupBy(item => item.Id)
                             .Select(group => group.First()))
                    _db.InsertOrReplace(invoice);
            });
        }

        public List<InvoiceData> GetAllInvoices()
        {
            return GetInvoiceMetadata().OrderByDescending(i => i.Date).ToList();
        }

        public List<InvoiceData> SearchInvoices(
            int? year = null,
            int? month = null,
            int? day = null,
            string? companyName = null,
            bool useRecipientName = false)
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
                string lowerQuery = companyName.ToLowerInvariant();
                result = result.Where(i =>
                {
                    string partyName = useRecipientName
                        ? GetRecipientName(i)
                        : i.SenderName;
                    return !string.IsNullOrWhiteSpace(partyName) &&
                           partyName.ToLowerInvariant().Contains(lowerQuery);
                });
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

        public int DeleteInvoices(IEnumerable<string> invoiceIds)
        {
            string[] ids = invoiceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            int deleted = 0;
            _db.RunInTransaction(() =>
            {
                foreach (string id in ids)
                    deleted += _db.Delete<InvoiceData>(id);
            });
            return deleted;
        }

        public void Dispose()
        {
            _db.Close();
            _db.Dispose();
        }

        private List<InvoiceData> GetInvoiceMetadata()
        {
            const string sql = @"SELECT Id, FileName, OriginalFilePath, Date, Year, Month, Day,
CompanyName, SenderName, SenderVatNumber, RecipientName, RecipientVatNumber,
RecipientCode, InvoiceNumber, TotalAmount, Section FROM InvoiceData";
            return _db.Query<InvoiceData>(sql);
        }

        private static string GetRecipientName(InvoiceData invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.RecipientName)
                ? invoice.CompanyName
                : invoice.RecipientName;
        }

        private InvoiceData? FindExistingInvoice(InvoiceData invoice)
        {
            InvoiceData? existing = _db.Table<InvoiceData>()
                .FirstOrDefault(item => item.Id == invoice.Id);
            if (existing != null)
                return existing;

            return _db.Table<InvoiceData>()
                .Where(item => item.FileName == invoice.FileName)
                .ToList()
                .FirstOrDefault(item =>
                    string.IsNullOrWhiteSpace(item.Section) ||
                    string.Equals(
                        item.Section,
                        invoice.Section,
                        StringComparison.OrdinalIgnoreCase));
        }

        private void HydrateMissingContentFromLegacyPaths()
        {
            foreach (InvoiceData invoice in _db.Query<InvoiceData>(
                         "SELECT * FROM InvoiceData WHERE FileContent IS NULL OR length(FileContent) = 0"))
            {
                TryHydrateFromOriginalPath(invoice);
            }
        }

        private void BackfillMissingInvoiceNumbers()
        {
            const string metadataKey = "InvoiceNumberBackfillVersion";
            _db.Execute(
                "CREATE TABLE IF NOT EXISTS AppMetadata ([Key] TEXT PRIMARY KEY, [Value] TEXT)");
            string? completedVersion = _db.ExecuteScalar<string>(
                "SELECT [Value] FROM AppMetadata WHERE [Key] = ?",
                metadataKey);
            if (completedVersion == "1")
                return;
            if (!_db.Table<InvoiceData>().Any())
                return;

            List<InvoiceData> missing = _db.Query<InvoiceData>(
                "SELECT * FROM InvoiceData WHERE InvoiceNumber IS NULL OR trim(InvoiceNumber) = ''");
            var recovered = new List<InvoiceData>();
            foreach (InvoiceData invoice in missing)
            {
                if (invoice.FileContent == null || invoice.FileContent.Length == 0)
                    continue;

                InvoiceData parsed = InvoiceParser.ParseInvoice(
                    invoice.FileName,
                    invoice.FileContent);
                if (string.IsNullOrWhiteSpace(parsed.InvoiceNumber))
                    continue;

                invoice.InvoiceNumber = parsed.InvoiceNumber;
                recovered.Add(invoice);
            }
            if (recovered.Count > 0)
                _db.RunInTransaction(() => recovered.ForEach(invoice => _db.Update(invoice)));
            _db.Execute(
                "INSERT OR REPLACE INTO AppMetadata ([Key], [Value]) VALUES (?, ?)",
                metadataKey,
                "1");
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
