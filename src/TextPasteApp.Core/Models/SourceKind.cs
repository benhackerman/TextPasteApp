namespace TextPasteApp.Core.Models;

/// <summary>De dónde salen los textos que muestra la ruleta.</summary>
public enum SourceKind
{
    History,
    Favorites,
    Taglist,
}

public static class SourceKindExtensions
{
    public static string DisplayName(this SourceKind kind) => kind switch
    {
        SourceKind.History => "Historial",
        SourceKind.Favorites => "Favoritos",
        SourceKind.Taglist => "Taglist",
        _ => kind.ToString(),
    };
}
