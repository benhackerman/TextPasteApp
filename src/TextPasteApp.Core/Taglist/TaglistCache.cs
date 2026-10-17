using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Taglist;

/// <summary>
/// Mantiene el taglist en memoria y lo vuelve a leer sólo si el archivo cambió
/// (así se puede editar el Excel y la ruleta se actualiza sola).
/// </summary>
public sealed class TaglistCache
{
    private TaglistSettings _settings = new();
    private DateTime _loadedWriteTime;
    private IReadOnlyList<PickItem> _items = Array.Empty<PickItem>();

    public string? LastError { get; private set; }
    public int Count => _items.Count;

    public void Configure(TaglistSettings settings)
    {
        _settings = settings;
        _loadedWriteTime = default;
        _items = Array.Empty<PickItem>();
        LastError = null;
    }

    /// <summary>Devuelve las filas, recargando si el archivo cambió. Nunca lanza excepciones: el error queda en LastError.</summary>
    public IReadOnlyList<PickItem> GetItems()
    {
        var path = _settings.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return _items = Array.Empty<PickItem>();

        try
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != _loadedWriteTime || _items.Count == 0 && LastError == null)
            {
                _items = TaglistReader.Read(_settings).Select(PickItem.FromTagRow).ToList();
                _loadedWriteTime = writeTime;
                LastError = null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            LastError = ex.Message;
        }
        return _items;
    }
}
