using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TextPasteApp.Core.Models;

/// <summary>
/// "Plantilla de taglist": describe cómo es tu Excel (qué hojas, dónde están los encabezados,
/// qué columna se pega). Se configura una vez; en un proyecto nuevo sólo cambias el archivo.
/// </summary>
public sealed class TaglistProfile
{
    public string Name { get; set; } = "Mi taglist";

    /// <summary>
    /// Archivo de ejemplo para «Detectar hojas». El archivo de cada proyecto se elige en <see cref="TaglistProject"/>.
    /// (En versiones anteriores era el único archivo; se usa para crear el primer proyecto.)
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Columnas que se ocultan en TODAS las hojas, separadas por coma (por ejemplo "Text_1, Text_2" cuando
    /// el taglist tiene los textos en varios idiomas y sólo usas Text_0). No se muestran ni se buscan.
    /// </summary>
    public string HiddenColumns { get; set; } = "";

    public List<SheetProfile> Sheets { get; set; } = new();
}

/// <summary>Configuración de una hoja del taglist (Actuadores, Entradas digitales, PID…).</summary>
public sealed class SheetProfile : INotifyPropertyChanged
{
    private string _sheetName = "";
    private string _pasteFormats = "";

    /// <summary>
    /// Avisa a las tablas de Configuración cuando cambia el formato, así el ejemplo se actualiza solo
    /// (sin Items.Refresh, que falla si la tabla está en medio de una edición).
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Si es false, la hoja no se carga ni aparece en la búsqueda.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nombre exacto de la hoja en el Excel.</summary>
    public string SheetName
    {
        get => _sheetName;
        set { _sheetName = value ?? ""; Changed(nameof(SheetName)); Changed(nameof(Example)); Changed(nameof(DisplayShort)); }
    }

    /// <summary>Nombre corto que se muestra en las etiquetas (ACT, DI, AI…). Vacío = nombre de la hoja.</summary>
    public string ShortName { get; set; } = "";

    /// <summary>Fila donde están los encabezados (empieza en 1).</summary>
    public int HeaderRow { get; set; } = 1;

    /// <summary>Columna que se selecciona por defecto para pegar. Nombre del encabezado o letra (A, B…).</summary>
    public string PasteColumn { get; set; } = "A";

    /// <summary>Columnas a mostrar, separadas por coma (nombre o letra). Vacío = todas las que tengan encabezado.</summary>
    public string VisibleColumns { get; set; } = "";

    /// <summary>
    /// Secuencia para llenar un formulario de una pasada (Shift+Enter): columnas separadas por coma.
    /// Ejemplo: "No, Text_0, Unit Assignment" → pega No, Tab, Text_0, Tab, Unit Assignment.
    /// </summary>
    public string Sequence { get; set; } = "";

    /// <summary>
    /// Formato al pegar la columna principal de esta hoja. Varios separados por "|" (el primero es el de
    /// por defecto; F2 cambia al siguiente). Ejemplos: "Act{valor:0000}" → Act0023;
    /// "PID{valor:0000} | PID{valor:0000}.st" → PID0005 o PID0005.st. Vacío = se usan las plantillas generales.
    /// </summary>
    public string PasteFormats
    {
        get => _pasteFormats;
        set { _pasteFormats = value ?? ""; Changed(nameof(PasteFormats)); Changed(nameof(Example)); }
    }

    /// <summary>Tecla entre los valores de la secuencia: "Tab" (por defecto), "Enter", "Abajo" o "Derecha".</summary>
    public string SequenceKey { get; set; } = "Tab";

    /// <summary>Cómo queda el formato con el número 23 (para ver el resultado al configurarlo).</summary>
    [JsonIgnore]
    public string Example
    {
        get
        {
            var formats = TaglistSheet.ParseFormats(PasteFormats);
            if (formats.Count == 0) return "23 (sin formato)";
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["valor"] = "23", ["texto"] = "23", ["hoja"] = SheetName, ["tag"] = "TAG", ["desc"] = "Descripción",
            };
            return string.Join("  |  ", formats.Select(f => Logic.TemplateEngine.Apply(f, fields)));
        }
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    [JsonIgnore]
    public string DisplayShort => string.IsNullOrWhiteSpace(ShortName) ? SheetName : ShortName;
}

/// <summary>
/// Un proyecto: qué archivo de taglist usar y con qué perfil. Se elige solo según la ventana donde estás
/// trabajando (palabras clave en el título) o según el Excel que tengas abierto.
/// </summary>
public sealed class TaglistProject
{
    public string Name { get; set; } = "Proyecto";

    /// <summary>Ruta al taglist (.xlsx / .xlsm / .xls / .csv).</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Nombre del perfil (estructura del taglist) que usa este proyecto.</summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Palabras clave separadas por coma. Si el título de la ventana donde vas a pegar contiene alguna,
    /// se usa este proyecto. Ejemplo: "Envasado, Planta Vino".
    /// </summary>
    public string WindowMatch { get; set; } = "";
}
