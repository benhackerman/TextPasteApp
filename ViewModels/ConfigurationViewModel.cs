using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPasteApp.Models;

namespace TextPasteApp.ViewModels
{
    public partial class ConfigurationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _hotkey = "Ctrl+K";

        [ObservableProperty]
        private bool _darkModeEnabled;

        [RelayCommand]
        private void Save()
        {
            // Lógica para guardar la configuración
        }
    }
}