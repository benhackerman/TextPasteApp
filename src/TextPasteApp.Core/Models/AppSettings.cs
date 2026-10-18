using System.Text.Json;

namespace TextPasteApp.Core.Models;

/// <summary>Toda la configuración de la app. Se guarda como settings.json en %AppData%\TextPasteApp.</summary>
public sealed class AppSettings
{
    /// <summary>Atajo de la ruleta (historial / favoritos): mantener y soltar.</summary>
    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>Atajo que abre directo el taglist con el buscador.</summary>
    public HotkeySettings TaglistHotkey { get; set; } = new() { Ctrl = true, Shift = true, Key = "T" };

    /// <summary>Atajo que abre el buscador de funciones (código AWL guardado).</summary>
    public HotkeySettings FunctionsHotkey { get; set; } = new() { Ctrl = true, Shift = true, Key = "F" };

    /// <summary>Segundos que estimas ahorrar por cada función pegada.</summary>
    public int SecondsSavedFunction { get; set; } = 60;

    /// <summary>Opacidad de la ruleta y del taglist (0.4 a 1).</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>Cuántos textos copiados se recuerdan.</summary>
    public int HistoryLimit { get; set; } = 50;

    /// <summary>Guardar el historial entre sesiones.</summary>
    public bool PersistHistory { get; set; } = true;

    /// <summary>Después de pegar, devolver al portapapeles lo que había antes.</summary>
    public bool RestoreClipboardAfterPaste { get; set; } = true;

    /// <summary>Un toque rápido del atajo deja la ruleta abierta con buscador.</summary>
    public bool TapToStick { get; set; } = true;

    /// <summary>Cantidad de sectores de la ruleta por página.</summary>
    public int SlicesPerPage { get; set; } = 8;

    /// <summary>Fuente que se muestra al abrir (se recuerda la última usada).</summary>
    public SourceKind LastSource { get; set; } = SourceKind.History;

    public List<Snippet> Favorites { get; set; } = new()
    {
        new Snippet("ON", "ON"),
        new Snippet("OFF", "OFF"),
    };

    /// <summary>Perfiles ("plantillas") de taglist. Normalmente uno solo que reutilizas en cada proyecto.</summary>
    public List<TaglistProfile> TaglistProfiles { get; set; } = new() { new TaglistProfile() };

    /// <summary>Nombre del perfil en uso (el del proyecto activo; se mantiene por compatibilidad).</summary>
    public string ActiveProfile { get; set; } = "Mi taglist";

    /// <summary>Proyectos: archivo de taglist + perfil + cómo reconocerlo.</summary>
    public List<TaglistProject> Projects { get; set; } = new();

    /// <summary>Último proyecto usado (se usa si ninguna regla reconoce la ventana actual).</summary>
    public string ActiveProject { get; set; } = "";

    /// <summary>Segundos que estimas ahorrar por cada valor pegado desde el taglist (para las estadísticas).</summary>
    public int SecondsSavedTaglist { get; set; } = 20;

    /// <summary>Segundos que estimas ahorrar por cada pegado desde la ruleta.</summary>
    public int SecondsSavedRing { get; set; } = 4;

    /// <summary>Los textos más largos que esto no se guardan en el historial.</summary>
    public int MaxHistoryTextLength { get; set; } = 2000;

    /// <summary>Pausa entre los valores de una secuencia, en milisegundos (súbela si el SCADA es lento).</summary>
    public int SequenceDelayMs { get; set; } = 120;

    /// <summary>Buscar versiones nuevas en GitHub al iniciar.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Última hoja usada en el taglist ("" = todas).</summary>
    public string LastSheet { get; set; } = "";

    /// <summary>El perfil activo (o el primero si el nombre no existe).</summary>
    public TaglistProfile CurrentProfile()
    {
        if (TaglistProfiles.Count == 0) TaglistProfiles.Add(new TaglistProfile());
        return TaglistProfiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? TaglistProfiles[0];
    }

    /// <summary>Perfil por nombre (o el primero).</summary>
    public TaglistProfile ProfileFor(TaglistProject project)
    {
        if (TaglistProfiles.Count == 0) TaglistProfiles.Add(new TaglistProfile());
        return TaglistProfiles.FirstOrDefault(p => p.Name == project.Profile) ?? TaglistProfiles[0];
    }

    /// <summary>
    /// Pasa la configuración antigua (un archivo dentro del perfil) a proyectos. Devuelve true si cambió algo.
    /// </summary>
    public bool EnsureProjects()
    {
        if (Projects.Count > 0) return false;
        var profile = CurrentProfile();
        if (string.IsNullOrWhiteSpace(profile.FilePath)) return false;
        Projects.Add(new TaglistProject
        {
            Name = Logic.ProjectResolver.FileNameOf(profile.FilePath),
            FilePath = profile.FilePath,
            Profile = profile.Name,
        });
        ActiveProject = Projects[0].Name;
        return true;
    }

    public List<TextTemplate> Templates { get; set; } = new()
    {
        new TextTemplate("Sin plantilla", "{valor}"),
        new TextTemplate("Módulo de control", "MC_{valor}"),
        new TextTemplate("Comando", "PLC1.MC_{valor}.CMD"),
    };

    public int ActiveTemplateIndex { get; set; }

    /// <summary>Copia independiente (para editar en la ventana de configuración sin tocar la original).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();
}

/// <summary>
/// Atajo de teclado. Se mantiene presionado para abrir la ruleta y se suelta para pegar.
/// Key es el nombre de una tecla de WPF (System.Windows.Input.Key), por ejemplo "Space" o "V".
/// </summary>
public sealed class HotkeySettings
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; }
    public bool Win { get; set; }
    public string Key { get; set; } = "Space";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}
