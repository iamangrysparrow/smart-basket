using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Sources;

/// <summary>
/// Интерфейс источника чеков
/// </summary>
public interface IReceiptSource
{
    /// <summary>
    /// Уникальное имя источника (из конфигурации)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Тип источника
    /// </summary>
    SourceType Type { get; }

    /// <summary>
    /// Имя парсера для обработки данных
    /// </summary>
    string ParserName { get; }

    /// <summary>
    /// Проверить подключение к источнику
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить сырые данные чеков из источника
    /// </summary>
    Task<IReadOnlyList<RawReceipt>> FetchAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
