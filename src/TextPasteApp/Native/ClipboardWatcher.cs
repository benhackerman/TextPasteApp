using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TextPasteApp.Native;

/// <summary>Avisa cada vez que alguien copia texto en Windows (Ctrl+C en cualquier programa).</summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private readonly HwndSource _window;

    public event Action<string>? TextCopied;

    public ClipboardWatcher()
    {
        // Ventana invisible "sólo mensajes" que recibe WM_CLIPBOARDUPDATE.
        _window = new HwndSource(new HwndSourceParameters("TextPasteApp.Clipboard")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
        Win32.AddClipboardFormatListener(_window.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE)
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, ReadClipboard);
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        var text = ClipboardHelper.TryGetText(skipPrivate: true);
        if (text != null) TextCopied?.Invoke(text);
    }

    public void Dispose()
    {
        Win32.RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
    }
}
