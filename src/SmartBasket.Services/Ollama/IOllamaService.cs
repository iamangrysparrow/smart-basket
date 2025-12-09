using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Ollama;

public interface IOllamaService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    void SetPromptTemplatePath(string path);
    /// <summary>
    /// Проверить подключение к Ollama
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(
        OllamaSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Распарсить текст письма и извлечь товарные позиции
    /// </summary>
    Task<ParsedReceipt> ParseReceiptAsync(
        OllamaSettings settings,
        string emailBody,
        DateTime emailDate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
