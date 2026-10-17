using TextPasteApp.Core.Logic;

namespace TextPasteApp.Core.Tests;

public class RadialGeometryTests
{
    [Theory]
    [InlineData(0, -100, 0)]   // arriba
    [InlineData(100, 0, 2)]    // derecha
    [InlineData(0, 100, 4)]    // abajo
    [InlineData(-100, 0, 6)]   // izquierda
    [InlineData(70, -70, 1)]   // arriba-derecha
    [InlineData(-20, -100, 0)] // levemente a la izquierda de arriba sigue siendo 0
    public void SliceAtEightSlices(double dx, double dy, int expected) =>
        Assert.Equal(expected, RadialGeometry.SliceAt(dx, dy, 8, 30));

    [Fact]
    public void DeadZoneReturnsMinusOne() => Assert.Equal(-1, RadialGeometry.SliceAt(5, 5, 8, 30));

    [Fact]
    public void PointAtRight()
    {
        var (x, y) = RadialGeometry.PointAt(90, 10);
        Assert.Equal(10, x, 6);
        Assert.Equal(0, y, 6);
    }
}
