namespace TextPasteApp.Core.Models;

/// <summary>Un texto guardado: del historial o de la lista de favoritos.</summary>
public sealed class Snippet
{
    /// <summary>Nombre corto opcional que se muestra en la ruleta. Si está vacío se usa el texto.</summary>
    public string Label { get; set; } = "";

    /// <summary>Texto que se pega.</summary>
    public string Text { get; set; } = "";

    /// <summary>Fijado: siempre aparece primero en el historial y no se borra al limpiarlo.</summary>
    public bool Pinned { get; set; }

    public Snippet() { }

    public Snippet(string text, string label = "")
    {
        Text = text;
        Label = label;
    }
}
