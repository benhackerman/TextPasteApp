using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Core.Taglist;

namespace TextPasteApp.Settings;

/// <summary>
/// Ventana de configuración. Trabaja sobre una copia de los ajustes; sólo al presionar Guardar
/// se le entregan al controlador.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly AppSettings _settings;
    private readonly ClipboardHistory _history;
    private readonly ObservableCollection<Snippet> _favorites;
    private readonly ObservableCollection<TextTemplate> _templates;
    private HotkeySettings _hotkey;
    private bool _wasPaused;

    public SettingsWindow(AppController controller, AppSettings settings, ClipboardHistory history)
    {
        InitializeComponent();
        _controller = controller;
        _settings = settings;
        _history = history;
        _hotkey = settings.Hotkey;
        _favorites = new ObservableCollection<Snippet>(settings.Favorites);
        _templates = new ObservableCollection<TextTemplate>(settings.Templates);

        LoadGeneral();
        LoadTaglist();
        FavoritesGrid.ItemsSource = _favorites;
        TemplatesGrid.ItemsSource = _templates;
        UpdateTemplatePreview();
    }

    // ───────────────────────── General ─────────────────────────

    private void LoadGeneral()
    {
        HotkeyBox.Text = _hotkey.ToString();
        UpdateHeader();
        TapToStickBox.IsChecked = _settings.TapToStick;
        RestoreClipboardBox.IsChecked = _settings.RestoreClipboardAfterPaste;
        PersistHistoryBox.IsChecked = _settings.PersistHistory;
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        HistoryLimitBox.Text = _settings.HistoryLimit.ToString();
        SlicesBox.ItemsSource = new[] { 6, 8, 10, 12 };
        SlicesBox.SelectedItem = _settings.SlicesPerPage is 6 or 8 or 10 or 12 ? _settings.SlicesPerPage : 8;
        RefreshHistoryList();
    }

    private void UpdateHeader() =>
        HeaderHint.Text = $"Mantén {_hotkey} para abrir la ruleta · suelta para pegar · toque rápido = buscador";

    private void RefreshHistoryList() =>
        HistoryList.ItemsSource = _history.Items.Select(s => s.Text.Replace("\r", "").Replace('\n', ' ')).ToList();

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Mientras se graba un atajo nuevo, el atajo actual no debe abrir la ruleta.
        _wasPaused = _controller.Paused;
        _controller.Paused = true;
        HotkeyBox.Text = "Presiona la combinación…";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _controller.Paused = _wasPaused;
        HotkeyBox.Text = _hotkey.ToString();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.Tab or Key.Escape)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            StatusText.Text = "El atajo necesita al menos Ctrl, Shift, Alt o Win.";
            return;
        }

        _hotkey = new HotkeySettings
        {
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Win = mods.HasFlag(ModifierKeys.Windows),
            Key = key.ToString(),
        };
        HotkeyBox.Text = _hotkey.ToString();
        UpdateHeader();
        StatusText.Text = "Atajo cambiado. Presiona Guardar para aplicarlo.";
    }

    private void HistoryToFavorite_Click(object sender, RoutedEventArgs e)
    {
        var index = HistoryList.SelectedIndex;
        if (index < 0 || index >= _history.Items.Count) return;
        _favorites.Add(new Snippet(_history.Items[index].Text));
        StatusText.Text = "Agregado a favoritos.";
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _controller.ClearHistory();
        RefreshHistoryList();
    }

    // ───────────────────────── Favoritos y plantillas ─────────────────────────

    private void FavoriteUp_Click(object sender, RoutedEventArgs e) => MoveSelected(FavoritesGrid, _favorites, -1);
    private void FavoriteDown_Click(object sender, RoutedEventArgs e) => MoveSelected(FavoritesGrid, _favorites, 1);
    private void FavoriteDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected(FavoritesGrid, _favorites);
    private void TemplateUp_Click(object sender, RoutedEventArgs e) => MoveSelected(TemplatesGrid, _templates, -1);
    private void TemplateDown_Click(object sender, RoutedEventArgs e) => MoveSelected(TemplatesGrid, _templates, 1);
    private void TemplateDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected(TemplatesGrid, _templates);

    private static void MoveSelected<T>(DataGrid grid, ObservableCollection<T> list, int direction)
    {
        if (grid.SelectedItem is not T item) return;
        var from = list.IndexOf(item);
        var to = from + direction;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.Move(from, to);
        grid.SelectedItem = item;
    }

    private static void DeleteSelected<T>(DataGrid grid, ObservableCollection<T> list)
    {
        if (grid.SelectedItem is T item) list.Remove(item);
    }

    private void Templates_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTemplatePreview();

    private void Templates_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(UpdateTemplatePreview); // después de que el valor editado se guarde

    private void Sample_Changed(object sender, TextChangedEventArgs e) => UpdateTemplatePreview();

    private void UpdateTemplatePreview()
    {
        if (TemplatePreview == null || SampleValueBox == null || SampleTagBox == null) return;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["valor"] = SampleValueBox.Text,
            ["texto"] = SampleValueBox.Text,
            ["tag"] = SampleTagBox.Text,
            ["desc"] = "Descripción de ejemplo",
        };
        TemplatePreview.Text = string.Join("\n", _templates
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Pattern))
            .Select(t => $"{t.Name,-22} →  {TemplateEngine.Apply(t.Pattern, fields)}"));
    }

    // ───────────────────────── Taglist ─────────────────────────

    private void LoadTaglist()
    {
        var t = _settings.Taglist;
        FilePathBox.Text = t.FilePath;
        HeaderRowBox.Text = t.HeaderRow.ToString();
        TagColumnBox.Text = t.TagColumn;
        ValueColumnBox.Text = t.ValueColumn;
        DescColumnBox.Text = t.DescriptionColumn;
        if (File.Exists(t.FilePath)) LoadSheets(t.SheetName);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir taglist",
            Filter = "Taglist (*.xlsx;*.xlsm;*.xls;*.csv)|*.xlsx;*.xlsm;*.xls;*.csv|Todos los archivos|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        FilePathBox.Text = dialog.FileName;
        LoadSheets(null);
        ReadColumns_Click(sender, e);
    }

    private void LoadSheets(string? select)
    {
        try
        {
            var sheets = TaglistReader.ListSheets(FilePathBox.Text);
            SheetBox.ItemsSource = sheets;
            SheetBox.SelectedItem = sheets.FirstOrDefault(s => string.Equals(s, select, StringComparison.OrdinalIgnoreCase))
                                    ?? sheets.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            StatusText.Text = "No se pudo abrir el archivo: " + ex.Message;
        }
    }

    private void Sheet_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ReadColumns_Click(sender, e);
    }

    private void ReadColumns_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var headers = TaglistReader.ReadHeaders(FilePathBox.Text, SheetBox.SelectedItem as string ?? "", ParseInt(HeaderRowBox.Text, 1))
                .Where(h => h.Length > 0).ToList();
            foreach (var box in new[] { TagColumnBox, ValueColumnBox, DescColumnBox })
            {
                var current = box.Text;
                box.ItemsSource = headers;
                box.Text = current;
            }
            StatusText.Text = headers.Count > 0
                ? $"Columnas: {string.Join(", ", headers.Take(12))}{(headers.Count > 12 ? "…" : "")}"
                : "No se encontraron encabezados en esa fila.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            StatusText.Text = "No se pudieron leer las columnas: " + ex.Message;
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = TaglistReader.Read(ReadTaglistSettings());
            PreviewGrid.ItemsSource = rows.Take(300)
                .Select(r => new { Tag = r.Tag, Valor = r.Value, Descripción = r.Description })
                .ToList();
            StatusText.Text = $"{rows.Count} filas leídas (mostrando hasta 300).";
            if (rows.Count > 0)
            {
                SampleValueBox.Text = rows[0].Value;
                SampleTagBox.Text = rows[0].Tag;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    private TaglistSettings ReadTaglistSettings() => new()
    {
        FilePath = FilePathBox.Text.Trim().Trim('"'),
        SheetName = SheetBox.SelectedItem as string ?? "",
        HeaderRow = Math.Max(1, ParseInt(HeaderRowBox.Text, 1)),
        TagColumn = TagColumnBox.Text.Trim(),
        ValueColumn = ValueColumnBox.Text.Trim(),
        DescriptionColumn = DescColumnBox.Text.Trim(),
    };

    // ───────────────────────── Guardar ─────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FavoritesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        TemplatesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        _settings.Hotkey = _hotkey;
        _settings.TapToStick = TapToStickBox.IsChecked == true;
        _settings.RestoreClipboardAfterPaste = RestoreClipboardBox.IsChecked == true;
        _settings.PersistHistory = PersistHistoryBox.IsChecked == true;
        _settings.HistoryLimit = Math.Clamp(ParseInt(HistoryLimitBox.Text, 50), 1, 500);
        _settings.SlicesPerPage = SlicesBox.SelectedItem is int n ? n : 8;
        _settings.Favorites = _favorites.Where(f => !string.IsNullOrEmpty(f.Text)).ToList();
        _settings.Templates = _templates.Where(t => !string.IsNullOrWhiteSpace(t.Pattern)).ToList();
        if (_settings.Templates.Count == 0) _settings.Templates.Add(new TextTemplate("Sin plantilla", "{valor}"));
        _settings.ActiveTemplateIndex = Math.Clamp(_settings.ActiveTemplateIndex, 0, _settings.Templates.Count - 1);
        _settings.Taglist = ReadTaglistSettings();

        try
        {
            StartupRegistration.Set(StartWithWindowsBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, "No se pudo configurar el inicio con Windows: " + ex.Message, "TextPaste");
        }

        _controller.ApplySettings(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static int ParseInt(string text, int fallback) => int.TryParse(text.Trim(), out var v) ? v : fallback;
}
