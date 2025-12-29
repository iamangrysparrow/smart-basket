namespace SmartBasket.Services.Llm;

/// <summary>
/// Сервис логирования использования токенов AI провайдерами
/// </summary>
public interface ITokenUsageService
{
    /// <summary>
    /// Записать статистику использования токенов
    /// </summary>
    /// <param name="provider">Название провайдера (GigaChat, YandexGPT, Ollama)</param>
    /// <param name="model">Используемая модель</param>
    /// <param name="aiFunction">Название AI функции (Chat, Classification, Labels, Parsing, Shopping, ShoppingChat)</param>
    /// <param name="usage">Статистика токенов</param>
    /// <param name="requestId">Уникальный ID запроса</param>
    /// <param name="sessionId">ID сессии (для группировки запросов)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task LogUsageAsync(
        string provider,
        string model,
        string aiFunction,
        LlmTokenUsage usage,
        string? requestId = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Названия AI функций для логирования
/// </summary>
public static class AiFunctionNames
{
    public const string Chat = "Chat";
    public const string Classification = "Classification";
    public const string Labels = "Labels";
    public const string Parsing = "Parsing";
    public const string Shopping = "Shopping";
    public const string ShoppingChat = "ShoppingChat";
    public const string Extraction = "Extraction";
}
