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

public class TaglistSessionContextTests
{
    private static TaglistBook Book()
    {
        var act = new TaglistSheet("Act", "ACT", new[] { "No", "Code", "Text_0", "Unit" }, 0, sequenceColumns: new[] { 0, 2, 3 });
        act.Add("1", "RCP.B1", "Bomba recepción", "1");
        act.Add("2", "TK.B1", "Bomba retorno", "");
        act.Add("3", "L1.B1", "Bomba llenadora", "5");
        var din = new TaglistSheet("DIn", "DIN", new[] { "No", "Code", "Text_0" }, 0);
        din.Add("3", "TK1.LSH", "Nivel alto");
        return new TaglistBook(new[] { act, din });
    }

    private static readonly TextTemplate[] Plain = { new("", "{valor}"), new("MC", "MC_{valor}") };

    [Fact]
    public void RecentRowsComeFirstWhenNothingTyped()
    {
        var book = Book();
        var recent = new[] { book.Sheets[0].Rows[2].Key };
        var s = new TaglistSession(book, "Act", Plain, 0, new TaglistContext { RecentKeys = recent });

        Assert.Equal(new[] { "3", "1", "2" }, s.Results.Select(r => r.PasteValue));
        Assert.True(s.IsRecent(s.Results[0]));

        s.SetQuery("tk");
        Assert.Equal("2", s.Preview); // con texto escrito manda la búsqueda
    }

    [Fact]
    public void SearchJumpsToAllSheetsWhenCurrentHasNothing()
    {
        var s = new TaglistSession(Book(), "Act", Plain, 0);
        s.SetQuery("lsh");
        Assert.True(s.IsAutoAll);
        Assert.Equal("TK1.LSH", s.Selected!.Title);
        Assert.Equal("Act", s.SheetName); // la hoja elegida no cambia

        s.SetQuery("rcp");
        Assert.False(s.IsAutoAll);
    }

    [Fact]
    public void DoneMarksAndProgress()
    {
        var book = Book();
        var done = new HashSet<string> { book.Sheets[0].Rows[0].Key };
        var s = new TaglistSession(book, "Act", Plain, 0, new TaglistContext { IsDone = done.Contains });

        Assert.True(s.IsDone(s.Results[0]));
        Assert.Equal((1, 3), s.Progress());
    }

    [Fact]
    public void SequenceAppliesTemplateToPasteColumnOnly()
    {
        var s = new TaglistSession(Book(), "Act", Plain, 1);
        Assert.Equal(new[] { "MC_1", "Bomba recepción", "1" }, s.SequenceValues);

        s.SetSheet(1);
        Assert.Null(s.SequenceValues); // DIn no tiene secuencia
    }
}

public class SheetPasteFormatTests
{
    private static TaglistBook Book()
    {
        var act = new TaglistSheet("Act", "ACT", new[] { "No", "Code", "Text_0" }, 0, new[] { 0, 2 })
        {
            PasteFormats = TaglistSheet.ParseFormats("Act{valor:0000}"),
        };
        act.Add("1", "RCP.B1", "Bomba recepción");
        act.Add("23", "L1.B1", "Bomba llenadora");
        act.Add("123", "CO1.MV1", "CO2 carbonatador");

        var pid = new TaglistSheet("PID", "PID", new[] { "No", "Code" }, 0)
        {
            PasteFormats = TaglistSheet.ParseFormats("PID{valor:0000} | PID{valor:0000}.st"),
        };
        pid.Add("5", "CO2.QC");

        var din = new TaglistSheet("DIn", "DIN", new[] { "No", "Code" }, 0); // sin formato propio
        din.Add("3", "TK1.LSH");
        return new TaglistBook(new[] { act, pid, din });
    }

    private static readonly TextTemplate[] Global = { new("Plano", "{valor}"), new("MC", "MC_{valor}") };

    [Fact]
    public void ActPadsToFourDigits()
    {
        var s = new TaglistSession(Book(), "Act", Global, 0);
        Assert.Equal("Act0001", s.Preview);
        s.MoveRow(1);
        Assert.Equal("Act0023", s.Preview);
        s.MoveRow(1);
        Assert.Equal("Act0123", s.Preview);
    }

    [Fact]
    public void OtherColumnsPasteRawAndSequenceFormatsOnlyTheMainColumn()
    {
        var s = new TaglistSession(Book(), "Act", Global, 0);
        s.MoveField(1);
        Assert.Equal("RCP.B1", s.Preview);
        Assert.Equal(new[] { "Act0001", "Bomba recepción" }, s.SequenceValues);
    }

    [Fact]
    public void F2CyclesSheetFormatsThenRaw()
    {
        var s = new TaglistSession(Book(), "PID", Global, 0);
        Assert.True(s.UsesSheetFormats);
        Assert.Equal("PID0005", s.Preview);
        s.NextTemplate();
        Assert.Equal("PID0005.st", s.Preview);
        s.NextTemplate();
        Assert.Equal("5", s.Preview);   // "Sin formato"
        s.NextTemplate();
        Assert.Equal("PID0005", s.Preview);
    }

    [Fact]
    public void SheetsWithoutFormatUseGlobalTemplatesAndFormatResetsPerSheet()
    {
        var s = new TaglistSession(Book(), "PID", Global, 1);
        s.NextTemplate();                // PID0005.st
        s.SetSheet(2);                   // DIn: plantillas generales
        Assert.False(s.UsesSheetFormats);
        Assert.Equal("MC_3", s.Preview);
        s.SetSheet(1);                   // vuelve a PID: parte en su formato por defecto
        Assert.Equal("PID0005", s.Preview);
        Assert.Equal(1, s.TemplateIndex); // la plantilla general elegida no se pierde
    }
}
