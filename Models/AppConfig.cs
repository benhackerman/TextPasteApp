using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TextPasteApp.Models;

public partial class AppConfig : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<TextItem> _textItems = new();

    [ObservableProperty]
    private string _hotkey = "Ctrl+K";

    [ObservableProperty]
    private bool _showStartupWindow = true;

    [ObservableProperty]
    private bool _darkModeEnabled;
}