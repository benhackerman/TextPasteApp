namespace TextPasteApp.Core.Models;

/// <summary>Fuentes de la ruleta. El taglist tiene su propia ventana con buscador.</summary>
public enum SourceKind
{
    History,
    Favorites,
}

public static class SourceKindExtensions
{
    public static string DisplayName(this SourceKind kind) => kind switch
    {
        SourceKind.History => "Historial",
        SourceKind.Favorites => "Favoritos",
        _ => kind.ToString(),
    };
}
