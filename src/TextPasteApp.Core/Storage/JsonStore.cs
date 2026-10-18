using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextPasteApp.Core.Storage;

/// <summary>
/// Guarda y lee archivos JSON en una carpeta (por defecto %AppData%\TextPasteApp).
/// Si un archivo está corrupto, lo respalda como .bad y devuelve valores por defecto en vez de fallar.
/// </summary>
public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes legibles en el archivo
        Converters = { new JsonStringEnumConverter() },
    };

    public string Folder { get; }

    public JsonStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextPasteApp");
        Directory.CreateDirectory(Folder);
    }

    public string PathFor(string name) => Path.Combine(Folder, name);

    public bool Exists(string name) => File.Exists(PathFor(name));

    public T Load<T>(string name) where T : new()
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            File.Copy(path, path + ".bad", overwrite: true);
            return new T();
        }
    }

    /// <summary>Exporta un objeto a un archivo cualquiera (por ejemplo un perfil de taglist para compartir).</summary>
    public static void ExportFile<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));

    /// <summary>Importa un objeto desde un archivo exportado. Lanza JsonException si el archivo no es válido.</summary>
    public static T ImportFile<T>(string path) where T : new() =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();

    public void Save<T>(string name, T value)
    {
        var path = PathFor(name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true); // escritura atómica: nunca queda un archivo a medias
    }
}
