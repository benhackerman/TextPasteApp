using System.Globalization;
using System.Text.RegularExpressions;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Reemplaza marcadores dentro de una plantilla.
///
///   {valor}        valor a pegar (número de actuador, o el texto copiado)
///   {tag}          tag del P&amp;ID
///   {desc}         descripción
///   {texto}        el texto tal cual
///   {Columna}      cualquier columna del Excel por su encabezado
///   {valor:0000}   formato numérico: 23 → 0023
///   {{ y }}        llaves literales
///
/// Los marcadores desconocidos se dejan igual para que se note el error.
/// </summary>
public static partial class TemplateEngine
{
    [GeneratedRegex(@"\{\{|\}\}|\{([^{}:]+)(?::([^{}]*))?\}")]
    private static partial Regex PlaceholderRegex();

    public static string Apply(string pattern, IReadOnlyDictionary<string, string> fields)
    {
        if (string.IsNullOrEmpty(pattern))
            return fields.TryGetValue("valor", out var v) ? v : "";

        return PlaceholderRegex().Replace(pattern, m =>
        {
            if (m.Value == "{{") return "{";
            if (m.Value == "}}") return "}";

            var name = m.Groups[1].Value.Trim();
            if (!TryGet(fields, name, out var value))
                return m.Value;

            var format = m.Groups[2].Success ? m.Groups[2].Value : null;
            return Format(value, format);
        });
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> fields, string name, out string value)
    {
        if (fields.TryGetValue(name, out value!))
            return true;

        // Por si el diccionario no ignora mayúsculas.
        foreach (var (k, v) in fields)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }
        value = "";
        return false;
    }

    private static string Format(string value, string? format)
    {
        if (string.IsNullOrEmpty(format))
            return value;

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            try { return number.ToString(format, CultureInfo.InvariantCulture); }
            catch (FormatException) { return value; }
        }
        return value;
    }
}
