using System.Text.RegularExpressions;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Parámetros dentro del código de una función: {{nombre}}.
/// Se usan llaves dobles porque AWL usa $ en sus textos ('$N', '$$') y casi nunca llaves dobles.
/// </summary>
public static partial class CodeTemplate
{
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Parameter();

    /// <summary>Nombres de los parámetros en el orden en que aparecen (sin repetir).</summary>
    public static IReadOnlyList<string> ParameterNames(string code)
    {
        var names = new List<string>();
        foreach (Match m in Parameter().Matches(code ?? ""))
        {
            var name = m.Groups[1].Value;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
        return names;
    }

    /// <summary>Reemplaza cada {{nombre}} por su valor. Los que no tienen valor quedan igual (para que se note).</summary>
    public static string Fill(string code, IReadOnlyDictionary<string, string> values)
    {
        var filled = Parameter().Replace(code ?? "", m =>
        {
            var name = m.Groups[1].Value;
            foreach (var (k, v) in values)
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
            return m.Value;
        });
        return NormalizeNewLines(filled);
    }

    /// <summary>Los editores de Windows (TIA Portal, Step 7) esperan CRLF.</summary>
    public static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
}
