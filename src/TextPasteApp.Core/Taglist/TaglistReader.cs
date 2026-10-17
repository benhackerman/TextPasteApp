using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Taglist;

/// <summary>
/// Lee el taglist desde Excel (.xlsx, .xlsm, .xls) o CSV, con todas sus hojas.
/// Abre el archivo en modo compartido, así funciona aunque esté abierto en Excel.
/// </summary>
public static partial class TaglistReader
{
    static TaglistReader()
    {
        // Necesario para los .xls antiguos y CSV con codificaciones de Windows.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Nombres de las hojas del archivo.</summary>
    public static IReadOnlyList<string> ListSheets(string path)
    {
        using var stream = OpenShared(path);
        using var reader = CreateReader(path, stream);
        var names = new List<string>();
        do { names.Add(SheetName(reader, names.Count)); } while (reader.NextResult());
        return names;
    }

    /// <summary>Encabezados de una hoja (para llenar combos en la configuración).</summary>
    public static IReadOnlyList<string> ReadHeaders(string path, string sheetName, int headerRow)
    {
        using var stream = OpenShared(path);
        using var reader = CreateReader(path, stream);
        var index = 0;
        do
        {
            if (string.IsNullOrWhiteSpace(sheetName) || SameName(SheetName(reader, index), sheetName))
                return ReadHeaderRow(reader, headerRow);
            index++;
        } while (reader.NextResult());
        return Array.Empty<string>();
    }

    /// <summary>
    /// Revisa el archivo y propone un perfil por hoja: adivina la fila de encabezados
    /// (la primera con 2 o más textos) y la columna a pegar (la que empieza con N°, Nº, NUM, ID…).
    /// </summary>
    public static List<SheetProfile> DetectSheets(string path) => Detect(path).Sheets;

    /// <summary>
    /// Igual que <see cref="DetectSheets"/>, y además sugiere columnas a ocultar: si hay textos en varios
    /// idiomas (Text_0, Text_1, Text_2…) propone ocultar todos menos Text_0.
    /// </summary>
    public static (List<SheetProfile> Sheets, string SuggestedHiddenColumns) Detect(string path)
    {
        var allHeaders = new List<string>();
        var sheets = DetectSheets(path, allHeaders);
        var hasText0 = allHeaders.Any(h => h.Equals("Text_0", StringComparison.OrdinalIgnoreCase));
        var languages = allHeaders
            .Where(h => LanguageColumn().IsMatch(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (sheets, hasText0 ? string.Join(", ", languages) : "");
    }

    [GeneratedRegex(@"^Text_[1-9]$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageColumn();

    private static List<SheetProfile> DetectSheets(string path, List<string> allHeaders)
    {
        using var stream = OpenShared(path);
        using var reader = CreateReader(path, stream);
        var result = new List<SheetProfile>();
        var index = 0;
        do
        {
            var name = SheetName(reader, index++);
            var (headerRow, headers) = GuessHeader(reader);
            if (headers.Count == 0) continue; // hoja vacía
            allHeaders.AddRange(headers);

            var numberColumn = headers.FindIndex(h => NumberHeader().IsMatch(h.Trim()));
            var paste = Math.Max(0, numberColumn);
            var profile = new SheetProfile
            {
                SheetName = name,
                ShortName = GuessShortName(name),
                HeaderRow = headerRow,
                PasteColumn = headers[paste].Length > 0 ? headers[paste] : "A",
            };

            // Quedan desmarcadas las hojas sin datos (sólo encabezados o filas reservadas)
            // y las que no tienen columna de número (suelen ser de configuración, no de módulos).
            var rows = ReadRows(reader, name, profile, headers, Array.Empty<string>()).Rows.Count;
            profile.Enabled = rows > 0 && numberColumn >= 0;
            result.Add(profile);
        } while (reader.NextResult());
        return result;
    }

    /// <summary>Lee todas las hojas habilitadas del perfil.</summary>
    public static TaglistBook ReadBook(TaglistProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.FilePath))
            return TaglistBook.Empty;
        if (!File.Exists(profile.FilePath))
            throw new FileNotFoundException("No se encontró el taglist", profile.FilePath);

        var wanted = profile.Sheets.Where(s => s.Enabled).ToList();
        var hidden = profile.HiddenColumns.Split(',', ';').Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();
        var sheets = new List<TaglistSheet>();

        using var stream = OpenShared(profile.FilePath);
        using var reader = CreateReader(profile.FilePath, stream);
        var index = 0;
        do
        {
            var name = SheetName(reader, index++);
            var config = wanted.FirstOrDefault(s => SameName(s.SheetName, name));
            if (config != null) sheets.Add(ReadRows(reader, name, config, ReadHeaderRow(reader, config.HeaderRow), hidden));
        } while (reader.NextResult());

        // Mismo orden que en el perfil (así Ctrl+1…9 siempre apunta a la misma hoja).
        var ordered = wanted
            .Select(w => sheets.FirstOrDefault(s => SameName(s.Name, w.SheetName)))
            .Where(s => s != null)
            .Cast<TaglistSheet>()
            .ToList();
        return new TaglistBook(ordered);
    }

    /// <summary>Lee las filas de datos (el lector ya pasó la fila de encabezados).</summary>
    private static TaglistSheet ReadRows(IExcelDataReader reader, string name, SheetProfile config, List<string> headers,
        IReadOnlyCollection<string> hiddenColumns)
    {
        // Columnas a mostrar: las pedidas, o todas las que tengan encabezado.
        var columns = new List<int>();
        if (!string.IsNullOrWhiteSpace(config.VisibleColumns))
        {
            foreach (var spec in config.VisibleColumns.Split(',', ';'))
            {
                var i = ResolveColumn(headers, spec);
                if (i >= 0 && !columns.Contains(i)) columns.Add(i);
            }
        }
        if (columns.Count == 0)
            columns = Enumerable.Range(0, headers.Count).Where(i => headers[i].Length > 0).ToList();

        // Columnas ocultas del perfil (aplican a todas las hojas).
        columns.RemoveAll(i => i < headers.Count && hiddenColumns.Contains(headers[i], StringComparer.OrdinalIgnoreCase));

        var pasteIndex = ResolveColumn(headers, config.PasteColumn);
        if (pasteIndex >= 0 && !columns.Contains(pasteIndex)) columns.Insert(0, pasteIndex);
        if (columns.Count == 0) columns.Add(Math.Max(0, pasteIndex));

        // Secuencia (Shift+Enter): columnas por nombre o letra; si alguna estaba oculta se agrega igual.
        var sequence = new List<int>();
        foreach (var spec in config.Sequence.Split(',', ';'))
        {
            var i = ResolveColumn(headers, spec);
            if (i < 0) continue;
            if (!columns.Contains(i)) columns.Add(i);
            sequence.Add(columns.IndexOf(i));
        }

        var names = columns.Select(i => i < headers.Count && headers[i].Length > 0 ? headers[i] : ColumnLetter(i)).ToList();
        var sheet = new TaglistSheet(name, config.ShortName, names, Math.Max(0, columns.IndexOf(pasteIndex)), sequence);

        while (reader.Read())
        {
            var values = columns.Select(i => i < reader.FieldCount ? CellText(reader.GetValue(i)) : "").ToArray();
            if (values.All(v => v.Length == 0) || sheet.IsPlaceholder(values)) continue; // vacías o reservadas
            sheet.Add(values);
        }
        return sheet;
    }

    /// <summary>
    /// Convierte lo que escribió el usuario en un índice de columna (0 = A).
    /// Primero busca por nombre de encabezado; si no, lo interpreta como letra de Excel (A, B, ..., AA).
    /// </summary>
    public static int ResolveColumn(IReadOnlyList<string> headers, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return -1;
        var s = spec.Trim();

        for (var i = 0; i < headers.Count; i++)
            if (string.Equals(headers[i], s, StringComparison.OrdinalIgnoreCase)) return i;

        if (s.Length <= 3 && s.All(char.IsAsciiLetter))
        {
            var index = 0;
            foreach (var c in s.ToUpperInvariant()) index = index * 26 + (c - 'A' + 1);
            return index - 1;
        }
        return -1;
    }

    public static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        for (index++; index > 0; index = (index - 1) / 26)
            sb.Insert(0, (char)('A' + (index - 1) % 26));
        return sb.ToString();
    }

    /// <summary>Texto de una celda. Los números enteros salen sin decimales (1023 y no 1023.0).</summary>
    public static string CellText(object? value) => value switch
    {
        null => "",
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? "",
    };

    /// <summary>"Actuadores" → "ACT"; "Entradas digitales" → "ED".</summary>
    public static string GuessShortName(string sheetName)
    {
        var words = sheetName.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        if (words.Length == 1) return words[0].Length <= 4 ? words[0].ToUpperInvariant() : words[0][..3].ToUpperInvariant();
        return string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
    }

    // Encabezados de número: "N°", "Nº", "N#", "No", "No.", "NRO", "NUM…", "N.", "ID".
    [GeneratedRegex(@"^(N\s*[°º#]|NO\.?$|NO\s|NRO\b|NUM|N\.|ID\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberHeader();

    private static (int Row, List<string> Headers) GuessHeader(IExcelDataReader reader)
    {
        for (var row = 1; row <= 15 && reader.Read(); row++)
        {
            var cells = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++) cells.Add(CellText(reader.GetValue(i)));
            var filled = cells.Where(c => c.Length > 0).ToList();
            var texts = filled.Count(c => !double.TryParse(c, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
            if (filled.Count >= 2 && texts * 2 >= filled.Count)
            {
                while (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
                return (row, cells);
            }
        }
        return (1, new List<string>());
    }

    private static List<string> ReadHeaderRow(IExcelDataReader reader, int headerRow)
    {
        for (var r = 1; r < Math.Max(1, headerRow); r++)
            if (!reader.Read()) return new List<string>();
        if (!reader.Read()) return new List<string>();

        var headers = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++) headers.Add(CellText(reader.GetValue(i)));
        return headers;
    }

    private static string SheetName(IExcelDataReader reader, int index) =>
        string.IsNullOrWhiteSpace(reader.Name) ? $"Hoja{index + 1}" : reader.Name;

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static IExcelDataReader CreateReader(string path, Stream stream) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" or ".txt" => ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration
            {
                AutodetectSeparators = new[] { ',', ';', '\t' },
            }),
            _ => ExcelReaderFactory.CreateReader(stream),
        };
}
