using SmartBasket.Services.Ollama;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Сервис парсинга чеков, использующий выбранный LLM провайдер
/// </summary>
public interface IReceiptParsingService
{
    /// <summary>
    /// Установить путь к файлу шаблона prompt
    /// </summary>
    void SetPromptTemplatePath(string path);

    /// <summary>
    /// Распарсить текст письма и извлечь товарные позиции
    /// </summary>
    Task<ParsedReceipt> ParseReceiptAsync(
        string emailBody,
        DateTime emailDate,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
