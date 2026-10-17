using System.Text;
using System.Text.RegularExpressions;

namespace TextPasteApp.Core.Logic;

public enum AwlTokenKind
{
    Text,        // espacios, comas, nombres de parámetros formales, palabras clave de bloque…
    Instruction, // U, UN, O, L, T, CALL, SPB, ==I, A, AN, JC… (alemán e inglés)
    Variable,    // M 10.0, MW 20, E 0.1, DB12.DBX0.0, T 5, Z 3, FB 100, "Símbolo", #temp…
    Constant,    // 10, W#16#FF, S5T#2S, 'texto', TRUE…
    Label,       // M001:
    Comment,     // // comentario
    Parameter,   // {{nombre}} de TextPaste
}

public readonly record struct AwlToken(AwlTokenKind Kind, string Text)
{
    /// <summary>Clave de la variable: mayúsculas y sin espacios ("M 10.0" y "m10.0" son la misma).</summary>
    public string Key => Kind == AwlTokenKind.Variable ? AwlParser.KeyOf(Text) : "";
}

/// <summary>Una variable encontrada en el código, cuántas veces aparece y cómo se escribió la primera vez.</summary>
public sealed record AwlVariable(string Key, string Display, int Count);

/// <summary>
/// Reconoce en código AWL (STL de Siemens) qué es instrucción y qué es variable, para poder cambiar una
/// variable en todo el bloque de una vez. Trabaja línea por línea:
///   [etiqueta:] instrucción operando   // comentario
///   Parámetro :=operando                (líneas de un CALL)
/// La primera palabra de la línea es la instrucción; lo que sigue son operandos. Así se resuelven los
/// casos ambiguos: "L" (cargar / área local), "T" (transferir / temporizador), "A" (Y en inglés / salida).
/// </summary>
public static partial class AwlParser
{
    private static readonly HashSet<string> Instructions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Lógica de bits (alemán / inglés)
        "U", "UN", "O", "ON", "X", "XN", "A", "AN", "=", "S", "R", "FP", "FN", "NOT", "SET", "CLR", "SAVE",
        "U(", "UN(", "O(", "ON(", "X(", "XN(", "A(", "AN(", ")",
        // Carga y transferencia
        "L", "T", "LAR1", "LAR2", "TAR1", "TAR2", "TAK", "PUSH", "POP", "ENT", "LEAVE", "INC", "DEC", "LC",
        // Aritmética y comparación
        "+I", "-I", "*I", "/I", "+D", "-D", "*D", "/D", "MOD", "+R", "-R", "*R", "/R", "+",
        "ABS", "SQR", "SQRT", "EXP", "LN", "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN",
        "==I", "<>I", ">I", "<I", ">=I", "<=I", "==D", "<>D", ">D", "<D", ">=D", "<=D",
        "==R", "<>R", ">R", "<R", ">=R", "<=R",
        // Conversión, palabras, desplazamientos
        "BTI", "ITB", "BTD", "DTB", "DTR", "ITD", "INVI", "INVD", "NEGI", "NEGD", "NEGR", "CAW", "CAD",
        "RND", "RND+", "RND-", "TRUNC", "SLW", "SRW", "SLD", "SRD", "SSI", "SSD", "RLD", "RRD", "RLDA", "RRDA",
        "UW", "OW", "XOW", "UD", "OD", "XOD", "AW", "OW", "XOW", "AD", "XOD",
        // Saltos (alemán / inglés)
        "SPA", "SPB", "SPBN", "SPBB", "SPBNB", "SPBI", "SPBIN", "SPO", "SPS", "SPZ", "SPN", "SPP", "SPM",
        "SPPZ", "SPMZ", "SPU", "SPL", "LOOP",
        "JU", "JC", "JCN", "JCB", "JNB", "JBI", "JNBI", "JO", "JOS", "JZ", "JN", "JP", "JM", "JPZ", "JMZ", "JUO", "JL",
        // Bloques
        "CALL", "UC", "CC", "BE", "BEA", "BEB", "BEU", "BEC", "AUF", "OPN", "TDB", "CDB", "NOP", "BLD", "MCRA", "MCRD", "MCR(", ")MCR",
        // Temporizadores y contadores
        "SI", "SV", "SE", "SS", "SA", "SP", "SD", "SF", "ZV", "ZR", "CU", "CD", "FR",
    };

    private static readonly HashSet<string> BlockKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "NETWORK", "TITLE", "FUNCTION", "FUNCTION_BLOCK", "ORGANIZATION_BLOCK", "DATA_BLOCK", "TYPE", "STRUCT",
        "END_STRUCT", "END_TYPE", "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP", "END_VAR",
        "BEGIN", "END_FUNCTION", "END_FUNCTION_BLOCK", "END_ORGANIZATION_BLOCK", "END_DATA_BLOCK", "VERSION",
        "AUTHOR", "FAMILY", "NAME", "KNOW_HOW_PROTECT",
    };

    // Operandos, del más específico al más general. El espacio entre área y número es opcional (M 10.0 = M10.0).
    [GeneratedRegex(@"\G(?:" +
        @"""[^""\r\n]*""" +                                         // "Símbolo"
        @"|\#[A-Za-z_][\w.]*" +                                     // #local
        @"|DB\s*\d+\s*\.\s*DB[XBWD]\s*\d+(?:\.\d)?" +               // DB12.DBX0.0
        @"|D[BI][XBWD]\s*\d+(?:\.\d)?" +                            // DBX0.0, DIW2
        @"|P?(?:E|I|A|Q)[BWD]\s*\d+" +                              // EW 2, PIW 256, AB 4
        @"|(?:E|I|A|Q|M|L)\s*\d+\.\d" +                             // E 0.0, A 4.1, M 10.0, L 0.0
        @"|(?:M|L)[BWD]\s*\d+" +                                    // MW 20, LD 4
        @"|(?:SFB|SFC|FB|FC|DB|DI|UDT|OB)\s*\d+" +                  // FB 100, DB12
        @"|(?:T|Z|C)\s*\d+(?![\d.#])" +                             // T 5, Z 3, C 3
        @")", RegexOptions.IgnoreCase)]
    private static partial Regex OperandAt();

    [GeneratedRegex(@"\G(?:" +
        @"'(?:[^'\r\n]|'')*'" +                                     // 'texto'
        @"|P\#(?:[A-Z]{1,3}\s*)?\d+\.\d" +                          // P#DBX 0.0, P#M 10.0
        @"|(?:DW|W|B|L|S5T|T|D|TOD|DT|C|P|S5TIME|TIME|DATE)\#[\w.:\-+#]+" +
        @"|\d+\#[0-9A-Fa-f_]+" +                                    // 2#1010, 16#FF
        @"|[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?" +
        @"|TRUE\b|FALSE\b" +
        @")", RegexOptions.IgnoreCase)]
    private static partial Regex ConstantAt();

    [GeneratedRegex(@"\G\{\{[^{}]+\}\}")]
    private static partial Regex ParameterAt();

    [GeneratedRegex(@"^\s*[A-Za-z_][\w]{0,3}:(?!=)")]
    private static partial Regex LabelAtStart();

    [GeneratedRegex(@"^\s*[A-Za-z_][\w.]*\s*:=")]
    private static partial Regex FormalParameterAtStart();

    [GeneratedRegex(@"\G(?:[A-Za-z_][\w]*\(?|==[IDR]|<>[IDR]|>=[IDR]|<=[IDR]|>[IDR]|<[IDR]|[+\-*/][IDR]|RND[+-]|\)MCR|\)|=|\+)", RegexOptions.IgnoreCase)]
    private static partial Regex MnemonicAt();

    public static string KeyOf(string variable)
    {
        if (variable.StartsWith('"')) return variable;               // los símbolos respetan mayúsculas
        return Regex.Replace(variable, @"\s+", "").ToUpperInvariant();
    }

    public static IReadOnlyList<AwlToken> Tokenize(string? code)
    {
        var tokens = new List<AwlToken>();
        if (string.IsNullOrEmpty(code)) return tokens;

        var lines = Regex.Split(code, @"(\r\n|\n|\r)");
        foreach (var part in lines)
        {
            if (part is "\r\n" or "\n" or "\r") { tokens.Add(new(AwlTokenKind.Text, part)); continue; }
            TokenizeLine(part, tokens);
        }
        return Merge(tokens);
    }

    private static void TokenizeLine(string line, List<AwlToken> tokens)
    {
        // Comentario
        var body = line;
        var comment = "";
        var commentAt = FindComment(line);
        if (commentAt >= 0) { body = line[..commentAt]; comment = line[commentAt..]; }

        var pos = 0;
        var trimmed = body.TrimStart();
        var firstWord = Regex.Match(trimmed, @"^[A-Za-z_]+").Value;

        var isAssignment = FormalParameterAtStart().IsMatch(body);
        if (!isAssignment && (BlockKeywords.Contains(firstWord) || trimmed.Contains(':') && trimmed.TrimEnd().EndsWith(';')))
        {
            // Líneas de declaración / estructura del bloque: sólo se reconocen parámetros y símbolos.
            ScanOperands(body, ref pos, tokens, allowBareWords: false, declarations: true);
        }
        else
        {
            var label = LabelAtStart().Match(body);
            if (label.Success)
            {
                var lead = body[..(label.Length - label.Value.TrimStart().Length)];
                if (lead.Length > 0) tokens.Add(new(AwlTokenKind.Text, lead));
                tokens.Add(new(AwlTokenKind.Label, label.Value.TrimStart()));
                pos = label.Length;
            }

            var formal = FormalParameterAtStart().Match(body[pos..]);
            if (formal.Success)
            {
                tokens.Add(new(AwlTokenKind.Text, formal.Value)); // "Cmd :=" (nombre del parámetro formal)
                pos += formal.Length;
            }
            else
            {
                SkipSpaces(body, ref pos, tokens);
                if (ParameterAt().Match(body, pos) is { Success: true } p0)
                {
                    tokens.Add(new(AwlTokenKind.Parameter, p0.Value));
                    pos += p0.Length;
                }
                else if (MnemonicAt().Match(body, pos) is { Success: true } m && Instructions.Contains(m.Value))
                {
                    tokens.Add(new(AwlTokenKind.Instruction, m.Value));
                    pos += m.Length;
                }
            }
            ScanOperands(body, ref pos, tokens, allowBareWords: true, declarations: false);
        }

        if (comment.Length > 0) tokens.Add(new(AwlTokenKind.Comment, comment));
    }

    /// <summary>Recorre el resto de la línea reconociendo parámetros, variables y constantes.</summary>
    private static void ScanOperands(string body, ref int pos, List<AwlToken> tokens, bool allowBareWords, bool declarations)
    {
        while (pos < body.Length)
        {
            if (ParameterAt().Match(body, pos) is { Success: true } p)
            {
                tokens.Add(new(AwlTokenKind.Parameter, p.Value));
                pos += p.Length;
                continue;
            }

            var previous = pos == 0 ? ' ' : body[pos - 1];
            var atWordStart = !char.IsLetterOrDigit(previous) && previous != '_' && previous != '.';
            if (atWordStart && !declarations && OperandAt().Match(body, pos) is { Success: true } v && EndsWord(body, pos + v.Length))
            {
                tokens.Add(new(AwlTokenKind.Variable, v.Value));
                pos += v.Length;
                continue;
            }
            if (atWordStart && declarations && body[pos] == '"' && OperandAt().Match(body, pos) is { Success: true } sym)
            {
                tokens.Add(new(AwlTokenKind.Variable, sym.Value));
                pos += sym.Length;
                continue;
            }
            if (atWordStart && !declarations && ConstantAt().Match(body, pos) is { Success: true } c && c.Length > 0 && EndsWord(body, pos + c.Length))
            {
                tokens.Add(new(AwlTokenKind.Constant, c.Value));
                pos += c.Length;
                continue;
            }

            // Palabra suelta (etiqueta de salto como destino, nombre…) o símbolo de puntuación: texto.
            var start = pos;
            if (char.IsLetterOrDigit(body[pos]) || body[pos] == '_')
                while (pos < body.Length && (char.IsLetterOrDigit(body[pos]) || body[pos] == '_' || body[pos] == '.')) pos++;
            else pos++;
            tokens.Add(new(AwlTokenKind.Text, body[start..pos]));
        }
    }

    private static bool EndsWord(string s, int index) =>
        index >= s.Length || !(char.IsLetterOrDigit(s[index]) || s[index] == '_');

    private static void SkipSpaces(string body, ref int pos, List<AwlToken> tokens)
    {
        var start = pos;
        while (pos < body.Length && char.IsWhiteSpace(body[pos])) pos++;
        if (pos > start) tokens.Add(new(AwlTokenKind.Text, body[start..pos]));
    }

    /// <summary>Posición de "//" fuera de textos '…' y símbolos "…", o -1.</summary>
    private static int FindComment(string line)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var ch = line[i];
            if (ch == '\'' && !inDouble) inSingle = !inSingle;
            else if (ch == '"' && !inSingle) inDouble = !inDouble;
            else if (!inSingle && !inDouble && ch == '/' && line[i + 1] == '/') return i;
        }
        return -1;
    }

    /// <summary>Une textos seguidos en uno solo (más fácil de dibujar).</summary>
    private static IReadOnlyList<AwlToken> Merge(List<AwlToken> tokens)
    {
        var result = new List<AwlToken>(tokens.Count);
        var text = new StringBuilder();
        foreach (var t in tokens)
        {
            if (t.Kind == AwlTokenKind.Text) { text.Append(t.Text); continue; }
            if (text.Length > 0) { result.Add(new(AwlTokenKind.Text, text.ToString())); text.Clear(); }
            result.Add(t);
        }
        if (text.Length > 0) result.Add(new(AwlTokenKind.Text, text.ToString()));
        return result;
    }

    // ───────────────────────── Variables ─────────────────────────

    /// <summary>
    /// Variables del código en el orden en que aparecen. Además de cada operando, si se usan accesos a un DB
    /// (DB12.DBX0.0) se agrega el DB solo ("DB12"): cambiarlo cambia el número de DB en todos sus accesos.
    /// </summary>
    public static IReadOnlyList<AwlVariable> Variables(string? code)
    {
        var order = new List<string>();
        var display = new Dictionary<string, string>();
        var count = new Dictionary<string, int>();

        void Add(string key, string text)
        {
            if (!count.ContainsKey(key)) { order.Add(key); display[key] = text; count[key] = 0; }
            count[key]++;
        }

        foreach (var token in Tokenize(code))
        {
            if (token.Kind != AwlTokenKind.Variable) continue;
            Add(token.Key, token.Text);
            if (DbOf(token.Key) is { } db && db != token.Key) Add(db, db);
        }
        return order.Select(k => new AwlVariable(k, display[k], count[k])).ToList();
    }

    /// <summary>
    /// Reemplaza una variable (por su clave) en todo el código. Si la clave es un DB ("DB12"), también cambia
    /// el número en los accesos completos (DB12.DBX0.0 → DB13.DBX0.0).
    /// </summary>
    public static string ReplaceVariable(string? code, string key, string replacement) =>
        ReplaceVariables(code, new Dictionary<string, string> { [key] = replacement });

    public static string ReplaceVariables(string? code, IReadOnlyDictionary<string, string> replacements)
    {
        if (string.IsNullOrEmpty(code) || replacements.Count == 0) return code ?? "";
        var map = replacements.Where(r => r.Value.Length > 0)
            .ToDictionary(r => KeyOf(r.Key), r => r.Value);
        var sb = new StringBuilder(code.Length);
        foreach (var token in Tokenize(code))
        {
            if (token.Kind == AwlTokenKind.Variable)
            {
                if (map.TryGetValue(token.Key, out var direct)) { sb.Append(direct); continue; }
                if (DbOf(token.Key) is { } db && db != token.Key && map.TryGetValue(db, out var newDb))
                {
                    var dot = token.Text.IndexOf('.');
                    sb.Append(newDb.Trim()).Append(token.Text[dot..]);
                    continue;
                }
            }
            sb.Append(token.Text);
        }
        return sb.ToString();
    }

    /// <summary>"DB12.DBX0.0" → "DB12"; "DB12" → "DB12"; otra cosa → null.</summary>
    private static string? DbOf(string key)
    {
        var m = Regex.Match(key, @"^(DB\d+)(?:\.|$)");
        return m.Success ? m.Groups[1].Value : null;
    }
}
