using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TextPasteApp.Update;

/// <summary>
/// Busca versiones nuevas en las "Releases" de GitHub y las instala con el instalador (sin pedir permisos de administrador).
/// Para publicar una versión: Actions → "Release" → Run workflow.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/benhackerman/TextPasteApp/releases/latest";

    public sealed record UpdateInfo(Version Version, string SetupUrl, string Notes);

    /// <summary>Versión de esta app (la de &lt;Version&gt; en el .csproj).</summary>
    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Devuelve la versión nueva si existe, o null. El error (sin internet, etc.) va en el segundo valor.</summary>
    public static async Task<(UpdateInfo? Update, string? Error)> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TextPasteApp/" + Current);
            using var response = await http.GetAsync(LatestReleaseApi);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Todavía no hay versiones publicadas."
                    : $"GitHub respondió {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return (null, $"Versión no reconocida: {tag}");

            var setup = root.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .FirstOrDefault(a => a.Name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (setup.Url.Length == 0) return (null, "La versión publicada no trae instalador.");

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return Normalize(latest) > Current ? (new UpdateInfo(Normalize(latest), setup.Url, notes), null) : (null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                   or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            return (null, "No se pudo consultar GitHub: " + ex.Message);
        }
    }


    /// <summary>Descarga el instalador, lo ejecuta en silencio y cierra la app (el instalador la vuelve a abrir).</summary>
    public static async Task InstallAsync(UpdateInfo update)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TextPaste-{update.Version}-Setup.exe");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TextPasteApp/" + Current);
            var bytes = await http.GetByteArrayAsync(update.SetupUrl);
            await File.WriteAllBytesAsync(path, bytes);
        }

        Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true,
        });
        System.Windows.Application.Current.Shutdown();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
