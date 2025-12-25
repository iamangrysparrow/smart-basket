using AiWebSniffer.Core.Models;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Результат выбора товара AI
/// </summary>
public record ProductSelection(
    string DraftItemId,
    string? SelectedProductId,
    int Quantity,
    string Reasoning,
    List<ProductAlternative>? Alternatives = null
);

/// <summary>
/// Альтернативный товар
/// </summary>
public record ProductAlternative(
    string ProductId,
    int Quantity,
    string Reasoning
);

/// <summary>
/// Сервис для AI-выбора лучшего товара из результатов поиска
/// </summary>
public interface IProductSelectorService
{
    /// <summary>
    /// Выбрать лучший товар из результатов поиска с помощью AI
    /// </summary>
    /// <param name="draftItem">Товар из списка покупок</param>
    /// <param name="searchResults">Результаты поиска в магазине</param>
    /// <param name="storeId">ID магазина</param>
    /// <param name="storeName">Название магазина</param>
    /// <param name="progress">Progress для отображения в UI</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Результат выбора AI</returns>
    Task<ProductSelection?> SelectBestProductAsync(
        DraftItem draftItem,
        List<ProductSearchResult> searchResults,
        string storeId,
        string storeName,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default);
}
