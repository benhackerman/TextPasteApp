using TextPasteApp.Core.Models;
using TextPasteApp.Core.Storage;

namespace TextPasteApp.Core.Tests;

public class JsonStoreTests
{
    [Fact]
    public void SavesAndLoadsSettings()
    {
        var store = new JsonStore(Path.Combine(Path.GetTempPath(), $"tpa-{Guid.NewGuid():N}"));
        var settings = new AppSettings { HistoryLimit = 7, LastSource = SourceKind.Favorites, Opacity = 0.8 };
        settings.CurrentProfile().Sheets.Add(new SheetProfile { SheetName = "Actuadores", PasteColumn = "N° ACT" });
        settings.Favorites.Add(new Snippet("Válvula", "V"));

        store.Save("settings.json", settings);
        var loaded = store.Load<AppSettings>("settings.json");

        Assert.Equal(7, loaded.HistoryLimit);
        Assert.Equal(SourceKind.Favorites, loaded.LastSource);
        Assert.Equal(0.8, loaded.Opacity);
        Assert.Equal("N° ACT", loaded.CurrentProfile().Sheets[0].PasteColumn);
        Assert.Equal("Válvula", loaded.Favorites[^1].Text);
        Directory.Delete(store.Folder, true);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var store = new JsonStore(Path.Combine(Path.GetTempPath(), $"tpa-{Guid.NewGuid():N}"));
        File.WriteAllText(store.PathFor("settings.json"), "{ esto no es json");

        var loaded = store.Load<AppSettings>("settings.json");

        Assert.Equal(50, loaded.HistoryLimit);
        Assert.True(File.Exists(store.PathFor("settings.json.bad")));
        Directory.Delete(store.Folder, true);
    }
}

public class UsagePersistenceTests
{
    [Fact]
    public void UsageSurvivesSaveAndLoad()
    {
        var store = new TextPasteApp.Core.Storage.JsonStore(Path.Combine(Path.GetTempPath(), $"tpa-{Guid.NewGuid():N}"));
        var tracker = new TextPasteApp.Core.Logic.UsageTracker();
        var sheet = new TaglistSheet("Act", "ACT", new[] { "No", "Code" }, 0);
        var row = sheet.Add("1", "RCP.B1");
        tracker.RecordTaglist("Vino", row, "1", 1, 20);

        store.Save("usage.json", tracker.Data);
        var loaded = new TextPasteApp.Core.Logic.UsageTracker(store.Load<UsageData>("usage.json"));

        Assert.True(loaded.IsDone("Vino", row.Key));
        Assert.Equal(1, loaded.AllTime().TaglistPastes);
        Assert.Equal(20, loaded.AllTime().SecondsSaved);
        Assert.Equal(row.Key, loaded.RecentRowKeys("Vino").Single());
        Directory.Delete(store.Folder, true);
    }
}
