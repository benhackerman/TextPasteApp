using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Native;

namespace TextPasteApp.Taglist;

/// <summary>
/// Buscador compacto del taglist: aparece bajo el puntero con una línea de búsqueda y una tabla de resultados
/// que se filtra mientras escribes. Maneja sus propias teclas sobre un <see cref="TaglistSession"/> y al final
/// le pide al controlador que pegue (o que cancele).
///   ↑↓ fila · rueda columna · → autocompletar · Tab hoja · Ctrl+1…9 hoja · F2 plantilla · F4 proyecto
///   Ctrl+D hecho · Ctrl+F usar fila en una función · Ctrl+rueda opacidad · Enter pega · Shift+Enter secuencia · Esc
/// </summary>
public partial class TaglistWindow : Window
{
    private const int VisibleRows = 6;

    private static readonly Brush Accent = Frozen("#FF3DDC97");
    private static readonly Brush TextBrush = Frozen("#FFE8EAED");
    private static readonly Brush Muted = Frozen("#FF9AA0AA");
    private static readonly Brush DarkText = Frozen("#FF0E1116");
    private static readonly Brush RowHighlight = Frozen("#1AFFFFFF");
    private static readonly Brush GhostBrush = Frozen("#FF5B6170");

    private IntPtr _hwnd;
    private TaglistSession? _session;
    private string? _emptyMessage;
    private Win32.POINT _anchor;
    private bool _hiding;
    private bool _settingText;

    /// <summary>
    /// El usuario eligió qué pegar (ya con la plantilla aplicada). Un solo valor con Enter;
    /// varios (secuencia con Tab entre ellos) con Shift+Enter.
    /// </summary>
    public event Action<IReadOnlyList<string>, bool>? PasteRequested;

    /// <summary>Ctrl+D: marcar / desmarcar la fila como hecha.</summary>
    public event Action<TagRow>? ToggleDoneRequested;

    /// <summary>Ctrl+F: usar la fila elegida para rellenar una función de código.</summary>
    public event Action<TagRow>? UseInFunctionRequested;

    /// <summary>F4: pasar al siguiente proyecto (otro taglist).</summary>
    public event Action? NextProjectRequested;
    public event Action? Cancelled;
    public event Action? SwitchToRingRequested;
    public event Action<double>? OpacityChanged;

    public TaglistSession? Session => _session;
    public bool IsOpen => IsVisible && _session != null;

    public TaglistWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetExtendedStyle(_hwnd, Win32.WS_EX_TOOLWINDOW, 0); // fuera de Alt+Tab
        };
        SearchBox.TextChanged += OnSearchChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnWheel;
        SheetText.MouseLeftButtonUp += (_, _) => Do(s => s.NextSheet());
        ProjectText.MouseLeftButtonUp += (_, _) => NextProjectRequested?.Invoke();
        // La ventana cambia de tamaño con los resultados: se mantiene pegada al puntero y dentro de la pantalla.
        SizeChanged += (_, _) =>
        {
            if (IsVisible) WindowPlacement.PlaceNear(_hwnd, _anchor);
        };
        Deactivated += (_, _) =>
        {
            if (IsOpen && !_hiding) Cancelled?.Invoke(); // clic fuera = cancelar
        };
    }

    // ───────────────────────── Mostrar / ocultar ─────────────────────────

    internal void Open(TaglistSession session, Win32.POINT cursor, double opacity, string projectLabel, string? emptyMessage)
    {
        _session = session;
        _emptyMessage = emptyMessage;
        _anchor = cursor;
        ProjectText.Text = projectLabel;
        SetSearchText("");

        new WindowInteropHelper(this).EnsureHandle();
        Opacity = 0;
        Render();
        Show();
        UpdateLayout();
        WindowPlacement.PlaceNear(_hwnd, _anchor);
        Opacity = Math.Clamp(opacity, 0.4, 1);

        // Windows sólo deja tomar el foco a quien recibió la última entrada.
        Paster.SendDummyKey();
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>Cambia el taglist mostrado (otro proyecto) sin cerrar la ventana; conserva lo escrito.</summary>
    internal void Replace(TaglistSession session, string projectLabel, string? emptyMessage)
    {
        _session = session;
        _emptyMessage = emptyMessage;
        ProjectText.Text = projectLabel;
        session.SetQuery(SearchBox.Text);
        Render();
    }

    public void Dismiss()
    {
        _hiding = true;
        Hide();
        _hiding = false;
        _session = null;
    }

    // ───────────────────────── Entrada ─────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingText) return;
        Do(s => s.SetQuery(SearchBox.Text));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session == null) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        switch (key)
        {
            case Key.Escape: Cancelled?.Invoke(); return;
            case Key.Enter when shift: PasteSequence(); return;
            case Key.Enter: Paste(); return;
            case Key.D when ctrl:
                if (_session.Selected is { } row) ToggleDoneRequested?.Invoke(row);
                Render();
                return;
            case Key.F4: NextProjectRequested?.Invoke(); return;
            case Key.F when ctrl:
                if (_session.Selected is { } forFunction) UseInFunctionRequested?.Invoke(forFunction);
                return;
            case Key.Down: Do(s => s.MoveRow(1)); return;
            case Key.Up: Do(s => s.MoveRow(-1)); return;
            case Key.PageDown: Do(s => s.MoveRow(VisibleRows)); return;
            case Key.PageUp: Do(s => s.MoveRow(-VisibleRows)); return;
            case Key.Tab when ctrl: SwitchToRingRequested?.Invoke(); return;
            case Key.Tab: Do(s => s.NextSheet(shift ? -1 : 1)); return;
            case Key.F2:
            case Key.T when ctrl:
                Do(s => s.NextTemplate());
                return;
            case Key.Right when SearchBox.CaretIndex == SearchBox.Text.Length && _session.Ghost.Length > 0:
                _session.AcceptGhost();
                SetSearchText(_session.Query);
                Render();
                return;
        }

        if (ctrl && SheetShortcut(key) is { } index)
        {
            Do(s => s.SetSheet(index));
            return;
        }
        e.Handled = false; // el resto lo maneja el cuadro de texto
    }

    /// <summary>Ctrl+0 = Todas, Ctrl+1…9 = hoja N.</summary>
    private static int? SheetShortcut(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0 - 1,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0 - 1,
        _ => null,
    };

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var step = e.Delta > 0 ? -1 : 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Opacity = Math.Clamp(Math.Round(Opacity - step * 0.05, 2), 0.4, 1);
            OpacityChanged?.Invoke(Opacity);
            return;
        }
        Do(s => s.MoveField(step));
    }

    private void Paste()
    {
        if (_session?.Preview is { Length: > 0 } text) PasteRequested?.Invoke(new[] { text }, false);
    }

    /// <summary>Shift+Enter: pega la secuencia de la hoja (o un valor normal si la hoja no tiene secuencia).</summary>
    private void PasteSequence()
    {
        if (_session?.SequenceValues is { Count: > 0 } values) PasteRequested?.Invoke(values, true);
        else Paste();
    }

    private void Do(Action<TaglistSession> change)
    {
        if (_session == null) return;
        change(_session);
        Render();
    }

    private void SetSearchText(string text)
    {
        _settingText = true;
        SearchBox.Text = text;
        SearchBox.CaretIndex = text.Length;
        _settingText = false;
    }

    // ───────────────────────── Dibujo ─────────────────────────

    private void Render()
    {
        if (_session is not { } s) return;

        GhostText.Inlines.Clear();
        GhostText.Inlines.Add(new Run(s.Query) { Foreground = Brushes.Transparent });
        GhostText.Inlines.Add(new Run(s.Ghost) { Foreground = GhostBrush });
        Placeholder.Visibility = s.Query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var sheet = s.SheetIndex == TaglistSession.AllSheets || s.IsAutoAll ? "TODAS" : s.Book.Sheets[s.SheetIndex].Short.ToUpperInvariant();
        SheetText.Text = s.Book.Sheets.Count == 0 ? "" : $"{sheet}{(s.IsAutoAll ? " (auto)" : "")} · {s.Results.Count}";
        SheetText.ToolTip = s.IsAutoAll
            ? $"No había coincidencias en {s.SheetDisplayName}: se muestran todas las hojas. Tab: cambiar de hoja"
            : "Tab: cambiar de hoja · Ctrl+1…9: hoja directa";
        var (done, total) = s.Progress();
        ProgressText.Text = total > 0 ? $"✓ {done}/{total}" : "";

        if (s.Selected is { } selected && selected.Sheet.SequenceColumns.Count > 0)
        {
            SequenceText.Text = "Shift+Enter: " + string.Join(" ⇥ ", selected.Sheet.SequenceColumns.Select(c => selected.Sheet.Columns[c]));
            SequenceText.Visibility = Visibility.Visible;
        }
        else SequenceText.Visibility = Visibility.Collapsed;
        TemplateText.Text = "plantilla: " + s.Template.Name;
        TemplateText.Visibility = s.TemplateIndex > 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderTable(s);
    }

    /// <summary>
    /// Tabla de resultados. Con una hoja: encabezados + columnas con datos. Con «Todas»: cada fila
    /// muestra la hoja y sus propias columnas. La celda verde es la que se pega.
    /// </summary>
    private void RenderTable(TaglistSession s)
    {
        Table.Children.Clear();
        if (s.Results.Count == 0)
        {
            EmptyText.Text = s.Book.Sheets.Count == 0
                ? _emptyMessage ?? "El taglist está vacío."
                : "Sin coincidencias. Tab cambia de hoja.";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }
        EmptyText.Visibility = Visibility.Collapsed;

        var (start, rows) = s.Window(VisibleRows);
        if (s.SheetIndex != TaglistSession.AllSheets && !s.IsAutoAll)
        {
            var sheet = s.Book.Sheets[s.SheetIndex];
            // Sólo las columnas que tienen algo en los resultados (más la de pegar).
            var columns = Enumerable.Range(0, sheet.Columns.Count)
                .Where(c => c == sheet.PasteColumn || s.Results.Any(r => r[c].Length > 0))
                .ToList();

            var header = new List<Cell> { new("", -1, "mark", false) };
            header.AddRange(columns.Select(c => new Cell(sheet.Columns[c], c, "c" + c, false)));
            Table.Children.Add(MakeRow(header, header: true, -1, s));
            for (var k = 0; k < rows.Count; k++)
            {
                var row = rows[k];
                var cells = new List<Cell> { Mark(s, row) };
                cells.AddRange(columns.Select(c => new Cell(row[c], c, "c" + c, c == sheet.DescriptionColumn)));
                Table.Children.Add(MakeRow(cells, header: false, start + k, s));
            }
        }
        else
        {
            for (var k = 0; k < rows.Count; k++)
            {
                var row = rows[k];
                var cells = new List<Cell> { Mark(s, row), new(row.Sheet.Short, -1, "sheet", false) };
                var position = 0;
                foreach (var c in row.UsefulColumns)
                    cells.Add(new Cell(row[c], c, "p" + position++, c == row.Sheet.DescriptionColumn));
                Table.Children.Add(MakeRow(cells, header: false, start + k, s));
            }
        }
    }

    private readonly record struct Cell(string Text, int Column, string Group, bool IsText);

    /// <summary>Primera celda de cada fila: ✓ si ya está hecha, ↺ si la usaste hace poco.</summary>
    private static Cell Mark(TaglistSession s, TagRow row) =>
        new(s.IsDone(row) ? "✓" : s.IsRecent(row) ? "↺" : "", -1, "mark", false);

    private FrameworkElement MakeRow(IReadOnlyList<Cell> cells, bool header, int rowIndex, TaglistSession s)
    {
        var selectedRow = !header && rowIndex == s.RowIndex;
        var grid = new Grid { Background = selectedRow ? RowHighlight : Brushes.Transparent };

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = cell.Group });

            var selectedCell = selectedRow && cell.Column >= 0 && cell.Column == s.FieldIndex;
            var text = new TextBlock
            {
                Text = header || cell.Group == "mark" ? cell.Text : cell.Text.Length > 0 ? cell.Text : "·",
                FontFamily = header || cell.IsText || cell.Column < 0 ? new FontFamily("Segoe UI") : new FontFamily("Consolas"),
                FontSize = header || cell.Column < 0 ? 10 : cell.IsText ? 12 : 13,
                FontWeight = header || cell.Column < 0 || selectedCell ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = selectedCell ? DarkText : cell.Text == "✓" ? Accent : header || cell.Column < 0 || cell.Text.Length == 0 ? Muted : TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = cell.IsText ? 260 : 150,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var border = new Border
            {
                Child = text,
                Padding = new Thickness(6, header ? 0 : 2, 8, header ? 2 : 3),
                Background = selectedCell ? Accent : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
            };
            Grid.SetColumn(border, i);
            grid.Children.Add(border);

            if (header) continue;
            var column = cell.Column;
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonDown += (_, e) =>
            {
                Do(x =>
                {
                    x.SelectRow(rowIndex);
                    if (column >= 0) x.SelectField(column);
                });
                if (e.ClickCount == 2) Paste();
            };
        }
        return grid;
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
