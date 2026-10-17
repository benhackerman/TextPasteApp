using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Tests;

public class UsageTrackerTests
{
    private static TagRow Row(string no, string code)
    {
        var sheet = new TaglistSheet("Act", "ACT", new[] { "No", "Code", "Text_0" }, 0);
        return sheet.Add(no, code, "Bomba");
    }

    [Fact]
    public void CountsPastesPerItemAndPerDay()
    {
        var now = new DateTime(2026, 10, 18, 9, 0, 0);
        var t = new UsageTracker(now: () => now);
        var row = Row("1", "RCP.B1");

        t.RecordTaglist("Vino", row, "1", fields: 1, secondsPerField: 20);
        t.RecordTaglist("Vino", row, "1", fields: 3, secondsPerField: 20); // secuencia de 3 campos
        t.RecordRing("Historial", "ON", seconds: 4);

        Assert.Equal(5, t.Today().Pastes);
        Assert.Equal(4, t.Today().TaglistPastes);
        Assert.Equal(84, t.Today().SecondsSaved);

        var top = t.Top(10);
        Assert.Equal("1 · RCP.B1", top[0].Label);
        Assert.Equal(2, top[0].Count);
        Assert.Equal("ON", top[1].Label);
    }

    [Fact]
    public void PastingMarksRowDoneAndCtrlDToggles()
    {
        var t = new UsageTracker();
        var row = Row("1", "RCP.B1");
        Assert.False(t.IsDone("Vino", row.Key));

        t.RecordTaglist("Vino", row, "1", 1, 20);
        Assert.True(t.IsDone("Vino", row.Key));
        Assert.False(t.IsDone("Otro proyecto", row.Key));

        Assert.False(t.ToggleDone("Vino", row.Key));
        Assert.False(t.IsDone("Vino", row.Key));
    }

    [Fact]
    public void RecentRowsAreNewestFirstPerProject()
    {
        var now = new DateTime(2026, 10, 18, 9, 0, 0);
        var t = new UsageTracker(now: () => now);
        var a = Row("1", "A");
        var b = Row("2", "B");

        t.RecordTaglist("Vino", a, "1", 1, 20);
        now = now.AddMinutes(1);
        t.RecordTaglist("Vino", b, "2", 1, 20);
        t.RecordTaglist("Cerveza", a, "1", 1, 20);

        Assert.Equal(new[] { b.Key, a.Key }, t.RecentRowKeys("Vino"));
        Assert.Equal(new[] { a.Key }, t.RecentRowKeys("Cerveza"));
    }

    [Fact]
    public void SeriesFillsMissingDaysAndSums()
    {
        var now = new DateTime(2026, 10, 18, 9, 0, 0);
        var t = new UsageTracker(now: () => now);
        t.RecordRing("Historial", "x", 4);
        now = now.AddDays(-2);
        t.RecordRing("Historial", "y", 4);
        now = now.AddDays(2);

        var series = t.Series(3);
        Assert.Equal(new[] { 1, 0, 1 }, series.Select(s => s.Usage.Pastes));
        Assert.Equal(2, t.LastDays(7).Pastes);
        Assert.Equal(2, t.AllTime().Pastes);
    }

    [Theory]
    [InlineData(40, "40 s")]
    [InlineData(600, "10 min")]
    [InlineData(3 * 3600 + 25 * 60, "3 h 25 min")]
    public void FormatsDurations(int seconds, string expected) => Assert.Equal(expected, UsageTracker.FormatDuration(seconds));
}
