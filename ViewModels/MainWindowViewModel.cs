using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Windows;
using TextPasteApp.Models;
using TextPasteApp.Services;
using TextPasteApp.Views;

namespace TextPasteApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private AppConfig _config;

    public MainWindowViewModel(IConfigurationService configService, IServiceProvider serviceProvider)
    {
        _configService = configService;
        _config = _configService.Config;
        _serviceProvider = serviceProvider;
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await _configService.LoadAsync();
        Config = _configService.Config;
    }

    [RelayCommand]
    private void AddTextItem() => Config.TextItems.Add(new TextItem());

    [RelayCommand]
    private void OpenAddNewTextItemWindow()
    {
        var view = _serviceProvider.GetRequiredService<AddNewTextItemView>();
        view.Owner = Application.Current.MainWindow;
        view.ShowDialog();
    }

    [RelayCommand]
    private async Task SaveConfig() => await _configService.SaveAsync();

    [RelayCommand]
    private async Task ImportConfig()
    {
        var dialog = new OpenFileDialog { Filter = "JSON Files|*.json" };
        if (dialog.ShowDialog() == true) await _configService.ImportAsync(dialog.FileName);
    }

    [RelayCommand]
    private async Task ExportConfig()
    {
        var dialog = new SaveFileDialog { Filter = "JSON Files|*.json" };
        if (dialog.ShowDialog() == true) await _configService.ExportAsync(dialog.FileName);
    }

    [RelayCommand]
    private void ToggleDarkMode()
    {
        Config.DarkModeEnabled = !Config.DarkModeEnabled;
        UpdateTheme();
    }

    private void UpdateTheme()
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri(Config.DarkModeEnabled
                ? "/Assets/Themes/DarkTheme.xaml"
                : "/Assets/Themes/LightTheme.xaml", UriKind.Relative)
        };

        Application.Current.Resources.MergedDictionaries[0] = dict;
    }
}
