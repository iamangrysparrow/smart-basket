using System.Windows;
using System.Windows.Controls;
using AiWebSniffer.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Services.Shopping;

namespace SmartBasket.WPF.Views.Shopping;

/// <summary>
/// Тип события workflow для UI
/// </summary>
public enum WorkflowEventType
{
    UserMessage,
    AiMessage,
    ToolCall,
    ToolResult,
    ProductSelectionCompleted,
    ProductSelectionFailed,
    SystemMessage,
    Error
}

/// <summary>
/// UI-обёртка для событий workflow.
/// ObservableObject для поддержки binding и обновления в процессе (streaming).
/// </summary>
public partial class WorkflowEvent : ObservableObject
{
    [ObservableProperty]
    private WorkflowEventType _eventType;

    [ObservableProperty]
    private DateTime _timestamp;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isCompleted = true;

    [ObservableProperty]
    private bool _isError;

    // === ToolCall ===

    [ObservableProperty]
    private string? _toolName;

    [ObservableProperty]
    private string? _toolArgs;

    [ObservableProperty]
    private string? _toolResult;

    [ObservableProperty]
    private bool _toolSuccess;

    // === Search Progress ===

    [ObservableProperty]
    private string? _storeName;

    [ObservableProperty]
    private string? _storeColor;

    /// <summary>Количество завершённых поисков</summary>
    [ObservableProperty]
    private int _completedCount;

    /// <summary>Общее количество товаров для поиска</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Процент завершения (0-100)</summary>
    public int ProgressPercent => TotalCount > 0 ? CompletedCount * 100 / TotalCount : 0;

    /// <summary>Поиск завершён</summary>
    public bool IsSearchCompleted => CompletedCount >= TotalCount && TotalCount > 0;

    /// <summary>
    /// Публичный метод для уведомления UI об изменении вычисляемых свойств.
    /// Вызывается извне при обновлении CompletedCount/TotalCount.
    /// </summary>
    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsSearchCompleted));
    }

    // === ProductSelection ===

    [ObservableProperty]
    private string? _productName;

    /// <summary>Эмодзи товара (из ProductEmoji)</summary>
    [ObservableProperty]
    private string _productEmoji = "📦";

    [ObservableProperty]
    private ProductSearchResult? _selectedProduct;

    [ObservableProperty]
    private string? _selectionReason;

    [ObservableProperty]
    private List<ProductSearchResult>? _alternatives;

    /// <summary>Количество единиц товара (из AI выбора)</summary>
    [ObservableProperty]
    private int _quantity = 1;

    /// <summary>Общая стоимость (цена × количество)</summary>
    public decimal LineTotal => (SelectedProduct?.Price ?? 0) * Quantity;

    /// <summary>Текст количества для UI (например "1 л × 2")</summary>
    public string QuantityText
    {
        get
        {
            if (SelectedProduct == null) return "";
            var size = SelectedProduct.Quantity > 0
                ? $"{SelectedProduct.Quantity:#.##} {SelectedProduct.Unit}"
                : SelectedProduct.Unit ?? "";
            return Quantity > 1 ? $"{size} × {Quantity}" : size;
        }
    }

    /// <summary>
    /// Фабричные методы для создания событий
    /// </summary>
    public static class Factory
    {
        public static WorkflowEvent UserMessage(string text) => new()
        {
            EventType = WorkflowEventType.UserMessage,
            Timestamp = DateTime.Now,
            Text = text
        };

        public static WorkflowEvent AiMessage(string text = "", bool isCompleted = false) => new()
        {
            EventType = WorkflowEventType.AiMessage,
            Timestamp = DateTime.Now,
            Text = text,
            IsCompleted = isCompleted
        };

        public static WorkflowEvent ToolCall(string name, string args) => new()
        {
            EventType = WorkflowEventType.ToolCall,
            Timestamp = DateTime.Now,
            ToolName = name,
            ToolArgs = args,
            IsCompleted = false
        };

        public static WorkflowEvent ToolResult(string name, string result, bool success) => new()
        {
            EventType = WorkflowEventType.ToolResult,
            Timestamp = DateTime.Now,
            ToolName = name,
            ToolResult = result,
            ToolSuccess = success
        };

        public static WorkflowEvent ProductSelectionCompleted(
            string itemName,
            string storeName,
            string storeColor,
            ProductSearchResult selected,
            string reason,
            List<ProductSearchResult> alternatives,
            int quantity = 1) => new()
        {
            EventType = WorkflowEventType.ProductSelectionCompleted,
            Timestamp = DateTime.Now,
            ProductName = itemName,
            StoreName = storeName,
            StoreColor = storeColor,
            SelectedProduct = selected,
            SelectionReason = reason,
            Alternatives = alternatives,
            Quantity = quantity
        };

        public static WorkflowEvent ProductSelectionFailed(
            string itemName,
            string storeName,
            string storeColor,
            string reason) => new()
        {
            EventType = WorkflowEventType.ProductSelectionFailed,
            Timestamp = DateTime.Now,
            ProductName = itemName,
            StoreName = storeName,
            StoreColor = storeColor,
            Text = reason,
            IsError = true
        };

        public static WorkflowEvent SystemMessage(string text, bool isWarning = false) => new()
        {
            EventType = WorkflowEventType.SystemMessage,
            Timestamp = DateTime.Now,
            Text = text,
            IsError = isWarning
        };

        public static WorkflowEvent Error(string error) => new()
        {
            EventType = WorkflowEventType.Error,
            Timestamp = DateTime.Now,
            Text = error,
            IsError = true
        };
    }

    /// <summary>
    /// Создать WorkflowEvent из WorkflowProgress.
    /// ВАЖНО: SearchStarted/Completed/Failed НЕ конвертируются напрямую —
    /// они агрегируются в ViewModel в один SearchProgress на магазин.
    /// ProductSelectionStarted также НЕ показывается — товар показывается только после выбора.
    /// </summary>
    public static WorkflowEvent? FromProgress(WorkflowProgress progress)
    {
        return progress switch
        {
            UserMessageProgress p => Factory.UserMessage(p.Text),
            TextDeltaProgress => null, // Дельты обрабатываются отдельно (накапливаются в AiMessage)
            ToolCallProgress p => Factory.ToolCall(p.Name, p.Args),
            ToolResultProgress p => Factory.ToolResult(p.Name, p.Result, p.Success),
            ChatCompleteProgress => null, // Обрабатывается отдельно (завершает AiMessage)
            ChatErrorProgress p => Factory.Error(p.Error),
            // Search events агрегируются в ViewModel в StoreProgressGroup — не конвертируем напрямую
            SearchStartedProgress => null,
            SearchCompletedProgress => null,
            SearchFailedProgress => null,
            SearchProgressEvent => null, // Обрабатывается через StoreProgressGroup
            // ProductSelectionStarted не показываем — товар показывается только после выбора
            ProductSelectionStartedProgress => null,
            ProductSelectionCompletedProgress p => Factory.ProductSelectionCompleted(
                p.DraftItemName, p.StoreName, p.StoreColor, p.Selected, p.Reason, p.Alternatives),
            ProductSelectionFailedProgress p => Factory.ProductSelectionFailed(
                p.DraftItemName, p.StoreName, p.StoreColor, p.Reason),
            SystemMessageProgress p => Factory.SystemMessage(p.Text, p.IsWarning),
            _ => null
        };
    }
}

/// <summary>
/// Template selector для WorkflowEvent
/// </summary>
public class WorkflowEventTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserMessageTemplate { get; set; }
    public DataTemplate? AiMessageTemplate { get; set; }
    public DataTemplate? ToolCallTemplate { get; set; }
    public DataTemplate? ToolResultTemplate { get; set; }
    public DataTemplate? StoreGroupTemplate { get; set; }
    public DataTemplate? SystemMessageTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is StoreProgressGroup)
            return StoreGroupTemplate;

        if (item is not WorkflowEvent evt)
            return base.SelectTemplate(item, container);

        return evt.EventType switch
        {
            WorkflowEventType.UserMessage => UserMessageTemplate,
            WorkflowEventType.AiMessage => AiMessageTemplate,
            WorkflowEventType.ToolCall => ToolCallTemplate,
            WorkflowEventType.ToolResult => ToolResultTemplate,
            WorkflowEventType.SystemMessage => SystemMessageTemplate,
            WorkflowEventType.Error => ErrorTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}

/// <summary>
/// Группа событий по магазину — заголовок с прогрессом + вложенные карточки товаров
/// </summary>
public partial class StoreProgressGroup : ObservableObject
{
    public string StoreName { get; init; } = "";
    public string StoreColor { get; init; } = "#888888";

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>Процент завершения (0-100)</summary>
    public int ProgressPercent => TotalCount > 0 ? CompletedCount * 100 / TotalCount : 0;

    /// <summary>Поиск завершён</summary>
    public bool IsSearchCompleted => CompletedCount >= TotalCount && TotalCount > 0;

    /// <summary>Карточки выбранных/не найденных товаров</summary>
    public System.Collections.ObjectModel.ObservableCollection<WorkflowEvent> Items { get; } = new();

    /// <summary>
    /// Публичный метод для уведомления UI об изменении вычисляемых свойств
    /// </summary>
    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsSearchCompleted));
    }
}

/// <summary>
/// Template selector для карточек внутри StoreProgressGroup
/// </summary>
public class ProductCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ProductSelectionCompletedTemplate { get; set; }
    public DataTemplate? ProductSelectionFailedTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not WorkflowEvent evt)
            return base.SelectTemplate(item, container);

        return evt.EventType switch
        {
            WorkflowEventType.ProductSelectionCompleted => ProductSelectionCompletedTemplate,
            WorkflowEventType.ProductSelectionFailed => ProductSelectionFailedTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
