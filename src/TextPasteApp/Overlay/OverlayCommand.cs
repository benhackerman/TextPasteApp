namespace TextPasteApp.Overlay;

/// <summary>Acciones que la ventana de la ruleta le pide al controlador (en modo buscador, con foco).</summary>
public enum OverlayCommand
{
    Cancel,
    Confirm,
    NextSource,
    PrevSource,
    Move,          // valor = pasos (flechas)
    Scroll,        // valor = pasos (rueda)
    HoverSlice,    // valor = sector (-1 = centro)
    NextTemplate,
    FilterChanged,
}
