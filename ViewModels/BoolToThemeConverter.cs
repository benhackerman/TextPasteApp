using System.Globalization;
using System.Windows.Data;

namespace TextPasteApp.ViewModels;

public class BoolToThemeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? "🌙 Dark Mode" : "☀️ Light Mode";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}