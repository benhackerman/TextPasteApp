using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using TextPasteApp.Services;
using TextPasteApp.ViewModels;
using TextPasteApp.Views;

namespace TextPasteApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var sv = new ServiceCollection();

            // Servicios
            sv.AddSingleton<IConfigurationService, JsonConfigurationService>();

            // ViewModels
            sv.AddTransient<MainWindowViewModel>();
            sv.AddTransient<AddNewTextItemViewModel>();
            sv.AddTransient<ConfigurationViewModel>();
            sv.AddTransient<RuntimeViewModel>();

            // Vistas
            sv.AddTransient<MainWindow>();
            sv.AddTransient<AddNewTextItemView>();
            sv.AddTransient<ConfigurationView>();
            sv.AddTransient<RuntimeView>();

            var provider = sv.BuildServiceProvider();
            var mainWindow = provider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e); // Llamada al método base
        }
    }
}