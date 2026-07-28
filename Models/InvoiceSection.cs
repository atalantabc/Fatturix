namespace FattureViewer.Models
{
    public enum InvoiceSection
    {
        Suppliers = 0,
        Customers = 1
    }

    public static class InvoiceSectionExtensions
    {
        public const string SuppliersValue = "Fornitori";
        public const string CustomersValue = "Clienti";

        public static string ToStorageValue(this InvoiceSection section)
        {
            return section == InvoiceSection.Customers
                ? CustomersValue
                : SuppliersValue;
        }
    }
}
