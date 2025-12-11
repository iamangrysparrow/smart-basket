using System.IO;
using System.Text.Json;
using SmartBasket.Core.Configuration;
using SmartBasket.Core.Helpers;

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
        // Encrypt sensitive data before saving
        EncryptSecrets(settings);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);

        // Decrypt back so in-memory settings remain usable
        DecryptSecrets(settings);
    }

    public Task SaveSettingsAsync(AppSettings settings)
    {
        return Task.Run(() => Save(settings));
    }

    /// <summary>
    /// Зашифровать все секретные поля перед сохранением
    /// </summary>
    private static void EncryptSecrets(AppSettings settings)
    {
        // ReceiptSources - Email passwords
        foreach (var source in settings.ReceiptSources)
        {
            if (source.Email != null && !string.IsNullOrEmpty(source.Email.Password))
            {
                source.Email.Password = SecretHelper.Encrypt(source.Email.Password);
            }
        }

        // AiProviders - API keys
        foreach (var provider in settings.AiProviders)
        {
            if (!string.IsNullOrEmpty(provider.ApiKey))
            {
                provider.ApiKey = SecretHelper.Encrypt(provider.ApiKey);
            }
        }
    }

    /// <summary>
    /// Расшифровать все секретные поля после загрузки
    /// </summary>
    public static void DecryptSecrets(AppSettings settings)
    {
        // ReceiptSources - Email passwords
        foreach (var source in settings.ReceiptSources)
        {
            if (source.Email != null && !string.IsNullOrEmpty(source.Email.Password))
            {
                source.Email.Password = SecretHelper.Decrypt(source.Email.Password);
            }
        }

        // AiProviders - API keys
        foreach (var provider in settings.AiProviders)
        {
            if (!string.IsNullOrEmpty(provider.ApiKey))
            {
                provider.ApiKey = SecretHelper.Decrypt(provider.ApiKey);
            }
        }
    }
}
