using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class FunctionTests
{
    private const string ActuatorCode =
        "// {{Code}} - {{Text_0}}\n" +
        "      CALL  FB   100 , DB{{No}}\n" +
        "       Cmd :=M{{No}}.0\n" +
        "      L     'Texto$N'\n";

    private static readonly List<CodeFunction> Library = new()
    {
        new() { Name = "Actuador estándar", Category = "Actuadores", Code = ActuatorCode },
        new() { Name = "Alarma nivel", Category = "Alarmas", Description = "Bit de alarma con retardo", Code = "U E 0.0\n= A {{salida}}" },
        new() { Name = "Contador horas", Category = "Contadores", Code = "L MW 10" },
    };

    [Fact]
    public void FindsParametersOnceInOrderAndIgnoresAwlDollarStrings()
    {
        Assert.Equal(new[] { "Code", "Text_0", "No" }, CodeTemplate.ParameterNames(ActuatorCode));
    }

    [Fact]
    public void FillReplacesKnownParametersAndUsesCrLf()
    {
        var code = CodeTemplate.Fill("A {{x}}\nB {{y}}", new Dictionary<string, string> { ["X"] = "1" });
        Assert.Equal("A 1\r\nB {{y}}", code);
    }

    [Fact]
    public void SearchRanksNameThenCategoryThenDescriptionThenCode()
    {
        var s = new FunctionSession(Library);
        Assert.Equal(new[] { "Actuador estándar", "Alarma nivel", "Contador horas" }, s.Results.Select(f => f.Name));

        s.SetQuery("alarma");
        Assert.Equal("Alarma nivel", s.Selected!.Name);
        s.SetQuery("retardo");
        Assert.Equal("Alarma nivel", s.Selected!.Name);
        s.SetQuery("mw10");
        Assert.Equal("Contador horas", s.Selected!.Name);
    }

    [Fact]
    public void ParametersComeFromTaglistRowThenLastValuesThenTyped()
    {
        var sheet = new TaglistSheet("Act", "ACT", new[] { "No", "Code", "Text_0" }, 0);
        var row = sheet.Add("12", "TK1.V1", "Válvula entrada");
        var last = new Dictionary<string, string> { ["salida"] = "4.1" };

        var s = new FunctionSession(Library, last, row);
        Assert.Equal("ACT 12 TK1.V1", s.RowLabel);
        Assert.Equal("12", s.ValueOf("No"));
        Assert.True(s.IsFromRow("No"));
        Assert.Contains("CALL  FB   100 , DB12", s.Preview);
        Assert.Contains("// TK1.V1 - Válvula entrada", s.Preview);

        s.SetValue("No", "13");
        Assert.False(s.IsFromRow("No"));
        Assert.Contains("DB13", s.Preview);

        s.SetQuery("alarma");
        Assert.Equal("4.1", s.ValueOf("salida"));
        Assert.EndsWith("= A 4.1", s.Preview);
    }
}
