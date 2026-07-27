using System;
using SQLite;

namespace FattureViewer.Models
{
    public class InvoiceData
    {
        [PrimaryKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string FileName { get; set; } = string.Empty;
        public string OriginalFilePath { get; set; } = string.Empty;

        // Il documento originale viene conservato nel database, così la
        // visualizzazione non dipende dalla cartella usata durante l'importazione.
        public byte[] FileContent { get; set; } = Array.Empty<byte>();
        
        public DateTime Date { get; set; }
        
        [Indexed]
        public int Year { get; set; }
        
        [Indexed]
        public int Month { get; set; }
        
        [Indexed]
        public int Day { get; set; }
        
        [Indexed]
        public string CompanyName { get; set; } = string.Empty;

        public string SenderName { get; set; } = string.Empty;
        public string RecipientCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }

        [Ignore]
        public bool IsValidInvoice => Year > 1 && Month > 0 && Day > 0 && !string.IsNullOrWhiteSpace(SenderName);
    }
}
