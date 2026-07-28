using System.Windows;
using System.Windows.Controls;

namespace FattureViewer
{
    public static class WebBrowserHelper
    {
        private const string EmptyDocument = "<html><body></body></html>";

        public static readonly DependencyProperty BodyProperty =
            DependencyProperty.RegisterAttached(
                "Body",
                typeof(string),
                typeof(WebBrowserHelper),
                new PropertyMetadata(EmptyDocument, OnBodyChanged));

        public static string GetBody(DependencyObject dependencyObject)
        {
            return (string)dependencyObject.GetValue(BodyProperty);
        }

        public static void SetBody(DependencyObject dependencyObject, string body)
        {
            dependencyObject.SetValue(BodyProperty, body);
        }

        private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WebBrowser webBrowser)
            {
                var body = e.NewValue as string;
                webBrowser.NavigateToString(string.IsNullOrWhiteSpace(body) ? EmptyDocument : body);
            }
        }
    }
}
