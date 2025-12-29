using System.IO;
using System.Windows;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Окно редактирования промптов для AI операции.
/// Поддерживает два промпта: системный и пользовательский.
/// </summary>
public partial class PromptEditorWindow : Window
{
    private readonly string _operation;
    private readonly string _providerKey;
    private readonly string _defaultSystemPrompt;
    private readonly string _defaultUserPrompt;
    private readonly Action<string>? _log;

    /// <summary>
    /// Placeholders для пользовательского промпта каждой операции
    /// </summary>
    private static readonly Dictionary<string, string[]> OperationPlaceholders = new()
    {
        ["ProductExtraction"] = new[] { "{{ITEMS}}" },
        ["Classification"] = new[] { "{{EXISTING_HIERARCHY}}", "{{EXISTING_HIERARCHY_JSON}}", "{{PRODUCTS}}" },
        ["Labels"] = new[] { "{{LABELS}}", "{{ITEMS}}" },
        ["Parsing"] = new[] { "{{YEAR}}", "{{RECEIPT_TEXT}}" },
        ["Shopping"] = new[] { "{{TODAY}}", "{{SHOPPING_LIST}}" },
        ["ProductMatcher"] = new[] { "{{PRODUCT_NAME}}", "{{SEARCH_RESULTS}}" }
    };

    /// <summary>
    /// Названия операций для отображения
    /// </summary>
    private static readonly Dictionary<string, string> OperationNames = new()
    {
        ["ProductExtraction"] = "Выделение продукта",
        ["Classification"] = "Классификация товаров",
        ["Labels"] = "Назначение меток",
        ["Parsing"] = "Парсинг чеков",
        ["Shopping"] = "Закупки: Чат с AI",
        ["ProductMatcher"] = "Закупки: Выбор товара"
    };

    /// <summary>
    /// Файлы дефолтных промптов (system, user)
    /// </summary>
    private static readonly Dictionary<string, (string SystemFile, string UserFile)> DefaultPromptFiles = new()
    {
        ["ProductExtraction"] = ("prompt_extract_products_system.txt", "prompt_extract_products_user.txt"),
        ["Classification"] = ("prompt_classify_products_system.txt", "prompt_classify_products_user.txt"),
        ["Labels"] = ("prompt_assign_labels_system.txt", "prompt_assign_labels_user.txt"),
        ["Parsing"] = ("prompt_template_system.txt", "prompt_template_user.txt"),
        ["Shopping"] = ("prompt_shopping_system.txt", "prompt_shopping_user.txt"),
        ["ProductMatcher"] = ("prompt_shopping_select_product_system.txt", "prompt_shopping_select_product_user.txt")
    };

    /// <summary>
    /// Умные промпты для продвинутых моделей (GPT-4, Claude и т.п.)
    /// </summary>
    private static readonly Dictionary<string, (string System, string User)> SmartPrompts = new()
    {
        ["Classification"] = (
            System: """
                Ты — классификатор товаров для домашней бухгалтерии.
                Классифицируй продукты в существующую иерархию категорий.

                ПРАВИЛА:
                1. Каждый продукт ОБЯЗАТЕЛЬНО должен быть отнесён к категории
                2. ЗАПРЕЩЕНО использовать "Не категоризировано", "Другое", "Прочее"
                3. Если подходящей категории нет — создай новую
                4. Используй существующие категории если они подходят

                Верни JSON: {"products": [{"name": "...", "parent": "..." или null, "product": true/false}]}
                """,
            User: """
                СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ (JSON):
                {{EXISTING_HIERARCHY_JSON}}

                ПРОДУКТЫ ДЛЯ КЛАССИФИКАЦИИ:
                {{PRODUCTS}}

                Классифицируй все продукты.
                """
        )
    };

    public PromptEditorWindow(
        string operation,
        string providerKey,
        string? currentSystemPrompt,
        string? currentUserPrompt,
        Action<string>? log = null)
    {
        InitializeComponent();

        _operation = operation;
        _providerKey = providerKey;
        _log = log;

        // Load default prompts from files
        (_defaultSystemPrompt, _defaultUserPrompt) = LoadDefaultPrompts(operation);

        // Set UI
        OperationText.Text = OperationNames.TryGetValue(operation, out var name) ? name : operation;
        ProviderText.Text = providerKey;

        // Set placeholders hint
        if (OperationPlaceholders.TryGetValue(operation, out var placeholders))
        {
            PlaceholdersText.Text = string.Join(", ", placeholders);
        }
        else
        {
            PlaceholdersText.Text = "Нет доступных placeholders";
        }

        // Set prompt texts (current or default)
        SystemPromptTextBox.Text = !string.IsNullOrWhiteSpace(currentSystemPrompt)
            ? currentSystemPrompt
            : _defaultSystemPrompt;

        UserPromptTextBox.Text = !string.IsNullOrWhiteSpace(currentUserPrompt)
            ? currentUserPrompt
            : _defaultUserPrompt;

        // Show smart prompt button only for operations that have one
        SmartPromptButton.Visibility = SmartPrompts.ContainsKey(operation)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _log?.Invoke($"[PromptEditor] Opened for {operation}/{providerKey}");
    }

    /// <summary>
    /// Legacy constructor for backward compatibility
    /// </summary>
    public PromptEditorWindow(
        string operation,
        string providerKey,
        string? currentPrompt,
        Action<string>? log = null)
        : this(operation, providerKey, currentPrompt, null, log)
    {
    }

    /// <summary>
    /// Текст системного промпта
    /// </summary>
    public string SystemPromptText => SystemPromptTextBox.Text;

    /// <summary>
    /// Текст пользовательского промпта
    /// </summary>
    public string UserPromptText => UserPromptTextBox.Text;

    /// <summary>
    /// Был ли системный промпт изменён относительно дефолта
    /// </summary>
    public bool IsSystemPromptCustom =>
        !string.Equals(SystemPromptText.Trim(), _defaultSystemPrompt.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Был ли пользовательский промпт изменён относительно дефолта
    /// </summary>
    public bool IsUserPromptCustom =>
        !string.Equals(UserPromptText.Trim(), _defaultUserPrompt.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Есть ли хотя бы один кастомный промпт
    /// </summary>
    public bool HasCustomPrompts => IsSystemPromptCustom || IsUserPromptCustom;

    /// <summary>
    /// Legacy property for backward compatibility
    /// </summary>
    public string PromptText => SystemPromptText;

    /// <summary>
    /// Legacy property for backward compatibility
    /// </summary>
    public bool IsCustomPrompt => HasCustomPrompts;

    /// <summary>
    /// Загрузить дефолтные промпты из файлов
    /// </summary>
    private (string SystemPrompt, string UserPrompt) LoadDefaultPrompts(string operation)
    {
        if (!DefaultPromptFiles.TryGetValue(operation, out var files))
        {
            _log?.Invoke($"[PromptEditor] No default files for operation: {operation}");
            return (string.Empty, string.Empty);
        }

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var systemPrompt = LoadPromptFile(Path.Combine(appDir, files.SystemFile), operation, "system");
        var userPrompt = LoadPromptFile(Path.Combine(appDir, files.UserFile), operation, "user");

        return (systemPrompt, userPrompt);
    }

    private string LoadPromptFile(string filePath, string operation, string promptType)
    {
        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                _log?.Invoke($"[PromptEditor] Loaded {promptType} prompt from: {filePath}");
                return content;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[PromptEditor] Error loading {filePath}: {ex.Message}");
            }
        }
        else
        {
            _log?.Invoke($"[PromptEditor] {promptType} file not found: {filePath}");
        }

        // Return hardcoded fallback
        return GetHardcodedDefaultPrompt(operation, promptType);
    }

    /// <summary>
    /// Хардкод дефолтных промптов на случай если файлы не найдены
    /// </summary>
    private static string GetHardcodedDefaultPrompt(string operation, string promptType)
    {
        return (operation, promptType) switch
        {
            ("Classification", "system") => """
                Ты — эксперт по классификации продуктов в иерархическую систему категорий.
                Продукт — это конечный элемент иерархии (лист дерева), у него нет потомков.

                КРИТИЧЕСКИ ВАЖНО:
                - Каждый продукт из входного списка ОБЯЗАТЕЛЬНО должен быть в ответе с "product": true
                - Используй существующие категории из иерархии, если они подходят
                - Создавай новые категории только если существующие не подходят

                ФОРМАТ ОТВЕТА (строго JSON):
                {"products": [{"name": "Название", "parent": null или "name-родителя", "product": true/false}]}
                """,

            ("Classification", "user") => """
                СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ КАТЕГОРИЙ (используй её):
                {{EXISTING_HIERARCHY}}

                ПРОДУКТЫ ДЛЯ КЛАССИФИКАЦИИ:
                {{PRODUCTS}}

                Классифицируй ВСЕ продукты из входного списка.
                """,

            ("Labels", "system") => """
                Назначь подходящие метки для каждого товара из списка.

                ПРАВИЛА:
                1. Выбирай метки ТОЛЬКО из списка ДОСТУПНЫЕ МЕТКИ
                2. Товар может иметь 0, 1 или несколько меток
                3. НЕ придумывай новые метки
                4. Если ни одна метка не подходит — верни пустой массив labels: []

                ФОРМАТ ОТВЕТА (строго JSON):
                [{"item": "название товара", "labels": ["Метка1", "Метка2"]}]
                """,

            ("Labels", "user") => """
                ДОСТУПНЫЕ МЕТКИ:
                {{LABELS}}

                ТОВАРЫ:
                {{ITEMS}}

                Назначь метки товарам.
                """,

            ("ProductExtraction", "system") => """
                Извлеки название продукта из названия товара.
                Убери бренды, объёмы, маркировки. Оставь только суть.

                Примеры:
                "Молоко Домик в деревне 2.5% 1л" → "Молоко"
                "Сок J7 апельсиновый 0.97л" → "Сок апельсиновый"

                ФОРМАТ ОТВЕТА (строго JSON):
                {"products": [{"item": "исходное название", "product": "название продукта"}]}
                """,

            ("ProductExtraction", "user") => """
                ТОВАРЫ:
                {{ITEMS}}

                Извлеки продукты.
                """,

            ("Parsing", "system") => """
                Извлеки данные чека в JSON.

                ПРАВИЛА ДЛЯ ЧЕКА:
                - shop: название магазина
                - order_datetime: дата YYYY-MM-DD:hh:mm
                - total: итоговая сумма
                - items: массив товаров

                ПРАВИЛА ДЛЯ ТОВАРА:
                - name: полное название
                - quantity: количество
                - unit: единица (шт/кг/л)
                - price: цена за единицу
                - amount: итоговая цена
                """,

            ("Parsing", "user") => """
                Год если не указан: {{YEAR}}

                Текст чека:
                {{RECEIPT_TEXT}}
                """,

            ("Shopping", "system") => """
                Ты — помощник для формирования списка покупок на неделю.
                Анализируй историю покупок и помогай составить оптимальный список.
                """,

            ("Shopping", "user") => """
                Дата: {{TODAY}}

                Список покупок:
                {{SHOPPING_LIST}}
                """,

            ("ProductMatcher", "system") => """
                Выбери наиболее подходящий товар из результатов поиска.
                Учитывай название, цену, объём.

                Верни JSON: {"best": индекс, "alternatives": [индексы до 3 шт]}
                """,

            ("ProductMatcher", "user") => """
                Ищем: {{PRODUCT_NAME}}

                Результаты поиска:
                {{SEARCH_RESULTS}}
                """,

            _ => string.Empty
        };
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Сбросить оба промпта к значениям по умолчанию?\nВсе изменения будут потеряны.",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SystemPromptTextBox.Text = _defaultSystemPrompt;
            UserPromptTextBox.Text = _defaultUserPrompt;
            _log?.Invoke($"[PromptEditor] Reset to default for {_operation}");
        }
    }

    private void SmartPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SmartPrompts.TryGetValue(_operation, out var smartPrompt))
            return;

        var result = MessageBox.Show(
            "Заменить текущие промпты на умные промпты для продвинутых моделей?\n\nЭти промпты оптимизированы для GPT-4, Claude и подобных моделей.",
            "Умный промпт",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SystemPromptTextBox.Text = smartPrompt.System;
            UserPromptTextBox.Text = smartPrompt.User;
            _log?.Invoke($"[PromptEditor] Smart prompt applied for {_operation}");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SystemPromptText) && string.IsNullOrWhiteSpace(UserPromptText))
        {
            MessageBox.Show(
                "Хотя бы один промпт должен быть заполнен",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _log?.Invoke($"[PromptEditor] Saved prompts for {_operation}/{_providerKey} (system custom: {IsSystemPromptCustom}, user custom: {IsUserPromptCustom})");
        DialogResult = true;
    }
}
