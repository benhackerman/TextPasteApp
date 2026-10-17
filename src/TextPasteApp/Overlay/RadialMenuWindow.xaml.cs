using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Native;

namespace TextPasteApp.Overlay;

/// <summary>
/// La ruleta que aparece bajo el puntero. No tiene lógica propia: dibuja un <see cref="OverlaySession"/>
/// y, en modo buscador (con foco), avisa al controlador lo que hace el usuario mediante <see cref="Command"/>.
/// </summary>
public partial class RadialMenuWindow : Window
{
    // Medidas en píxeles lógicos (DIP). El centro del canvas es el centro de la ruleta.
    private const double Size = 520, C = Size / 2;
    private const double InnerR = 92, OuterR = 222, DeadZone = 55;
    private const int ListRows = 9;
    private const double RowH = 30;

    private static readonly Brush Bg = Frozen("#EE16181E");
    private static readonly Brush SliceFill = Frozen("#E6242833");
    private static readonly Brush EmptyFill = Frozen("#99181A20");
    private static readonly Brush Stroke = Frozen("#33FFFFFF");
    private static readonly Brush TextBrush = Frozen("#FFE8EAED");
    private static readonly Brush Muted = Frozen("#FF9AA0AA");
    private static readonly Brush DarkText = Frozen("#FF0E1116");
    private static readonly Brush HintBg = Frozen("#DD0E1014");

    private IntPtr _hwnd;
    private Win32.POINT _centerPx;
    private bool _sticky;
    private readonly List<(Rect Rect, int Index)> _rowHits = new();
    private bool _isListMode;

    /// <summary>Lo que el usuario pidió (modo buscador). El int es el valor asociado (pasos, sector, fila).</summary>
    public event Action<OverlayCommand, int>? Command;

    public string FilterText => SearchBox.Text;
    public bool IsSticky => _sticky;

    public RadialMenuWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Que nunca robe el foco a la app donde se va a pegar, ni aparezca en Alt+Tab.
            Win32.SetExtendedStyle(_hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW, 0);
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible) PlaceAt(_centerPx, moveCursor: false);
        };

        SearchBox.TextChanged += (_, _) =>
        {
            if (_sticky) Command?.Invoke(OverlayCommand.FilterChanged, 0);
        };
        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += OnMouseMove;
        MouseWheel += (_, e) =>
        {
            if (_sticky) Command?.Invoke(OverlayCommand.Scroll, e.Delta > 0 ? -1 : 1);
        };
        MouseLeftButtonUp += OnLeftClick;
        MouseRightButtonUp += (_, _) =>
        {
            if (_sticky) Command?.Invoke(OverlayCommand.NextTemplate, 0);
        };
        Deactivated += (_, _) =>
        {
            if (_sticky && IsVisible) Command?.Invoke(OverlayCommand.Cancel, 0);
        };
    }

    // ───────────────────────── Mostrar / ocultar ─────────────────────────

    /// <summary>Muestra la ruleta centrada en el puntero (sin quitarle el foco a nadie).</summary>
    internal void Open(Win32.POINT cursor)
    {
        _sticky = false;
        SearchBox.Text = "";
        SearchBox.Visibility = Visibility.Collapsed;

        new WindowInteropHelper(this).EnsureHandle();
        Opacity = 0;          // evita un parpadeo en la posición anterior
        Show();
        PlaceAt(cursor, moveCursor: true);
        Opacity = 1;
    }

    /// <summary>Pasa a modo buscador: toma el foco para poder escribir.</summary>
    public void EnterSticky()
    {
        _sticky = true;
        SearchBox.Visibility = Visibility.Visible;
        Win32.SetExtendedStyle(_hwnd, 0, Win32.WS_EX_NOACTIVATE);
        Paster.SendDummyKey(); // Windows sólo deja tomar el foco a quien recibió la última entrada
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    public void Dismiss()
    {
        _sticky = false;
        SearchBox.Visibility = Visibility.Collapsed;
        Hide();
        if (_hwnd != IntPtr.Zero)
            Win32.SetExtendedStyle(_hwnd, Win32.WS_EX_NOACTIVATE, 0);
    }

    /// <summary>
    /// Centra la ventana en el punto (píxeles físicos). Si queda cortada por el borde de la pantalla,
    /// la corre hacia adentro y mueve el puntero al nuevo centro.
    /// </summary>
    private void PlaceAt(Win32.POINT cursor, bool moveCursor)
    {
        var (work, scale) = Win32.MonitorAt(cursor);
        var sizePx = (int)Math.Round(Size * scale);
        var half = sizePx / 2;

        var cx = work.Right - work.Left >= sizePx ? Math.Clamp(cursor.X, work.Left + half, work.Right - half) : cursor.X;
        var cy = work.Bottom - work.Top >= sizePx ? Math.Clamp(cursor.Y, work.Top + half, work.Bottom - half) : cursor.Y;

        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, cx - half, cy - half, sizePx, sizePx, Win32.SWP_NOACTIVATE);
        if (moveCursor && (cx != cursor.X || cy != cursor.Y))
            Win32.SetCursorPos(cx, cy);

        _centerPx = new Win32.POINT { X = cx, Y = cy };
    }

    // ───────────────────────── Hit testing ─────────────────────────

    /// <summary>Sector bajo un punto de pantalla (píxeles físicos, como los da el hook del mouse).</summary>
    internal int SliceAtScreen(Win32.POINT screen, int sliceCount)
    {
        if (!IsVisible || PresentationSource.FromVisual(this) == null) return -1;
        var p = Root.PointFromScreen(new Point(screen.X, screen.Y));
        return RadialGeometry.SliceAt(p.X - C, p.Y - C, sliceCount, DeadZone);
    }

    private int SliceAtDip(Point p, int sliceCount) => RadialGeometry.SliceAt(p.X - C, p.Y - C, sliceCount, DeadZone);

    private int RowAt(Point p)
    {
        foreach (var (rect, index) in _rowHits)
            if (rect.Contains(p)) return index;
        return -1;
    }

    // ───────────────────────── Eventos (modo buscador) ─────────────────────────

    private int _lastSliceCount = 8;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_sticky || _isListMode) return;
        Command?.Invoke(OverlayCommand.HoverSlice, SliceAtDip(e.GetPosition(Root), _lastSliceCount));
    }

    private void OnLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (!_sticky) return;
        var p = e.GetPosition(Root);
        if (_isListMode)
        {
            var row = RowAt(p);
            if (row < 0) return;
            Command?.Invoke(OverlayCommand.SelectRow, row);
            Command?.Invoke(OverlayCommand.Confirm, 0);
        }
        else
        {
            var slice = SliceAtDip(p, _lastSliceCount);
            if (slice < 0) return;
            Command?.Invoke(OverlayCommand.HoverSlice, slice);
            Command?.Invoke(OverlayCommand.Confirm, 0);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_sticky) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        (OverlayCommand, int)? cmd = e.Key switch
        {
            Key.Escape => (OverlayCommand.Cancel, 0),
            Key.Enter => (OverlayCommand.Confirm, 0),
            Key.Tab => (shift ? OverlayCommand.PrevSource : OverlayCommand.NextSource, 0),
            Key.Up => (OverlayCommand.Move, -1),
            Key.Down => (OverlayCommand.Move, 1),
            Key.PageUp => (OverlayCommand.Scroll, _isListMode ? -ListRows : -1),
            Key.PageDown => (OverlayCommand.Scroll, _isListMode ? ListRows : 1),
            Key.F2 => (OverlayCommand.NextTemplate, 0),
            Key.T when ctrl => (OverlayCommand.NextTemplate, 0),
            _ => null,
        };

        if (cmd is { } c)
        {
            e.Handled = true;
            Command?.Invoke(c.Item1, c.Item2);
        }
    }

    // ───────────────────────── Dibujo ─────────────────────────

    /// <summary>Redibuja todo según el estado. emptyMessage se muestra si la fuente no tiene elementos.</summary>
    public void Render(OverlaySession s, string? emptyMessage = null)
    {
        Drawing.Children.Clear();
        _rowHits.Clear();
        _isListMode = s.IsListMode;
        _lastSliceCount = s.SlicesPerPage;

        var accent = AccentFor(s.Source);
        if (s.IsListMode) DrawList(s, accent, emptyMessage);
        else DrawRing(s, accent, emptyMessage);
        DrawHint(s);

        Canvas.SetLeft(SearchBox, C - SearchBox.Width / 2);
        Canvas.SetTop(SearchBox, 4);
    }

    private void DrawRing(OverlaySession s, Brush accent, string? emptyMessage)
    {
        var n = s.SlicesPerPage;
        var items = s.PageItems;
        var selected = s.SelectedSlice;
        var step = 360.0 / n;
        const double gap = 1.2;

        AddEllipse(OuterR + 8, Frozen("#33000000"), null, 0);

        for (var i = 0; i < n; i++)
        {
            var mid = RadialGeometry.SliceCenterAngle(i, n);
            var has = i < items.Count;
            var isSel = i == selected;

            Drawing.Children.Add(new Path
            {
                Data = SliceGeometry(mid - step / 2 + gap, mid + step / 2 - gap, InnerR, isSel ? OuterR + 6 : OuterR),
                Fill = isSel ? accent : has ? SliceFill : EmptyFill,
                Stroke = Stroke,
                StrokeThickness = 1,
            });

            if (!has) continue;
            var (x, y) = RadialGeometry.PointAt(mid, (InnerR + OuterR) / 2 + 4);
            AddText(items[i].Title, C + x - 56, C + y - 18, 112, 13, isSel ? DarkText : TextBrush,
                isSel ? FontWeights.SemiBold : FontWeights.Normal, maxLines: 2, height: 36, center: true);
        }

        // Disco central
        AddEllipse(InnerR - 8, Bg, accent, 2);
        AddText(s.Source.DisplayName().ToUpperInvariant(), C - 70, C - 62, 140, 11, accent, FontWeights.Bold);
        DrawSourceDots(s, C - 44, accent);

        if (s.Preview is { } preview)
        {
            AddText(preview, C - 66, C - 30, 132, 13, TextBrush, FontWeights.SemiBold, maxLines: 3, height: 54, center: true);
        }
        else
        {
            var msg = s.Items.Count == 0 ? emptyMessage ?? "Vacío" : $"{s.Items.Count} textos";
            AddText(msg, C - 66, C - 22, 132, 12, Muted, FontWeights.Normal, maxLines: 3, height: 50, center: true);
        }

        if (s.PageCount > 1)
            AddText($"pág. {s.Page + 1}/{s.PageCount}", C - 50, C + 30, 100, 11, Muted, FontWeights.Normal);
        if (s.TemplateIndex > 0)
            AddChip(s.Template.Name, C + 48, accent);
    }

    private void DrawList(OverlaySession s, Brush accent, string? emptyMessage)
    {
        AddEllipse(OuterR + 8, Frozen("#33000000"), null, 0);
        AddEllipse(OuterR, Bg, accent, 2);

        AddText(s.Source.DisplayName().ToUpperInvariant(), C - 100, 62, 200, 11, accent, FontWeights.Bold);
        DrawSourceDots(s, 80, accent);
        var countText = s.Filter.Length > 0 ? $"{s.Items.Count} coincidencias" : $"{s.Items.Count} filas";
        AddText(countText, C - 100, 90, 200, 11, Muted, FontWeights.Normal);

        if (s.Items.Count == 0)
        {
            AddText(emptyMessage ?? "Sin resultados", C - 140, C - 30, 280, 13, Muted, FontWeights.Normal,
                maxLines: 4, height: 80, center: true);
            return;
        }

        var (start, rows) = s.ListWindow(ListRows);
        var top = C - ListRows * RowH / 2 + 4;
        for (var r = 0; r < rows.Count; r++)
        {
            var index = start + r;
            var y = top + r * RowH;
            var isSel = index == s.SelectedIndex;
            var distance = Math.Abs(index - s.SelectedIndex);
            var opacity = isSel ? 1 : Math.Max(0.25, 1 - distance * 0.16); // efecto "tambor"

            if (isSel)
            {
                var bar = new Rectangle { Width = 330, Height = RowH - 2, RadiusX = 7, RadiusY = 7, Fill = accent };
                Canvas.SetLeft(bar, C - 165);
                Canvas.SetTop(bar, y);
                Drawing.Children.Add(bar);
            }

            var fg = isSel ? DarkText : TextBrush;
            var tag = AddText(rows[r].Title, C - 152, y + 5, 180, 14, fg, isSel ? FontWeights.Bold : FontWeights.Normal);
            var value = AddText(rows[r].Subtitle, C + 30, y + 5, 122, 14, fg, FontWeights.SemiBold, align: TextAlignment.Right);
            value.FontFamily = new FontFamily("Consolas");
            tag.Opacity = value.Opacity = opacity;

            _rowHits.Add((new Rect(C - 165, y, 330, RowH), index));
        }

        var footer = top + ListRows * RowH + 6;
        if (s.Selected is { } sel)
        {
            if (sel.Description.Length > 0)
                AddText(sel.Description, C - 150, footer, 300, 11, Muted, FontWeights.Normal);
            AddText("→ " + s.Preview, C - 150, footer + 17, 300, 14, accent, FontWeights.Bold);
        }
        if (s.TemplateIndex > 0)
            AddChip(s.Template.Name, footer + 40, accent);
    }

    /// <summary>Tres puntos que indican la fuente activa (la activa es una píldora alargada).</summary>
    private void DrawSourceDots(OverlaySession s, double y, Brush accent)
    {
        var kinds = new[] { SourceKind.History, SourceKind.Favorites, SourceKind.Taglist };
        const double dot = 6, pill = 16, spacing = 6;
        var x = C - (pill + 2 * dot + 2 * spacing) / 2;
        foreach (var kind in kinds)
        {
            var active = kind == s.Source;
            var shape = new Rectangle
            {
                Width = active ? pill : dot,
                Height = dot,
                RadiusX = dot / 2,
                RadiusY = dot / 2,
                Fill = active ? accent : Muted,
                Opacity = active ? 1 : 0.5,
            };
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            Drawing.Children.Add(shape);
            x += shape.Width + spacing;
        }
    }

    private void DrawHint(OverlaySession s)
    {
        var text = _sticky
            ? "Escribe para buscar · ↑↓ mover · Enter pegar · Tab fuente · F2 plantilla · Esc"
            : s.IsListMode
                ? "Suelta para pegar · Rueda: mover · Tab: fuente · Clic der.: plantilla · Esc"
                : "Suelta para pegar · Rueda: página · Tab: fuente · Clic der.: plantilla · Esc";

        var block = new TextBlock { Text = text, FontSize = 11, Foreground = Muted };
        var pill = new Border
        {
            Child = block,
            Background = HintBg,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 4),
        };
        pill.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(pill, C - pill.DesiredSize.Width / 2);
        Canvas.SetTop(pill, Size - 30);
        Drawing.Children.Add(pill);
    }

    private void AddChip(string text, double y, Brush accent)
    {
        var chip = new Border
        {
            Child = new TextBlock { Text = "✎ " + text, FontSize = 11, Foreground = accent },
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 2),
            Background = Bg,
        };
        chip.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chip, C - chip.DesiredSize.Width / 2);
        Canvas.SetTop(chip, y);
        Drawing.Children.Add(chip);
    }

    // ───────────────────────── Helpers de dibujo ─────────────────────────

    private TextBlock AddText(string text, double x, double y, double width, double size, Brush fg, FontWeight weight,
        int maxLines = 1, double height = 0, bool center = false, TextAlignment? align = null)
    {
        var block = new TextBlock
        {
            Text = text.Replace("\r", "").Replace('\n', ' '),
            Width = width,
            FontSize = size,
            Foreground = fg,
            FontWeight = weight,
            TextAlignment = align ?? TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = maxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (height > 0)
        {
            block.MaxHeight = height;
            if (center)
            {
                // Centrado vertical dentro del alto disponible.
                block.Measure(new System.Windows.Size(width, height));
                y += Math.Max(0, (height - block.DesiredSize.Height) / 2);
            }
        }
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        Drawing.Children.Add(block);
        return block;
    }

    private void AddEllipse(double radius, Brush? fill, Brush? stroke, double thickness)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(e, C - radius);
        Canvas.SetTop(e, C - radius);
        Drawing.Children.Add(e);
    }

    private static Geometry SliceGeometry(double a0, double a1, double r1, double r2)
    {
        static Point P(double angle, double r)
        {
            var (x, y) = RadialGeometry.PointAt(angle, r);
            return new Point(C + x, C + y);
        }

        var large = a1 - a0 > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(a0, r1), isFilled: true, isClosed: true);
            ctx.LineTo(P(a0, r2), true, true);
            ctx.ArcTo(P(a1, r2), new System.Windows.Size(r2, r2), 0, large, SweepDirection.Clockwise, true, true);
            ctx.LineTo(P(a1, r1), true, true);
            ctx.ArcTo(P(a0, r1), new System.Windows.Size(r1, r1), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    public static Brush AccentFor(SourceKind kind) => kind switch
    {
        SourceKind.History => Frozen("#FF4C9AFF"),
        SourceKind.Favorites => Frozen("#FFFFB547"),
        _ => Frozen("#FF3DDC97"),
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
