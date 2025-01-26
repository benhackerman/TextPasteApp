using System.IO;
using System.Text.Json;
using TextPasteApp.Models;

namespace TextPasteApp.Services;

public class JsonConfigurationService : IConfigurationService
{
    private const string CONFIG_PATH = "config.json";
    public AppConfig Config { get; private set; } = new();

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Config);
        await File.WriteAllTextAsync(CONFIG_PATH, json);
    }

    public async Task LoadAsync()
    {
        if (File.Exists(CONFIG_PATH))
        {
            var json = await File.ReadAllTextAsync(CONFIG_PATH);
            Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new();
        }
    }

    public async Task ExportAsync(string path) => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Config));

    public async Task ImportAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new();
        await SaveAsync();
    }
}