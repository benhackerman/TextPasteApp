using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Taglist;

/// <summary>
/// Mantiene el taglist en memoria y lo vuelve a leer sólo si el archivo cambió
/// (así puedes editar el Excel y el buscador se actualiza solo).
/// </summary>
public sealed class TaglistCache
{
    private TaglistProfile _profile = new();
    private DateTime _loadedWriteTime;
    private TaglistBook _book = TaglistBook.Empty;

    public string? LastError { get; private set; }
    public bool HasFile => !string.IsNullOrWhiteSpace(_profile.FilePath);
    public bool HasSheets => _profile.Sheets.Any(s => s.Enabled);

    public void Configure(TaglistProfile profile)
    {
        _profile = profile;
        _loadedWriteTime = default;
        _book = TaglistBook.Empty;
        LastError = null;
    }

    /// <summary>Devuelve el libro, recargando si el archivo cambió. Nunca lanza excepciones: el error queda en LastError.</summary>
    public TaglistBook GetBook()
    {
        if (!HasFile) return _book = TaglistBook.Empty;

        try
        {
            var writeTime = File.GetLastWriteTimeUtc(_profile.FilePath);
            if (writeTime != _loadedWriteTime)
            {
                _book = TaglistReader.ReadBook(_profile);
                _loadedWriteTime = writeTime;
                LastError = null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            LastError = ex.Message;
            _loadedWriteTime = default; // reintentar la próxima vez
        }
        return _book;
    }
}
