namespace TextPasteApp.Core.Models;

/// <summary>Una fila del taglist (Excel/CSV).</summary>
public sealed class TagRow
{
    /// <summary>Tag del P&amp;ID, por ejemplo "XV-1023". Es lo que se busca.</summary>
    public string Tag { get; init; } = "";

    /// <summary>Valor que normalmente se pega, por ejemplo el número de actuador o módulo.</summary>
    public string Value { get; init; } = "";

    /// <summary>Descripción opcional que ayuda a reconocer la fila.</summary>
    public string Description { get; init; } = "";

    /// <summary>Todas las celdas de la fila, por nombre de columna. Se pueden usar en plantillas: {NombreColumna}.</summary>
    public IReadOnlyDictionary<string, string> Cells { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
