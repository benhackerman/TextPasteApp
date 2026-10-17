namespace TextPasteApp.Core.Models;

/// <summary>
/// Plantilla que envuelve el texto elegido antes de pegarlo.
/// Ejemplo: "PLC1.MC_{valor}.CMD" con valor = 1023 pega "PLC1.MC_1023.CMD".
/// Ver <see cref="Logic.TemplateEngine"/> para la sintaxis completa.
/// </summary>
public sealed class TextTemplate
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "{valor}";

    public TextTemplate() { }

    public TextTemplate(string name, string pattern)
    {
        Name = name;
        Pattern = pattern;
    }
}
