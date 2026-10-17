using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Core.Storage;
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
    private HotkeySettings _taglistHotkey;
    private bool _wasPaused;

    // Perfiles de taglist
    private readonly List<TaglistProfile> _profiles;
    private TaglistProfile _profile;
    private ObservableCollection<SheetProfile> _sheets = new();
    private bool _loadingProfiles;

    public SettingsWindow(AppController controller, AppSettings settings, ClipboardHistory history)
    {
        InitializeComponent();
        _controller = controller;
        _settings = settings;
        _history = history;
        _hotkey = settings.Hotkey;
        _taglistHotkey = settings.TaglistHotkey;
        _favorites = new ObservableCollection<Snippet>(settings.Favorites);
        _templates = new ObservableCollection<TextTemplate>(settings.Templates);
        _profiles = settings.TaglistProfiles;
        _profile = settings.CurrentProfile();

        LoadGeneral();
        FavoritesGrid.ItemsSource = _favorites;
        TemplatesGrid.ItemsSource = _templates;
        LoadProfiles(_profile.Name);
        UpdateTemplatePreview();
    }

    // ───────────────────────── General ─────────────────────────

    private void LoadGeneral()
    {
        HotkeyBox.Text = _hotkey.ToString();
        TaglistHotkeyBox.Text = _taglistHotkey.ToString();
        UpdateHeader();
        OpacitySlider.Value = Math.Round(_settings.Opacity * 100);
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
        HeaderHint.Text = $"Ruleta: mantén {_hotkey} y suelta para pegar · Taglist: {_taglistHotkey}";

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText != null) OpacityText.Text = $"{OpacitySlider.Value:0} %";
    }

    private void RefreshHistoryList() =>
        HistoryList.ItemsSource = _history.Items.Select(s => s.Text.Replace("\r", "").Replace('\n', ' ')).ToList();

    private bool IsTaglistBox(object sender) => (sender as FrameworkElement)?.Tag as string == "taglist";

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Mientras se graba un atajo nuevo, los atajos actuales no deben abrir nada.
        _wasPaused = _controller.Paused;
        _controller.Paused = true;
        ((TextBox)sender).Text = "Presiona la combinación…";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _controller.Paused = _wasPaused;
        ((TextBox)sender).Text = (IsTaglistBox(sender) ? _taglistHotkey : _hotkey).ToString();
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

        var hotkey = new HotkeySettings
        {
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Win = mods.HasFlag(ModifierKeys.Windows),
            Key = key.ToString(),
        };
        if (IsTaglistBox(sender)) _taglistHotkey = hotkey; else _hotkey = hotkey;
        ((TextBox)sender).Text = hotkey.ToString();
        UpdateHeader();
        StatusText.Text = _hotkey.ToString() == _taglistHotkey.ToString()
            ? "Los dos atajos son iguales: elige combinaciones distintas."
            : "Atajo cambiado. Presiona Guardar para aplicarlo.";
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
    private void SheetUp_Click(object sender, RoutedEventArgs e) => MoveSelected(SheetsGrid, _sheets, -1);
    private void SheetDown_Click(object sender, RoutedEventArgs e) => MoveSelected(SheetsGrid, _sheets, 1);

    private static void MoveSelected<T>(DataGrid grid, ObservableCollection<T> list, int direction)
    {
        grid.CommitEdit(DataGridEditingUnit.Row, true);
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

    // ───────────────────────── Perfiles de taglist ─────────────────────────

    private void LoadProfiles(string select)
    {
        _loadingProfiles = true;
        ProfileBox.ItemsSource = _profiles.Select(p => p.Name).ToList();
        ProfileBox.SelectedItem = _profiles.Any(p => p.Name == select) ? select : _profiles[0].Name;
        _loadingProfiles = false;
        _profile = _profiles.First(p => p.Name == (string)ProfileBox.SelectedItem);
        ShowProfile();
    }

    private void ShowProfile()
    {
        ProfileNameBox.Text = _profile.Name;
        FilePathBox.Text = _profile.FilePath;
        _sheets = new ObservableCollection<SheetProfile>(_profile.Sheets);
        SheetsGrid.ItemsSource = _sheets;
        PreviewGrid.ItemsSource = null;
    }

    /// <summary>Guarda en el perfil lo que hay en pantalla (archivo y tabla de hojas).</summary>
    private void StoreProfile()
    {
        SheetsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _profile.FilePath = FilePathBox.Text.Trim().Trim('"');
        _profile.Sheets = _sheets.ToList();
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfiles || ProfileBox.SelectedItem is not string name) return;
        StoreProfile();
        _profile = _profiles.First(p => p.Name == name);
        ShowProfile();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var n = 2; _profiles.Any(p => p.Name == name); n++) name = $"{baseName} {n}";
        return name;
    }

    private void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        StoreProfile();
        var profile = new TaglistProfile { Name = UniqueName("Perfil") };
        _profiles.Add(profile);
        LoadProfiles(profile.Name);
    }

    private void ProfileDuplicate_Click(object sender, RoutedEventArgs e)
    {
        StoreProfile();
        var copy = JsonSerializer.Deserialize<TaglistProfile>(JsonSerializer.Serialize(_profile))!;
        copy.Name = UniqueName(_profile.Name + " (copia)");
        _profiles.Add(copy);
        LoadProfiles(copy.Name);
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1)
        {
            StatusText.Text = "Tiene que quedar al menos un perfil.";
            return;
        }
        if (MessageBox.Show(this, $"¿Eliminar el perfil «{_profile.Name}»?", "TextPaste", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        _profiles.Remove(_profile);
        LoadProfiles(_profiles[0].Name);
    }

    private void ProfileName_LostFocus(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0 || name == _profile.Name) { ProfileNameBox.Text = _profile.Name; return; }
        if (_profiles.Any(p => p.Name == name))
        {
            StatusText.Text = "Ya existe un perfil con ese nombre.";
            ProfileNameBox.Text = _profile.Name;
            return;
        }
        StoreProfile();
        if (_settings.ActiveProfile == _profile.Name) _settings.ActiveProfile = name;
        _profile.Name = name;
        LoadProfiles(name);
    }

    private void ProfileExport_Click(object sender, RoutedEventArgs e)
    {
        StoreProfile();
        var dialog = new SaveFileDialog
        {
            Title = "Exportar perfil de taglist",
            FileName = $"{_profile.Name}.taglist.json",
            Filter = "Perfil de taglist (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            JsonStore.ExportFile(dialog.FileName, _profile);
            StatusText.Text = "Perfil exportado. Puedes usarlo en otro computador con «Importar…».";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "No se pudo exportar: " + ex.Message;
        }
    }

    private void ProfileImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Importar perfil de taglist", Filter = "Perfil de taglist (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StoreProfile();
            var profile = JsonStore.ImportFile<TaglistProfile>(dialog.FileName);
            profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? "Importado" : profile.Name);
            _profiles.Add(profile);
            LoadProfiles(profile.Name);
            StatusText.Text = "Perfil importado. Revisa la ruta del archivo.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            StatusText.Text = "No se pudo importar: " + ex.Message;
        }
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
        Detect_Click(sender, e);
    }

    /// <summary>
    /// Lee las hojas del archivo y propone su configuración. Las hojas que ya estaban en el perfil
    /// conservan lo que ajustaste; las nuevas se agregan al final.
    /// </summary>
    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        StoreProfile();
        try
        {
            var detected = TaglistReader.DetectSheets(_profile.FilePath);
            var merged = new List<SheetProfile>();
            foreach (var old in _profile.Sheets)
                if (detected.Any(d => Same(d.SheetName, old.SheetName))) merged.Add(old);
            var added = detected.Where(d => !merged.Any(m => Same(m.SheetName, d.SheetName))).ToList();
            merged.AddRange(added);

            var missing = _profile.Sheets.Count(o => !detected.Any(d => Same(d.SheetName, o.SheetName)));
            _profile.Sheets = merged;
            ShowProfile();
            StatusText.Text = $"{detected.Count} hojas en el archivo · {added.Count} nuevas" +
                              (missing > 0 ? $" · {missing} del perfil ya no existen (se quitaron)" : "") +
                              ". Revisa la columna a pegar de cada una.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ArgumentException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            StatusText.Text = "No se pudo abrir el archivo: " + ex.Message;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        StoreProfile();
        try
        {
            var book = TaglistReader.ReadBook(_profile);
            StatusText.Text = $"{book.Sheets.Count} hojas, {book.RowCount} filas: " +
                              string.Join(" · ", book.Sheets.Select(s => $"{s.Short} {s.Rows.Count}"));

            var wanted = (SheetsGrid.SelectedItem as SheetProfile)?.SheetName;
            var sheet = book.Sheets.FirstOrDefault(s => wanted != null && Same(s.Name, wanted)) ?? book.Sheets.FirstOrDefault();
            PreviewGrid.ItemsSource = sheet == null ? null : ToTable(sheet).DefaultView;

            if (sheet?.Rows.FirstOrDefault() is { } first)
            {
                SampleValueBox.Text = first.PasteValue;
                SampleTagBox.Text = first.Title;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    /// <summary>Hoja → tabla para la vista previa (máx. 300 filas). La columna a pegar va marcada con ★.</summary>
    private static DataTable ToTable(TaglistSheet sheet)
    {
        var table = new DataTable();
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            // Los nombres con . / [ ] rompen el enlace de datos del DataGrid; y no pueden repetirse.
            var name = new string(sheet.Columns[i].Select(c => ".[]/()".Contains(c) ? ' ' : c).ToArray()).Trim();
            if (i == sheet.PasteColumn) name = "★ " + name;
            while (name.Length == 0 || table.Columns.Contains(name)) name += "_";
            table.Columns.Add(name);
        }
        foreach (var row in sheet.Rows.Take(300))
            table.Rows.Add(row.Values.Cast<object>().ToArray());
        return table;
    }

    // ───────────────────────── Guardar ─────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FavoritesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        TemplatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        StoreProfile();

        _settings.Hotkey = _hotkey;
        _settings.TaglistHotkey = _taglistHotkey;
        _settings.Opacity = OpacitySlider.Value / 100.0;
        _settings.TapToStick = TapToStickBox.IsChecked == true;
        _settings.RestoreClipboardAfterPaste = RestoreClipboardBox.IsChecked == true;
        _settings.PersistHistory = PersistHistoryBox.IsChecked == true;
        _settings.HistoryLimit = Math.Clamp(ParseInt(HistoryLimitBox.Text, 50), 1, 500);
        _settings.SlicesPerPage = SlicesBox.SelectedItem is int n ? n : 8;
        _settings.Favorites = _favorites.Where(f => !string.IsNullOrEmpty(f.Text)).ToList();
        _settings.Templates = _templates.Where(t => !string.IsNullOrWhiteSpace(t.Pattern)).ToList();
        if (_settings.Templates.Count == 0) _settings.Templates.Add(new TextTemplate("Sin plantilla", "{valor}"));
        _settings.ActiveTemplateIndex = Math.Clamp(_settings.ActiveTemplateIndex, 0, _settings.Templates.Count - 1);
        _settings.TaglistProfiles = _profiles;
        _settings.ActiveProfile = _profile.Name;

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
