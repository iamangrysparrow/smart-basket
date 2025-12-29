using AiWebSniffer.Core.Models;

namespace SmartBasket.Services.Shopping;

// ═══════════════════════════════════════════════════════════════════════════
// БАЗОВЫЙ ТИП
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Базовый тип для всех событий workflow закупок
/// </summary>
public abstract record WorkflowProgress(DateTime Timestamp);

// ═══════════════════════════════════════════════════════════════════════════
// ЭТАП 1: Составление корзины (чат с AI)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Сообщение пользователя
/// </summary>
public record UserMessageProgress(string Text)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Дельта текста от AI (для streaming)
/// </summary>
public record TextDeltaProgress(string Text)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// AI вызывает инструмент
/// </summary>
public record ToolCallProgress(string Name, string Args)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Результат выполнения инструмента
/// </summary>
public record ToolResultProgress(string Name, string Result, bool Success)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Чат завершён (AI закончил отвечать)
/// </summary>
public record ChatCompleteProgress(string FullText)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Ошибка в чате
/// </summary>
public record ChatErrorProgress(string Error)
    : WorkflowProgress(DateTime.UtcNow);

// ═══════════════════════════════════════════════════════════════════════════
// ЭТАП 2: Поиск в магазинах
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Начат поиск товара в магазине (внутреннее событие, используется для агрегации)
/// </summary>
public record SearchStartedProgress(string ProductName, string StoreName, string StoreColor)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Поиск завершён успешно (внутреннее событие, используется для агрегации)
/// </summary>
public record SearchCompletedProgress(
    string ProductName,
    string StoreName,
    string StoreColor,
    int ResultCount,
    List<ProductSearchResult> Results)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Поиск завершён с ошибкой (внутреннее событие, используется для агрегации)
/// </summary>
public record SearchFailedProgress(
    string ProductName,
    string StoreName,
    string StoreColor,
    string Error)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Агрегированный прогресс поиска в магазине — ОДИН на магазин (для UI).
/// Генерируется в ViewModel из SearchStarted/Completed/Failed событий.
/// </summary>
public record SearchProgressEvent(
    string StoreName,
    string StoreColor,
    int CompletedCount,
    int TotalCount)
    : WorkflowProgress(DateTime.UtcNow)
{
    public int ProgressPercent => TotalCount > 0 ? CompletedCount * 100 / TotalCount : 0;
    public bool IsCompleted => CompletedCount >= TotalCount;
}

// ═══════════════════════════════════════════════════════════════════════════
// ЭТАП 3: Выбор товаров (AI выбирает лучший вариант)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Начат выбор товара для позиции
/// </summary>
public record ProductSelectionStartedProgress(
    string DraftItemName,
    string StoreName,
    string StoreColor)
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Выбор товара завершён
/// </summary>
public record ProductSelectionCompletedProgress(
    string DraftItemName,
    string StoreName,
    string StoreColor,
    ProductSearchResult Selected,
    string Reason,
    List<ProductSearchResult> Alternatives)  // Максимум 3, из того же магазина
    : WorkflowProgress(DateTime.UtcNow);

/// <summary>
/// Не удалось выбрать товар (не найден подходящий)
/// </summary>
public record ProductSelectionFailedProgress(
    string DraftItemName,
    string StoreName,
    string StoreColor,
    string Reason)
    : WorkflowProgress(DateTime.UtcNow);

// ═══════════════════════════════════════════════════════════════════════════
// СИСТЕМНЫЕ
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Системное сообщение (информация, предупреждение)
/// </summary>
public record SystemMessageProgress(string Text, bool IsWarning = false)
    : WorkflowProgress(DateTime.UtcNow);
