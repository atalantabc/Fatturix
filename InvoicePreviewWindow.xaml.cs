using System.Windows;
using FattureViewer.Models;
using FattureViewer.Services;

namespace FattureViewer
{
    public partial class InvoicePreviewWindow : Window
    {
        public InvoicePreviewWindow(InvoiceData invoice)
        {
            InitializeComponent();
            InvoiceTitle.Text =
                $"{invoice.SenderName} · Fattura {invoice.InvoiceNumber} · " +
                $"{invoice.Date:dd/MM/yyyy}";

            try
            {
                InvoiceDocumentContent content =
                    InvoiceDocumentService.Render(invoice, invoice.FileContent);
                Viewer.HtmlContent = content.Html;
                Viewer.XmlContent = content.Xml;
            }
            catch (System.Exception ex)
            {
                Viewer.XmlContent = "Errore: " + ex.Message;
                Viewer.HtmlContent =
                    "<html><body><h2 style='color:red'>Errore</h2><p>" +
                    System.Net.WebUtility.HtmlEncode(ex.Message) +
                    "</p></body></html>";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
