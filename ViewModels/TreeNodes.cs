using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FattureViewer.Models;

namespace FattureViewer.ViewModels
{
    public class TreeItemNode : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class YearNode : TreeItemNode
    {
        public int Year { get; set; }
        public ObservableCollection<MonthNode> Months { get; set; } = new ObservableCollection<MonthNode>();
    }

    public class MonthNode : TreeItemNode
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public ObservableCollection<InvoiceNode> Invoices { get; set; } = new ObservableCollection<InvoiceNode>();
    }

    public class CompanyNode : TreeItemNode
    {
        public ObservableCollection<InvoiceNode> Invoices { get; set; } = new ObservableCollection<InvoiceNode>();
    }

    public class InvoiceNode : TreeItemNode
    {
        public InvoiceData Data { get; set; }
    }
}
