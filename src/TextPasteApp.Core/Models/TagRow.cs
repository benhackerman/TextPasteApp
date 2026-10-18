namespace TextPasteApp.Core.Models;

/// <summary>Una hoja del taglist ya leída: sus columnas visibles y sus filas.</summary>
public sealed class TaglistSheet
{
    public string Name { get; }
    public string Short { get; }
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Índice (dentro de Columns) de la columna que se pega por defecto.</summary>
    public int PasteColumn { get; }

    /// <summary>Columna que se muestra como título de la fila (TAG / Code / Código; si no hay, la primera que no es la de pegar).</summary>
    public int TitleColumn { get; }

    /// <summary>True si la columna de título se reconoció por su nombre (y no es sólo "la primera que no es la de pegar").</summary>
    public bool HasNamedTitle { get; }

    /// <summary>Columna de descripción (DESCRIPCIÓN / Text_0 / Texto / Nombre / Name), o -1.</summary>
    public int DescriptionColumn { get; }

    public List<TagRow> Rows { get; } = new();

    /// <summary>Columnas que se pegan con Shift+Enter, en orden, separadas por Tab (vacía = sin secuencia).</summary>
    public IReadOnlyList<int> SequenceColumns { get; }

    /// <summary>Tecla entre valores de la secuencia (ver <see cref="SequenceKeys"/>).</summary>
    public string SequenceKey { get; }

    public TaglistSheet(string name, string shortName, IReadOnlyList<string> columns, int pasteColumn,
        IReadOnlyList<int>? sequenceColumns = null, string sequenceKey = "Tab")
    {
        SequenceColumns = sequenceColumns ?? Array.Empty<int>();
        SequenceKey = SequenceKeys.Normalize(sequenceKey);
        Name = name;
        Short = string.IsNullOrWhiteSpace(shortName) ? name : shortName;
        Columns = columns;
        PasteColumn = Math.Clamp(pasteColumn, 0, Math.Max(0, columns.Count - 1));
        TitleColumn = FindColumn(columns, "TAG", "CODE", "CÓDIGO", "CODIGO");
        HasNamedTitle = TitleColumn >= 0 && TitleColumn != PasteColumn;
        if (!HasNamedTitle)
            TitleColumn = Enumerable.Range(0, columns.Count).FirstOrDefault(i => i != PasteColumn);
        DescriptionColumn = FindColumn(columns, "DESCRIP", "TEXT", "NOMBRE", "NAME");
        if (DescriptionColumn == PasteColumn || DescriptionColumn == TitleColumn) DescriptionColumn = -1;
    }

    /// <summary>
    /// True si la fila es un "espacio reservado": en muchos taglists las filas vienen numeradas de antemano
    /// (No, IO) pero sin tag ni texto. Esas filas no se muestran.
    /// </summary>
    public bool IsPlaceholder(IReadOnlyList<string> values)
    {
        string At(int i) => i >= 0 && i < values.Count ? values[i] : "";
        if (HasNamedTitle || DescriptionColumn >= 0)
            return (!HasNamedTitle || At(TitleColumn).Length == 0) && At(DescriptionColumn).Length == 0;
        return Enumerable.Range(0, values.Count).All(i => i == PasteColumn || values[i].Length == 0);
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

    /// <summary>Columnas con contenido (más la de pegar aunque esté vacía). Las vacías no se muestran ni se recorren con la rueda.</summary>
    public IReadOnlyList<int> UsefulColumns =>
        Enumerable.Range(0, Sheet.Columns.Count).Where(i => i == Sheet.PasteColumn || this[i].Length > 0).ToList();

    public string Title => this[Sheet.TitleColumn];
    public string Description => this[Sheet.DescriptionColumn];
    public string PasteValue => this[Sheet.PasteColumn];

    /// <summary>Identifica la fila entre recargas del Excel (para estadísticas y marcas de "hecho").</summary>
    public string Key => $"{Sheet.Name}|{PasteValue}|{Title}";

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

/// <summary>Teclas posibles entre los valores de una secuencia.</summary>
public static class SequenceKeys
{
    public static readonly string[] All = { "Tab", "Enter", "Abajo", "Derecha" };

    public static string Normalize(string? key) =>
        All.FirstOrDefault(k => string.Equals(k, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Tab";

    /// <summary>Código de tecla virtual de Windows.</summary>
    public static int VirtualKey(string key) => Normalize(key) switch
    {
        "Enter" => 0x0D,
        "Abajo" => 0x28,
        "Derecha" => 0x27,
        _ => 0x09,
    };

    /// <summary>Símbolo para mostrar entre columnas: ⇥ ↵ ↓ →.</summary>
    public static string Symbol(string key) => Normalize(key) switch
    {
        "Enter" => "↵",
        "Abajo" => "↓",
        "Derecha" => "→",
        _ => "⇥",
    };
}
