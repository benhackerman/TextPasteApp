using TextPasteApp.Models;

namespace TextPasteApp.Services;

public interface IConfigurationService
{
    AppConfig Config { get; }
    Task SaveAsync();
    Task LoadAsync();
    Task ExportAsync(string path);
    Task ImportAsync(string path);
}