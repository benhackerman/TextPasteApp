using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class TextSearchTests
{
    private static PickItem Tag(string tag, string value, string desc = "") =>
        PickItem.FromTagRow(new TagRow { Tag = tag, Value = value, Description = desc });

    [Fact]
    public void NormalizeIgnoresSeparatorsCaseAndAccents() =>
        Assert.Equal("xv1023valvula", TextSearch.Normalize("XV-10_23 Válvula"));

    [Fact]
    public void RanksExactThenPrefixThenContains()
    {
        var items = new[]
        {
            Tag("AXV-102", "1"),
            Tag("XV-1023", "2"),
            Tag("XV-102", "3"),
            Tag("FT-200", "4", "aguas abajo de XV102"),
        };

        var result = TextSearch.Filter(items, "xv102").Select(i => i.Subtitle).ToArray();

        Assert.Equal(new[] { "3", "2", "1", "4" }, result);
    }

    [Fact]
    public void EmptyQueryReturnsEverything()
    {
        var items = new[] { Tag("A", "1"), Tag("B", "2") };
        Assert.Same(items, TextSearch.Filter(items, "  "));
    }
}
