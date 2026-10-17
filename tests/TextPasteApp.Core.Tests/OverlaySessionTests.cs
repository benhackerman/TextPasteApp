using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class OverlaySessionTests
{
    private static OverlaySession Create(int historyCount = 10)
    {
        var sources = new Dictionary<SourceKind, IReadOnlyList<PickItem>>
        {
            [SourceKind.History] = Enumerable.Range(0, historyCount).Select(i => PickItem.FromSnippet(new Snippet($"h{i}"))).ToList(),
            [SourceKind.Favorites] = new List<PickItem> { PickItem.FromSnippet(new Snippet("ON")) },
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
    public void TabTogglesBetweenHistoryAndFavorites()
    {
        var s = Create();
        s.NextSource();
        Assert.Equal(SourceKind.Favorites, s.Source);
        s.NextSource();
        Assert.Equal(SourceKind.History, s.Source);
    }

    [Fact]
    public void FilterSelectsFirstAndTemplateApplies()
    {
        var s = Create();
        s.SetFilter("h7");
        Assert.Equal("h7", s.Preview);

        s.NextTemplate();
        Assert.Equal("MC_h7.CMD", s.Preview);
    }

    [Fact]
    public void ArrowKeysMoveAcrossPages()
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
