namespace SmartBasket.Services.Llm;

/// <summary>
/// Результат генерации текста LLM
/// </summary>
public class LlmGenerationResult
{
    public bool IsSuccess { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Общий интерфейс для LLM провайдеров (Ollama, YandexGPT, etc.)
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Название провайдера для отображения
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Проверить подключение к LLM
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сгенерировать текст по prompt
    /// </summary>
    /// <param name="prompt">Prompt для генерации</param>
    /// <param name="maxTokens">Максимальное количество токенов в ответе</param>
    /// <param name="temperature">Температура генерации (0.0 - 1.0)</param>
    /// <param name="progress">Отчёт о прогрессе (для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task<LlmGenerationResult> GenerateAsync(
        string prompt,
        int maxTokens = 2000,
        double temperature = 0.1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
