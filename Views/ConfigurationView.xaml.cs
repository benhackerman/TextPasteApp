using System.Windows;
using TextPasteApp.ViewModels;

namespace TextPasteApp.Views
{
    public partial class ConfigurationView : Window
    {
        public ConfigurationView(ConfigurationViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}