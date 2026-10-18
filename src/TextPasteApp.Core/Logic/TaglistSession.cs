using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>Lo que el buscador necesita saber de tu uso: filas recientes y filas ya hechas.</summary>
public sealed class TaglistContext
{
    public static readonly TaglistContext Empty = new();

    /// <summary>Claves de fila (TagRow.Key) usadas hace poco, la más nueva primero. Sin texto escrito, salen arriba.</summary>
    public IReadOnlyList<string> RecentKeys { get; init; } = Array.Empty<string>();

    /// <summary>¿La fila ya está parametrizada? (se muestra con ✓).</summary>
    public Func<string, bool> IsDone { get; init; } = _ => false;
}

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
    private readonly TaglistContext _context;
    private int _fieldOverride = -1;

    public TaglistBook Book { get; }

    /// <summary>Hoja activa: índice en Book.Sheets, o <see cref="AllSheets"/>.</summary>
    public int SheetIndex { get; private set; }

    public string Query { get; private set; } = "";
    public IReadOnlyList<TagRow> Results { get; private set; } = Array.Empty<TagRow>();
    public int RowIndex { get; private set; }
    public int TemplateIndex { get; private set; }

    /// <summary>
    /// True si en la hoja elegida no había coincidencias y se está mostrando el resultado de todas las hojas
    /// (así no tienes que adivinar en qué hoja está un tag).
    /// </summary>
    public bool IsAutoAll { get; private set; }

    public TaglistSession(TaglistBook book, string? startSheet, IReadOnlyList<TextTemplate> templates, int templateIndex,
        TaglistContext? context = null)
    {
        Book = book;
        _templates = templates;
        _context = context ?? TaglistContext.Empty;
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
    /// Valores de la secuencia (Shift+Enter) de la fila elegida, o null si la hoja no tiene secuencia.
    /// La plantilla se aplica a la columna de pegar.
    /// </summary>
    public IReadOnlyList<string>? SequenceValues
    {
        get
        {
            if (Selected is not { } row || row.Sheet.SequenceColumns.Count == 0) return null;
            return row.Sheet.SequenceColumns
                .Select(c => c == row.Sheet.PasteColumn ? TemplateEngine.Apply(Template.Pattern, row.Fields(c)) : row[c])
                .ToList();
        }
    }

    public bool IsDone(TagRow row) => _context.IsDone(row.Key);

    /// <summary>Filas hechas / total en la hoja elegida (o en todo el libro con «Todas»).</summary>
    public (int Done, int Total) Progress()
    {
        var rows = RowsOf(SheetIndex).ToList();
        return (rows.Count(IsDone), rows.Count);
    }

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

    /// <summary>Rueda: siguiente / anterior columna con contenido de la fila (da la vuelta).</summary>
    public void MoveField(int steps)
    {
        if (Selected is not { } row) return;
        var useful = row.UsefulColumns;
        if (useful.Count == 0) return;
        var position = Math.Max(0, IndexOf(useful, FieldIndex));
        _fieldOverride = useful[((position + steps) % useful.Count + useful.Count) % useful.Count];
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
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
    public int CountIn(int sheetIndex) => Query.Length == 0
        ? RowsOf(sheetIndex).Count()
        : Search(RowsOf(sheetIndex), TextSearch.Normalize(Query)).Count;

    /// <summary>Filas visibles alrededor de la selección.</summary>
    public (int Start, IReadOnlyList<TagRow> Rows) Window(int visible)
    {
        if (Results.Count == 0) return (0, Array.Empty<TagRow>());
        var start = Math.Clamp(RowIndex - visible / 2 + 1, 0, Math.Max(0, Results.Count - visible));
        return (start, Results.Skip(start).Take(visible).ToList());
    }

    private void Refilter()
    {
        var q = TextSearch.Normalize(Query);
        Results = q.Length == 0 ? RecentFirst(RowsOf(SheetIndex)) : Search(RowsOf(SheetIndex), q);
        IsAutoAll = false;
        if (Results.Count == 0 && q.Length > 0 && SheetIndex != AllSheets)
        {
            var everywhere = Search(RowsOf(AllSheets), q);
            if (everywhere.Count > 0)
            {
                Results = everywhere;
                IsAutoAll = true;
            }
        }
        RowIndex = 0;
        _fieldOverride = -1;
    }

    /// <summary>Sin texto escrito: primero las filas que usaste hace poco, después el resto en el orden del Excel.</summary>
    private IReadOnlyList<TagRow> RecentFirst(IEnumerable<TagRow> rows)
    {
        var list = rows.Take(MaxResults * 20).ToList();
        if (_context.RecentKeys.Count == 0) return list;
        var byKey = new Dictionary<string, TagRow>();
        foreach (var row in list) byKey.TryAdd(row.Key, row);
        var recent = _context.RecentKeys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        var set = new HashSet<TagRow>(recent);
        return recent.Concat(list.Where(r => !set.Contains(r))).ToList();
    }

    public bool IsRecent(TagRow row) => Query.Length == 0 && _context.RecentKeys.Contains(row.Key);

    private IEnumerable<TagRow> RowsOf(int sheetIndex) => sheetIndex == AllSheets
        ? Book.Sheets.SelectMany(s => s.Rows)
        : Book.Sheets[sheetIndex].Rows;

    /// <summary>
    /// Orden: celda idéntica → celda que empieza igual → celda que lo contiene. En cada nivel van primero
    /// las coincidencias en el tag (Code) o el número (columna a pegar) y después las de otras columnas
    /// (descripción, etc.). Dentro de cada grupo, el orden del Excel.
    /// </summary>
    private static IReadOnlyList<TagRow> Search(IEnumerable<TagRow> rows, string q)
    {
        if (q.Length == 0) return rows.Take(MaxResults * 20).ToList();

        var buckets = new List<TagRow>[6];
        for (var i = 0; i < buckets.Length; i++) buckets[i] = new List<TagRow>();

        foreach (var row in rows)
        {
            var best = int.MaxValue;
            for (var c = 0; c < row.Normalized.Length && best > 0; c++)
            {
                var cell = row.Normalized[c];
                if (cell.Length == 0) continue;
                var level = cell == q ? 0 : cell.StartsWith(q, StringComparison.Ordinal) ? 1 : cell.Contains(q, StringComparison.Ordinal) ? 2 : -1;
                if (level < 0) continue;
                var key = level * 2 + (c == row.Sheet.TitleColumn || c == row.Sheet.PasteColumn ? 0 : 1);
                best = Math.Min(best, key);
            }
            if (best != int.MaxValue) buckets[best].Add(row);
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
