using TextPasteApp.Core.Logic;

namespace TextPasteApp.Core.Tests;

public class AwlParserTests
{
    private const string Code =
        "// Bomba {{Code}}\n" +
        "      U     E      0.0                 // marcha\n" +
        "      UN    M     10.1\n" +
        "      =     A      4.1\n" +
        "      L     MW    20\n" +
        "      T     DB12.DBW 4\n" +
        "      U     DB12.DBX 0.0\n" +
        "      L     S5T#2S\n" +
        "      SV    T      5\n" +
        "      SPB   M001\n" +
        "M001: U     \"Motor_Ein\"\n" +
        "      CALL  FB   100 , DB12\n" +
        "       Cmd  :=M10.1\n" +
        "       Name :='Hola$N'\n";

    private static List<(AwlTokenKind Kind, string Text)> Significant(string code) =>
        AwlParser.Tokenize(code).Where(t => t.Kind != AwlTokenKind.Text).Select(t => (t.Kind, t.Text.Trim())).ToList();

    [Fact]
    public void SeparatesInstructionsFromVariables()
    {
        var tokens = Significant("      L     MW    20\n      T     T 5\n");
        Assert.Equal(new[]
        {
            (AwlTokenKind.Instruction, "L"), (AwlTokenKind.Variable, "MW    20"),
            (AwlTokenKind.Instruction, "T"), (AwlTokenKind.Variable, "T 5"),
        }, tokens);
    }

    [Fact]
    public void RecognizesCommentsLabelsConstantsAndParameters()
    {
        var tokens = Significant(Code);
        Assert.Contains((AwlTokenKind.Comment, "// marcha"), tokens);
        Assert.Contains((AwlTokenKind.Label, "M001:"), tokens);
        Assert.Contains((AwlTokenKind.Constant, "S5T#2S"), tokens);
        Assert.Contains((AwlTokenKind.Constant, "'Hola$N'"), tokens);
        Assert.Contains((AwlTokenKind.Instruction, "SPB"), tokens);
        Assert.Contains((AwlTokenKind.Variable, "\"Motor_Ein\""), tokens);
        Assert.DoesNotContain(tokens, t => t.Text == "M001" && t.Kind == AwlTokenKind.Variable); // destino de salto
        // {{Code}} dentro del comentario sigue siendo parte del comentario
        Assert.Contains((AwlTokenKind.Comment, "// Bomba {{Code}}"), tokens);
    }

    [Fact]
    public void ListsVariablesOnceWithCountsAndDbBlock()
    {
        var vars = AwlParser.Variables(Code);
        var keys = vars.Select(v => v.Key).ToList();

        Assert.Equal(new[] { "E0.0", "M10.1", "A4.1", "MW20", "DB12.DBW4", "DB12", "DB12.DBX0.0", "T5", "\"Motor_Ein\"", "FB100" }, keys);
        Assert.Equal(2, vars.Single(v => v.Key == "M10.1").Count);   // UN M 10.1 y Cmd :=M10.1
        Assert.Equal(3, vars.Single(v => v.Key == "DB12").Count);    // DB12.DBW4, DB12.DBX0.0 y DB12 del CALL
    }

    [Fact]
    public void ReplacingAVariableChangesEveryOccurrenceKeepingTheRest()
    {
        var code = AwlParser.ReplaceVariable(Code, "m 10.1", "M 30.0");
        Assert.Contains("UN    M 30.0", code);
        Assert.Contains("Cmd  :=M 30.0", code);
        Assert.DoesNotContain("10.1", code);
        Assert.Contains("SPB   M001", code);       // la etiqueta no se toca
        Assert.Contains("{{Code}}", code);         // los parámetros tampoco
    }

    [Fact]
    public void ChangingTheDbNumberUpdatesAllItsAccesses()
    {
        var code = AwlParser.ReplaceVariable(Code, "DB12", "DB13");
        Assert.Contains("T     DB13.DBW 4", code);
        Assert.Contains("U     DB13.DBX 0.0", code);
        Assert.Contains("CALL  FB   100 , DB13", code);
        Assert.DoesNotContain("DB12", code);
    }

    [Fact]
    public void EnglishMnemonicsWork()
    {
        var tokens = Significant("      A     I      0.0\n      AN    Q      4.1\n      JC    END\n");
        Assert.Equal(AwlTokenKind.Instruction, tokens[0].Kind);
        Assert.Equal((AwlTokenKind.Variable, "I      0.0"), tokens[1]);
        Assert.Equal((AwlTokenKind.Instruction, "AN"), tokens[2]);
        Assert.Equal((AwlTokenKind.Variable, "Q      4.1"), tokens[3]);
        Assert.Equal((AwlTokenKind.Instruction, "JC"), tokens[4]);
    }

    [Fact]
    public void PointerConstantStaysTogether()
    {
        var tokens = Significant("      L     P#DBX 0.0\n");
        Assert.Equal((AwlTokenKind.Constant, "P#DBX 0.0"), tokens[1]);
    }

    [Fact]
    public void RoundTripKeepsTextIdentical()
    {
        Assert.Equal(Code, string.Concat(AwlParser.Tokenize(Code).Select(t => t.Text)));
    }
}
