namespace TextPasteApp.Core.Models;

/// <summary>Una hoja del taglist ya leída: sus columnas visibles y sus filas.</summary>
public sealed class TaglistSheet
{
    public string Name { get; }
    public string Short { get; }
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Índice (dentro de Columns) de la columna que se pega por defecto.</summary>
    public int PasteColumn { get; }

    /// <summary>Columna que se muestra como título de la fila (la que contiene "TAG"; si no hay, la primera que no es la de pegar).</summary>
    public int TitleColumn { get; }

    /// <summary>Columna de descripción (encabezado con "DESCRIP", "TEXTO" o "NOMBRE"), o -1.</summary>
    public int DescriptionColumn { get; }

    public List<TagRow> Rows { get; } = new();

    public TaglistSheet(string name, string shortName, IReadOnlyList<string> columns, int pasteColumn)
    {
        Name = name;
        Short = string.IsNullOrWhiteSpace(shortName) ? name : shortName;
        Columns = columns;
        PasteColumn = Math.Clamp(pasteColumn, 0, Math.Max(0, columns.Count - 1));
        TitleColumn = FindColumn(columns, "TAG");
        if (TitleColumn < 0 || TitleColumn == PasteColumn && columns.Count > 1)
            TitleColumn = Enumerable.Range(0, columns.Count).FirstOrDefault(i => i != PasteColumn);
        DescriptionColumn = FindColumn(columns, "DESCRIP", "TEXTO", "NOMBRE");
    }

    /// <summary>Agrega una fila (los valores en el mismo orden que Columns).</summary>
    public TagRow Add(params string[] values)
    {
        var row = new TagRow(this, values);
        Rows.Add(row);
        return row;
    }

    private static int FindColumn(IReadOnlyList<string> columns, params string[] keys)
    {
        foreach (var key in keys)
            for (var i = 0; i < columns.Count; i++)
                if (columns[i].Contains(key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}

/// <summary>Una fila del taglist.</summary>
public sealed class TagRow
{
    public TaglistSheet Sheet { get; }
    public IReadOnlyList<string> Values { get; }
    internal string[] Normalized { get; }

    public TagRow(TaglistSheet sheet, string[] values)
    {
        Sheet = sheet;
        Values = values;
        Normalized = values.Select(Logic.TextSearch.Normalize).ToArray();
    }

    public string this[int column] => column >= 0 && column < Values.Count ? Values[column] : "";

    public string Title => this[Sheet.TitleColumn];
    public string Description => this[Sheet.DescriptionColumn];
    public string PasteValue => this[Sheet.PasteColumn];

    /// <summary>Valores para la plantilla: {valor} = columna elegida, {tag}, {desc} y cada columna por su encabezado.</summary>
    public IReadOnlyDictionary<string, string> Fields(int selectedColumn)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Sheet.Columns.Count; i++)
            fields.TryAdd(Sheet.Columns[i], this[i]);
        fields["valor"] = this[selectedColumn];
        fields["texto"] = this[selectedColumn];
        fields["tag"] = Title;
        fields["desc"] = Description;
        fields["hoja"] = Sheet.Name;
        return fields;
    }

    public override string ToString() => $"{Sheet.Short}: {string.Join(" | ", Values)}";
}

/// <summary>El taglist completo: todas las hojas habilitadas del perfil.</summary>
public sealed class TaglistBook
{
    public static readonly TaglistBook Empty = new(Array.Empty<TaglistSheet>());

    public IReadOnlyList<TaglistSheet> Sheets { get; }
    public int RowCount => Sheets.Sum(s => s.Rows.Count);

    public TaglistBook(IReadOnlyList<TaglistSheet> sheets) => Sheets = sheets;
}
