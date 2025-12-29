using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Entities;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Реализация сервиса логирования использования токенов
/// </summary>
public class TokenUsageService : ITokenUsageService
{
    private readonly IDbContextFactory<SmartBasket.Data.SmartBasketDbContext> _dbContextFactory;
    private readonly ILogger<TokenUsageService> _logger;

    public TokenUsageService(
        IDbContextFactory<SmartBasket.Data.SmartBasketDbContext> dbContextFactory,
        ILogger<TokenUsageService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task LogUsageAsync(
        string provider,
        string model,
        string aiFunction,
        LlmTokenUsage usage,
        string? requestId = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Используем отдельный DbContext для каждого вызова, чтобы избежать
            // конфликтов при concurrent access (несколько запросов параллельно)
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = new TokenUsage
            {
                Provider = provider,
                Model = model,
                DateTime = DateTime.UtcNow,
                RequestId = requestId,
                SessionId = sessionId,
                AiFunction = aiFunction,
                PromptTokens = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens,
                PrecachedPromptTokens = usage.PrecachedPromptTokens,
                ReasoningTokens = usage.ReasoningTokens,
                TotalTokens = usage.TotalTokens
            };

            dbContext.TokenUsages.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "[TokenUsage] Logged: {Provider}/{Model} {Function} - prompt:{Prompt} completion:{Completion} total:{Total}",
                provider, model, aiFunction,
                usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не прерываем основной поток
            _logger.LogWarning(ex,
                "[TokenUsage] Failed to log usage for {Provider}/{Model}: {Error}",
                provider, model, ex.Message);
        }
    }
}
