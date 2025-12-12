using System.IO;
using System.Windows;

namespace SmartBasket.WPF.Views;

/// <summary>
/// Окно редактирования промпта для AI операции
/// </summary>
public partial class PromptEditorWindow : Window
{
    private readonly string _operation;
    private readonly string _providerKey;
    private readonly string _defaultPrompt;
    private readonly Action<string>? _log;

    /// <summary>
    /// Placeholders для каждой операции
    /// </summary>
    private static readonly Dictionary<string, string[]> OperationPlaceholders = new()
    {
        ["Classification"] = new[] { "{{EXISTING_HIERARCHY}}", "{{EXISTING_HIERARCHY_JSON}}", "{{ITEMS}}" },
        ["Labels"] = new[] { "{{LABELS}}", "{{ITEMS}}" },
        ["Parsing"] = new[] { "{{YEAR}}", "{{RECEIPT_TEXT}}" }
    };

    /// <summary>
    /// Названия операций для отображения
    /// </summary>
    private static readonly Dictionary<string, string> OperationNames = new()
    {
        ["Classification"] = "Классификация товаров",
        ["Labels"] = "Назначение меток",
        ["Parsing"] = "Парсинг чеков"
    };

    /// <summary>
    /// Файлы дефолтных промптов
    /// </summary>
    private static readonly Dictionary<string, string> DefaultPromptFiles = new()
    {
        ["Classification"] = "prompt_classify_products.txt",
        ["Labels"] = "prompt_assign_labels.txt",
        ["Parsing"] = "prompt_template.txt"
    };

    /// <summary>
    /// Умные промпты для продвинутых моделей (GPT-4, Claude и т.п.)
    /// </summary>
    private static readonly Dictionary<string, string> SmartPrompts = new()
    {
        ["Classification"] = """
            Ты — классификатор товаров для домашней бухгалтерии.

            ТЕКУЩИЙ КАТАЛОГ ПРОДУКТОВ (JSON):
            {{EXISTING_HIERARCHY_JSON}}

            ТОВАРЫ ДЛЯ КЛАССИФИКАЦИИ:
            {{ITEMS}}

            ПРАВИЛА:
            1. Каждый товар ОБЯЗАТЕЛЬНО должен быть отнесён к конкретной категории продуктов
            2. ЗАПРЕЩЕНО использовать категории типа "Не категоризировано", "Другое", "Прочее", "Разное"
            3. Если подходящей категории нет — создай новую с понятным названием (Батон -> Хлеб, Варенье -> Консервация)
            4. Используй существующие категории из каталога если они подходят

            Верни JSON:
            - products: новые категории (если нужны), каждая с name и parent (null для корневых)
            - items: для каждого товара укажи name, product (категория) и path (путь в иерархии как массив)

            Пример:
            {
              "products": [{"name": "Йогурт", "parent": "Молочные продукты"}],
              "items": [{"name": "Молоко Домик в деревне 2.5%", "product": "Молоко", "path": ["Молочные продукты", "Молоко"]}]
            }
            """
    };

    public PromptEditorWindow(
        string operation,
        string providerKey,
        string? currentPrompt,
        Action<string>? log = null)
    {
        InitializeComponent();

        _operation = operation;
        _providerKey = providerKey;
        _log = log;

        // Load default prompt from file
        _defaultPrompt = LoadDefaultPrompt(operation);

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

        // Set prompt text (current or default)
        PromptTextBox.Text = !string.IsNullOrWhiteSpace(currentPrompt)
            ? currentPrompt
            : _defaultPrompt;

        // Show smart prompt button only for operations that have one
        SmartPromptButton.Visibility = SmartPrompts.ContainsKey(operation)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _log?.Invoke($"[PromptEditor] Opened for {operation}/{providerKey}");
    }

    /// <summary>
    /// Текст промпта после редактирования
    /// </summary>
    public string PromptText => PromptTextBox.Text;

    /// <summary>
    /// Был ли промпт изменён относительно дефолта
    /// </summary>
    public bool IsCustomPrompt => !string.Equals(PromptText.Trim(), _defaultPrompt.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Загрузить дефолтный промпт из файла
    /// </summary>
    private string LoadDefaultPrompt(string operation)
    {
        if (!DefaultPromptFiles.TryGetValue(operation, out var fileName))
        {
            _log?.Invoke($"[PromptEditor] No default file for operation: {operation}");
            return string.Empty;
        }

        // Try to find file in app directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(appDir, fileName);

        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                _log?.Invoke($"[PromptEditor] Loaded default from: {filePath}");
                return content;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[PromptEditor] Error loading {filePath}: {ex.Message}");
            }
        }
        else
        {
            _log?.Invoke($"[PromptEditor] Default file not found: {filePath}");
        }

        // Fallback hardcoded prompts
        return GetHardcodedDefaultPrompt(operation);
    }

    /// <summary>
    /// Хардкод дефолтных промптов на случай если файлы не найдены
    /// </summary>
    private static string GetHardcodedDefaultPrompt(string operation)
    {
        return operation switch
        {
            // Примечание: Для умных моделей (GPT-4, Claude и т.п.) используйте:
            // {{EXISTING_HIERARCHY_JSON}} вместо {{EXISTING_HIERARCHY}} для JSON-формата иерархии
            // и добавьте "path" в items для указания пути: ["Родитель", "Категория"]
            "Classification" => """
                Выдели продукты из списка товаров и построй иерархию.

                СУЩЕСТВУЮЩАЯ ИЕРАРХИЯ ПРОДУКТОВ (используй её, добавляй новые только если нужно):
                {{EXISTING_HIERARCHY}}

                ТОВАРЫ ДЛЯ КЛАССИФИКАЦИИ:
                {{ITEMS}}

                ПРАВИЛА:
                1. Продукт - это категория товаров (Молоко, Овощи, Фрукты, Кофе молотый и т.п.)
                2. Продукты могут быть иерархичными: Овощи -> Томаты
                3. Если продукт уже есть в существующей иерархии - используй его
                4. Если продукта нет - создай новый и укажи parent если он вложенный
                5. ЗАПРЕЩЕНО использовать "Не категоризировано", "Другое", "Прочее" - всегда создавай конкретную категорию

                ФОРМАТ ОТВЕТА (строго JSON):
                {
                  "products": [
                    {"name": "Название продукта", "parent": null или "Название родителя"}
                  ],
                  "items": [
                    {"name": "Полное название товара", "product": "Название продукта"}
                  ]
                }

                Классифицируй товары:
                """,

            "Labels" => """
                Назначь подходящие метки для каждого товара из списка.

                ДОСТУПНЫЕ МЕТКИ:
                {{LABELS}}

                ТОВАРЫ:
                {{ITEMS}}

                ПРАВИЛА:
                1. Для каждого товара выбери подходящие метки ТОЛЬКО из списка ДОСТУПНЫЕ МЕТКИ
                2. Товар может иметь 0, 1 или несколько меток
                3. НЕ придумывай новые метки
                4. Если ни одна метка не подходит — верни пустой массив labels: []

                ФОРМАТ ОТВЕТА (строго JSON массив объектов):
                [
                  {"item": "название товара 1", "labels": ["Метка1", "Метка2"]},
                  {"item": "название товара 2", "labels": []}
                ]

                Назначь метки:
                """,

            "Parsing" => """
                Извлеки данные чека в JSON.

                ПРАВИЛА ДЛЯ ЧЕКА:
                - shop: название магазина
                - order_datetime: дата YYYY-MM-DD:hh:mm (год {{YEAR}} если не указан)
                - total: итоговая сумма
                - items: массив товаров

                ПРАВИЛА ДЛЯ ТОВАРА:
                - name: полное название
                - quantity: количество
                - unit: единица (шт/кг/л)
                - price: цена за единицу
                - amount: итоговая цена

                JSON:
                {"shop":"","order_datetime":"","total":0,"items":[{"name":"","quantity":1,"unit":"шт","price":0,"amount":0}]}

                Текст чека:
                {{RECEIPT_TEXT}}
                """,

            _ => string.Empty
        };
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Сбросить промпт к значению по умолчанию?\nВсе изменения будут потеряны.",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            PromptTextBox.Text = _defaultPrompt;
            _log?.Invoke($"[PromptEditor] Reset to default for {_operation}");
        }
    }

    private void SmartPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SmartPrompts.TryGetValue(_operation, out var smartPrompt))
            return;

        var result = MessageBox.Show(
            "Заменить текущий промпт на умный промпт для продвинутых моделей?\n\nЭтот промпт использует JSON-формат иерархии и поддерживает path в ответе.",
            "Умный промпт",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            PromptTextBox.Text = smartPrompt;
            _log?.Invoke($"[PromptEditor] Smart prompt applied for {_operation}");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptText))
        {
            MessageBox.Show(
                "Промпт не может быть пустым",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PromptTextBox.Focus();
            return;
        }

        _log?.Invoke($"[PromptEditor] Saved prompt for {_operation}/{_providerKey} (custom: {IsCustomPrompt})");
        DialogResult = true;
    }
}
