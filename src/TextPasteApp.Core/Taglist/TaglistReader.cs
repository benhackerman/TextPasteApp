using System.Globalization;
using System.Text;
using ExcelDataReader;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Taglist;

/// <summary>
/// Lee el taglist desde Excel (.xlsx, .xlsm, .xls) o CSV.
/// Abre el archivo en modo compartido, así funciona aunque esté abierto en Excel.
/// </summary>
public static class TaglistReader
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
        do { names.Add(reader.Name ?? $"Hoja{names.Count + 1}"); } while (reader.NextResult());
        return names;
    }

    /// <summary>Encabezados de la hoja (para llenar los combos de columnas en la configuración).</summary>
    public static IReadOnlyList<string> ReadHeaders(string path, string sheetName, int headerRow)
    {
        using var stream = OpenShared(path);
        using var reader = CreateReader(path, stream);
        if (!MoveToSheet(reader, sheetName)) return Array.Empty<string>();
        return ReadHeaderRow(reader, headerRow);
    }

    public static IReadOnlyList<TagRow> Read(TaglistSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.FilePath))
            return Array.Empty<TagRow>();
        if (!File.Exists(settings.FilePath))
            throw new FileNotFoundException("No se encontró el taglist", settings.FilePath);

        using var stream = OpenShared(settings.FilePath);
        using var reader = CreateReader(settings.FilePath, stream);
        if (!MoveToSheet(reader, settings.SheetName))
            throw new InvalidDataException($"La hoja \"{settings.SheetName}\" no existe en el archivo.");

        var headers = ReadHeaderRow(reader, settings.HeaderRow);
        var tagCol = ResolveColumn(headers, settings.TagColumn);
        var valueCol = ResolveColumn(headers, settings.ValueColumn);
        var descCol = ResolveColumn(headers, settings.DescriptionColumn);

        if (tagCol < 0) throw new InvalidDataException($"No se encontró la columna de tag \"{settings.TagColumn}\".");
        if (valueCol < 0) throw new InvalidDataException($"No se encontró la columna de valor \"{settings.ValueColumn}\".");

        var rows = new List<TagRow>();
        while (reader.Read())
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var key = i < headers.Count && headers[i].Length > 0 ? headers[i] : ColumnLetter(i);
                cells.TryAdd(key, CellText(reader.GetValue(i)));
            }

            var tag = Cell(reader, tagCol);
            var value = Cell(reader, valueCol);
            if (tag.Length == 0 && value.Length == 0) continue; // filas vacías o separadores

            rows.Add(new TagRow
            {
                Tag = tag,
                Value = value,
                Description = descCol >= 0 ? Cell(reader, descCol) : "",
                Cells = cells,
            });
        }
        return rows;
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

    private static string Cell(IExcelDataReader reader, int index) =>
        index >= 0 && index < reader.FieldCount ? CellText(reader.GetValue(index)) : "";

    private static List<string> ReadHeaderRow(IExcelDataReader reader, int headerRow)
    {
        for (var r = 1; r < Math.Max(1, headerRow); r++)
            if (!reader.Read()) return new List<string>();
        if (!reader.Read()) return new List<string>();

        var headers = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++) headers.Add(CellText(reader.GetValue(i)));
        return headers;
    }

    private static bool MoveToSheet(IExcelDataReader reader, string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName)) return true;
        do
        {
            if (string.Equals(reader.Name, sheetName, StringComparison.OrdinalIgnoreCase)) return true;
        } while (reader.NextResult());
        return false;
    }

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
