using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Core.Storage;
using TextPasteApp.Core.Taglist;
using TextPasteApp.Functions;
using TextPasteApp.Native;
using TextPasteApp.Overlay;
using TextPasteApp.Settings;
using TextPasteApp.Stats;
using TextPasteApp.Taglist;
using TextPasteApp.Tray;
using TextPasteApp.Update;

namespace TextPasteApp;

/// <summary>
/// El "cerebro" de la app. Conecta las piezas:
///   teclado/mouse (hooks) → ChordTracker → sesión → ventana → Paster → estadísticas
///
/// Dos atajos:
///   • Ruleta (Ctrl+Shift+Espacio): mantener, apuntar, soltar = pegar. Historial y favoritos.
///   • Taglist (Ctrl+Shift+T): buscador compacto bajo el puntero. El taglist (proyecto) se elige solo:
///     por el título de la ventana activa, por el Excel abierto o el último usado (ver ProjectResolver).
/// </summary>
public sealed class AppController : IDisposable
{
    private const string SettingsFile = "settings.json";
    private const string HistoryFile = "history.json";
    private const string UsageFile = "usage.json";
    private const string FunctionsFile = "functions.json";
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly JsonStore _store = new();
    private readonly Dictionary<string, TaglistCache> _taglists = new();
    private readonly ChordTracker _ringChord = new(0, false, false, false, false);
    private readonly ChordTracker _taglistChord = new(0, false, false, false, false);
    private readonly ChordTracker _functionsChord = new(0, false, false, false, false);
    private readonly DispatcherTimer _usageSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private AppSettings _settings = new();
    private ClipboardHistory _history = new();
    private UsageTracker _usage = new();
    private KeyboardHook? _keyboard;
    private MouseHook? _mouse;
    private ClipboardWatcher? _clipboard;
    private RadialMenuWindow? _overlay;
    private TaglistWindow? _taglistWindow;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private StatsWindow? _statsWindow;
    private FunctionsWindow? _functionsWindow;
    private FunctionLibrary _functions = new();

    /// <summary>Última fila elegida en el taglist: sus columnas rellenan los parámetros de las funciones.</summary>
    private TagRow? _lastTaglistRow;

    private OverlaySession? _session;
    private TaglistProject? _taglistProject;
    private IntPtr _targetWindow;
    private DateTime _openedAt;
    private bool _captureMouse;
    private DateTime _ignoreClipboardUntil;

    /// <summary>Pausado desde la bandeja, o mientras se graba un atajo nuevo en la configuración.</summary>
    public bool Paused { get; set; }

    public bool IsFirstRun { get; private set; }

    /// <summary>Versión nueva encontrada en GitHub (o null).</summary>
    public UpdateChecker.UpdateInfo? AvailableUpdate { get; private set; }

    public IReadOnlyList<TaglistProject> Projects => _settings.Projects;
    public string ActiveProjectName => _settings.ActiveProject;

    public void Start()
    {
        IsFirstRun = !_store.Exists(SettingsFile);
        _settings = _store.Load<AppSettings>(SettingsFile);
        var migrated = _settings.EnsureProjects();

        var savedHistory = _settings.PersistHistory ? _store.Load<List<Snippet>>(HistoryFile) : null;
        _history = new ClipboardHistory(_settings.HistoryLimit, savedHistory);
        _history.Changed += SaveHistory;

        _functions = _store.Load<FunctionLibrary>(FunctionsFile);
        _usage = new UsageTracker(_store.Load<UsageData>(UsageFile));
        _usage.Changed += () => { _usageSaveTimer.Stop(); _usageSaveTimer.Start(); };
        _usageSaveTimer.Tick += (_, _) => SaveUsage();

        ApplySettings(_settings, save: IsFirstRun || migrated);

        _overlay = new RadialMenuWindow();
        _overlay.Command += OnOverlayCommand;

        _taglistWindow = new TaglistWindow();
        _taglistWindow.PasteRequested += (values, isSequence) => CloseTaglist(values, isSequence);
        _taglistWindow.Cancelled += () => CloseTaglist(null, false);
        _taglistWindow.SwitchToRingRequested += SwitchTaglistToRing;
        _taglistWindow.OpacityChanged += SetOpacity;
        _taglistWindow.ToggleDoneRequested += row =>
        {
            if (_taglistProject != null) _usage.ToggleDone(_taglistProject.Name, row.Key);
        };
        _taglistWindow.NextProjectRequested += NextTaglistProject;
        _taglistWindow.UseInFunctionRequested += row =>
        {
            var target = _targetWindow;
            CloseTaglist(null, false);
            _lastTaglistRow = row;
            OpenFunctions(target);
        };

        _functionsWindow = new FunctionsWindow();
        _functionsWindow.PasteRequested += (f, code, values) => CloseFunctions(f, code, values);
        _functionsWindow.Cancelled += () => CloseFunctions(null, null, null);
        _functionsWindow.NewFromClipboardRequested += () => { CloseFunctions(null, null, null); NewFunctionFromClipboard(); };
        _functionsWindow.EditRequested += f => { CloseFunctions(null, null, null); EditFunction(f); };

        _clipboard = new ClipboardWatcher();
        _clipboard.TextCopied += OnTextCopied;

        _ringChord.IsKeyDown = IsModifierPhysicallyDown;
        _taglistChord.IsKeyDown = IsModifierPhysicallyDown;
        _functionsChord.IsKeyDown = IsModifierPhysicallyDown;
        _keyboard = new KeyboardHook { Handler = OnKey };
        _mouse = new MouseHook { Handler = OnMouse };

        _tray = new TrayIcon(this, HotkeySummary());

        if (_settings.AutoUpdate)
            _dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20)); // no competir con el arranque de Windows
                await CheckForUpdatesAsync(silent: true);
            });
    }

    // ───────────────────────── Configuración ─────────────────────────

    public void ApplySettings(AppSettings settings, bool save = true)
    {
        _settings = settings;
        _history.Limit = Math.Max(1, settings.HistoryLimit);
        _history.MaxLength = Math.Max(20, settings.MaxHistoryTextLength);
        _history.Trim();
        _taglists.Clear(); // perfiles o archivos pudieron cambiar: se releen al usarlos

        Configure(_ringChord, settings.Hotkey);
        Configure(_taglistChord, settings.TaglistHotkey);
        Configure(_functionsChord, settings.FunctionsHotkey);

        _tray?.SetHotkeyText(HotkeySummary());
        _tray?.RefreshProjects();
        if (save) _store.Save(SettingsFile, settings);
        if (save) SaveHistory();
    }

    private static void Configure(ChordTracker chord, HotkeySettings hotkey)
    {
        var key = Enum.TryParse<Key>(hotkey.Key, out var k) ? k : Key.None;
        chord.Configure(key == Key.None ? 0 : KeyInterop.VirtualKeyFromKey(key),
            hotkey.Ctrl, hotkey.Shift, hotkey.Alt, hotkey.Win);
    }

    private string HotkeySummary() => $"Ruleta {_settings.Hotkey} · Taglist {_settings.TaglistHotkey} · Funciones {_settings.FunctionsHotkey}";

    public void OpenSettings(string? tab = null)
    {
        CloseRing(commit: false);
        CloseTaglist(null, false);
        CloseFunctions(null, null, null);
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this, _settings.Clone(), _history);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (tab != null) _settingsWindow.SelectTab(tab);
        _settingsWindow.Activate();
    }

    public void OpenStats()
    {
        if (_statsWindow != null)
        {
            _statsWindow.Activate();
            return;
        }
        var project = CurrentProject();
        var book = project == null ? TaglistBook.Empty : CacheFor(project).GetBook();
        _statsWindow = new StatsWindow(_usage, project?.Name, book, _settings.SecondsSavedTaglist, _settings.SecondsSavedRing, _settings.SecondsSavedFunction);
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
        _statsWindow.Activate();
    }

    /// <summary>Elige a mano el proyecto activo (desde la bandeja).</summary>
    public void SetActiveProject(string name)
    {
        _settings.ActiveProject = name;
        _store.Save(SettingsFile, _settings);
        _tray?.RefreshProjects();
    }

    public string ReloadTaglist()
    {
        _taglists.Clear();
        var project = CurrentProject();
        if (project == null) return "No hay proyectos de taglist configurados.";
        var cache = CacheFor(project);
        var book = cache.GetBook();
        return cache.LastError ?? $"{project.Name}: {book.Sheets.Count} hojas, {book.RowCount} filas.";
    }

    public void ClearHistory() => _history.Clear();

    // ───────────────────────── Respaldo ─────────────────────────

    /// <summary>Todo lo de la app en un archivo: configuración, historial y estadísticas.</summary>
    public sealed class Backup
    {
        public string Version { get; set; } = UpdateChecker.Current.ToString();
        public DateTime Created { get; set; } = DateTime.Now;
        public AppSettings Settings { get; set; } = new();
        public List<Snippet> History { get; set; } = new();
        public UsageData Usage { get; set; } = new();
        public FunctionLibrary Functions { get; set; } = new();
    }

    public void ExportBackup(string path) => JsonStore.ExportFile(path, new Backup
    {
        Settings = _settings,
        History = _history.Items.ToList(),
        Usage = _usage.Data,
        Functions = _functions,
    });

    /// <summary>Reemplaza todo por lo del respaldo. Lanza JsonException/IOException si el archivo no sirve.</summary>
    public void ImportBackup(string path)
    {
        var backup = JsonStore.ImportFile<Backup>(path);
        _statsWindow?.Close();

        _history = new ClipboardHistory(backup.Settings.HistoryLimit, backup.History);
        _history.Changed += SaveHistory;
        _usage = new UsageTracker(backup.Usage);
        _functions = backup.Functions;
        SaveFunctions();
        _usage.Changed += () => { _usageSaveTimer.Stop(); _usageSaveTimer.Start(); };

        backup.Settings.EnsureProjects();
        ApplySettings(backup.Settings);
        SaveUsage();
    }

    public void ToggleHistoryPin(string text) => _history.TogglePin(text);

    private void SetOpacity(double opacity)
    {
        _settings.Opacity = Math.Clamp(opacity, 0.4, 1);
        _store.Save(SettingsFile, _settings);
    }

    private void SaveHistory()
    {
        if (_settings.PersistHistory) _store.Save(HistoryFile, _history.Items.ToList());
        else _store.Save(HistoryFile, _history.Items.Where(s => s.Pinned).ToList()); // lo fijado siempre se guarda
    }

    private void SaveUsage()
    {
        _usageSaveTimer.Stop();
        _store.Save(UsageFile, _usage.Data);
    }

    // ───────────────────────── Actualizaciones ─────────────────────────

    /// <summary>Busca una versión nueva. silent = sin avisos si no hay nada o si falla.</summary>
    public async Task CheckForUpdatesAsync(bool silent)
    {
        var (update, error) = await UpdateChecker.CheckAsync();
        AvailableUpdate = update;
        _tray?.SetUpdate(update);
        if (update != null)
            _tray?.ShowBalloon($"Hay una versión nueva ({update.Version}). Clic derecho en el ícono → «Actualizar».");
        else if (!silent)
            _tray?.ShowBalloon(error ?? $"Tienes la última versión ({UpdateChecker.Current}).");
    }

    public async Task InstallUpdateAsync()
    {
        if (AvailableUpdate == null) return;
        try
        {
            _tray?.ShowBalloon("Descargando la actualización…");
            SaveUsage();
            await UpdateChecker.InstallAsync(AvailableUpdate);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException
                                   or System.IO.IOException or System.ComponentModel.Win32Exception)
        {
            _tray?.ShowBalloon("No se pudo actualizar: " + ex.Message);
        }
    }

    // ───────────────────────── Entrada global ─────────────────────────

    /// <summary>Se llama por CADA tecla del sistema. Tiene que ser rápido.</summary>
    private bool OnKey(int vk, bool isDown)
    {
        // Con una ventana nuestra con foco (buscador), las teclas llegan normal a esa ventana.
        if (Paused || _overlay?.IsSticky == true || _taglistWindow?.IsOpen == true || _functionsWindow?.IsOpen == true) return false;

        var ring = _ringChord.OnKey(vk, isDown);
        if (_ringChord.IsActive || ring.Signal != ChordSignal.None)
        {
            if (ring.Signal != ChordSignal.None)
                _dispatcher.BeginInvoke(() => OnChordSignal(ring));
            return ring.Swallow;
        }

        var taglist = _taglistChord.OnKey(vk, isDown);
        if (taglist.Signal == ChordSignal.Opened)
        {
            // El taglist no usa "soltar para pegar": desde aquí las teclas van al buscador.
            _taglistChord.Reset();
            _dispatcher.BeginInvoke(OpenTaglist);
            return true;
        }

        var functions = _functionsChord.OnKey(vk, isDown);
        if (functions.Signal == ChordSignal.Opened)
        {
            _functionsChord.Reset();
            _dispatcher.BeginInvoke(() => OpenFunctions(null));
            return true;
        }
        return taglist.Swallow || functions.Swallow;
    }

    /// <summary>Se llama por cada evento del mouse. Sólo actúa mientras la ruleta está en modo "mantener".</summary>
    private bool OnMouse(int msg, Win32.POINT pt, int wheelDelta)
    {
        if (!_captureMouse) return false;

        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                _dispatcher.BeginInvoke(() => Hover(pt));
                return false;
            case Win32.WM_MOUSEWHEEL:
                var steps = wheelDelta > 0 ? -1 : 1;
                _dispatcher.BeginInvoke(() => Do(s => s.Scroll(steps)));
                return true;
            case Win32.WM_LBUTTONDOWN:
                _dispatcher.BeginInvoke(() => CloseRing(commit: true));
                return true;
            case Win32.WM_RBUTTONDOWN:
                _dispatcher.BeginInvoke(() => Do(s => s.NextTemplate()));
                return true;
            case Win32.WM_MBUTTONDOWN:
                _dispatcher.BeginInvoke(() => Do(s => s.NextSource()));
                return true;
            case Win32.WM_LBUTTONUP or Win32.WM_RBUTTONUP or Win32.WM_MBUTTONUP:
                return true; // ya se procesó el "down"; que la app de destino no vea medio clic
            default:
                return false;
        }
    }

    private void OnChordSignal(ChordResult result)
    {
        switch (result.Signal)
        {
            case ChordSignal.Opened: OpenRing(); break;
            case ChordSignal.Released: OnRingReleased(); break;
            case ChordSignal.Cancel: CloseRing(commit: false); break;
            case ChordSignal.Confirm: CloseRing(commit: true); break;
            case ChordSignal.NextSource: Do(s => s.NextSource()); break;
            case ChordSignal.PrevSource: Do(s => s.NextSource(-1)); break;
            case ChordSignal.Up: Do(s => s.Move(-1)); break;
            case ChordSignal.Down: Do(s => s.Move(1)); break;
            case ChordSignal.NextTemplate: Do(s => s.NextTemplate()); break;
            case ChordSignal.TogglePin: TogglePinSelected(); break;
            case ChordSignal.PickSlice:
                if (_session == null || result.Value >= _session.PageItems.Count) break;
                _session.HoverSlice(result.Value);
                CloseRing(commit: true);
                break;
        }
    }

    private void OnOverlayCommand(OverlayCommand command, int value)
    {
        switch (command)
        {
            case OverlayCommand.Cancel: CloseRing(commit: false); break;
            case OverlayCommand.Confirm: CloseRing(commit: true); break;
            case OverlayCommand.NextSource: Do(s => s.NextSource()); break;
            case OverlayCommand.PrevSource: Do(s => s.NextSource(-1)); break;
            case OverlayCommand.Move: Do(s => s.Move(value)); break;
            case OverlayCommand.Scroll: Do(s => s.Scroll(value)); break;
            case OverlayCommand.HoverSlice: Do(s => s.HoverSlice(value)); break;
            case OverlayCommand.NextTemplate: Do(s => s.NextTemplate()); break;
            case OverlayCommand.FilterChanged: Do(s => s.SetFilter(_overlay!.FilterText)); break;
            case OverlayCommand.TogglePin: TogglePinSelected(); break;
        }
    }

    private void OnTextCopied(string text)
    {
        if (DateTime.UtcNow < _ignoreClipboardUntil) return; // lo pusimos nosotros al pegar
        _history.Add(text, Win32.ForegroundAppName(), DateTime.Now);
    }

    // ───────────────────────── Ruleta ─────────────────────────

    private void OpenRing()
    {
        if (_overlay == null) return;
        if (_ringChord.UsesAltOrWin) Paster.SendDummyKey();

        _targetWindow = Win32.GetForegroundWindow();
        _openedAt = DateTime.UtcNow;
        _session = BuildRingSession(_settings.LastSource);

        _overlay.Open(Win32.CursorPosition(), _settings.Opacity);
        Render();
        _captureMouse = true;
    }

    private OverlaySession BuildRingSession(SourceKind source)
    {
        var sources = new Dictionary<SourceKind, IReadOnlyList<PickItem>>
        {
            [SourceKind.History] = _history.Items.Select(PickItem.FromSnippet).ToList(),
            [SourceKind.Favorites] = _settings.Favorites.Select(PickItem.FromSnippet).ToList(),
        };
        return new OverlaySession(sources, source, _settings.SlicesPerPage, _settings.Templates, _settings.ActiveTemplateIndex);
    }

    /// <summary>P (o Ctrl+P en modo buscador): fija o suelta el elemento apuntado del historial.</summary>
    private void TogglePinSelected()
    {
        if (_session?.Selected is not { } item || _session.Source != SourceKind.History) return;
        _history.TogglePin(item.Text);

        // Rearmar la sesión para ver el nuevo orden, conservando fuente, filtro y plantilla.
        var filter = _session.Filter;
        var template = _session.TemplateIndex;
        _session = BuildRingSession(SourceKind.History);
        while (_session.TemplateIndex != template && _session.TemplateCount > 1) _session.NextTemplate();
        if (filter.Length > 0) _session.SetFilter(filter);
        Render();
    }

    /// <summary>Se soltó el atajo: pegar, o pasar a modo buscador si fue sólo un toque.</summary>
    private void OnRingReleased()
    {
        if (_session == null) return;

        var wasTap = !_session.Interacted && DateTime.UtcNow - _openedAt < TapThreshold;
        if (wasTap && _settings.TapToStick)
        {
            EnterRingSearch();
            return;
        }
        CloseRing(commit: true);
    }

    private void EnterRingSearch()
    {
        _captureMouse = false;
        _ringChord.Reset();
        _overlay!.EnterSticky();
        Render();
    }

    private void CloseRing(bool commit)
    {
        if (_session == null) return;

        var text = commit ? _session.Preview : null;
        var source = _session.Source;

        // Recordar la fuente y la plantilla para la próxima vez.
        _settings.LastSource = _session.Source;
        _settings.ActiveTemplateIndex = _session.TemplateIndex;

        _captureMouse = false;
        _ringChord.Reset();
        _session = null;
        _overlay?.Dismiss();
        _store.Save(SettingsFile, _settings);

        if (!string.IsNullOrEmpty(text))
        {
            _usage.RecordRing(source.DisplayName(), text, _settings.SecondsSavedRing);
            Paste(new[] { text }, sequence: false);
        }
    }

    private void Hover(Win32.POINT pt)
    {
        if (_session == null || _overlay == null) return;
        var slice = _overlay.SliceAtScreen(pt, _session.SlicesPerPage);
        if (slice == _session.SelectedSlice) return;
        _session.HoverSlice(slice);
        Render();
    }

    /// <summary>Aplica un cambio a la ruleta abierta y redibuja.</summary>
    private void Do(Action<OverlaySession> change)
    {
        if (_session == null) return;
        change(_session);
        Render();
    }

    private void Render()
    {
        if (_session == null || _overlay == null) return;

        string? empty = _session.Filter.Length > 0
            ? "Sin resultados"
            : _session.Source == SourceKind.History
                ? "Copia algo con Ctrl+C y aparecerá aquí"
                : "Agrega favoritos en la configuración";
        _overlay.Render(_session, empty);
    }

    // ───────────────────────── Taglist ─────────────────────────

    private TaglistCache CacheFor(TaglistProject project)
    {
        if (!_taglists.TryGetValue(project.Name, out var cache))
        {
            cache = new TaglistCache();
            var profile = _settings.ProfileFor(project);
            // El caché lee el archivo del proyecto con la estructura del perfil.
            cache.Configure(new TaglistProfile
            {
                Name = profile.Name,
                FilePath = project.FilePath,
                HiddenColumns = profile.HiddenColumns,
                Sheets = profile.Sheets,
            });
            _taglists[project.Name] = cache;
        }
        return cache;
    }

    /// <summary>El proyecto que corresponde ahora (ventana activa, Excel abierto o el último usado).</summary>
    private TaglistProject? CurrentProject() => ResolveProject().Project;

    private (TaglistProject? Project, ProjectReason Reason) ResolveProject()
    {
        var foreground = _targetWindow != IntPtr.Zero ? _targetWindow : Win32.GetForegroundWindow();
        return ProjectResolver.Resolve(_settings.Projects, Win32.WindowTitle(foreground),
            Win32.VisibleWindowTitles(), _settings.ActiveProject);
    }

    private void OpenTaglist()
    {
        if (_taglistWindow == null) return;
        CloseRing(commit: false);
        if (_taglistChord.UsesAltOrWin) Paster.SendDummyKey();

        _targetWindow = Win32.GetForegroundWindow();
        var (project, reason) = ResolveProject();
        _taglistProject = project;
        var (session, label, empty) = BuildTaglistSession(project, reason);
        _taglistWindow.Open(session, Win32.CursorPosition(), _settings.Opacity, label, empty);
    }

    private (TaglistSession Session, string Label, string? Empty) BuildTaglistSession(TaglistProject? project, ProjectReason reason)
    {
        if (project == null)
        {
            var none = new TaglistSession(TaglistBook.Empty, null, _settings.Templates, _settings.ActiveTemplateIndex);
            return (none, "", "Agrega un proyecto en Configuración → Taglist: elige el Excel y presiona «Detectar hojas».");
        }

        var cache = CacheFor(project);
        var book = cache.GetBook();
        var profile = _settings.ProfileFor(project);

        string? empty = null;
        if (!profile.Sheets.Any(s => s.Enabled)) empty = $"El perfil «{profile.Name}» no tiene hojas. En Configuración → Taglist presiona «Detectar hojas».";
        else if (cache.LastError != null) empty = "Error al leer el taglist:\n" + cache.LastError;
        else if (book.Sheets.Count == 0) empty = "Ninguna hoja del perfil se encontró en el archivo. ¿Cambió el nombre de las hojas?";

        var context = new TaglistContext
        {
            RecentKeys = _usage.RecentRowKeys(project.Name),
            IsDone = key => _usage.IsDone(project.Name, key),
        };
        var session = new TaglistSession(book, _settings.LastSheet, _settings.Templates, _settings.ActiveTemplateIndex, context);
        var why = reason switch
        {
            ProjectReason.WindowTitle => " (ventana)",
            ProjectReason.OpenInExcel => " (Excel)",
            _ => "",
        };
        return (session, project.Name + why, empty);
    }

    /// <summary>F4 en el buscador: siguiente proyecto.</summary>
    private void NextTaglistProject()
    {
        var usable = _settings.Projects.Where(p => !string.IsNullOrWhiteSpace(p.FilePath)).ToList();
        if (usable.Count < 2 || _taglistWindow == null) return;
        var index = _taglistProject == null ? -1 : usable.FindIndex(p => p.Name == _taglistProject.Name);
        _taglistProject = usable[(index + 1) % usable.Count];
        _settings.ActiveProject = _taglistProject.Name;
        var (session, label, empty) = BuildTaglistSession(_taglistProject, ProjectReason.LastUsed);
        _taglistWindow.Replace(session, label, empty);
        _tray?.RefreshProjects();
    }

    private void CloseTaglist(IReadOnlyList<string>? values, bool isSequence)
    {
        if (_taglistWindow?.Session is not { } session) return;

        // Recordar hoja, plantilla y proyecto: la próxima vez sigues donde estabas.
        if (session.Book.Sheets.Count > 0) _settings.LastSheet = session.SheetName;
        _settings.ActiveTemplateIndex = session.TemplateIndex;
        if (_taglistProject != null) _settings.ActiveProject = _taglistProject.Name;
        _store.Save(SettingsFile, _settings);

        if (values is { Count: > 0 } && session.Selected is { } pastedRow) _lastTaglistRow = pastedRow;
        if (values is { Count: > 0 } && session.Selected is { } row && _taglistProject != null)
            _usage.RecordTaglist(_taglistProject.Name, row, string.Join(" ⇥ ", values),
                isSequence ? values.Count : 1, _settings.SecondsSavedTaglist);

        var separator = SequenceKeys.VirtualKey(session.Selected?.Sheet.SequenceKey ?? "Tab");
        _taglistWindow.Dismiss();
        if (values is { Count: > 0 }) Paste(values, isSequence, separator);
    }

    /// <summary>Ctrl+Tab en el taglist: cambia a la ruleta (historial/favoritos) en modo buscador.</summary>
    private void SwitchTaglistToRing()
    {
        var target = _targetWindow;
        CloseTaglist(null, false);
        OpenRing();
        _targetWindow = target;
        EnterRingSearch();
    }

    // ───────────────────────── Funciones (código guardado) ─────────────────────────

    public FunctionLibrary FunctionLibrary => _functions;

    public void SaveFunctions() => _store.Save(FunctionsFile, _functions);

    /// <summary>Abre el buscador de funciones. target = ventana donde pegar (null = la que tiene el foco ahora).</summary>
    private void OpenFunctions(IntPtr? target)
    {
        if (_functionsWindow == null) return;
        CloseRing(commit: false);
        if (_functionsChord.UsesAltOrWin) Paster.SendDummyKey();
        _targetWindow = target ?? Win32.GetForegroundWindow();
        var session = new FunctionSession(_functions.Functions, _functions.LastValues, _lastTaglistRow);
        _functionsWindow.Open(session, Win32.CursorPosition(), _settings.Opacity);
    }

    private void CloseFunctions(CodeFunction? function, string? code, IReadOnlyDictionary<string, string>? values)
    {
        if (_functionsWindow?.IsOpen != true) return;
        _functionsWindow.Dismiss();
        if (function == null || string.IsNullOrEmpty(code)) return;

        if (values != null)
        {
            foreach (var (k, v) in values) _functions.LastValues[k] = v;
            SaveFunctions();
        }
        _usage.RecordFunction(function.Name, code, _settings.SecondsSavedFunction);
        Paste(new[] { code }, sequence: false);
    }

    /// <summary>Guarda lo que hay en el portapapeles como función nueva (pide nombre y categoría).</summary>
    public void NewFunctionFromClipboard()
    {
        var code = ClipboardHelper.TryGetText() ?? "";
        var function = new CodeFunction { Code = code };
        if (ShowFunctionEditor(function))
        {
            _functions.Functions.Add(function);
            SaveFunctions();
            _tray?.ShowBalloon($"Función «{function.Name}» guardada. Búscala con {_settings.FunctionsHotkey}.");
        }
    }

    public bool EditFunction(CodeFunction function)
    {
        if (!ShowFunctionEditor(function)) return false;
        SaveFunctions();
        return true;
    }

    public bool ShowFunctionEditor(CodeFunction function)
    {
        var editor = new FunctionEditorWindow(function, _functions.Functions.Select(f => f.Category));
        return editor.ShowDialog() == true;
    }

    private void Paste(IReadOnlyList<string> values, bool sequence, int separatorVk = Win32.VK_TAB)
    {
        var delay = Math.Clamp(_settings.SequenceDelayMs, 30, 2000);
        _ignoreClipboardUntil = DateTime.UtcNow.AddSeconds(sequence ? 1.5 + values.Count * (delay * 2 / 1000.0) : 1.5);
        _ = sequence
            ? Paster.PasteSequenceAsync(values, _targetWindow, _settings.RestoreClipboardAfterPaste, separatorVk, delay)
            : Paster.PasteAsync(values[0], _targetWindow, _settings.RestoreClipboardAfterPaste);
    }

    private static bool IsModifierPhysicallyDown(int vk) => vk == ChordTracker.VkLWin
        ? Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)
        : Win32.IsKeyDown(vk);

    public void Exit() => Application.Current.Shutdown();

    public void Dispose()
    {
        CloseRing(commit: false);
        CloseTaglist(null, false);
        CloseFunctions(null, null, null);
        SaveUsage();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
    }
}
