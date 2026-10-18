using TextPasteApp.Core.Models;
using TextPasteApp.Core.Taglist;

namespace TextPasteApp.Core.Tests;

public class TaglistReaderTests
{
    [Theory]
    [InlineData("A", 0)]
    [InlineData("c", 2)]
    [InlineData("AA", 26)]
    [InlineData("Actuador", 1)]
    [InlineData("tag", 0)]
    [InlineData("", -1)]
    [InlineData("No existe", -1)]
    public void ResolvesColumnsByHeaderOrLetter(string spec, int expected) =>
        Assert.Equal(expected, TaglistReader.ResolveColumn(new[] { "TAG", "ACTUADOR", "DESCRIPCION" }, spec));

    [Fact]
    public void ColumnLetterRoundTrip()
    {
        Assert.Equal("A", TaglistReader.ColumnLetter(0));
        Assert.Equal("Z", TaglistReader.ColumnLetter(25));
        Assert.Equal("AA", TaglistReader.ColumnLetter(26));
    }

    [Fact]
    public void CellTextDropsTrailingZeroOnWholeNumbers()
    {
        Assert.Equal("1023", TaglistReader.CellText(1023.0));
        Assert.Equal("1.5", TaglistReader.CellText(1.5));
        Assert.Equal("", TaglistReader.CellText(null));
    }

    [Theory]
    [InlineData("Actuadores", "ACT")]
    [InlineData("PID", "PID")]
    [InlineData("Entradas digitales", "ED")]
    public void GuessesShortNames(string name, string expected) =>
        Assert.Equal(expected, TaglistReader.GuessShortName(name));

    private static string WriteCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taglist-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }

    private const string Csv =
        "Proyecto Planta Norte;;;\n" +
        "TAG;N° ACT;DESCRIPCIÓN;ÁREA\n" +
        "XV-1023;23;Válvula entrada;310\n" +
        ";;;\n" +
        "M-2001;101;Motor agitador;320\n";

    [Fact]
    public void DetectsHeaderRowAndNumberColumn()
    {
        var path = WriteCsv(Csv);
        try
        {
            var sheet = Assert.Single(TaglistReader.DetectSheets(path));
            Assert.Equal(2, sheet.HeaderRow);
            Assert.Equal("N° ACT", sheet.PasteColumn);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadsBookWithVisibleColumnsAndPasteColumn()
    {
        var path = WriteCsv(Csv);
        try
        {
            var detected = Assert.Single(TaglistReader.DetectSheets(path));
            detected.VisibleColumns = "TAG, descripción"; // la de pegar se agrega sola
            var book = TaglistReader.ReadBook(new TaglistProfile { FilePath = path, Sheets = { detected } });

            var sheet = Assert.Single(book.Sheets);
            Assert.Equal(new[] { "N° ACT", "TAG", "DESCRIPCIÓN" }, sheet.Columns);
            Assert.Equal(2, sheet.Rows.Count);
            Assert.Equal("23", sheet.Rows[0].PasteValue);
            Assert.Equal("XV-1023", sheet.Rows[0].Title);
            Assert.Equal("Motor agitador", sheet.Rows[1].Description);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DisabledSheetsAreSkipped()
    {
        var path = WriteCsv(Csv);
        try
        {
            var detected = Assert.Single(TaglistReader.DetectSheets(path));
            detected.Enabled = false;
            Assert.Empty(TaglistReader.ReadBook(new TaglistProfile { FilePath = path, Sheets = { detected } }).Sheets);
        }
        finally { File.Delete(path); }
    }
}
