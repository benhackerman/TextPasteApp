using TextPasteApp.Core.Logic;

namespace TextPasteApp.Core.Tests;

public class ClipboardHistoryTests
{
    [Fact]
    public void NewestFirstWithoutDuplicates()
    {
        var h = new ClipboardHistory(10);
        h.Add("a");
        h.Add("b");
        h.Add("a");

        Assert.Equal(new[] { "a", "b" }, h.Items.Select(s => s.Text));
    }

    [Fact]
    public void RespectsLimitAndIgnoresBlank()
    {
        var h = new ClipboardHistory(2);
        Assert.False(h.Add("   "));
        h.Add("1"); h.Add("2"); h.Add("3");

        Assert.Equal(new[] { "3", "2" }, h.Items.Select(s => s.Text));
    }
}

public class ClipboardHistoryPinTests
{
    [Fact]
    public void PinnedStayFirstSurviveLimitAndClear()
    {
        var h = new ClipboardHistory(2);
        h.Add("a"); h.Add("b");
        Assert.True(h.TogglePin("a"));
        Assert.Equal(new[] { "a", "b" }, h.Items.Select(s => s.Text));

        h.Add("c"); h.Add("d"); // límite 2 sólo para los no fijados
        Assert.Equal(new[] { "a", "d", "c" }, h.Items.Select(s => s.Text));

        h.Add("a"); // copiar algo fijado no lo mueve ni lo duplica
        Assert.Equal(3, h.Items.Count);

        h.Clear();
        Assert.Equal(new[] { "a" }, h.Items.Select(s => s.Text));

        Assert.False(h.TogglePin("a"));
        Assert.False(h.Items[0].Pinned);
    }

    [Fact]
    public void IgnoresTextsLongerThanMax()
    {
        var h = new ClipboardHistory(10) { MaxLength = 5 };
        Assert.False(h.Add("123456"));
        Assert.True(h.Add("12345"));
    }
}

public class ClipboardHistorySourceTests
{
    [Fact]
    public void KeepsSourceAppAndTimeAndMakesThemSearchable()
    {
        var h = new ClipboardHistory(10);
        h.Add("RCP.B1", "EXCEL", new DateTime(2026, 10, 18, 10, 32, 0));

        Assert.Equal("EXCEL", h.Items[0].SourceApp);
        var item = TextPasteApp.Core.Models.PickItem.FromSnippet(h.Items[0]);
        Assert.Equal("EXCEL · 10:32", item.Description);
        Assert.Single(TextPasteApp.Core.Logic.TextSearch.Filter(new[] { item }, "excel"));
    }
}

public class SequenceKeyTests
{
    [Theory]
    [InlineData("enter", "Enter", 0x0D, "↵")]
    [InlineData("Abajo", "Abajo", 0x28, "↓")]
    [InlineData("", "Tab", 0x09, "⇥")]
    [InlineData("cualquiera", "Tab", 0x09, "⇥")]
    public void NormalizesAndMapsKeys(string input, string normalized, int vk, string symbol)
    {
        Assert.Equal(normalized, TextPasteApp.Core.Models.SequenceKeys.Normalize(input));
        Assert.Equal(vk, TextPasteApp.Core.Models.SequenceKeys.VirtualKey(input));
        Assert.Equal(symbol, TextPasteApp.Core.Models.SequenceKeys.Symbol(input));
    }
}
