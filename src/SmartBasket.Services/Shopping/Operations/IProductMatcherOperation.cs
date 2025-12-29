using AiWebSniffer.Core.Models;
using SmartBasket.Core.Shopping;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Результат выбора товара AI моделью
/// </summary>
public record ProductSelectionResult(
    /// <summary>Выбранный товар (null если не найден подходящий)</summary>
    ProductSearchResult? Selected,

    /// <summary>Обоснование выбора</summary>
    string Reason,

    /// <summary>Альтернативы (до 3 штук, из того же магазина)</summary>
    List<ProductSearchResult> Alternatives,

    /// <summary>Успешно ли выполнен выбор</summary>
    bool Success,

    /// <summary>Количество упаковок для заказа (рассчитано AI)</summary>
    int Quantity = 1
);

/// <summary>
/// Интерфейс операции выбора товара из результатов поиска.
/// Инкапсулирует AI логику: фильтрация нерелевантных, выбор лучшего, альтернативы.
/// </summary>
public interface IProductMatcherOperation
{
    /// <summary>
    /// Выбрать товар из результатов поиска.
    /// </summary>
    /// <param name="draftItem">Позиция из списка покупок</param>
    /// <param name="candidates">Результаты поиска в магазине</param>
    /// <param name="purchaseHistory">История покупок этого товара (для учёта предпочтений)</param>
    /// <param name="llmSessionId">ID сессии LLM для кэширования токенов</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Результат выбора с обоснованием и альтернативами</returns>
    Task<ProductSelectionResult> SelectProductAsync(
        DraftItem draftItem,
        List<ProductSearchResult> candidates,
        List<string>? purchaseHistory = null,
        string? llmSessionId = null,
        CancellationToken ct = default);
}
