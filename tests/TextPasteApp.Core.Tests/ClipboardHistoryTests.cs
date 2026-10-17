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
