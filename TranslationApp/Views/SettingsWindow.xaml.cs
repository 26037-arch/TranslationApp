using System.Windows;
using TranslationApp.Services;
using TranslationApp.Services.Startup;
using TranslationApp.Settings;

namespace TranslationApp.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly StartupManager _startupManager;

    public SettingsWindow(AppSettings settings, SettingsService settingsService, StartupManager startupManager)
    {
        _settings = settings;
        _settingsService = settingsService;
        _startupManager = startupManager;
        InitializeComponent();
        DataContext = settings;
        StartupCheckBox.IsChecked = startupManager.IsEnabled;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartupCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
        _startupManager.SetEnabled(_settings.StartWithWindows);
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
