using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TextPasteApp;

/// <summary>Punto de entrada. Vive en la bandeja del sistema: no hay ventana principal.</summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "TextPasteApp.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("TextPaste ya está abierto (mira el ícono junto al reloj).", "TextPaste");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        _controller = new AppController();
        _controller.Start();

        if (_controller.IsFirstRun)
            _controller.OpenSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Un error inesperado no debe cerrar la app (los hooks dejarían de funcionar sin aviso).</summary>
    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextPasteApp");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"[{DateTime.Now:s}] {e.Exception}\n\n");
        }
        catch (IOException) { }

        MessageBox.Show(e.Exception.Message, "TextPaste — error", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
