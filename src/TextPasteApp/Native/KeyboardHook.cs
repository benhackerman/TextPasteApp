using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TextPasteApp.Native;

/// <summary>
/// Escucha todo el teclado del sistema (aunque la app no tenga el foco).
/// El handler recibe (código virtual, presionada) y devuelve true para "tragarse" la tecla.
/// Importante: el handler debe ser rápido; el trabajo pesado se hace con Dispatcher.BeginInvoke.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Win32.HookProc _proc; // referencia viva para que el GC no la recolecte
    private IntPtr _hook;

    public Func<int, bool, bool>? Handler { get; set; }

    public KeyboardHook()
    {
        _proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"No se pudo instalar el hook de teclado (error {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler != null)
        {
            var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != Win32.InjectedMarker)
            {
                var msg = (int)wParam;
                var down = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                try
                {
                    if (Handler((int)data.vkCode, down)) return new IntPtr(1);
                }
                catch (Exception ex)
                {
                    // Una excepción aquí no debe congelar el teclado de todo el sistema.
                    Debug.WriteLine(ex);
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
