using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TextPasteApp.Tray;

/// <summary>Ícono en la bandeja del sistema (junto al reloj) con el menú de la app.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _controller;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _projectsItem;
    private readonly Forms.ToolStripMenuItem _updateItem;
    private readonly Drawing.Icon _image;

    public TrayIcon(AppController controller, string hotkeyText)
    {
        _controller = controller;
        _image = CreateIcon();

        _pauseItem = new Forms.ToolStripMenuItem("Pausar", null, (_, _) => TogglePause());
        _projectsItem = new Forms.ToolStripMenuItem("Proyecto");
        _updateItem = new Forms.ToolStripMenuItem("Actualizar", null, async (_, _) => await _controller.InstallUpdateAsync())
        {
            Visible = false,
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold),
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_updateItem);
        menu.Items.Add("Configuración…", null, (_, _) => _controller.OpenSettings());
        menu.Items.Add("Estadísticas…", null, (_, _) => _controller.OpenStats());
        menu.Items.Add(_projectsItem);
        menu.Items.Add("Recargar taglist", null, (_, _) => ShowBalloon(_controller.ReloadTaglist()));
        menu.Items.Add("Borrar historial (menos lo fijado)", null, (_, _) => _controller.ClearHistory());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add($"Buscar actualizaciones (v{Update.UpdateChecker.Current})", null,
            async (_, _) => await _controller.CheckForUpdatesAsync(silent: false));
        menu.Items.Add("Salir", null, (_, _) => _controller.Exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _controller.OpenSettings();
        SetHotkeyText(hotkeyText);
        RefreshProjects();
    }

    /// <summary>Submenú "Proyecto": el activo con ✓. Elegir uno lo deja como predeterminado.</summary>
    public void RefreshProjects()
    {
        _projectsItem.DropDownItems.Clear();
        foreach (var project in _controller.Projects)
        {
            var name = project.Name;
            _projectsItem.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => _controller.SetActiveProject(name))
            {
                Checked = name == _controller.ActiveProjectName,
            });
        }
        if (_projectsItem.DropDownItems.Count == 0)
            _projectsItem.DropDownItems.Add(new Forms.ToolStripMenuItem("(ninguno: agrégalos en Configuración)") { Enabled = false });
        _projectsItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        _projectsItem.DropDownItems.Add(new Forms.ToolStripMenuItem("Se elige solo según la ventana o el Excel abierto") { Enabled = false });
    }

    public void SetUpdate(Update.UpdateChecker.UpdateInfo? update)
    {
        _updateItem.Visible = update != null;
        if (update != null) _updateItem.Text = $"Actualizar a la versión {update.Version}";
    }

    public void SetHotkeyText(string hotkeyText)
    {
        var text = $"TextPaste — {hotkeyText}";
        _icon.Text = text.Length > 63 ? text[..63] : text; // límite de Windows
    }

    public void ShowBalloon(string message)
    {
        _icon.BalloonTipTitle = "TextPaste";
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    private void TogglePause()
    {
        _controller.Paused = !_controller.Paused;
        _pauseItem.Checked = _controller.Paused;
    }

    /// <summary>Dibuja el ícono en código (un anillo dividido) para no depender de un archivo .ico.</summary>
    private static Drawing.Icon CreateIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            var colors = new[]
            {
                Drawing.Color.FromArgb(76, 154, 255),
                Drawing.Color.FromArgb(255, 181, 71),
                Drawing.Color.FromArgb(61, 220, 151),
            };
            for (var i = 0; i < 3; i++)
            {
                using var pen = new Drawing.Pen(colors[i], 6);
                g.DrawArc(pen, 4, 4, 24, 24, -90 + i * 120 + 6, 108);
            }
        }
        var handle = bmp.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
