using TextPasteApp.Core.Logic;
using static TextPasteApp.Core.Logic.ChordTracker;

namespace TextPasteApp.Core.Tests;

public class ChordTrackerTests
{
    private const int VkSpace = 0x20, VkA = 0x41;

    private static ChordTracker CtrlShiftSpace() => new(VkSpace, ctrl: true, shift: true, alt: false, win: false);

    [Fact]
    public void OpensOnChordAndReleasesWhenAllModifiersUp()
    {
        var t = CtrlShiftSpace();
        Assert.Equal(ChordResult.Pass, t.OnKey(VkLControl, true));
        Assert.Equal(ChordResult.Pass, t.OnKey(VkLShift, true));
        Assert.Equal(new ChordResult(true, ChordSignal.Opened), t.OnKey(VkSpace, true));
        Assert.True(t.IsActive);

        // Repetición del teclado y soltar el gatillo: se tragan sin señal.
        Assert.Equal(new ChordResult(true, ChordSignal.None), t.OnKey(VkSpace, true));
        Assert.Equal(new ChordResult(true, ChordSignal.None), t.OnKey(VkSpace, false));

        // Soltar un modificador todavía no pega.
        Assert.Equal(ChordResult.Pass, t.OnKey(VkLControl, false));
        Assert.Equal(new ChordResult(false, ChordSignal.Released), t.OnKey(VkLShift, false));
        Assert.False(t.IsActive);
    }

    [Fact]
    public void DoesNotOpenWithMissingOrExtraModifiers()
    {
        var t = CtrlShiftSpace();
        t.OnKey(VkLControl, true);
        Assert.Equal(ChordResult.Pass, t.OnKey(VkSpace, true));

        t.OnKey(VkLShift, true);
        t.OnKey(VkLMenu, true);
        Assert.Equal(ChordResult.Pass, t.OnKey(VkSpace, true));
    }

    [Fact]
    public void NavigationKeysBecomeSignalsWhileActive()
    {
        var t = CtrlShiftSpace();
        t.OnKey(VkRControl, true);
        t.OnKey(VkRShift, true);
        t.OnKey(VkSpace, true);

        Assert.Equal(ChordSignal.NextSource, t.OnKey(VkTab, true).Signal);
        Assert.Equal(ChordSignal.Down, t.OnKey(VkDown, true).Signal);
        Assert.Equal(ChordSignal.NextTemplate, t.OnKey(VkT, true).Signal);
        Assert.Equal(new ChordResult(true, ChordSignal.None), t.OnKey(VkA, true));
        Assert.Equal(new ChordResult(true, ChordSignal.Cancel), t.OnKey(VkEscape, true));
        Assert.False(t.IsActive);
    }

    [Fact]
    public void StuckModifierIsCorrectedWithPhysicalState()
    {
        var t = CtrlShiftSpace();
        t.OnKey(VkLMenu, true); // Alt "pegado": nunca llegó el evento de soltar
        t.OnKey(VkLControl, true);
        t.OnKey(VkLShift, true);
        t.IsKeyDown = vk => vk != VkMenu;

        Assert.Equal(ChordSignal.Opened, t.OnKey(VkSpace, true).Signal);
    }
}
