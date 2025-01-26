using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TextPasteApp.ViewModels
{
    public partial class AddNewTextItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _shortName = "New Item";

        [ObservableProperty]
        private string _text = "Text to paste";

        [RelayCommand]
        private void Save()
        {
            // Lógica para guardar el nuevo TextItem
        }
    }
}