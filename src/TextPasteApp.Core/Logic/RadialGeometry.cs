namespace TextPasteApp.Core.Logic;

/// <summary>Matemática de la ruleta. El sector 0 está arriba y se avanza en sentido horario.</summary>
public static class RadialGeometry
{
    /// <summary>
    /// Qué sector apunta el mouse. dx/dy son relativos al centro (dy positivo hacia abajo, como en pantalla).
    /// Devuelve -1 si el mouse está dentro de la zona muerta del centro.
    /// </summary>
    public static int SliceAt(double dx, double dy, int sliceCount, double deadZoneRadius)
    {
        if (sliceCount <= 0) return -1;
        if (Math.Sqrt(dx * dx + dy * dy) < deadZoneRadius) return -1;

        var angle = AngleDegrees(dx, dy);
        var size = 360.0 / sliceCount;
        return (int)Math.Floor((angle + size / 2) / size) % sliceCount;
    }

    /// <summary>Ángulo en grados, 0 = arriba, creciendo en sentido horario, rango [0, 360).</summary>
    public static double AngleDegrees(double dx, double dy)
    {
        var deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>Ángulo central del sector.</summary>
    public static double SliceCenterAngle(int index, int sliceCount) => index * 360.0 / sliceCount;

    /// <summary>Punto a cierta distancia del centro en un ángulo (0 = arriba, horario).</summary>
    public static (double X, double Y) PointAt(double angleDegrees, double radius)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        return (Math.Sin(rad) * radius, -Math.Cos(rad) * radius);
    }
}
