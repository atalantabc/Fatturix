using System.Windows;
using System.Windows.Controls;

namespace FattureViewer
{
    public partial class InvoiceViewerControl : UserControl
    {
        public static readonly DependencyProperty HtmlContentProperty =
            DependencyProperty.Register(
                nameof(HtmlContent),
                typeof(string),
                typeof(InvoiceViewerControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty XmlContentProperty =
            DependencyProperty.Register(
                nameof(XmlContent),
                typeof(string),
                typeof(InvoiceViewerControl),
                new PropertyMetadata(""));

        public InvoiceViewerControl()
        {
            InitializeComponent();
        }

        public string HtmlContent
        {
            get => (string)GetValue(HtmlContentProperty);
            set => SetValue(HtmlContentProperty, value);
        }

        public string XmlContent
        {
            get => (string)GetValue(XmlContentProperty);
            set => SetValue(XmlContentProperty, value);
        }
    }
}
