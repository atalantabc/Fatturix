using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class DatabaseService
    {
        private SQLiteConnection _db;

        public DatabaseService()
        {
            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FattureViewer", "fatture.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            
            _db = new SQLiteConnection(dbPath);
            _db.CreateTable<InvoiceData>();
        }

        public void SaveInvoice(InvoiceData invoice)
        {
            var existing = _db.Table<InvoiceData>().FirstOrDefault(i => i.Id == invoice.Id || i.FileName == invoice.FileName);
            if (existing != null)
            {
                invoice.Id = existing.Id;
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
            return _db.Table<InvoiceData>().OrderByDescending(i => i.Date).ToList();
        }

        public List<InvoiceData> SearchInvoices(int? year = null, int? month = null, int? day = null, string companyName = null)
        {
            var query = _db.Table<InvoiceData>();

            if (year.HasValue && year.Value > 0)
                query = query.Where(i => i.Year == year.Value);
            
            if (month.HasValue && month.Value > 0)
                query = query.Where(i => i.Month == month.Value);
            
            if (day.HasValue && day.Value > 0)
                query = query.Where(i => i.Day == day.Value);

            var result = query.ToList();

            if (!string.IsNullOrWhiteSpace(companyName))
            {
                string lowerQuery = companyName.ToLower();
                result = result.Where(i => !string.IsNullOrEmpty(i.SenderName) && i.SenderName.ToLower().Contains(lowerQuery)).ToList();
            }

            return result.OrderByDescending(i => i.Date).ToList();
        }
        
        public void ClearAll()
        {
            _db.DeleteAll<InvoiceData>();
        }
    }
}
