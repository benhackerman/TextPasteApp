using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class ProjectResolverTests
{
    private static readonly List<TaglistProject> Projects = new()
    {
        new() { Name = "Vino", FilePath = @"C:\P\Taglist_Envasado_Vino.xlsx", WindowMatch = "Envasado, Vino" },
        new() { Name = "Cerveza", FilePath = @"C:\P\Taglist_Cerveza.xlsx", WindowMatch = "Cervecería" },
        new() { Name = "Sin archivo", FilePath = "" },
    };

    [Fact]
    public void WindowTitleKeywordWins()
    {
        var (p, why) = ProjectResolver.Resolve(Projects, "SCADA Studio - Cervecería Sur", new[] { "Taglist_Envasado_Vino.xlsx - Excel" }, "Vino");
        Assert.Equal("Cerveza", p!.Name);
        Assert.Equal(ProjectReason.WindowTitle, why);
    }

    [Fact]
    public void FileOpenInExcelIsNext()
    {
        var (p, why) = ProjectResolver.Resolve(Projects, "Bloc de notas", new[] { "Taglist_Cerveza.xlsx - Excel" }, "Vino");
        Assert.Equal("Cerveza", p!.Name);
        Assert.Equal(ProjectReason.OpenInExcel, why);
    }

    [Fact]
    public void FallsBackToLastUsedThenFirst()
    {
        Assert.Equal("Cerveza", ProjectResolver.Resolve(Projects, "x", Array.Empty<string>(), "Cerveza").Project!.Name);
        Assert.Equal("Vino", ProjectResolver.Resolve(Projects, "x", Array.Empty<string>(), "no existe").Project!.Name);
        Assert.Null(ProjectResolver.Resolve(new List<TaglistProject>(), "x", Array.Empty<string>(), null).Project);
    }

    [Fact]
    public void OldSettingsMigrateToAProject()
    {
        var settings = new AppSettings();
        settings.CurrentProfile().FilePath = @"C:\P\Taglist_Envasado_Vino.xlsx";
        Assert.True(settings.EnsureProjects());
        Assert.Equal("Taglist_Envasado_Vino", settings.Projects.Single().Name);
        Assert.Equal(settings.CurrentProfile().Name, settings.Projects[0].Profile);
        Assert.False(settings.EnsureProjects());
    }
}
