using TextPasteApp.Core.Logic;

namespace TextPasteApp.Core.Models;

/// <summary>
/// Un elemento elegible en la ruleta, sin importar de qué fuente venga.
/// Fields son los valores que puede usar una plantilla: {valor}, {tag}, {desc}, {texto} y las columnas del Excel.
/// </summary>
public sealed class PickItem
{
    /// <summary>Texto principal (nombre corto o tag).</summary>
    public string Title { get; }

    /// <summary>Texto secundario (el valor o el texto completo).</summary>
    public string Subtitle { get; }

    public string Description { get; }

    public IReadOnlyDictionary<string, string> Fields { get; }

    internal string NormalizedTitle { get; }
    internal string NormalizedAll { get; }

    public PickItem(string title, string subtitle, string description, IReadOnlyDictionary<string, string> fields)
    {
        Title = title;
        Subtitle = subtitle;
        Description = description;
        Fields = fields;
        NormalizedTitle = TextSearch.Normalize(title);
        NormalizedAll = TextSearch.Normalize($"{title} {subtitle} {description}");
    }

    public static PickItem FromSnippet(Snippet s)
    {
        var title = string.IsNullOrWhiteSpace(s.Label) ? FirstLine(s.Text) : s.Label;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["valor"] = s.Text,
            ["texto"] = s.Text,
            ["tag"] = title,
            ["desc"] = "",
        };
        return new PickItem(title, s.Text, "", fields);
    }

    public static PickItem FromTagRow(TagRow row)
    {
        var fields = new Dictionary<string, string>(row.Cells, StringComparer.OrdinalIgnoreCase)
        {
            ["valor"] = row.Value,
            ["tag"] = row.Tag,
            ["desc"] = row.Description,
            ["texto"] = row.Value,
        };
        return new PickItem(row.Tag, row.Value, row.Description, fields);
    }

    private static string FirstLine(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 60 ? line[..60] + "…" : line;
    }

    public override string ToString() => Title;
}
