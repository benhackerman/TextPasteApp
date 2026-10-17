using System.Globalization;
using System.Text;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Búsqueda tolerante: ignora mayúsculas, tildes, espacios, guiones, puntos y guiones bajos.
/// Así "xv1023" encuentra "XV-1023".
/// </summary>
public static class TextSearch
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.' or '/') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Filtra y ordena: primero coincidencia exacta del título, luego los que empiezan igual,
    /// luego los que lo contienen en el título y al final los que lo contienen en cualquier campo.
    /// Dentro de cada grupo se respeta el orden original.
    /// </summary>
    public static IReadOnlyList<PickItem> Filter(IReadOnlyList<PickItem> items, string? query, int max = 1000)
    {
        var q = Normalize(query);
        if (q.Length == 0) return items;

        var exact = new List<PickItem>();
        var starts = new List<PickItem>();
        var inTitle = new List<PickItem>();
        var anywhere = new List<PickItem>();

        foreach (var item in items)
        {
            if (item.NormalizedTitle == q) exact.Add(item);
            else if (item.NormalizedTitle.StartsWith(q, StringComparison.Ordinal)) starts.Add(item);
            else if (item.NormalizedTitle.Contains(q, StringComparison.Ordinal)) inTitle.Add(item);
            else if (item.NormalizedAll.Contains(q, StringComparison.Ordinal)) anywhere.Add(item);
        }

        return exact.Concat(starts).Concat(inTitle).Concat(anywhere).Take(max).ToList();
    }
}
