using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class TaglistSessionTests
{
    private static TaglistBook Book()
    {
        var act = new TaglistSheet("Actuadores", "ACT", new[] { "N° ACT", "TAG", "DESCRIPCIÓN" }, pasteColumn: 0);
        act.Add("21", "XV-1021", "Válvula descarga TK-01");
        act.Add("23", "XV-1023", "Válvula de entrada TK-01");
        act.Add("102", "P-201", "Bomba alimentación");

        var di = new TaglistSheet("Entradas digitales", "DI", new[] { "TAG", "N° DI", "DESCRIPCIÓN" }, pasteColumn: 1);
        di.Add("ZSO-1023", "312", "XV-1023 abierta");
        di.Add("LSH-4410", "310", "Nivel alto TK-01");

        return new TaglistBook(new[] { act, di });
    }

    private static readonly List<TextTemplate> Templates = new() { new("Plano", "{valor}"), new("MC", "MC_{valor}") };

    [Fact]
    public void StartsInRememberedSheetAndPastesDefaultColumn()
    {
        var s = new TaglistSession(Book(), "Actuadores", Templates, 0);
        Assert.Equal("Actuadores", s.SheetName);
        Assert.Equal(3, s.Results.Count);
        Assert.Equal("21", s.Preview);
    }

    [Fact]
    public void UnknownSheetFallsBackToFirstAndEmptyMeansAll()
    {
        Assert.Equal("Actuadores", new TaglistSession(Book(), "No existe", Templates, 0).SheetName);
        var all = new TaglistSession(Book(), "", Templates, 0);
        Assert.Equal(TaglistSession.AllSheets, all.SheetIndex);
        Assert.Equal(5, all.Results.Count);
    }

    [Fact]
    public void SearchesAnyColumnAndRanksExactFirst()
    {
        var s = new TaglistSession(Book(), "", Templates, 0);

        s.SetQuery("xv1023");
        // Exacto (tag XV-1023) primero; luego la DI que lo menciona en la descripción.
        Assert.Equal(new[] { "XV-1023", "ZSO-1023" }, s.Results.Select(r => r.Title));

        s.SetQuery("bomba");
        Assert.Equal("102", s.Preview);

        s.SetQuery("310");
        Assert.Equal("LSH-4410", s.Selected!.Title);
        Assert.Equal("310", s.Preview); // en DI la columna por defecto es "N° DI"
    }

    [Fact]
    public void WheelChangesColumnAndArrowsResetIt()
    {
        var s = new TaglistSession(Book(), "Actuadores", Templates, 0);
        s.MoveField(1);
        Assert.Equal("XV-1021", s.Preview);
        s.MoveField(1);
        Assert.Equal("Válvula descarga TK-01", s.Preview);
        s.MoveField(1); // da la vuelta
        Assert.Equal("21", s.Preview);

        s.MoveField(-1);
        s.MoveRow(1);
        Assert.Equal("23", s.Preview); // nueva fila parte en la columna por defecto
    }

    [Fact]
    public void TabCyclesSheetsIncludingAll()
    {
        var s = new TaglistSession(Book(), "Actuadores", Templates, 0);
        s.NextSheet();
        Assert.Equal("Entradas digitales", s.SheetName);
        s.NextSheet();
        Assert.Equal(TaglistSession.AllSheets, s.SheetIndex);
        s.NextSheet();
        Assert.Equal("Actuadores", s.SheetName);
        s.NextSheet(-1);
        Assert.Equal(TaglistSession.AllSheets, s.SheetIndex);
    }

    [Fact]
    public void GhostAutocompletesFromFirstResult()
    {
        var s = new TaglistSession(Book(), "Actuadores", Templates, 0);
        s.SetQuery("xv-102");
        Assert.Equal("1", s.Ghost); // XV-1021
        s.AcceptGhost();
        Assert.Equal("xv-1021", s.Query);
        Assert.Equal("", s.Ghost);
    }

    [Fact]
    public void TemplateAndCounts()
    {
        var s = new TaglistSession(Book(), "Actuadores", Templates, 1);
        s.SetQuery("1023");
        Assert.Equal("MC_23", s.Preview);
        Assert.Equal(1, s.CountIn(0));
        Assert.Equal(1, s.CountIn(1));
        Assert.Equal(2, s.CountIn(TaglistSession.AllSheets));
    }

    [Fact]
    public void EmptyBookIsSafe()
    {
        var s = new TaglistSession(TaglistBook.Empty, "Actuadores", Templates, 0);
        s.MoveRow(1); s.MoveField(1); s.NextSheet(); s.SetQuery("x");
        Assert.Null(s.Preview);
    }
}
