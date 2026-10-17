namespace TextPasteApp.Core.Models;

/// <summary>
/// Todo lo que la app recuerda de tu uso (se guarda en usage.json): cuántas veces pegaste cada cosa,
/// totales por día y qué filas del taglist ya parametrizaste en cada proyecto.
/// Se guarda agregado (no un registro de cada pegado) para que el archivo siga chico con los años.
/// </summary>
public sealed class UsageData
{
    public DateTime Since { get; set; } = DateTime.Now;

    /// <summary>Uso por elemento. Clave: "T|proyecto|fila" para el taglist, "R|texto" para la ruleta.</summary>
    public Dictionary<string, ItemUsage> Items { get; set; } = new();

    /// <summary>Totales por día. Clave: "yyyy-MM-dd".</summary>
    public Dictionary<string, DayUsage> Days { get; set; } = new();

    /// <summary>Filas marcadas como hechas, por proyecto (clave de fila: hoja|valor|tag).</summary>
    public Dictionary<string, HashSet<string>> Done { get; set; } = new();
}

public sealed class ItemUsage
{
    /// <summary>"Taglist", "Historial", "Favoritos" o "Funciones".</summary>
    public string Source { get; set; } = "";
    public string Project { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string RowKey { get; set; } = "";

    /// <summary>Lo que se ve en las estadísticas (tag o texto corto).</summary>
    public string Label { get; set; } = "";

    /// <summary>Último texto pegado.</summary>
    public string Text { get; set; } = "";

    public int Count { get; set; }
    public DateTime FirstUsed { get; set; }
    public DateTime LastUsed { get; set; }
}

public sealed class DayUsage
{
    public int Pastes { get; set; }
    public int TaglistPastes { get; set; }
    public int RingPastes { get; set; }
    public int FunctionPastes { get; set; }
    public int SecondsSaved { get; set; }
}
