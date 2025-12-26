using System.Runtime.CompilerServices;
using AiWebSniffer.Core.Interfaces;
using SmartBasket.Core.Shopping;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Операция сборки корзин: поиск товаров во всех магазинах с AI-выбором лучшего.
/// Этапы 2 + 3 workflow: Search → ProductSelection
/// </summary>
public interface IBasketBuilderOperation
{
    /// <summary>
    /// Собрать корзины во всех магазинах для списка товаров.
    /// Стримит события поиска и выбора через IAsyncEnumerable.
    /// </summary>
    /// <param name="items">Список товаров для поиска</param>
    /// <param name="webViewContext">Контекст WebView2 для парсеров</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Поток событий WorkflowProgress</returns>
    IAsyncEnumerable<WorkflowProgress> BuildBasketsAsync(
        IReadOnlyList<DraftItem> items,
        IWebViewContext webViewContext,
        [EnumeratorCancellation] CancellationToken ct = default);

    /// <summary>
    /// Получить собранные корзины после завершения BuildBasketsAsync
    /// </summary>
    IReadOnlyDictionary<string, PlannedBasket> GetBuiltBaskets();
}
