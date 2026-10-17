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

    [Fact]
    public void ReadsCsvWithHeaderRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taglist-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path,
            "Proyecto X;;\n" +
            "TAG;ACTUADOR;DESCRIPCION\n" +
            "XV-1023;23;Válvula entrada\n" +
            ";;\n" +
            "M-2001;101;Bomba\n");
        try
        {
            var rows = TaglistReader.Read(new TaglistSettings
            {
                FilePath = path,
                HeaderRow = 2,
                TagColumn = "TAG",
                ValueColumn = "Actuador",
                DescriptionColumn = "C",
            });

            Assert.Equal(2, rows.Count);
            Assert.Equal("XV-1023", rows[0].Tag);
            Assert.Equal("23", rows[0].Value);
            Assert.Equal("Válvula entrada", rows[0].Description);
            Assert.Equal("101", rows[1].Cells["ACTUADOR"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
