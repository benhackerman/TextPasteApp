namespace TextPasteApp.Core.Models;

/// <summary>
/// Una "función" guardada: un trozo de código (AWL u otro) para volver a pegarlo.
/// Puede tener parámetros {{nombre}} que se rellenan al pegar.
/// </summary>
public sealed class CodeFunction
{
    public string Name { get; set; } = "";

    /// <summary>Para agrupar y buscar: "Actuadores", "Alarmas", "Comunicación"…</summary>
    public string Category { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>El código tal cual se pega. Parámetros: {{nombre}}.</summary>
    public string Code { get; set; } = "";

    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>Lo que se guarda en functions.json: las funciones y los últimos valores usados en cada parámetro.</summary>
public sealed class FunctionLibrary
{
    public List<CodeFunction> Functions { get; set; } = new();

    /// <summary>Último valor escrito para cada parámetro (se propone la próxima vez).</summary>
    public Dictionary<string, string> LastValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
