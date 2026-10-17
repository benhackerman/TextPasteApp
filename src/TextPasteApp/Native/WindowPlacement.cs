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

    /// <summary>
    /// Coloca la ventana (con su tamaño actual) justo debajo del punto, como un autocompletar.
    /// Si no cabe abajo la pone arriba, y nunca se sale del monitor.
    /// </summary>
    public static void PlaceNear(IntPtr hwnd, Win32.POINT anchor)
    {
        if (!Win32.GetWindowRect(hwnd, out var rect)) return;
        var (work, scale) = Win32.MonitorAt(anchor);
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        var gap = (int)Math.Round(14 * scale);

        var x = anchor.X - (int)Math.Round(24 * scale);
        var y = anchor.Y + gap;
        if (y + h > work.Bottom) y = anchor.Y - gap - h;   // no cabe abajo → arriba
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));

        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Coloca la ventana de modo que el punto (anchorX, anchorY) de la ventana (en DIP) quede sobre el punto de
    /// pantalla, sin salirse del monitor. Devuelve dónde quedó realmente ese punto (píxeles físicos).
    /// </summary>
    public static Win32.POINT AnchorAt(IntPtr hwnd, Win32.POINT point, double widthDip, double heightDip, double anchorX, double anchorY)
    {
        var (work, scale) = Win32.MonitorAt(point);
        var w = (int)Math.Round(widthDip * scale);
        var h = (int)Math.Round(heightDip * scale);
        var ax = (int)Math.Round(anchorX * scale);
        var ay = (int)Math.Round(anchorY * scale);

        var x = Math.Clamp(point.X - ax, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Math.Clamp(point.Y - ay, work.Top, Math.Max(work.Top, work.Bottom - h));
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
        return new Win32.POINT { X = x + ax, Y = y + ay };
    }
}
