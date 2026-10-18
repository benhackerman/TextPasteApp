using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Estado del buscador de funciones: filtro, selección y valores de los parámetros.
/// Los parámetros se proponen así: 1) columna de la última fila elegida en el taglist con el mismo nombre
/// ({{No}}, {{Code}}, {{Text_0}}, {{valor}}, {{tag}}, {{desc}}…), 2) último valor que escribiste, 3) vacío.
/// </summary>
public sealed class FunctionSession
{
    public const int MaxResults = 300;

    private readonly IReadOnlyList<CodeFunction> _all;
    private readonly IReadOnlyDictionary<string, string> _lastValues;
    private readonly IReadOnlyDictionary<string, string> _rowFields;
    private readonly Dictionary<string, string> _typed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _variables = new();
    private CodeFunction? _variablesFor;

    public string Query { get; private set; } = "";
    public IReadOnlyList<CodeFunction> Results { get; private set; } = Array.Empty<CodeFunction>();
    public int Index { get; private set; }

    /// <summary>Descripción de la fila del taglist que se usa para rellenar (o null).</summary>
    public string? RowLabel { get; }

    public FunctionSession(IReadOnlyList<CodeFunction> functions, IReadOnlyDictionary<string, string>? lastValues = null,
        TagRow? row = null)
    {
        _all = functions;
        _lastValues = lastValues ?? new Dictionary<string, string>();
        _rowFields = row?.Fields(row.Sheet.PasteColumn) ?? new Dictionary<string, string>();
        RowLabel = row == null ? null : $"{row.Sheet.Short} {row.PasteValue} {row.Title}".Trim();
        Refilter();
    }

    public CodeFunction? Selected => Results.Count > 0 ? Results[Math.Clamp(Index, 0, Results.Count - 1)] : null;

    public IReadOnlyList<string> Parameters => Selected is { } f ? CodeTemplate.ParameterNames(f.Code) : Array.Empty<string>();

    /// <summary>Variables AWL reconocidas en el código de la función elegida (M 10.0, DB12, "Símbolo"…).</summary>
    public IReadOnlyList<AwlVariable> Variables => Selected is { } f ? AwlParser.Variables(f.Code) : Array.Empty<AwlVariable>();

    /// <summary>Valor actual de una variable: el que escribiste o el original.</summary>
    public string VariableValue(AwlVariable variable)
    {
        SyncVariables();
        return _variables.TryGetValue(variable.Key, out var v) ? v : variable.Display.Trim();
    }

    public bool IsVariableChanged(AwlVariable variable)
    {
        SyncVariables();
        return _variables.TryGetValue(variable.Key, out var v) && AwlParser.KeyOf(v) != variable.Key;
    }

    /// <summary>Cambia una variable en todo el código de la función.</summary>
    public void SetVariable(string key, string value)
    {
        SyncVariables();
        _variables[key] = value;
    }

    /// <summary>Los cambios de variables valen para la función elegida; al elegir otra se empieza de cero.</summary>
    private void SyncVariables()
    {
        if (ReferenceEquals(_variablesFor, Selected)) return;
        _variables.Clear();
        _variablesFor = Selected;
    }

    /// <summary>Valor actual de un parámetro (escrito, de la fila del taglist o el último usado).</summary>
    public string ValueOf(string parameter)
    {
        if (_typed.TryGetValue(parameter, out var typed)) return typed;
        if (_rowFields.TryGetValue(parameter, out var fromRow) && fromRow.Length > 0) return fromRow;
        return _lastValues.TryGetValue(parameter, out var last) ? last : "";
    }

    /// <summary>¿El valor propuesto viene de la fila del taglist?</summary>
    public bool IsFromRow(string parameter) =>
        !_typed.ContainsKey(parameter) && _rowFields.TryGetValue(parameter, out var v) && v.Length > 0;

    public void SetValue(string parameter, string value) => _typed[parameter] = value;

    /// <summary>Valores usados (para recordarlos la próxima vez).</summary>
    public IReadOnlyDictionary<string, string> CurrentValues =>
        Parameters.ToDictionary(p => p, ValueOf, StringComparer.OrdinalIgnoreCase);

    /// <summary>El código final con los parámetros rellenados.</summary>
    public string? Preview
    {
        get
        {
            if (Selected is not { } f) return null;
            SyncVariables();
            var changed = _variables.Where(v => v.Value.Trim().Length > 0).ToDictionary(v => v.Key, v => v.Value.Trim());
            return CodeTemplate.Fill(AwlParser.ReplaceVariables(f.Code, changed), CurrentValues);
        }
    }

    public void SetQuery(string? query)
    {
        Query = query ?? "";
        Refilter();
    }

    public void Move(int steps)
    {
        if (Results.Count == 0) return;
        Index = Math.Clamp(Index + steps, 0, Results.Count - 1);
    }

    public void Select(int index)
    {
        if (index >= 0 && index < Results.Count) Index = index;
    }

    /// <summary>Busca en nombre y categoría primero, después en descripción y código.</summary>
    private void Refilter()
    {
        var q = TextSearch.Normalize(Query);
        if (q.Length == 0)
        {
            Results = _all.OrderBy(f => f.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxResults).ToList();
        }
        else
        {
            var scored = new List<(CodeFunction F, int Score)>();
            foreach (var f in _all)
            {
                var name = TextSearch.Normalize(f.Name);
                var score =
                    name == q ? 0 :
                    name.StartsWith(q, StringComparison.Ordinal) ? 1 :
                    name.Contains(q, StringComparison.Ordinal) ? 2 :
                    TextSearch.Normalize(f.Category).Contains(q, StringComparison.Ordinal) ? 3 :
                    TextSearch.Normalize(f.Description).Contains(q, StringComparison.Ordinal) ? 4 :
                    TextSearch.Normalize(f.Code).Contains(q, StringComparison.Ordinal) ? 5 : -1;
                if (score >= 0) scored.Add((f, score));
            }
            Results = scored.OrderBy(x => x.Score).Select(x => x.F).Take(MaxResults).ToList();
        }
        Index = 0;
    }
}
