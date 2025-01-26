using CommunityToolkit.Mvvm.ComponentModel;

namespace TextPasteApp.Models;

public partial class TextItem : ObservableObject
{
    [ObservableProperty]
    private string _shortName = "New Item";

    [ObservableProperty]
    private string _text = "Text to paste";

    [ObservableProperty]
    private string _textColor = "#000000";

    [ObservableProperty]
    private string _boxColor = "#FFFFFF";

    [ObservableProperty]
    private double _opacity = 1.0;
}
