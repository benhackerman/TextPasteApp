using System;

namespace TextPasteApp.Native;

/// <summary>Coloca una ventana centrada en un punto de la pantalla sin que se salga del monitor.</summary>
internal static class WindowPlacement
{
    /// <summary>
    /// Centra la ventana (tamaño en DIP) en el punto (píxeles físicos). Si quedaría cortada por el borde,
    /// la corre hacia adentro. Devuelve el centro final en píxeles físicos.
    /// </summary>
    public static Win32.POINT CenterOn(IntPtr hwnd, Win32.POINT point, double widthDip, double heightDip)
    {
        var (work, scale) = Win32.MonitorAt(point);
        var w = (int)Math.Round(widthDip * scale);
        var h = (int)Math.Round(heightDip * scale);

        var cx = work.Right - work.Left >= w ? Math.Clamp(point.X, work.Left + w / 2, work.Right - w / 2) : point.X;
        var cy = work.Bottom - work.Top >= h ? Math.Clamp(point.Y, work.Top + h / 2, work.Bottom - h / 2) : point.Y;

        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, cx - w / 2, cy - h / 2, w, h, Win32.SWP_NOACTIVATE);
        return new Win32.POINT { X = cx, Y = cy };
    }
}
