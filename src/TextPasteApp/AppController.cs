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
using TextPasteApp.Tray;

namespace TextPasteApp;

/// <summary>
/// El "cerebro" de la app. Conecta las piezas:
///   teclado/mouse (hooks) → ChordTracker → OverlaySession → RadialMenuWindow → Paster
/// y además guarda el historial del portapapeles y la configuración.
///
/// Flujo normal ("mantener"):
///   1. Mantienes Ctrl+Shift y presionas Espacio → se abre la ruleta bajo el puntero.
///   2. Mueves el mouse hacia un sector / giras la rueda / Tab cambia de fuente.
///   3. Sueltas Ctrl+Shift → se pega lo elegido en la ventana donde estabas.
///
/// Flujo "buscador": si tocas el atajo rápido sin mover nada, la ruleta queda abierta con un cuadro de búsqueda.
/// </summary>
public sealed class AppController : IDisposable
{
    private const string SettingsFile = "settings.json";
    private const string HistoryFile = "history.json";
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly JsonStore _store = new();
    private readonly TaglistCache _taglist = new();
    private readonly ChordTracker _chord = new(0, false, false, false, false);

    private AppSettings _settings = new();
    private ClipboardHistory _history = new();
    private KeyboardHook? _keyboard;
    private MouseHook? _mouse;
    private ClipboardWatcher? _clipboard;
    private RadialMenuWindow? _overlay;
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

        _clipboard = new ClipboardWatcher();
        _clipboard.TextCopied += OnTextCopied;

        _chord.IsKeyDown = IsModifierPhysicallyDown;
        _keyboard = new KeyboardHook { Handler = OnKey };
        _mouse = new MouseHook { Handler = OnMouse };

        _tray = new TrayIcon(this, _settings.Hotkey.ToString());
    }

    // ───────────────────────── Configuración ─────────────────────────

    public void ApplySettings(AppSettings settings, bool save = true)
    {
        _settings = settings;
        _history.Limit = Math.Max(1, settings.HistoryLimit);
        _history.Trim();
        _taglist.Configure(settings.Taglist);

        var key = Enum.TryParse<Key>(settings.Hotkey.Key, out var k) ? k : Key.Space;
        _chord.Configure(KeyInterop.VirtualKeyFromKey(key),
            settings.Hotkey.Ctrl, settings.Hotkey.Shift, settings.Hotkey.Alt, settings.Hotkey.Win);

        _tray?.SetHotkeyText(settings.Hotkey.ToString());
        if (save) _store.Save(SettingsFile, settings);
        if (save) SaveHistory();
    }

    public void OpenSettings()
    {
        Close(commit: false);
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
        _taglist.Configure(_settings.Taglist);
        var count = _taglist.GetItems().Count;
        return _taglist.LastError ?? $"Taglist cargado: {count} filas.";
    }

    public void ClearHistory() => _history.Clear();

    private void SaveHistory()
    {
        if (_settings.PersistHistory) _store.Save(HistoryFile, _history.Items.ToList());
        else if (_store.Exists(HistoryFile)) _store.Save(HistoryFile, new List<Snippet>());
    }

    // ───────────────────────── Entrada global ─────────────────────────

    /// <summary>Se llama por CADA tecla del sistema. Tiene que ser rápido.</summary>
    private bool OnKey(int vk, bool isDown)
    {
        if (Paused || _overlay?.IsSticky == true) return false;

        var result = _chord.OnKey(vk, isDown);
        if (result.Signal != ChordSignal.None)
        {
            var signal = result.Signal;
            _dispatcher.BeginInvoke(() => OnChordSignal(signal));
        }
        return result.Swallow;
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
                _dispatcher.BeginInvoke(() => Close(commit: true));
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

    private void OnChordSignal(ChordSignal signal)
    {
        switch (signal)
        {
            case ChordSignal.Opened: Open(); break;
            case ChordSignal.Released: OnReleased(); break;
            case ChordSignal.Cancel: Close(commit: false); break;
            case ChordSignal.Confirm: Close(commit: true); break;
            case ChordSignal.NextSource: Do(s => s.NextSource()); break;
            case ChordSignal.PrevSource: Do(s => s.NextSource(-1)); break;
            case ChordSignal.Up: Do(s => s.Move(-1)); break;
            case ChordSignal.Down: Do(s => s.Move(1)); break;
            case ChordSignal.NextTemplate: Do(s => s.NextTemplate()); break;
        }
    }

    private void OnOverlayCommand(OverlayCommand command, int value)
    {
        switch (command)
        {
            case OverlayCommand.Cancel: Close(commit: false); break;
            case OverlayCommand.Confirm: Close(commit: true); break;
            case OverlayCommand.NextSource: Do(s => s.NextSource()); break;
            case OverlayCommand.PrevSource: Do(s => s.NextSource(-1)); break;
            case OverlayCommand.Move: Do(s => s.Move(value)); break;
            case OverlayCommand.Scroll: Do(s => s.Scroll(value)); break;
            case OverlayCommand.HoverSlice: Do(s => s.HoverSlice(value)); break;
            case OverlayCommand.SelectRow: Do(s => s.Select(value)); break;
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

    private void Open()
    {
        if (_overlay == null) return;
        if (_chord.UsesAltOrWin) Paster.SendDummyKey();

        _targetWindow = Win32.GetForegroundWindow();
        _openedAt = DateTime.UtcNow;

        var sources = new Dictionary<SourceKind, IReadOnlyList<PickItem>>
        {
            [SourceKind.History] = _history.Items.Select(PickItem.FromSnippet).ToList(),
            [SourceKind.Favorites] = _settings.Favorites.Select(PickItem.FromSnippet).ToList(),
            [SourceKind.Taglist] = _taglist.GetItems(),
        };
        _session = new OverlaySession(sources, _settings.LastSource, _settings.SlicesPerPage,
            _settings.Templates, _settings.ActiveTemplateIndex);

        _overlay.Open(Win32.CursorPosition());
        Render();
        _captureMouse = true;
    }

    /// <summary>Se soltó el atajo: pegar, o pasar a modo buscador si fue sólo un toque.</summary>
    private void OnReleased()
    {
        if (_session == null) return;

        var wasTap = !_session.Interacted && DateTime.UtcNow - _openedAt < TapThreshold;
        if (wasTap && _settings.TapToStick)
        {
            _captureMouse = false;
            _overlay!.EnterSticky();
            Render();
            return;
        }
        Close(commit: true);
    }

    private void Close(bool commit)
    {
        if (_session == null) return;

        var text = commit ? _session.Preview : null;
        var target = _targetWindow;

        // Recordar la fuente y la plantilla para la próxima vez.
        _settings.LastSource = _session.Source;
        _settings.ActiveTemplateIndex = _session.TemplateIndex;

        _captureMouse = false;
        _chord.Reset();
        _session = null;
        _overlay?.Dismiss();
        _store.Save(SettingsFile, _settings);

        if (!string.IsNullOrEmpty(text))
        {
            _ignoreClipboardUntil = DateTime.UtcNow.AddSeconds(1.5);
            _ = Paster.PasteAsync(text, target, _settings.RestoreClipboardAfterPaste);
        }
    }

    private void Hover(Win32.POINT pt)
    {
        if (_session == null || _overlay == null || _session.IsListMode) return;
        var slice = _overlay.SliceAtScreen(pt, _session.SlicesPerPage);
        if (slice == _session.SelectedSlice) return;
        _session.HoverSlice(slice);
        Render();
    }

    /// <summary>Aplica un cambio a la sesión abierta y redibuja.</summary>
    private void Do(Action<OverlaySession> change)
    {
        if (_session == null) return;
        change(_session);
        Render();
    }

    private void Render()
    {
        if (_session == null || _overlay == null) return;

        string? empty = _session.Source switch
        {
            SourceKind.History => "Copia algo con Ctrl+C y aparecerá aquí",
            SourceKind.Favorites => "Agrega favoritos en la configuración",
            SourceKind.Taglist when _taglist.LastError != null => "Error al leer el taglist:\n" + _taglist.LastError,
            SourceKind.Taglist when string.IsNullOrWhiteSpace(_settings.Taglist.FilePath) =>
                "Elige el Excel del taglist en la configuración (ícono de la bandeja)",
            _ => _session.Filter.Length > 0 ? "Sin resultados" : null,
        };
        if (_session.Filter.Length > 0 && _session.Items.Count == 0) empty = "Sin resultados";
        _overlay.Render(_session, empty);
    }

    private static bool IsModifierPhysicallyDown(int vk) => vk == ChordTracker.VkLWin
        ? Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN)
        : Win32.IsKeyDown(vk);

    public void Exit() => Application.Current.Shutdown();

    public void Dispose()
    {
        Close(commit: false);
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
    }
}
