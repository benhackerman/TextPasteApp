using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class OverlaySessionTests
{
    private static OverlaySession Create(int historyCount = 10, int tagCount = 100)
    {
        var sources = new Dictionary<SourceKind, IReadOnlyList<PickItem>>
        {
            [SourceKind.History] = Enumerable.Range(0, historyCount).Select(i => PickItem.FromSnippet(new Snippet($"h{i}"))).ToList(),
            [SourceKind.Favorites] = new List<PickItem>(),
            [SourceKind.Taglist] = Enumerable.Range(0, tagCount)
                .Select(i => PickItem.FromTagRow(new TagRow { Tag = $"XV-{1000 + i}", Value = $"{i}" })).ToList(),
        };
        var templates = new List<TextTemplate> { new("Plano", "{valor}"), new("Cmd", "MC_{valor}.CMD") };
        return new OverlaySession(sources, SourceKind.History, 8, templates, 0);
    }

    [Fact]
    public void RingStartsWithoutSelectionAndHoverSelects()
    {
        var s = Create();
        Assert.Null(s.Selected);
        Assert.False(s.Interacted);

        s.HoverSlice(2);
        Assert.Equal("h2", s.Preview);
        Assert.True(s.Interacted);
    }

    [Fact]
    public void WheelPagesTheRing()
    {
        var s = Create(historyCount: 10);
        Assert.Equal(2, s.PageCount);

        s.Scroll(1);
        Assert.Equal(1, s.Page);
        Assert.Equal(2, s.PageItems.Count);

        s.HoverSlice(5); // sector vacío en la segunda página
        Assert.Null(s.Selected);

        s.Scroll(1); // vuelve a la primera
        Assert.Equal(0, s.Page);
    }

    [Fact]
    public void TaglistIsListModeWithFirstRowSelected()
    {
        var s = Create();
        s.NextSource(); // Favoritos
        s.NextSource(); // Taglist
        Assert.True(s.IsListMode);
        Assert.Equal("0", s.Preview);

        s.Scroll(5);
        Assert.Equal("5", s.Preview);
        s.Scroll(-50);
        Assert.Equal("0", s.Preview);
    }

    [Fact]
    public void FilterAndTemplate()
    {
        var s = Create();
        s.SetSource(SourceKind.Taglist);
        s.SetFilter("xv1042");
        Assert.Equal("42", s.Preview);

        s.NextTemplate();
        Assert.Equal("MC_42.CMD", s.Preview);
    }

    [Fact]
    public void ListWindowCentersOnSelection()
    {
        var s = Create();
        s.SetSource(SourceKind.Taglist);
        s.Scroll(50);
        var (start, rows) = s.ListWindow(9);
        Assert.Equal(46, start);
        Assert.Equal(9, rows.Count);
    }

    [Fact]
    public void ArrowKeysMoveAcrossPagesInRing()
    {
        var s = Create();
        s.Move(-1); // sin selección, hacia atrás = último elemento
        Assert.Equal("h9", s.Preview);
        Assert.Equal(1, s.Page);
        s.Move(1);
        Assert.Equal("h0", s.Preview);
        Assert.Equal(0, s.Page);
    }
}
