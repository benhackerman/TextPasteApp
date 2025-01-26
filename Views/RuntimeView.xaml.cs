using System.Windows;
using TextPasteApp.ViewModels;

namespace TextPasteApp.Views
{
    public partial class RuntimeView : Window
    {
        public RuntimeView(RuntimeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}