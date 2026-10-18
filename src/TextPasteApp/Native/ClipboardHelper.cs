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
    /// <summary>
    /// Formatos con los que los gestores de contraseñas (KeePass, 1Password, Bitwarden…) piden
    /// que su contenido no se guarde en historiales de portapapeles.
    /// </summary>
    private static readonly string[] PrivateFormats =
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
    };

    public static string? TryGetText() => TryGetText(skipPrivate: false);

    /// <summary>Lee el texto copiado. Con skipPrivate, devuelve null si quien copió pidió no guardarlo.</summary>
    public static string? TryGetText(bool skipPrivate)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (skipPrivate && IsPrivate()) return null;
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return null;
    }

    private static bool IsPrivate()
    {
        var data = Clipboard.GetDataObject();
        if (data == null) return false;
        var formats = data.GetFormats(false);
        if (Array.Exists(PrivateFormats, f => Array.IndexOf(formats, f) >= 0)) return true;

        // "CanIncludeInClipboardHistory" = 0 también significa "no guardar" (lo usa el propio Windows).
        if (Array.IndexOf(formats, "CanIncludeInClipboardHistory") >= 0 &&
            data.GetData("CanIncludeInClipboardHistory") is System.IO.MemoryStream ms && ms.Length >= 4)
        {
            var bytes = ms.ToArray();
            return BitConverter.ToInt32(bytes, 0) == 0;
        }
        return false;
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
