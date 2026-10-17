using TextPasteApp.Core.Models;

namespace TextPasteApp.Core.Logic;

/// <summary>Por qué se eligió un proyecto (se muestra en el buscador).</summary>
public enum ProjectReason
{
    None,
    WindowTitle,   // el título de la ventana donde vas a pegar contiene una palabra clave del proyecto
    OpenInExcel,   // el archivo del proyecto está abierto en Excel
    LastUsed,      // el último que usaste
    Only,          // es el único
}

/// <summary>
/// Decide qué taglist usar cuando abres el buscador:
///   1. Si el título de la ventana activa (tu SCADA) contiene una palabra clave de un proyecto → ese.
///   2. Si el archivo de un proyecto está abierto en Excel → ese.
///   3. Si no, el último que usaste.
/// </summary>
public static class ProjectResolver
{
    public static (TaglistProject? Project, ProjectReason Reason) Resolve(
        IReadOnlyList<TaglistProject> projects,
        string? foregroundTitle,
        IEnumerable<string> windowTitles,
        string? lastProject)
    {
        var usable = projects.Where(p => !string.IsNullOrWhiteSpace(p.FilePath)).ToList();
        if (usable.Count == 0) return (null, ProjectReason.None);

        var title = foregroundTitle ?? "";
        foreach (var project in usable)
        {
            var keywords = project.WindowMatch.Split(',', ';').Select(k => k.Trim()).Where(k => k.Length > 0);
            if (keywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return (project, ProjectReason.WindowTitle);
        }

        var titles = windowTitles.ToList();
        foreach (var project in usable)
        {
            var fileName = FileNameOf(project.FilePath);
            if (fileName.Length > 0 && titles.Any(t => t.Contains(fileName, StringComparison.OrdinalIgnoreCase)))
                return (project, ProjectReason.OpenInExcel);
        }

        var last = usable.FirstOrDefault(p => p.Name == lastProject);
        if (last != null) return (last, usable.Count == 1 ? ProjectReason.Only : ProjectReason.LastUsed);
        return (usable[0], usable.Count == 1 ? ProjectReason.Only : ProjectReason.LastUsed);
    }

    /// <summary>Nombre del archivo sin carpeta ni extensión (acepta rutas con \ o /).</summary>
    public static string FileNameOf(string path)
    {
        var name = path.Split('\\', '/').LastOrDefault() ?? "";
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
