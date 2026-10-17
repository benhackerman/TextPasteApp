using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextPasteApp.Core.Logic;
using TextPasteApp.Core.Models;
using TextPasteApp.Core.Storage;
using TextPasteApp.Core.Taglist;
using TextPasteApp.Native;
using TextPasteApp.Overlay;
using TextPasteApp.Settings;
using TextPasteApp.Taglist;
using TextPasteApp.Tray;

namespace TextPasteApp;

/// <summary>
/// El "cerebro" de la app. Conecta las piezas:
///   teclado/mouse (hooks) → ChordTracker → sesión → ventana → Paster
/// y además guarda el historial del portapapeles y la configuración.
///
/// Dos atajos:
///   • Ruleta (Ctrl+Shift+Espacio): mantener, apuntar con el mouse, soltar = pegar. Historial y favoritos.
///     Un toque rápido la deja abierta con buscador.
///   • Taglist (Ctrl+Shift+T): abre el buscador del taglist con el foco en el cuadro de texto.
///     Escribes, ↑↓ fila, rueda columna, Tab hoja, Enter pega.
/// </summary>
public sealed class AppController : IDisposable
{
    private const string SettingsFile = "settings.json";
    private const string HistoryFile = "history.json";
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly JsonStore _store = new();
    private readonly TaglistCache _taglist = new();
    private readonly ChordTracker _ringChord = new(0, false, false, false, false);
    private readonly ChordTracker _taglistChord = new(0, false, false, false, false);

    private AppSettings _settings = new();
    private ClipboardHistory _history = new();
    private KeyboardHook? _keyboard;
    private MouseHook? _mouse;
    private ClipboardWatcher? _clipboard;
    private RadialMenuWindow? _overlay;
    private TaglistWindow? _taglistWindow;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    private OverlaySession? _session;
    private IntPtr _targetWindow;
    private DateTime _openedAt;
    private bool _captureMouse;
    private DateTime _ignoreClipboardUntil;

    /// <summary>Pausado desde la bandeja, o mientras se graba un atajo nuevo en la configuración.</summary>
    public bool Paused { get; set; }

    public bool IsFirstRun { get; private set; }

    public void Start()
    {
        IsFirstRun = !_store.Exists(SettingsFile);
        _settings = _store.Load<AppSettings>(SettingsFile);
        var savedHistory = _settings.PersistHistory ? _store.Load<List<Snippet>>(HistoryFile) : null;
        _history = new ClipboardHistory(_settings.HistoryLimit, savedHistory);
        _history.Changed += SaveHistory;

        ApplySettings(_settings, save: IsFirstRun);

        _overlay = new RadialMenuWindow();
        _overlay.Command += OnOverlayCommand;

        _taglistWindow = new TaglistWindow();
        _taglistWindow.PasteRequested += text => CloseTaglist(text);
        _taglistWindow.Cancelled += () => CloseTaglist(null);
        _taglistWindow.SwitchToRingRequested += SwitchTaglistToRing;
        _taglistWindow.OpacityChanged += SetOpacity;

        _clipboard = new ClipboardWatcher();
        _clipboard.TextCopied += OnTextCopied;

        _ringChord.IsKeyDown = IsModifierPhysicallyDown;
        _taglistChord.IsKeyDown = IsModifierPhysicallyDown;
        _keyboard = new KeyboardHook { Handler = OnKey };
        _mouse = new MouseHook { Handler = OnMouse };

        _tray = new TrayIcon(this, HotkeySummary());
    }

    // ───────────────────────── Configuración ─────────────────────────

    public void ApplySettings(AppSettings settings, bool save = true)
    {
        _settings = settings;
        _history.Limit = Math.Max(1, settings.HistoryLimit);
        _history.Trim();
        _taglist.Configure(settings.CurrentProfile());

        Configure(_ringChord, settings.Hotkey);
        Configure(_taglistChord, settings.TaglistHotkey);

        _tray?.SetHotkeyText(HotkeySummary());
        if (save) _store.Save(SettingsFile, settings);
        if (save) SaveHistory();
    }

    private static void Configure(ChordTracker chord, HotkeySettings hotkey)
    {
        var key = Enum.TryParse<Key>(hotkey.Key, out var k) ? k : Key.None;
        chord.Configure(key == Key.None ? 0 : KeyInterop.VirtualKeyFromKey(key),
            hotkey.Ctrl, hotkey.Shift, hotkey.Alt, hotkey.Win);
    }

    private string HotkeySummary() => $"Ruleta {_settings.Hotkey} · Taglist {_settings.TaglistHotkey}";

    public void OpenSettings()
    {
        CloseRing(commit: false);
        CloseTaglist(null);
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this, _settings.Clone(), _history);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public string ReloadTaglist()
    {
        _taglist.Configure(_settings.CurrentProfile());
        var book = _taglist.GetBook();
        return _taglist.LastError ?? $"Taglist cargado: {book.Sheets.Count} hojas, {book.RowCount} filas.";
    }

    public void ClearHistory() => _history.Clear();

    private void SetOpacity(double opacity)
    {
        _settings.Opacity = Math.Clamp(opacity, 0.4, 1);
        _store.Save(SettingsFile, _settings);
    }

    private void SaveHistory()
    {
        if (_settings.PersistHistory) _store.Save(HistoryFile, _history.Items.ToList());
        else if (_store.Exists(HistoryFile)) _store.Save(HistoryFile, new List<Snippet>());
    }

    // ───────────────────────── Entrada global ─────────────────────────

    /// <summary>Se llama por CADA tecla del sistema. Tiene que ser rápido.</summary>
    private bool OnKey(int vk, bool isDown)
    {
        // Con una ventana nuestra con foco (buscador), las teclas llegan normal a esa ventana.
        if (Paused || _overlay?.IsSticky == true || _taglistWindow?.IsOpen == true) return false;

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
        return taglist.Swallow;
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
        }
    }

    private void OnTextCopied(string text)
    {
        if (DateTime.UtcNow < _ignoreClipboardUntil) return; // lo pusimos nosotros al pegar
        _history.Add(text);
    }

    // ───────────────────────── Ruleta ─────────────────────────

    private void OpenRing()
    {
        if (_overlay == null) return;
        if (_ringChord.UsesAltOrWin) Paster.SendDummyKey();

        _targetWindow = Win32.GetForegroundWindow();
        _openedAt = DateTime.UtcNow;

        var sources = new Dictionary<SourceKind, IReadOnlyList<PickItem>>
        {
            [SourceKind.History] = _history.Items.Select(PickItem.FromSnippet).ToList(),
            [SourceKind.Favorites] = _settings.Favorites.Select(PickItem.FromSnippet).ToList(),
        };
        _session = new OverlaySession(sources, _settings.LastSource, _settings.SlicesPerPage,
            _settings.Templates, _settings.ActiveTemplateIndex);

        _overlay.Open(Win32.CursorPosition(), _settings.Opacity);
        Render();
        _captureMouse = true;
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

        // Recordar la fuente y la plantilla para la próxima vez.
        _settings.LastSource = _session.Source;
        _settings.ActiveTemplateIndex = _session.TemplateIndex;

        _captureMouse = false;
        _ringChord.Reset();
        _session = null;
        _overlay?.Dismiss();
        _store.Save(SettingsFile, _settings);
        PasteIfAny(text);
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

    private void OpenTaglist()
    {
        if (_taglistWindow == null) return;
        CloseRing(commit: false);
        if (_taglistChord.UsesAltOrWin) Paster.SendDummyKey();

        _targetWindow = Win32.GetForegroundWindow();
        var book = _taglist.GetBook();

        string? empty = null;
        if (!_taglist.HasFile) empty = "Elige el Excel del taglist en Configuración → Taglist (ícono junto al reloj).";
        else if (!_taglist.HasSheets) empty = "El perfil no tiene hojas. En Configuración → Taglist presiona «Detectar hojas».";
        else if (_taglist.LastError != null) empty = "Error al leer el taglist:\n" + _taglist.LastError;
        else if (book.Sheets.Count == 0) empty = "Ninguna hoja del perfil se encontró en el archivo. ¿Cambió el nombre de las hojas?";

        var session = new TaglistSession(book, _settings.LastSheet, _settings.Templates, _settings.ActiveTemplateIndex);
        _taglistWindow.Open(session, Win32.CursorPosition(), _settings.Opacity, empty);
    }

    private void CloseTaglist(string? text)
    {
        if (_taglistWindow?.Session is not { } session) return;

        // Recordar hoja y plantilla: la próxima vez sigues donde estabas.
        if (session.Book.Sheets.Count > 0) _settings.LastSheet = session.SheetName;
        _settings.ActiveTemplateIndex = session.TemplateIndex;
        _store.Save(SettingsFile, _settings);

        _taglistWindow.Dismiss();
        PasteIfAny(text);
    }

    /// <summary>Ctrl+Tab en el taglist: cambia a la ruleta (historial/favoritos) en modo buscador.</summary>
    private void SwitchTaglistToRing()
    {
        var target = _targetWindow;
        CloseTaglist(null);
        OpenRing();
        _targetWindow = target;
        EnterRingSearch();
    }

    private void PasteIfAny(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _ignoreClipboardUntil = DateTime.UtcNow.AddSeconds(1.5);
        _ = Paster.PasteAsync(text, _targetWindow, _settings.RestoreClipboardAfterPaste);
    }

    private static bool IsModifierPhysicallyDown(int vk) => vk == ChordTracker.VkLWin
        ? Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)
        : Win32.IsKeyDown(vk);

    public void Exit() => Application.Current.Shutdown();

    public void Dispose()
    {
        CloseRing(commit: false);
        CloseTaglist(null);
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
    }
}
