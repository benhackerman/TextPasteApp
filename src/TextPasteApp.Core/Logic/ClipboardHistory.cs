using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Últimos textos copiados, sin repetidos. Primero los fijados (en el orden en que se fijaron),
/// después los demás del más nuevo al más antiguo. Los fijados no cuentan para el límite ni se borran al limpiar.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxTextLength = 20_000;

    private readonly List<Snippet> _items = new();

    public int Limit { get; set; }

    /// <summary>Largo máximo de un texto para guardarlo (los más largos se ignoran).</summary>
    public int MaxLength { get; set; } = MaxTextLength;

    public IReadOnlyList<Snippet> Items => _items;

    public event Action? Changed;

    public ClipboardHistory(int limit = 50, IEnumerable<Snippet>? initial = null)
    {
        Limit = Math.Max(1, limit);
        if (initial != null)
        {
            var valid = initial.Where(s => IsValid(s.Text)).ToList();
            _items.AddRange(valid.Where(s => s.Pinned));
            _items.AddRange(valid.Where(s => !s.Pinned).Take(Limit));
        }
    }

    /// <summary>Agrega un texto. Si ya existía, lo sube al primer lugar. Devuelve false si se ignoró.</summary>
    public bool Add(string? text) => Add(text, "", null);

    /// <summary>Agrega un texto copiado, con el programa de origen y la hora.</summary>
    public bool Add(string? text, string sourceApp, DateTime? copiedAt)
    {
        if (!IsValid(text) || text!.Length > MaxLength) return false;

        var pinnedCount = _items.Count(s => s.Pinned);
        var existing = _items.FindIndex(s => s.Text == text);
        if (existing >= 0 && _items[existing].Pinned) return false; // los fijados no se mueven
        if (existing == pinnedCount) return false;                  // ya es el más nuevo
        if (existing > 0) _items.RemoveAt(existing);

        _items.Insert(pinnedCount, new Snippet(text) { SourceApp = sourceApp, CopiedAt = copiedAt });
        Trim();
        Changed?.Invoke();
        return true;
    }

    public void Remove(Snippet snippet)
    {
        if (_items.Remove(snippet)) Changed?.Invoke();
    }

    /// <summary>Borra todo menos lo fijado.</summary>
    public void Clear()
    {
        _items.RemoveAll(s => !s.Pinned);
        Changed?.Invoke();
    }

    /// <summary>Fija o suelta un texto. Devuelve el nuevo estado (true = fijado).</summary>
    public bool TogglePin(string text)
    {
        var index = _items.FindIndex(s => s.Text == text);
        if (index < 0) return false;
        var item = _items[index];
        _items.RemoveAt(index);
        item.Pinned = !item.Pinned;
        // Fijado: al final de los fijados. Suelto: primero de los no fijados.
        _items.Insert(_items.Count(s => s.Pinned), item);
        Changed?.Invoke();
        return item.Pinned;
    }

    public void Trim()
    {
        var unpinned = _items.Where(s => !s.Pinned).ToList();
        foreach (var extra in unpinned.Skip(Limit)) _items.Remove(extra);
    }

    private static bool IsValid(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;
}
