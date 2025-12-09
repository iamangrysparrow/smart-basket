using System.IO;
using System.Text.Json;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        // Use same path as ConfigurationBuilder in App.xaml.cs
        _settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
    }

    public string SettingsPath => _settingsPath;

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
