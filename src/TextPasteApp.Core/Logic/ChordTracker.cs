namespace TextPasteApp.Core.Logic;

/// <summary>Lo que la ruleta debe hacer después de una tecla.</summary>
public enum ChordSignal
{
    None,
    Opened,      // se presionó el atajo: mostrar la ruleta
    Released,    // se soltaron los modificadores: pegar lo elegido
    Cancel,      // Esc
    Confirm,     // Enter
    NextSource,  // Tab
    PrevSource,  // (reservado)
    Up,          // flecha arriba / RePág
    Down,        // flecha abajo / AvPág
    NextTemplate,// T
    PickSlice,   // 1–9: elegir y pegar el sector N (Value = 0..8)
}

/// <summary>Resultado de procesar una tecla: si hay que tragársela (que la app de destino no la vea) y qué señal emitir.</summary>
public readonly record struct ChordResult(bool Swallow, ChordSignal Signal, int Value = 0)
{
    public static readonly ChordResult Pass = new(false, ChordSignal.None);
}

/// <summary>
/// Detecta "mantener presionado el atajo" y "soltarlo" a partir de eventos de teclado crudos
/// (códigos virtuales de Windows). No sabe nada de ventanas: es lógica pura y testeable.
///
/// Flujo: Ctrl+Shift presionados + Espacio → Opened. Mientras siga activo, Tab/flechas/Esc/Enter
/// se convierten en señales y no llegan a la app de destino. Cuando se sueltan todos los modificadores → Released.
/// </summary>
public sealed class ChordTracker
{
    // Códigos virtuales de Windows (VK_*)
    public const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    public const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    public const int VkTab = 0x09, VkReturn = 0x0D, VkEscape = 0x1B, VkPrior = 0x21, VkNext = 0x22;
    public const int VkUp = 0x26, VkDown = 0x28, VkLeft = 0x25, VkRight = 0x27, VkT = 0x54;

    private readonly HashSet<int> _pressedModifiers = new();
    private readonly HashSet<int> _requiredModifiers = new();
    private int _triggerKey;

    /// <summary>True mientras el atajo está presionado y la ruleta está en modo "mantener".</summary>
    public bool IsActive { get; private set; }

    public ChordTracker(int triggerVk, bool ctrl, bool shift, bool alt, bool win) =>
        Configure(triggerVk, ctrl, shift, alt, win);

    public void Configure(int triggerVk, bool ctrl, bool shift, bool alt, bool win)
    {
        _triggerKey = triggerVk;
        _requiredModifiers.Clear();
        if (ctrl) _requiredModifiers.Add(VkControl);
        if (shift) _requiredModifiers.Add(VkShift);
        if (alt) _requiredModifiers.Add(VkMenu);
        if (win) _requiredModifiers.Add(VkLWin);
        IsActive = false;
    }

    /// <summary>
    /// Opcional: consulta el estado físico de una tecla (VK normalizado). Se usa para corregir modificadores
    /// "pegados" si se perdió un evento de soltar (por ejemplo al bloquear la sesión con Win+L).
    /// </summary>
    public Func<int, bool>? IsKeyDown { get; set; }

    public bool UsesAltOrWin => _requiredModifiers.Contains(VkMenu) || _requiredModifiers.Contains(VkLWin);

    /// <summary>Sale del modo "mantener" sin emitir señales (por ejemplo al pasar a modo buscador).</summary>
    public void Reset() => IsActive = false;

    public ChordResult OnKey(int vk, bool isDown)
    {
        var modifier = NormalizeModifier(vk);
        if (modifier != 0)
        {
            if (isDown) _pressedModifiers.Add(modifier);
            else _pressedModifiers.Remove(modifier);

            // Los modificadores nunca se tragan: la app de destino los vio bajar y tiene que verlos subir.
            if (IsActive && !isDown && !_requiredModifiers.Any(_pressedModifiers.Contains))
            {
                IsActive = false;
                return new ChordResult(false, ChordSignal.Released);
            }
            return ChordResult.Pass;
        }

        if (!IsActive)
        {
            if (isDown && vk == _triggerKey && ModifiersMatch())
            {
                IsActive = true;
                return new ChordResult(true, ChordSignal.Opened);
            }
            return ChordResult.Pass;
        }

        // Activo: ninguna tecla llega a la app de destino.
        if (!isDown) return new ChordResult(true, ChordSignal.None);

        return vk switch
        {
            VkEscape => Cancelled(),
            VkReturn => new ChordResult(true, ChordSignal.Confirm),
            VkTab or VkRight => new ChordResult(true, ChordSignal.NextSource),
            VkLeft => new ChordResult(true, ChordSignal.PrevSource),
            VkUp or VkPrior => new ChordResult(true, ChordSignal.Up),
            VkDown or VkNext => new ChordResult(true, ChordSignal.Down),
            VkT => new ChordResult(true, ChordSignal.NextTemplate),
            >= 0x31 and <= 0x39 => new ChordResult(true, ChordSignal.PickSlice, vk - 0x31),   // 1–9
            >= 0x61 and <= 0x69 => new ChordResult(true, ChordSignal.PickSlice, vk - 0x61),   // teclado numérico
            _ => new ChordResult(true, ChordSignal.None), // incluye la repetición del gatillo
        };
    }

    private ChordResult Cancelled()
    {
        IsActive = false;
        return new ChordResult(true, ChordSignal.Cancel);
    }

    private bool ModifiersMatch()
    {
        if (IsKeyDown != null) _pressedModifiers.RemoveWhere(m => !IsKeyDown(m));
        return _requiredModifiers.SetEquals(_pressedModifiers);
    }

    private static int NormalizeModifier(int vk) => vk switch
    {
        VkShift or VkLShift or VkRShift => VkShift,
        VkControl or VkLControl or VkRControl => VkControl,
        VkMenu or VkLMenu or VkRMenu => VkMenu,
        VkLWin or VkRWin => VkLWin,
        _ => 0,
    };
}
