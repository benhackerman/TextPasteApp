using System.Text.Json;

namespace TextPasteApp.Core.Models;

/// <summary>Toda la configuración de la app. Se guarda como settings.json en %AppData%\TextPasteApp.</summary>
public sealed class AppSettings
{
    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>Cuántos textos copiados se recuerdan.</summary>
    public int HistoryLimit { get; set; } = 50;

    /// <summary>Guardar el historial entre sesiones.</summary>
    public bool PersistHistory { get; set; } = true;

    /// <summary>Después de pegar, devolver al portapapeles lo que había antes.</summary>
    public bool RestoreClipboardAfterPaste { get; set; } = true;

    /// <summary>Un toque rápido del atajo deja la ruleta abierta con buscador.</summary>
    public bool TapToStick { get; set; } = true;

    /// <summary>Cantidad de sectores de la ruleta por página.</summary>
    public int SlicesPerPage { get; set; } = 8;

    /// <summary>Fuente que se muestra al abrir (se recuerda la última usada).</summary>
    public SourceKind LastSource { get; set; } = SourceKind.History;

    public List<Snippet> Favorites { get; set; } = new()
    {
        new Snippet("ON", "ON"),
        new Snippet("OFF", "OFF"),
    };

    public TaglistSettings Taglist { get; set; } = new();

    public List<TextTemplate> Templates { get; set; } = new()
    {
        new TextTemplate("Sin plantilla", "{valor}"),
        new TextTemplate("Módulo de control", "MC_{valor}"),
        new TextTemplate("Comando", "PLC1.MC_{valor}.CMD"),
    };

    public int ActiveTemplateIndex { get; set; }

    /// <summary>Copia independiente (para editar en la ventana de configuración sin tocar la original).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();
}

/// <summary>
/// Atajo de teclado. Se mantiene presionado para abrir la ruleta y se suelta para pegar.
/// Key es el nombre de una tecla de WPF (System.Windows.Input.Key), por ejemplo "Space" o "V".
/// </summary>
public sealed class HotkeySettings
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "Space";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

/// <summary>Dónde está el taglist y qué columnas usar.</summary>
public sealed class TaglistSettings
{
    /// <summary>Ruta al .xlsx / .xlsm / .xls / .csv.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Nombre de la hoja. Vacío = la primera.</summary>
    public string SheetName { get; set; } = "";

    /// <summary>Fila donde están los encabezados (empieza en 1).</summary>
    public int HeaderRow { get; set; } = 1;

    /// <summary>Columna con el tag del P&amp;ID. Nombre del encabezado o letra (A, B, ...).</summary>
    public string TagColumn { get; set; } = "A";

    /// <summary>Columna con el valor a pegar (número de actuador / módulo).</summary>
    public string ValueColumn { get; set; } = "B";

    /// <summary>Columna con la descripción (opcional).</summary>
    public string DescriptionColumn { get; set; } = "";
}
