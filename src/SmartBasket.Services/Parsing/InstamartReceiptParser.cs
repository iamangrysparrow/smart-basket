using System.Globalization;
using System.Text.RegularExpressions;
using SmartBasket.Services.Llm;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Regex-парсер для чеков Instamart (СберМаркет)
/// </summary>
public class InstamartReceiptParser : IReceiptTextParser
{
    /// <summary>
    /// Уникальный идентификатор парсера для конфигурации
    /// </summary>
    public string Name => "InstamartParser";

    /// <summary>
    /// Магазины, поддерживаемые этим парсером (для будущего авто-определения)
    /// </summary>
    public IReadOnlyList<string> SupportedShops { get; } = new[]
    {
        "Instamart", "СберМаркет", "sbermarket.ru", "kuper.ru", "Kuper"
    };

    // Маркеры для определения магазина в CanParse()
    private static readonly string[] ShopMarkers = { "Instamart", "instamart", "INSTAMART", "kuper.ru", "Kuper" };

    // Паттерн номера заказа: H + 11 цифр
    private static readonly Regex OrderNumberRegex = new(@"H\d{11}", RegexOptions.Compiled);

    // Паттерн даты: "26 ноября" или "5 декабря"
    private static readonly Regex DateRegex = new(@"(\d{1,2})\s+(января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Паттерн количества: "1 шт ×" или "0.524 кг ×"
    private static readonly Regex QuantityRegex = new(@"([\d.,]+)\s*(шт|кг|г|л|мл)\s*[×x]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Паттерн цены: "303,99 ₽" или "30.99 ₽"
    private static readonly Regex PriceRegex = new(@"([\d\s]+[.,]\d{2})\s*₽", RegexOptions.Compiled);

    // Паттерн характеристики товара: "970 мл" или "680 г"
    private static readonly Regex UnitCharacteristicRegex = new(@"^(\d+)\s*(мл|г|кг|л)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Месяцы для парсинга даты
    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["января"] = 1, ["февраля"] = 2, ["марта"] = 3, ["апреля"] = 4,
        ["мая"] = 5, ["июня"] = 6, ["июля"] = 7, ["августа"] = 8,
        ["сентября"] = 9, ["октября"] = 10, ["ноября"] = 11, ["декабря"] = 12
    };

    public bool CanParse(string receiptText)
    {
        return ShopMarkers.Any(marker => receiptText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public ParsedReceipt Parse(string receiptText, DateTime emailDate)
    {
        var result = new ParsedReceipt
        {
            Shop = "Instamart",
            Date = emailDate
        };

        try
        {
            // Извлекаем номер заказа
            var orderMatch = OrderNumberRegex.Match(receiptText);
            if (orderMatch.Success)
            {
                result.OrderNumber = orderMatch.Value;
            }

            // Извлекаем дату
            var dateMatch = DateRegex.Match(receiptText);
            if (dateMatch.Success)
            {
                var day = int.Parse(dateMatch.Groups[1].Value);
                var monthName = dateMatch.Groups[2].Value.ToLower();
                if (Months.TryGetValue(monthName, out var month))
                {
                    var year = emailDate.Year;
                    // Если месяц в чеке больше текущего месяца email, значит это прошлый год
                    if (month > emailDate.Month)
                    {
                        year--;
                    }
                    result.Date = new DateTime(year, month, day);
                }
            }

            // Парсим товарные позиции
            result.Items = ParseItems(receiptText);

            // Вычисляем итого
            result.Total = result.Items.Sum(i => i.Amount ?? (i.Price * i.Quantity));

            result.IsSuccess = result.Items.Count > 0;
            result.Message = result.IsSuccess
                ? $"Parsed {result.Items.Count} items from {Name}"
                : "No items found in receipt";
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = $"Parse error: {ex.Message}";
        }

        return result;
    }

    private List<ParsedReceiptItem> ParseItems(string receiptText)
    {
        var items = new List<ParsedReceiptItem>();

        // Разбиваем на строки и фильтруем пустые
        var lines = receiptText
            .Split(new[] { '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        // Находим блоки товаров: ищем паттерн "X шт/кг ×"
        for (int i = 0; i < lines.Count; i++)
        {
            var quantityMatch = QuantityRegex.Match(lines[i]);
            if (!quantityMatch.Success) continue;

            // Нашли строку с количеством - ищем название выше
            var name = FindItemName(lines, i);
            if (string.IsNullOrEmpty(name)) continue;

            // Парсим количество
            var quantityStr = quantityMatch.Groups[1].Value.Replace(',', '.').Replace(" ", "");
            var quantity = decimal.Parse(quantityStr, CultureInfo.InvariantCulture);
            var unit = quantityMatch.Groups[2].Value.ToLower();

            // Ищем цену и сумму ниже
            var (price, amount) = FindPriceAndAmount(lines, i, quantity);

            // Определяем характеристику товара (UnitOfMeasure, UnitQuantity)
            var (unitOfMeasure, unitQuantity) = DetermineItemCharacteristics(lines, i, quantity, unit);

            items.Add(new ParsedReceiptItem
            {
                Name = name,
                Quantity = quantity,
                Unit = unit,
                Price = price,
                Amount = amount,
                UnitOfMeasure = unitOfMeasure,
                UnitQuantity = unitQuantity
            });
        }

        return items;
    }

    private string? FindItemName(List<string> lines, int quantityLineIndex)
    {
        // Название - это строка перед строкой с количеством
        // Пропускаем пустые строки и служебные маркеры
        for (int j = quantityLineIndex - 1; j >= 0; j--)
        {
            var line = lines[j];

            // Пропускаем служебные строки
            if (IsServiceLine(line)) continue;

            // Пропускаем строки с ценой (предыдущего товара)
            if (PriceRegex.IsMatch(line)) continue;

            // Пропускаем строки с количеством (предыдущего товара)
            if (QuantityRegex.IsMatch(line)) continue;

            // Пропускаем характеристики (предыдущего товара)
            if (UnitCharacteristicRegex.IsMatch(line)) continue;

            // Нашли название
            return line;
        }

        return null;
    }

    private (decimal? price, decimal? amount) FindPriceAndAmount(List<string> lines, int quantityLineIndex, decimal quantity)
    {
        decimal? price = null;
        decimal? amount = null;

        // Ищем цены в следующих строках (до "Собрано" или следующего товара)
        var pricesFound = new List<decimal>();

        for (int j = quantityLineIndex + 1; j < lines.Count && j < quantityLineIndex + 5; j++)
        {
            var line = lines[j];

            // Достигли конца блока товара
            if (line.Contains("Собрано", StringComparison.OrdinalIgnoreCase)) break;
            if (QuantityRegex.IsMatch(line)) break;

            // Ищем цену
            var priceMatch = PriceRegex.Match(line);
            if (priceMatch.Success)
            {
                var priceStr = priceMatch.Groups[1].Value
                    .Replace(" ", "")
                    .Replace(',', '.');
                if (decimal.TryParse(priceStr, CultureInfo.InvariantCulture, out var p))
                {
                    pricesFound.Add(p);
                }
            }
        }

        // Логика определения цены и суммы
        if (pricesFound.Count == 1)
        {
            // Одна цена - это и цена и сумма (quantity = 1 или весовой товар)
            price = pricesFound[0];
            amount = pricesFound[0];
        }
        else if (pricesFound.Count >= 2)
        {
            // Две цены: первая - цена за единицу, вторая - сумма
            price = pricesFound[0];
            amount = pricesFound[1];
        }

        return (price, amount);
    }

    private (string unitOfMeasure, decimal unitQuantity) DetermineItemCharacteristics(
        List<string> lines, int quantityLineIndex, decimal quantity, string unit)
    {
        // По умолчанию берём единицу из количества в чеке
        var defaultUnit = NormalizeUnit(unit);
        var defaultQuantity = 1m;

        // Если quantity == 1 (штучный товар), ищем характеристику в следующих строках
        if (quantity == 1)
        {
            for (int j = quantityLineIndex + 1; j < lines.Count && j < quantityLineIndex + 5; j++)
            {
                var line = lines[j];

                // Достигли конца блока
                if (line.Contains("Собрано", StringComparison.OrdinalIgnoreCase)) break;
                if (QuantityRegex.IsMatch(line)) break;

                // Проверяем на характеристику товара (не цену!)
                var charMatch = UnitCharacteristicRegex.Match(line);
                if (charMatch.Success)
                {
                    var unitQty = decimal.Parse(charMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    var unitMeasure = NormalizeUnit(charMatch.Groups[2].Value);
                    return (unitMeasure, unitQty);
                }
            }
        }

        // Для весовых товаров (quantity != 1) характеристика берётся из unit
        return (defaultUnit, defaultQuantity);
    }

    private static string NormalizeUnit(string unit)
    {
        return unit.ToLower() switch
        {
            "шт" => "шт",
            "кг" => "кг",
            "г" => "г",
            "л" => "л",
            "мл" => "мл",
            _ => unit.ToLower()
        };
    }

    private static bool IsServiceLine(string line)
    {
        var servicePatterns = new[]
        {
            "Собрано", "Оплата", "Доставка", "Сервисный сбор",
            "Ваш заказ", "Покупки", "Итого", "К оплате"
        };

        return servicePatterns.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
