using TextPasteApp.Core.Models;
using TextPasteApp.Core.Storage;

namespace TextPasteApp.Core.Tests;

public class JsonStoreTests
{
    [Fact]
    public void SavesAndLoadsSettings()
    {
        var store = new JsonStore(Path.Combine(Path.GetTempPath(), $"tpa-{Guid.NewGuid():N}"));
        var settings = new AppSettings { HistoryLimit = 7, LastSource = SourceKind.Taglist };
        settings.Favorites.Add(new Snippet("Válvula", "V"));

        store.Save("settings.json", settings);
        var loaded = store.Load<AppSettings>("settings.json");

        Assert.Equal(7, loaded.HistoryLimit);
        Assert.Equal(SourceKind.Taglist, loaded.LastSource);
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
