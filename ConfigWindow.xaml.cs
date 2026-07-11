using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FattureViewer
{
    public partial class ConfigWindow : Window
    {
        private string _configPath;
        private bool _isHighlighting = false;

        public ConfigWindow()
        {
            InitializeComponent();
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            LoadConfig();
        }

        private void LoadConfig()
        {
            string content = "";
            if (File.Exists(_configPath))
            {
                content = File.ReadAllText(_configPath);
            }
            else
            {
                // Default config
                content = "// File di configurazione generato automaticamente\r\n" +
                          "PASSIVE_DIR \"Fatture_Passive\"\r\n" +
                          "COPY \"*.xml\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"\r\n" +
                          "COPY \"*.p7m\" TO \"Fatture_Passive\" RENAME \"{year}_{month}_{filename}\"\r\n";
            }

            SetText(content);
        }

        private void SetText(string text)
        {
            _isHighlighting = true;
            EditorBox.Document.Blocks.Clear();
            EditorBox.Document.Blocks.Add(new Paragraph(new Run(text)));
            HighlightSyntax();
            _isHighlighting = false;
        }

        private string GetText()
        {
            TextRange textRange = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            return textRange.Text;
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isHighlighting) return;
            
            _isHighlighting = true;
            HighlightSyntax();
            _isHighlighting = false;
        }

        private void HighlightSyntax()
        {
            string[] keywords = { "PASSIVE_DIR", "ZIP_WORK_DIR", "CREATE_DIR", "COPY", "MOVE", "TO", "RENAME" };

            TextPointer pointer = EditorBox.Document.ContentStart;

            while (pointer != null)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = pointer.GetTextInRun(LogicalDirection.Forward);
                    
                    // Simple keyword matching
                    int index = -1;
                    string foundKeyword = null;
                    foreach (var kw in keywords)
                    {
                        int idx = textRun.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                        if (idx != -1 && (index == -1 || idx < index))
                        {
                            index = idx;
                            foundKeyword = kw;
                        }
                    }

                    if (index != -1)
                    {
                        TextPointer startPos = pointer.GetPositionAtOffset(index);
                        TextPointer endPos = startPos.GetPositionAtOffset(foundKeyword.Length);
                        
                        TextRange range = new TextRange(startPos, endPos);
                        range.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Blue);
                        range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                        
                        pointer = endPos;
                        continue;
                    }
                }
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                File.WriteAllText(_configPath, GetText().TrimEnd('\r', '\n'));
                MessageBox.Show("Configurazione salvata con successo.", "Salvataggio", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il salvataggio: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
