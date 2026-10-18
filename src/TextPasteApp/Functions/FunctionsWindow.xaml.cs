using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Native;

namespace TextPasteApp.Functions;

/// <summary>
/// Buscador compacto de funciones (código AWL guardado). Aparece bajo el puntero; al elegir una función
/// muestra sus parámetros {{nombre}} ya propuestos (de la fila del taglist o del último uso) y la vista previa.
/// </summary>
public partial class FunctionsWindow : Window
{
    private const int VisibleRows = 5;
    private const int PreviewLines = 8;

    private static readonly Brush Accent = Frozen("#FFB98CFF");      // violeta: funciones
    private static readonly Brush RowAccent = Frozen("#FF3DDC97");   // verde: viene del taglist
    private static readonly Brush TextBrush = Frozen("#FFE8EAED");
    private static readonly Brush Muted = Frozen("#FF9AA0AA");
    private static readonly Brush DarkText = Frozen("#FF0E1116");
    private static readonly Brush BoxBrush = Frozen("#FF0F1115");
    private static readonly Brush BoxBorder = Frozen("#FF3A3F4B");

    // Colores de la vista previa: instrucción / variable / constante / comentario.
    private static readonly Brush InstructionBrush = Frozen("#FF7AA2F7");
    private static readonly Brush VariableBrush = Frozen("#FFB98CFF");
    private static readonly Brush ConstantBrush = Frozen("#FFE0AF68");
    private static readonly Brush CommentBrush = Frozen("#FF6B7280");

    private IntPtr _hwnd;
    private FunctionSession? _session;
    private Win32.POINT _anchor;
    private bool _hiding;
    private CodeFunction? _paramsFor;

    public event Action<CodeFunction, string, IReadOnlyDictionary<string, string>>? PasteRequested;
    public event Action? Cancelled;
    public event Action? NewFromClipboardRequested;
    public event Action<CodeFunction>? EditRequested;

    public FunctionSession? Session => _session;
    public bool IsOpen => IsVisible && _session != null;

    public FunctionsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.SetExtendedStyle(_hwnd, Win32.WS_EX_TOOLWINDOW, 0);
        };
        SearchBox.TextChanged += (_, _) => Do(s => s.SetQuery(SearchBox.Text));
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            Do(s => s.Move(e.Delta > 0 ? -1 : 1));
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible) WindowPlacement.PlaceNear(_hwnd, _anchor);
        };
        Deactivated += (_, _) =>
        {
            if (IsOpen && !_hiding) Cancelled?.Invoke();
        };
    }

    internal void Open(FunctionSession session, Win32.POINT cursor, double opacity)
    {
        _session = session;
        _anchor = cursor;
        _paramsFor = null;
        SearchBox.Text = "";

        new WindowInteropHelper(this).EnsureHandle();
        Opacity = 0;
        Render();
        Show();
        UpdateLayout();
        WindowPlacement.PlaceNear(_hwnd, _anchor);
        Opacity = Math.Clamp(opacity, 0.4, 1);

        Paster.SendDummyKey();
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    public void Dismiss()
    {
        _hiding = true;
        Hide();
        _hiding = false;
        _session = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session == null) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var inSearch = Keyboard.FocusedElement == SearchBox;

        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; Cancelled?.Invoke(); return;
            case Key.Enter: e.Handled = true; Paste(); return;
            case Key.Down when inSearch: e.Handled = true; Do(s => s.Move(1)); return;
            case Key.Up when inSearch: e.Handled = true; Do(s => s.Move(-1)); return;
            case Key.N when ctrl: e.Handled = true; NewFromClipboardRequested?.Invoke(); return;
            case Key.E when ctrl:
                e.Handled = true;
                if (_session.Selected is { } f) EditRequested?.Invoke(f);
                return;
        }
    }

    private void Paste()
    {
        if (_session?.Selected is { } f && _session.Preview is { } code)
            PasteRequested?.Invoke(f, code, _session.CurrentValues);
    }

    private void Do(Action<FunctionSession> change)
    {
        if (_session == null) return;
        change(_session);
        Render();
    }

    // ───────────────────────── Dibujo ─────────────────────────

    private void Render()
    {
        if (_session is not { } s) return;
        Placeholder.Visibility = s.Query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RowText.Text = s.RowLabel is { } row ? "con " + row : "";
        CountText.Text = $"FUNCIONES · {s.Results.Count}";

        RenderResults(s);
        if (!ReferenceEquals(_paramsFor, s.Selected)) RenderParams(s); // no rearmar mientras escribes un parámetro
        RenderPreview(s);
    }

    private void RenderResults(FunctionSession s)
    {
        ResultsPanel.Children.Clear();
        if (s.Results.Count == 0)
        {
            EmptyText.Text = s.Query.Length == 0
                ? "Todavía no hay funciones. Copia un bloque de código y presiona Ctrl+N para guardarlo."
                : "Sin coincidencias. Ctrl+N guarda lo que tienes copiado como función nueva.";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }
        EmptyText.Visibility = Visibility.Collapsed;

        var start = Math.Clamp(s.Index - VisibleRows / 2 + 1, 0, Math.Max(0, s.Results.Count - VisibleRows));
        for (var i = start; i < Math.Min(s.Results.Count, start + VisibleRows); i++)
        {
            var f = s.Results[i];
            var on = i == s.Index;
            var grid = new Grid { Background = on ? Accent : Brushes.Transparent, Cursor = Cursors.Hand };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "name" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "cat" });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = new TextBlock { Text = f.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = on ? DarkText : TextBrush, Margin = new Thickness(6, 2, 10, 3), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
            var cat = new TextBlock { Text = f.Category, FontSize = 11, Foreground = on ? DarkText : Muted, Margin = new Thickness(0, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
            var desc = new TextBlock { Text = f.Description, FontSize = 11, Foreground = on ? DarkText : Muted, Margin = new Thickness(0, 3, 6, 3), MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cat, 1);
            Grid.SetColumn(desc, 2);
            grid.Children.Add(name);
            grid.Children.Add(cat);
            grid.Children.Add(desc);

            var index = i;
            grid.MouseLeftButtonDown += (_, e) =>
            {
                Do(x => x.Select(index));
                if (e.ClickCount == 2) Paste();
            };
            ResultsPanel.Children.Add(new Border { Child = grid, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 1) });
        }
    }

    /// <summary>
    /// Un cuadro por parámetro {{…}} (verde = tomado de la fila del taglist) y uno por variable AWL reconocida
    /// (violeta): lo que escribas ahí reemplaza esa variable en todo el código.
    /// </summary>
    private void RenderParams(FunctionSession s)
    {
        _paramsFor = s.Selected;
        ParamsPanel.Children.Clear();
        foreach (var name in s.Parameters)
        {
            var parameter = name;
            var fromRow = s.IsFromRow(parameter);
            var box = MakeBox(s.ValueOf(parameter), fromRow ? RowAccent : Brushes.White, fromRow ? RowAccent : BoxBorder);
            box.TextChanged += (_, _) =>
            {
                s.SetValue(parameter, box.Text);
                box.Foreground = Brushes.White;
                box.BorderBrush = BoxBorder;
                RenderPreview(s);
            };
            ParamsPanel.Children.Add(Field(parameter, box));
        }

        foreach (var variable in s.Variables)
        {
            var v = variable;
            var box = MakeBox(s.VariableValue(v), VariableBrush, BoxBorder);
            box.ToolTip = $"{v.Display.Trim()} aparece {v.Count} {(v.Count == 1 ? "vez" : "veces")}. Escribe otra y se cambia en todo el código.";
            box.TextChanged += (_, _) =>
            {
                s.SetVariable(v.Key, box.Text);
                var changed = s.IsVariableChanged(v);
                box.Foreground = changed ? RowAccent : VariableBrush;
                box.BorderBrush = changed ? RowAccent : BoxBorder;
                RenderPreview(s);
            };
            ParamsPanel.Children.Add(Field(v.Count > 1 ? $"×{v.Count}" : "", box));
        }
        ParamsPanel.Visibility = ParamsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBox MakeBox(string text, Brush foreground, Brush border) => new()
    {
        Text = text,
        MinWidth = 70,
        MaxWidth = 170,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Background = BoxBrush,
        Foreground = foreground,
        CaretBrush = Brushes.White,
        BorderBrush = border,
        Padding = new Thickness(4, 1, 4, 1),
    };

    private static FrameworkElement Field(string label, TextBox box)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        if (label.Length > 0)
            item.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        item.Children.Add(box);
        return item;
    }

    /// <summary>Vista previa coloreada: así se ve qué reconoció como instrucción y qué como variable.</summary>
    private void RenderPreview(FunctionSession s)
    {
        var code = s.Preview;
        PreviewBorder.Visibility = code == null ? Visibility.Collapsed : Visibility.Visible;
        if (code == null) return;

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var visible = string.Join("\n", lines.Take(PreviewLines));
        var changedKeys = s.Variables.Where(s.IsVariableChanged).Select(v => AwlParser.KeyOf(s.VariableValue(v))).ToHashSet();

        PreviewText.Inlines.Clear();
        foreach (var token in AwlParser.Tokenize(visible))
        {
            var run = new System.Windows.Documents.Run(token.Text);
            switch (token.Kind)
            {
                case AwlTokenKind.Instruction: run.Foreground = InstructionBrush; break;
                case AwlTokenKind.Variable:
                    run.Foreground = changedKeys.Contains(token.Key) ? RowAccent : VariableBrush;
                    if (changedKeys.Contains(token.Key)) run.FontWeight = FontWeights.Bold;
                    break;
                case AwlTokenKind.Constant: run.Foreground = ConstantBrush; break;
                case AwlTokenKind.Comment: run.Foreground = CommentBrush; break;
                case AwlTokenKind.Label: run.FontWeight = FontWeights.Bold; break;
                case AwlTokenKind.Parameter: run.Foreground = Frozen("#FFF7768E"); break; // parámetro sin valor
            }
            PreviewText.Inlines.Add(run);
        }
        if (lines.Length > PreviewLines)
            PreviewText.Inlines.Add(new System.Windows.Documents.Run($"\n… {lines.Length - PreviewLines} líneas más") { Foreground = CommentBrush });
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
