using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Estado del buscador del taglist mientras está abierto.
///
///   - Escribes → se filtran las filas que tengan ese texto en CUALQUIER columna.
///   - ↑ ↓ → cambia de fila.   Rueda → cambia la columna que se va a pegar (parte en la columna por defecto de la hoja).
///   - Tab → siguiente hoja ("Todas" busca en el libro completo).
///   - Enter → pega <see cref="Preview"/>.
/// </summary>
public sealed class TaglistSession
{
    public const int AllSheets = -1;
    public const int MaxResults = 500;

    private readonly IReadOnlyList<TextTemplate> _templates;
    private int _fieldOverride = -1;

    public TaglistBook Book { get; }

    /// <summary>Hoja activa: índice en Book.Sheets, o <see cref="AllSheets"/>.</summary>
    public int SheetIndex { get; private set; }

    public string Query { get; private set; } = "";
    public IReadOnlyList<TagRow> Results { get; private set; } = Array.Empty<TagRow>();
    public int RowIndex { get; private set; }
    public int TemplateIndex { get; private set; }

    public TaglistSession(TaglistBook book, string? startSheet, IReadOnlyList<TextTemplate> templates, int templateIndex)
    {
        Book = book;
        _templates = templates;
        TemplateIndex = templates.Count == 0 ? 0 : Math.Clamp(templateIndex, 0, templates.Count - 1);
        SheetIndex = IndexOfSheet(startSheet);
        Refilter();
    }

    public string SheetName => SheetIndex == AllSheets ? "" : Book.Sheets[SheetIndex].Name;
    public string SheetDisplayName => SheetIndex == AllSheets ? "Todas" : Book.Sheets[SheetIndex].Name;

    public TagRow? Selected => Results.Count > 0 ? Results[Math.Clamp(RowIndex, 0, Results.Count - 1)] : null;

    /// <summary>Columna (de la fila seleccionada) que se va a pegar.</summary>
    public int FieldIndex
    {
        get
        {
            if (Selected is not { } row) return -1;
            var count = row.Sheet.Columns.Count;
            return _fieldOverride >= 0 ? Math.Min(_fieldOverride, count - 1) : row.Sheet.PasteColumn;
        }
    }

    public TextTemplate Template => _templates.Count > 0 ? _templates[TemplateIndex] : new TextTemplate("", "{valor}");

    /// <summary>Texto final que se pega (con la plantilla aplicada), o null si no hay fila.</summary>
    public string? Preview => Selected is { } row ? TemplateEngine.Apply(Template.Pattern, row.Fields(FieldIndex)) : null;

    /// <summary>
    /// Autocompletar: si alguna celda del primer resultado empieza con lo escrito, devuelve lo que falta
    /// (se muestra en gris y se acepta con →).
    /// </summary>
    public string Ghost
    {
        get
        {
            if (Query.Length == 0 || Results.Count == 0) return "";
            var match = Results[0].Values.FirstOrDefault(v => v.Length > Query.Length && v.StartsWith(Query, StringComparison.OrdinalIgnoreCase));
            return match == null ? "" : match[Query.Length..];
        }
    }

    public void SetQuery(string? query)
    {
        Query = query ?? "";
        Refilter();
    }

    public void AcceptGhost()
    {
        var ghost = Ghost;
        if (ghost.Length > 0) SetQuery(Query + ghost);
    }

    public void MoveRow(int steps)
    {
        if (Results.Count == 0) return;
        RowIndex = Math.Clamp(RowIndex + steps, 0, Results.Count - 1);
        _fieldOverride = -1; // cada fila parte en la columna por defecto de su hoja
    }

    public void SelectRow(int index)
    {
        if (index < 0 || index >= Results.Count) return;
        RowIndex = index;
        _fieldOverride = -1;
    }

    /// <summary>Rueda: siguiente / anterior columna de la fila (da la vuelta).</summary>
    public void MoveField(int steps)
    {
        if (Selected is not { } row || row.Sheet.Columns.Count == 0) return;
        var count = row.Sheet.Columns.Count;
        _fieldOverride = ((FieldIndex + steps) % count + count) % count;
    }

    public void SelectField(int index)
    {
        if (Selected is { } row && index >= 0 && index < row.Sheet.Columns.Count) _fieldOverride = index;
    }

    /// <summary>Tab: recorre Todas → hoja 1 → hoja 2 → … → Todas.</summary>
    public void NextSheet(int direction = 1)
    {
        var total = Book.Sheets.Count + 1; // +1 por "Todas"
        var position = SheetIndex + 1;     // Todas = 0
        position = ((position + direction) % total + total) % total;
        SetSheet(position - 1);
    }

    /// <summary>Elige una hoja por índice (o <see cref="AllSheets"/>).</summary>
    public void SetSheet(int index)
    {
        if (index < AllSheets || index >= Book.Sheets.Count) return;
        SheetIndex = index;
        Refilter();
    }

    public void NextTemplate()
    {
        if (_templates.Count > 0) TemplateIndex = (TemplateIndex + 1) % _templates.Count;
    }

    /// <summary>Cuántas coincidencias tiene la búsqueda actual en una hoja (para los contadores de las pestañas).</summary>
    public int CountIn(int sheetIndex) => Search(RowsOf(sheetIndex), TextSearch.Normalize(Query)).Count;

    /// <summary>Filas visibles alrededor de la selección.</summary>
    public (int Start, IReadOnlyList<TagRow> Rows) Window(int visible)
    {
        if (Results.Count == 0) return (0, Array.Empty<TagRow>());
        var start = Math.Clamp(RowIndex - visible / 2 + 1, 0, Math.Max(0, Results.Count - visible));
        return (start, Results.Skip(start).Take(visible).ToList());
    }

    private void Refilter()
    {
        Results = Search(RowsOf(SheetIndex), TextSearch.Normalize(Query));
        RowIndex = 0;
        _fieldOverride = -1;
    }

    private IEnumerable<TagRow> RowsOf(int sheetIndex) => sheetIndex == AllSheets
        ? Book.Sheets.SelectMany(s => s.Rows)
        : Book.Sheets[sheetIndex].Rows;

    /// <summary>
    /// Orden: primero las filas con una celda idéntica a lo buscado, luego las que tienen una celda que empieza igual,
    /// luego las que lo contienen. Dentro de cada grupo, el orden del Excel.
    /// </summary>
    private static IReadOnlyList<TagRow> Search(IEnumerable<TagRow> rows, string q)
    {
        if (q.Length == 0) return rows.Take(MaxResults * 20).ToList();

        var buckets = new[] { new List<TagRow>(), new List<TagRow>(), new List<TagRow>() };
        foreach (var row in rows)
        {
            var best = 3;
            foreach (var cell in row.Normalized)
            {
                if (cell.Length == 0) continue;
                if (cell == q) { best = 0; break; }
                if (best > 1 && cell.StartsWith(q, StringComparison.Ordinal)) best = 1;
                else if (best > 2 && cell.Contains(q, StringComparison.Ordinal)) best = 2;
            }
            if (best < 3) buckets[best].Add(row);
        }
        return buckets.SelectMany(b => b).Take(MaxResults).ToList();
    }

    private int IndexOfSheet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return AllSheets;
        for (var i = 0; i < Book.Sheets.Count; i++)
            if (string.Equals(Book.Sheets[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return Book.Sheets.Count > 0 ? 0 : AllSheets;
    }
}
