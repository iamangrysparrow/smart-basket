namespace SmartBasket.Core.Configuration;

public class YandexGptSettings
{
    /// <summary>
    /// API Key для Yandex Cloud (IAM token или API key)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Folder ID в Yandex Cloud
    /// </summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>
    /// Модель YandexGPT (yandexgpt-lite, yandexgpt, yandexgpt-32k)
    /// </summary>
    public string Model { get; set; } = "yandexgpt-lite";

    /// <summary>
    /// Температура генерации (0.0 - 1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Таймаут запроса в секундах
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Максимальное количество токенов в ответе
    /// </summary>
    public int MaxTokens { get; set; } = 2000;
}
