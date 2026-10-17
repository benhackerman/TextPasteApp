using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;

namespace TextPasteApp.Stats;

/// <summary>
/// Cuánto usas la app y cuánto tiempo te ahorra: totales, pegados por día, lo que más pegas
/// y el avance del proyecto actual (filas del taglist ya parametrizadas).
/// </summary>
public partial class StatsWindow : Window
{
    private const int Days = 30;
    private static readonly Brush BarBrush = Frozen("#FF1F9D6B");
    private static readonly Brush BaselineBrush = Frozen("#FFD1D5DB");
    private static readonly Brush Muted = Frozen("#FF6B7280");

    private readonly UsageTracker _usage;
    private readonly string? _project;
    private readonly TaglistBook _book;

    public StatsWindow(UsageTracker usage, string? project, TaglistBook book, int secondsTaglist, int secondsRing, int secondsFunction)
    {
        InitializeComponent();
        _usage = usage;
        _project = project;
        _book = book;

        SinceText.Text = $"Desde el {usage.Data.Since:dd-MM-yyyy}";
        FootText.Text = $"El tiempo ahorrado es una estimación: {secondsTaglist} s por valor del taglist, {secondsRing} s por pegado de la ruleta y {secondsFunction} s por función. Ajústalo en Configuración → General.";
        Refresh();
    }

    private void Refresh()
    {
        var today = _usage.Today();
        var week = _usage.LastDays(7);
        var all = _usage.AllTime();

        Cards.Children.Clear();
        Cards.Children.Add(Card("Hoy", $"{today.Pastes}", $"pegados · {UsageTracker.FormatDuration(today.SecondsSaved)} ahorrados"));
        Cards.Children.Add(Card("Últimos 7 días", $"{week.Pastes}", $"pegados · {UsageTracker.FormatDuration(week.SecondsSaved)} ahorrados"));
        Cards.Children.Add(Card("Total", $"{all.Pastes}", $"{all.TaglistPastes} taglist · {all.RingPastes} ruleta · {all.FunctionPastes} funciones"));
        Cards.Children.Add(Card("Tiempo ahorrado", UsageTracker.FormatDuration(all.SecondsSaved), "en total (estimado)"));

        TopGrid.ItemsSource = _usage.Top(30);
        DrawChart();
        DrawProgress();
    }

    private static Border Card(string title, string value, string detail)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, Foreground = Muted, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        panel.Children.Add(new TextBlock { Text = detail, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = panel,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(8),
            Background = Frozen("#FFF3F5F8"),
        };
    }

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Barras finas, una por día, con tooltip. Una sola serie: el título la nombra (sin leyenda).</summary>
    private void DrawChart()
    {
        Chart.Children.Clear();
        var width = Chart.ActualWidth;
        var height = Chart.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var series = _usage.Series(Days);
        ChartStart.Text = series[0].Date.ToString("dd-MM");
        var max = Math.Max(1, series.Max(d => d.Usage.Pastes));
        const double gap = 2;
        var slot = width / series.Count;
        var barWidth = Math.Max(2, slot - gap);
        var plotHeight = height - 16; // espacio para la etiqueta del máximo

        // Línea base y referencia del máximo (recesivas).
        Chart.Children.Add(new Line { X1 = 0, X2 = width, Y1 = height - 0.5, Y2 = height - 0.5, Stroke = BaselineBrush, StrokeThickness = 1 });
        var maxLabel = new TextBlock { Text = $"{max}", FontSize = 10, Foreground = Muted };
        Canvas.SetLeft(maxLabel, 0);
        Canvas.SetTop(maxLabel, 0);
        Chart.Children.Add(maxLabel);

        for (var i = 0; i < series.Count; i++)
        {
            var (date, usage) = series[i];
            var tip = $"{date:dddd dd-MM}: {usage.Pastes} pegados ({usage.TaglistPastes} taglist, {usage.RingPastes} ruleta, {usage.FunctionPastes} funciones) · {UsageTracker.FormatDuration(usage.SecondsSaved)} ahorrados";

            // Zona de hover más grande que la barra.
            var hit = new Rectangle { Width = slot, Height = height, Fill = Brushes.Transparent, ToolTip = tip };
            Canvas.SetLeft(hit, i * slot);
            Chart.Children.Add(hit);

            if (usage.Pastes == 0) continue;
            var h = Math.Max(3, usage.Pastes / (double)max * plotHeight);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = h,
                Fill = BarBrush,
                RadiusX = Math.Min(4, barWidth / 2),
                RadiusY = Math.Min(4, barWidth / 2),
                ToolTip = tip,
            };
            Canvas.SetLeft(bar, i * slot + gap / 2);
            Canvas.SetTop(bar, height - h);
            Chart.Children.Add(bar);
        }
    }

    private void DrawProgress()
    {
        ProgressPanel.Children.Clear();
        if (_project == null || _book.Sheets.Count == 0)
        {
            ProjectTitle.Text = "Avance del proyecto";
            ProgressPanel.Children.Add(new TextBlock { Text = "Sin taglist configurado.", Foreground = Muted });
            ResetDoneButton.Visibility = Visibility.Collapsed;
            return;
        }

        ProjectTitle.Text = $"Avance · {_project}";
        var totalDone = 0;
        var total = 0;
        foreach (var sheet in _book.Sheets)
        {
            var done = sheet.Rows.Count(r => _usage.IsDone(_project, r.Key));
            totalDone += done;
            total += sheet.Rows.Count;
            ProgressPanel.Children.Add(ProgressRow(sheet.Name, done, sheet.Rows.Count));
        }
        ProgressPanel.Children.Insert(0, ProgressRow("Total", totalDone, total, bold: true));
    }

    private static FrameworkElement ProgressRow(string name, int done, int total, bool bold = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var label = new TextBlock { Text = name, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        var bar = new ProgressBar { Maximum = Math.Max(1, total), Value = done, Height = 8, Foreground = BarBrush, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Text = $"{done}/{total}", HorizontalAlignment = HorizontalAlignment.Right, Foreground = Muted };
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(count, 2);
        grid.Children.Add(label);
        grid.Children.Add(bar);
        grid.Children.Add(count);
        return grid;
    }

    private void ResetDone_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        if (MessageBox.Show(this, $"¿Quitar todas las marcas ✓ del proyecto «{_project}»?", "TextPaste", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        _usage.ClearDone(_project);
        DrawProgress();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
