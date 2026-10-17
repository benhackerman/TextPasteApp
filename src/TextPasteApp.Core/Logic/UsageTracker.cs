using System.Globalization;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Registra cada pegado y responde preguntas para las estadísticas, las marcas de "hecho"
/// y la lista de recientes del taglist.
/// </summary>
public sealed class UsageTracker
{
    public const string SourceTaglist = "Taglist";
    public const string SourceFunctions = "Funciones";
    public const int MaxItems = 5000;

    private readonly Func<DateTime> _now;

    public UsageData Data { get; }

    /// <summary>Se dispara después de cada cambio (para guardar).</summary>
    public event Action? Changed;

    public UsageTracker(UsageData? data = null, Func<DateTime>? now = null)
    {
        Data = data ?? new UsageData();
        _now = now ?? (() => DateTime.Now);
    }

    // ───────────────────────── Registrar ─────────────────────────

    /// <summary>Pegado desde el taglist. fields = cuántos valores se pegaron (1, o más con una secuencia).</summary>
    public void RecordTaglist(string project, TagRow row, string pastedText, int fields, int secondsPerField)
    {
        var key = $"T|{project}|{row.Key}";
        Touch(key, SourceTaglist, project, row.Sheet.Name, row.Key, Label(row), pastedText);
        Mark(project, row.Key, true);
        AddToDay(d =>
        {
            d.Pastes += fields;
            d.TaglistPastes += fields;
            d.SecondsSaved += fields * Math.Max(0, secondsPerField);
        });
    }

    /// <summary>Pegado desde la ruleta (historial o favoritos).</summary>
    public void RecordRing(string source, string text, int seconds)
    {
        var label = text.Replace("\r", "").Replace('\n', ' ').Trim();
        if (label.Length > 80) label = label[..80] + "…";
        Touch($"R|{text}", source, "", "", "", label, text);
        AddToDay(d =>
        {
            d.Pastes++;
            d.RingPastes++;
            d.SecondsSaved += Math.Max(0, seconds);
        });
    }

    /// <summary>Pegado de una función de código guardada.</summary>
    public void RecordFunction(string name, string code, int seconds)
    {
        Touch($"F|{name}", SourceFunctions, "", "", "", name, code);
        AddToDay(d =>
        {
            d.Pastes++;
            d.FunctionPastes++;
            d.SecondsSaved += Math.Max(0, seconds);
        });
    }

    private static string Label(TagRow row) =>
        row.Title.Length > 0 && row.Title != row.PasteValue ? $"{row.PasteValue} · {row.Title}" : row.PasteValue;

    private void Touch(string key, string source, string project, string sheet, string rowKey, string label, string text)
    {
        var now = _now();
        if (!Data.Items.TryGetValue(key, out var item))
        {
            item = new ItemUsage { Source = source, Project = project, Sheet = sheet, RowKey = rowKey, FirstUsed = now };
            Data.Items[key] = item;
            Prune();
        }
        item.Label = label;
        item.Text = text;
        item.Count++;
        item.LastUsed = now;
    }

    private void AddToDay(Action<DayUsage> change)
    {
        var day = DayKey(_now());
        if (!Data.Days.TryGetValue(day, out var usage)) Data.Days[day] = usage = new DayUsage();
        change(usage);
        Changed?.Invoke();
    }

    /// <summary>Si hay demasiados elementos, olvida los usados una sola vez hace más tiempo.</summary>
    private void Prune()
    {
        if (Data.Items.Count <= MaxItems) return;
        foreach (var key in Data.Items.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value.LastUsed)
                     .Take(Data.Items.Count - MaxItems).Select(kv => kv.Key).ToList())
            Data.Items.Remove(key);
    }

    // ───────────────────────── Hecho / recientes ─────────────────────────

    public bool IsDone(string project, string rowKey) =>
        Data.Done.TryGetValue(project, out var set) && set.Contains(rowKey);

    /// <summary>Marca o desmarca una fila como hecha (Ctrl+D). Devuelve el nuevo estado.</summary>
    public bool ToggleDone(string project, string rowKey)
    {
        var done = !IsDone(project, rowKey);
        Mark(project, rowKey, done);
        Changed?.Invoke();
        return done;
    }

    public void ClearDone(string project)
    {
        if (Data.Done.Remove(project)) Changed?.Invoke();
    }

    private void Mark(string project, string rowKey, bool done)
    {
        if (!Data.Done.TryGetValue(project, out var set)) Data.Done[project] = set = new HashSet<string>();
        if (done) set.Add(rowKey); else set.Remove(rowKey);
    }

    /// <summary>Claves de las filas del taglist usadas más recientemente en un proyecto (la más nueva primero).</summary>
    public IReadOnlyList<string> RecentRowKeys(string project, int max = 15) => Data.Items.Values
        .Where(i => i.Source == SourceTaglist && i.Project == project)
        .OrderByDescending(i => i.LastUsed)
        .Take(max)
        .Select(i => i.RowKey)
        .ToList();

    // ───────────────────────── Estadísticas ─────────────────────────

    public DayUsage Today() => Data.Days.TryGetValue(DayKey(_now()), out var d) ? d : new DayUsage();

    /// <summary>Suma de los últimos N días (incluido hoy).</summary>
    public DayUsage LastDays(int days)
    {
        var total = new DayUsage();
        foreach (var (date, usage) in Series(days))
        {
            total.Pastes += usage.Pastes;
            total.TaglistPastes += usage.TaglistPastes;
            total.RingPastes += usage.RingPastes;
            total.FunctionPastes += usage.FunctionPastes;
            total.SecondsSaved += usage.SecondsSaved;
        }
        return total;
    }

    public DayUsage AllTime()
    {
        var total = new DayUsage();
        foreach (var usage in Data.Days.Values)
        {
            total.Pastes += usage.Pastes;
            total.TaglistPastes += usage.TaglistPastes;
            total.RingPastes += usage.RingPastes;
            total.FunctionPastes += usage.FunctionPastes;
            total.SecondsSaved += usage.SecondsSaved;
        }
        return total;
    }

    /// <summary>Un valor por día, del más antiguo a hoy (días sin uso incluidos, en cero).</summary>
    public IReadOnlyList<(DateTime Date, DayUsage Usage)> Series(int days)
    {
        var today = _now().Date;
        return Enumerable.Range(0, days)
            .Select(i => today.AddDays(i - days + 1))
            .Select(d => (d, Data.Days.TryGetValue(DayKey(d), out var u) ? u : new DayUsage()))
            .ToList();
    }

    /// <summary>Lo que más pegas.</summary>
    public IReadOnlyList<ItemUsage> Top(int count, string? source = null) => Data.Items.Values
        .Where(i => source == null || i.Source == source)
        .OrderByDescending(i => i.Count)
        .ThenByDescending(i => i.LastUsed)
        .Take(count)
        .ToList();

    public static string DayKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"3 h 25 min", "12 min", "40 s".</summary>
    public static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds} s";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} min";
        if (t.TotalDays < 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        return $"{(int)t.TotalHours} h";
    }
}
