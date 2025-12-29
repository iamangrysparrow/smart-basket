using Microsoft.Extensions.Logging;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Менеджер сессий для AI операций.
/// Управляет жизненным циклом сессий и обновляет контекст между вызовами.
/// </summary>
public interface IAiSessionManager
{
    /// <summary>
    /// Создать новый контекст сессии для операции.
    /// </summary>
    /// <param name="operationType">Тип операции для логирования</param>
    /// <returns>Новый контекст сессии с уникальным SessionId</returns>
    LlmSessionContext CreateSession(string? operationType = null);

    /// <summary>
    /// Обновить контекст после получения ответа от LLM.
    /// Используется для сохранения response_id (YandexAgent stateful API).
    /// </summary>
    /// <param name="context">Текущий контекст</param>
    /// <param name="result">Результат от LLM</param>
    /// <returns>Обновлённый контекст</returns>
    LlmSessionContext UpdateFromResponse(LlmSessionContext context, LlmGenerationResult result);
}

/// <summary>
/// Реализация менеджера сессий AI.
/// </summary>
public class AiSessionManager : IAiSessionManager
{
    private readonly ILogger<AiSessionManager> _logger;

    public AiSessionManager(ILogger<AiSessionManager> logger)
    {
        _logger = logger;
    }

    public LlmSessionContext CreateSession(string? operationType = null)
    {
        var context = LlmSessionContext.Create(operationType);
        _logger.LogDebug(
            "Created AI session: {SessionId} for operation: {OperationType}",
            context.SessionId,
            operationType ?? "(none)");
        return context;
    }

    public LlmSessionContext UpdateFromResponse(LlmSessionContext context, LlmGenerationResult result)
    {
        // Если ответ содержит ResponseId (YandexAgent), обновляем контекст
        if (!string.IsNullOrEmpty(result.ResponseId))
        {
            var updatedContext = context.WithPreviousResponseId(result.ResponseId);
            _logger.LogDebug(
                "Updated session {SessionId}: PreviousResponseId = {ResponseId}",
                context.SessionId,
                result.ResponseId);
            return updatedContext;
        }

        return context;
    }
}
