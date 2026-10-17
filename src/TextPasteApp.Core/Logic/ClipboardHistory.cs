using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>Últimos textos copiados, el más nuevo primero, sin repetidos.</summary>
public sealed class ClipboardHistory
{
    public const int MaxTextLength = 20_000;

    private readonly List<Snippet> _items = new();

    public int Limit { get; set; }

    public IReadOnlyList<Snippet> Items => _items;

    public event Action? Changed;

    public ClipboardHistory(int limit = 50, IEnumerable<Snippet>? initial = null)
    {
        Limit = Math.Max(1, limit);
        if (initial != null)
            _items.AddRange(initial.Where(s => IsValid(s.Text)).Take(Limit));
    }

    /// <summary>Agrega un texto. Si ya existía, lo sube al primer lugar. Devuelve false si se ignoró.</summary>
    public bool Add(string? text)
    {
        if (!IsValid(text)) return false;

        var existing = _items.FindIndex(s => s.Text == text);
        if (existing == 0) return false;
        if (existing > 0) _items.RemoveAt(existing);

        _items.Insert(0, new Snippet(text!));
        Trim();
        Changed?.Invoke();
        return true;
    }

    public void Remove(Snippet snippet)
    {
        if (_items.Remove(snippet)) Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke();
    }

    public void Trim()
    {
        if (_items.Count > Limit)
            _items.RemoveRange(Limit, _items.Count - Limit);
    }

    private static bool IsValid(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;
}
