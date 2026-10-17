using TextPasteApp.Core.Logic;

namespace TextPasteApp.Core.Tests;

public class TemplateEngineTests
{
    private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["valor"] = "23",
        ["tag"] = "XV-1023",
        ["desc"] = "Válvula de entrada",
        ["AREA"] = "310",
    };

    [Theory]
    [InlineData("{valor}", "23")]
    [InlineData("PLC1.MC_{valor}.CMD", "PLC1.MC_23.CMD")]
    [InlineData("{tag} - {desc}", "XV-1023 - Válvula de entrada")]
    [InlineData("{area}-{VALOR}", "310-23")]
    [InlineData("MC{valor:0000}", "MC0023")]
    [InlineData("{{literal}} {valor}", "{literal} 23")]
    [InlineData("{noexiste}", "{noexiste}")]
    [InlineData("{tag:000}", "XV-1023")]
    public void Applies(string pattern, string expected) =>
        Assert.Equal(expected, TemplateEngine.Apply(pattern, Fields));

    [Fact]
    public void EmptyPatternReturnsValue() => Assert.Equal("23", TemplateEngine.Apply("", Fields));
}
