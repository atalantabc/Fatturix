using System.Windows;
using System.Windows.Controls;

namespace FattureViewer
{
    public static class WebBrowserHelper
    {
        public static readonly DependencyProperty BodyProperty =
            DependencyProperty.RegisterAttached("Body", typeof(string), typeof(WebBrowserHelper), new PropertyMetadata(OnBodyChanged));

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
                webBrowser.NavigateToString((string)e.NewValue ?? "<html><body></body></html>");
            }
        }
    }
}
