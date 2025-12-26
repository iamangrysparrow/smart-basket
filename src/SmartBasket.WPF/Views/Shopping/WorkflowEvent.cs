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
    SearchStarted,
    SearchCompleted,
    SearchFailed,
    ProductSelectionStarted,
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

    // === Search ===

    [ObservableProperty]
    private string? _storeName;

    [ObservableProperty]
    private string? _storeColor;

    [ObservableProperty]
    private string? _productName;

    [ObservableProperty]
    private int _searchResultCount;

    // === ProductSelection ===

    [ObservableProperty]
    private ProductSearchResult? _selectedProduct;

    [ObservableProperty]
    private string? _selectionReason;

    [ObservableProperty]
    private List<ProductSearchResult>? _alternatives;

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

        public static WorkflowEvent SearchStarted(string productName, string storeName, string storeColor) => new()
        {
            EventType = WorkflowEventType.SearchStarted,
            Timestamp = DateTime.Now,
            ProductName = productName,
            StoreName = storeName,
            StoreColor = storeColor,
            IsCompleted = false
        };

        public static WorkflowEvent SearchCompleted(string productName, string storeName, string storeColor, int count) => new()
        {
            EventType = WorkflowEventType.SearchCompleted,
            Timestamp = DateTime.Now,
            ProductName = productName,
            StoreName = storeName,
            StoreColor = storeColor,
            SearchResultCount = count
        };

        public static WorkflowEvent SearchFailed(string productName, string storeName, string storeColor, string error) => new()
        {
            EventType = WorkflowEventType.SearchFailed,
            Timestamp = DateTime.Now,
            ProductName = productName,
            StoreName = storeName,
            StoreColor = storeColor,
            Text = error,
            IsError = true
        };

        public static WorkflowEvent ProductSelectionStarted(string itemName, string storeName, string storeColor) => new()
        {
            EventType = WorkflowEventType.ProductSelectionStarted,
            Timestamp = DateTime.Now,
            ProductName = itemName,
            StoreName = storeName,
            StoreColor = storeColor,
            IsCompleted = false
        };

        public static WorkflowEvent ProductSelectionCompleted(
            string itemName,
            string storeName,
            string storeColor,
            ProductSearchResult selected,
            string reason,
            List<ProductSearchResult> alternatives) => new()
        {
            EventType = WorkflowEventType.ProductSelectionCompleted,
            Timestamp = DateTime.Now,
            ProductName = itemName,
            StoreName = storeName,
            StoreColor = storeColor,
            SelectedProduct = selected,
            SelectionReason = reason,
            Alternatives = alternatives
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
    /// Создать WorkflowEvent из WorkflowProgress
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
            SearchStartedProgress p => Factory.SearchStarted(p.ProductName, p.StoreName, p.StoreColor),
            SearchCompletedProgress p => Factory.SearchCompleted(p.ProductName, p.StoreName, p.StoreColor, p.ResultCount),
            SearchFailedProgress p => Factory.SearchFailed(p.ProductName, p.StoreName, p.StoreColor, p.Error),
            ProductSelectionStartedProgress p => Factory.ProductSelectionStarted(p.DraftItemName, p.StoreName, p.StoreColor),
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
    public DataTemplate? SearchStartedTemplate { get; set; }
    public DataTemplate? SearchCompletedTemplate { get; set; }
    public DataTemplate? SearchFailedTemplate { get; set; }
    public DataTemplate? ProductSelectionStartedTemplate { get; set; }
    public DataTemplate? ProductSelectionCompletedTemplate { get; set; }
    public DataTemplate? ProductSelectionFailedTemplate { get; set; }
    public DataTemplate? SystemMessageTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not WorkflowEvent evt)
            return base.SelectTemplate(item, container);

        return evt.EventType switch
        {
            WorkflowEventType.UserMessage => UserMessageTemplate,
            WorkflowEventType.AiMessage => AiMessageTemplate,
            WorkflowEventType.ToolCall => ToolCallTemplate,
            WorkflowEventType.ToolResult => ToolResultTemplate,
            WorkflowEventType.SearchStarted => SearchStartedTemplate,
            WorkflowEventType.SearchCompleted => SearchCompletedTemplate,
            WorkflowEventType.SearchFailed => SearchFailedTemplate,
            WorkflowEventType.ProductSelectionStarted => ProductSelectionStartedTemplate,
            WorkflowEventType.ProductSelectionCompleted => ProductSelectionCompletedTemplate,
            WorkflowEventType.ProductSelectionFailed => ProductSelectionFailedTemplate,
            WorkflowEventType.SystemMessage => SystemMessageTemplate,
            WorkflowEventType.Error => ErrorTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
