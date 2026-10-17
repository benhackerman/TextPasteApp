using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TextPasteApp.Native;

/// <summary>
/// Escucha el mouse del sistema. Sólo se usa mientras la ruleta está abierta en modo "mantener",
/// para leer la rueda y los clics sin que la app de destino los reciba.
/// El handler recibe (mensaje WM_*, posición en píxeles físicos, delta de la rueda) y devuelve true para tragárselo.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Win32.HookProc _proc;
    private IntPtr _hook;

    public Func<int, Win32.POINT, int, bool>? Handler { get; set; }

    public MouseHook()
    {
        _proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"No se pudo instalar el hook de mouse (error {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler != null)
        {
            var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            var delta = (short)((data.mouseData >> 16) & 0xFFFF);
            try
            {
                if (Handler((int)wParam, data.pt, delta)) return new IntPtr(1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
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
