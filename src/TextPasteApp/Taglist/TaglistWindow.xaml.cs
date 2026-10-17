using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TextPasteApp.Core.Logic;
using TextPasteApp.Native;

namespace TextPasteApp.Taglist;

/// <summary>
/// Buscador del taglist. Toma el foco para poder escribir, maneja sus propias teclas sobre un
/// <see cref="TaglistSession"/> y al final le pide al controlador que pegue (o que cancele).
/// </summary>
public partial class TaglistWindow : Window
{
    private const int VisibleRows = 8;

    private static readonly Brush Accent = Frozen("#FF3DDC97");
    private static readonly Brush AccentSoft = Frozen("#203DDC97");
    private static readonly Brush TextBrush = Frozen("#FFE8EAED");
    private static readonly Brush Muted = Frozen("#FF9AA0AA");
    private static readonly Brush DarkText = Frozen("#FF0E1116");
    private static readonly Brush ChipBorder = Frozen("#FF3A3F4B");
    private static readonly Brush GhostBrush = Frozen("#FF5B6170");

    private IntPtr _hwnd;
    private TaglistSession? _session;
    private string? _emptyMessage;
    private bool _hiding;
    private bool _settingText;
    private FrameworkElement? _selectedField;

    /// <summary>El usuario eligió un texto (ya con la plantilla aplicada).</summary>
    public event Action<string>? PasteRequested;
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
        TemplateButton.Click += (_, _) => Do(s => s.NextTemplate());
        Deactivated += (_, _) =>
        {
            if (IsOpen && !_hiding) Cancelled?.Invoke(); // clic fuera = cancelar
        };
    }

    // ───────────────────────── Mostrar / ocultar ─────────────────────────

    internal void Open(TaglistSession session, Win32.POINT cursor, double opacity, string profileText, string? emptyMessage)
    {
        _session = session;
        _emptyMessage = emptyMessage;
        ProfileText.Text = profileText;
        SetSearchText("");

        new WindowInteropHelper(this).EnsureHandle();
        Opacity = 0;
        Show();
        WindowPlacement.CenterOn(_hwnd, cursor, Width, Height);
        Opacity = Math.Clamp(opacity, 0.4, 1);

        // Windows sólo deja tomar el foco a quien recibió la última entrada.
        Paster.SendDummyKey();
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
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
            case Key.Enter: Paste(); return;
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
        if (_session?.Preview is { Length: > 0 } text) PasteRequested?.Invoke(text);
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

        RenderSheets(s);

        GhostText.Inlines.Clear();
        GhostText.Inlines.Add(new Run(s.Query) { Foreground = Brushes.Transparent });
        GhostText.Inlines.Add(new Run(s.Ghost) { Foreground = GhostBrush });
        Placeholder.Visibility = s.Query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var where = s.SheetIndex == TaglistSession.AllSheets ? "todas las hojas" : s.SheetDisplayName;
        CountText.Text = s.Book.Sheets.Count == 0
            ? ""
            : $"{s.Results.Count}{(s.Results.Count >= TaglistSession.MaxResults && s.Query.Length > 0 ? "+" : "")} " +
              $"{(s.Query.Length > 0 ? "coincidencias" : "filas")} en {where}";

        RenderResults(s);
        RenderFields(s);

        PreviewText.Text = s.Preview is { } p ? "→ " + p : "";
        TemplateButton.Content = "Plantilla: " + s.Template.Name;
    }

    private void RenderSheets(TaglistSession s)
    {
        SheetsPanel.Children.Clear();
        AddChip("Todas", TaglistSession.AllSheets, s, "Ctrl+0");
        for (var i = 0; i < s.Book.Sheets.Count; i++)
            AddChip(s.Book.Sheets[i].Name, i, s, i < 9 ? $"Ctrl+{i + 1}" : null);
    }

    private void AddChip(string name, int index, TaglistSession s, string? shortcut)
    {
        var active = s.SheetIndex == index;
        var label = s.Query.Length > 0 ? $"{name}  {s.CountIn(index)}" : name;
        var chip = new Border
        {
            Child = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = active ? DarkText : TextBrush,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            },
            Background = active ? Accent : Brushes.Transparent,
            BorderBrush = active ? Accent : ChipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 5, 11, 6),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = shortcut,
        };
        chip.MouseLeftButtonUp += (_, _) => Do(x => x.SetSheet(index));
        SheetsPanel.Children.Add(chip);
    }

    private void RenderResults(TaglistSession s)
    {
        ResultsPanel.Children.Clear();
        if (s.Results.Count == 0)
        {
            var msg = s.Book.Sheets.Count == 0
                ? _emptyMessage ?? "El taglist está vacío."
                : "Sin coincidencias en esta hoja. Tab para buscar en otra, o en «Todas».";
            ResultsPanel.Children.Add(new TextBlock { Text = msg, Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 12, 6, 0) });
            return;
        }

        var showSheet = s.SheetIndex == TaglistSession.AllSheets;
        var (start, rows) = s.Window(VisibleRows);
        for (var k = 0; k < rows.Count; k++)
        {
            var index = start + k;
            var row = rows[k];
            var on = index == s.RowIndex;
            var fg = on ? DarkText : TextBrush;

            var top = new DockPanel();
            if (showSheet)
            {
                var badge = new TextBlock { Text = row.Sheet.Short, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = fg, Opacity = 0.7, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(badge, Dock.Right);
                top.Children.Add(badge);
            }
            top.Children.Add(new TextBlock
            {
                Text = row.Title.Length > 0 && row.Title != row.PasteValue ? $"{row.PasteValue}  ·  {row.Title}" : row.PasteValue,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                FontWeight = on ? FontWeights.Bold : FontWeights.Normal,
                Foreground = fg,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var content = new StackPanel();
            content.Children.Add(top);
            if (row.Description.Length > 0)
                content.Children.Add(new TextBlock { Text = row.Description, FontSize = 11, Foreground = fg, Opacity = on ? 0.85 : 0.6, TextTrimming = TextTrimming.CharacterEllipsis });

            var item = new Border
            {
                Child = content,
                Background = on ? Accent : Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
            };
            item.MouseLeftButtonDown += (_, e) =>
            {
                Do(x => x.SelectRow(index));
                if (e.ClickCount == 2) Paste();
            };
            ResultsPanel.Children.Add(item);
        }
    }

    private void RenderFields(TaglistSession s)
    {
        FieldsPanel.Children.Clear();
        _selectedField = null;
        if (s.Selected is not { } row) return;

        foreach (var i in row.UsefulColumns) // las columnas vacías no se muestran
        {
            var index = i;
            var on = i == s.FieldIndex;
            var name = row.Sheet.Columns[i] + (i == row.Sheet.PasteColumn ? "  · por defecto" : "");

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = on ? Accent : Muted });
            content.Children.Add(new TextBlock
            {
                Text = row[i].Length > 0 ? row[i] : "—",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = on ? Brushes.White : TextBrush,
                TextWrapping = TextWrapping.Wrap,
            });

            var field = new Border
            {
                Child = content,
                Background = on ? AccentSoft : Brushes.Transparent,
                BorderBrush = on ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 5, 9, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
            };
            field.MouseLeftButtonDown += (_, e) =>
            {
                Do(x => x.SelectField(index));
                if (e.ClickCount == 2) Paste();
            };
            FieldsPanel.Children.Add(field);
            if (on) _selectedField = field;
        }

        // Que la columna elegida siempre quede a la vista aunque la hoja tenga muchas.
        Dispatcher.BeginInvoke(() => _selectedField?.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
