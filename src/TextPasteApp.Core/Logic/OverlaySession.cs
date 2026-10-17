using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>
/// Estado de la ruleta mientras está abierta: qué fuente se ve (Historial / Favoritos), qué página,
/// qué está seleccionado, el filtro de búsqueda y la plantilla activa. La ventana sólo dibuja lo que esta clase dice.
/// La rueda cambia de página; el mouse apunta a un sector.
/// (El taglist tiene su propio buscador: ver <see cref="TaglistSession"/>.)
/// </summary>
public sealed class OverlaySession
{
    private static readonly SourceKind[] SourceOrder = { SourceKind.History, SourceKind.Favorites };

    private readonly IReadOnlyDictionary<SourceKind, IReadOnlyList<PickItem>> _sources;
    private readonly IReadOnlyList<TextTemplate> _templates;

    public SourceKind Source { get; private set; }
    public string Filter { get; private set; } = "";
    public IReadOnlyList<PickItem> Items { get; private set; } = Array.Empty<PickItem>();
    public int SlicesPerPage { get; }
    public int Page { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public int TemplateIndex { get; private set; }

    /// <summary>True si el usuario hizo algo (mover, rueda, tecla). Sirve para distinguir un "toque" de un "mantener".</summary>
    public bool Interacted { get; private set; }

    public int PageCount => Math.Max(1, (Items.Count + SlicesPerPage - 1) / SlicesPerPage);
    public PickItem? Selected => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
    public TextTemplate Template => _templates.Count > 0 ? _templates[TemplateIndex] : new TextTemplate("", "{valor}");
    public int TemplateCount => _templates.Count;

    /// <summary>Texto final que se va a pegar (con la plantilla aplicada), o null si no hay nada elegido.</summary>
    public string? Preview => Selected is { } item ? TemplateEngine.Apply(Template.Pattern, item.Fields) : null;

    /// <summary>Índice dentro de la página actual del elemento seleccionado, o -1.</summary>
    public int SelectedSlice =>
        SelectedIndex >= 0 && SelectedIndex / SlicesPerPage == Page ? SelectedIndex % SlicesPerPage : -1;

    public OverlaySession(
        IReadOnlyDictionary<SourceKind, IReadOnlyList<PickItem>> sources,
        SourceKind start,
        int slicesPerPage,
        IReadOnlyList<TextTemplate> templates,
        int templateIndex)
    {
        _sources = sources;
        _templates = templates;
        SlicesPerPage = Math.Clamp(slicesPerPage, 3, 16);
        TemplateIndex = templates.Count == 0 ? 0 : Math.Clamp(templateIndex, 0, templates.Count - 1);
        SetSource(start);
    }

    public int CountFor(SourceKind kind) => _sources.TryGetValue(kind, out var list) ? list.Count : 0;

    /// <summary>Elementos de la página actual (para la ruleta).</summary>
    public IReadOnlyList<PickItem> PageItems =>
        Items.Skip(Page * SlicesPerPage).Take(SlicesPerPage).ToList();

    public void NextSource(int direction = 1)
    {
        var i = Array.IndexOf(SourceOrder, Source);
        var next = SourceOrder[((i + direction) % SourceOrder.Length + SourceOrder.Length) % SourceOrder.Length];
        SetSource(next);
        Interacted = true;
    }

    public void SetSource(SourceKind kind)
    {
        Source = kind;
        Refilter();
    }

    public void SetFilter(string? filter)
    {
        Filter = filter ?? "";
        Refilter();
        Interacted = true;
        // Con filtro siempre hay algo elegido para que Enter funcione directo.
        if (Filter.Length > 0 && Items.Count > 0) Select(0);
    }

    /// <summary>Rueda del mouse: página siguiente (steps positivo) o anterior.</summary>
    public void Scroll(int steps)
    {
        Interacted = true;
        if (Items.Count == 0) return;
        Page = ((Page + steps) % PageCount + PageCount) % PageCount;
        SelectedIndex = -1; // el mouse vuelve a elegir sobre la nueva página
    }

    /// <summary>Teclas de flecha / AvPág: mueven la selección uno a uno en cualquier modo.</summary>
    public void Move(int steps)
    {
        Interacted = true;
        if (Items.Count == 0) return;
        var from = SelectedIndex < 0 ? (steps > 0 ? -1 : Items.Count) : SelectedIndex;
        Select(((from + steps) % Items.Count + Items.Count) % Items.Count);
    }

    /// <summary>El mouse apunta a un sector de la ruleta (-1 = centro, nada).</summary>
    public void HoverSlice(int slice)
    {
        var index = slice < 0 ? -1 : Page * SlicesPerPage + slice;
        var newIndex = index < Items.Count ? index : -1;
        if (newIndex != SelectedIndex) Interacted = true;
        SelectedIndex = newIndex;
    }

    /// <summary>Selecciona un elemento por índice absoluto.</summary>
    public void Select(int index)
    {
        SelectedIndex = index >= 0 && index < Items.Count ? index : -1;
        if (SelectedIndex >= 0) Page = SelectedIndex / SlicesPerPage;
    }

    public void NextTemplate()
    {
        Interacted = true;
        if (_templates.Count > 0) TemplateIndex = (TemplateIndex + 1) % _templates.Count;
    }

    private void Refilter()
    {
        var all = _sources.TryGetValue(Source, out var list) ? list : Array.Empty<PickItem>();
        Items = TextSearch.Filter(all, Filter);
        Page = 0;
        SelectedIndex = -1;
    }
}
