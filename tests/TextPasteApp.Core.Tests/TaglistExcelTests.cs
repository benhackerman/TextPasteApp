using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Core.Taglist;

namespace TextPasteApp.Core.Tests;

/// <summary>
/// Pruebas con un .xlsx real de ejemplo (datos inventados) con el mismo formato que los taglists de trabajo:
/// hojas Act / AIn con columnas No, IO, Code, Text_0…, filas reservadas sin tag, hojas vacías y una de configuración.
/// </summary>
public class TaglistExcelTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "taglist_ejemplo.xlsx");

    private static TaglistBook Load(out List<SheetProfile> detected)
    {
        detected = TaglistReader.DetectSheets(Fixture);
        return TaglistReader.ReadBook(new TaglistProfile { FilePath = Fixture, Sheets = detected });
    }

    [Fact]
    public void DetectsModuleSheetsAndSkipsEmptyOrConfigSheets()
    {
        Load(out var detected);
        var enabled = detected.Where(s => s.Enabled).Select(s => s.SheetName).ToArray();

        Assert.Equal(new[] { "Act", "AIn" }, enabled);                 // Fieldbus y Valx vacías, Settings sin columna N°
        Assert.All(detected.Where(s => s.Enabled), s => Assert.Equal("No", s.PasteColumn));
        Assert.Equal(2, detected.Single(s => s.SheetName == "Settings").HeaderRow);
    }

    [Fact]
    public void SkipsReservedRowsAndRecognizesCodeAndText()
    {
        var book = Load(out _);
        var act = book.Sheets[0];

        Assert.Equal(new[] { "1", "2", "5" }, act.Rows.Select(r => r.PasteValue)); // 3 y 4 son reservadas
        Assert.Equal("Code", act.Columns[act.TitleColumn]);
        Assert.Equal("Text_0", act.Columns[act.DescriptionColumn]);
        Assert.Equal("RCP.B1", act.Rows[0].Title);
    }

    [Fact]
    public void WheelSkipsEmptyColumns()
    {
        var s = new TaglistSession(Load(out _), "Act", new[] { new TextTemplate("", "{valor}") }, 0);
        var seen = new List<string?>();
        for (var i = 0; i < 6; i++) { seen.Add(s.Preview); s.MoveField(1); }

        // No → IO → Code → Text_0 → Unit Assignment → vuelve a No (FB1, Text_1, Switch y Comment están vacías)
        Assert.Equal(new[] { "1", "0.0", "RCP.B1", "Bomba recepción", "1", "1" }, seen);
    }

    [Fact]
    public void CodeMatchesRankAboveDescriptionMatches()
    {
        var s = new TaglistSession(Load(out _), "", new[] { new TextTemplate("", "{valor}") }, 0);
        s.SetQuery("co2");
        // AIn CO2.FIT1 (code empieza con co2) antes que Act CO1.MV1 (sólo la descripción empieza con CO2)
        Assert.Equal(new[] { "CO2.FIT1", "CO1.MV1" }, s.Results.Select(r => r.Title));
    }
}
