using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class TextSearchTests
{
    private static PickItem Item(string label, string text) => PickItem.FromSnippet(new Snippet(text, label));

    [Fact]
    public void NormalizeIgnoresSeparatorsCaseAndAccents() =>
        Assert.Equal("xv1023valvula", TextSearch.Normalize("XV-10_23 Válvula"));

    [Fact]
    public void RanksExactThenPrefixThenContains()
    {
        var items = new[]
        {
            Item("AXV-102", "1"),
            Item("XV-1023", "2"),
            Item("XV-102", "3"),
            Item("FT-200", "aguas abajo de XV102"),
        };

        var result = TextSearch.Filter(items, "xv102").Select(i => i.Title).ToArray();

        Assert.Equal(new[] { "XV-102", "XV-1023", "AXV-102", "FT-200" }, result);
    }

    [Fact]
    public void EmptyQueryReturnsEverything()
    {
        var items = new[] { Item("A", "1"), Item("B", "2") };
        Assert.Same(items, TextSearch.Filter(items, "  "));
    }
}
