using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace TextPasteApp.Native;

/// <summary>
/// Acceso al portapapeles con reintentos: otro programa puede tenerlo abierto un instante
/// y WPF lanza COMException en ese caso.
/// </summary>
internal static class ClipboardHelper
{
    public static string? TryGetText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return null;
    }

    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }
}
