namespace SmartBasket.Core.Shopping;

/// <summary>
/// Сформированная корзина для магазина (этап 3).
/// Содержит выбранные товары и итоговую информацию.
/// </summary>
public class PlannedBasket
{
    /// <summary>
    /// ID магазина
    /// </summary>
    public string Store { get; set; } = "";

    /// <summary>
    /// Отображаемое имя магазина
    /// </summary>
    public string StoreName { get; set; } = "";

    /// <summary>
    /// Товары в корзине
    /// </summary>
    public List<PlannedItem> Items { get; set; } = new();

    /// <summary>
    /// Общая стоимость корзины
    /// </summary>
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Количество найденных товаров
    /// </summary>
    public int ItemsFound { get; set; }

    /// <summary>
    /// Общее количество товаров в списке
    /// </summary>
    public int ItemsTotal { get; set; }

    /// <summary>
    /// Все товары найдены
    /// </summary>
    public bool IsComplete => ItemsFound == ItemsTotal;

    /// <summary>
    /// Примерный вес корзины (для расчёта доставки)
    /// </summary>
    public decimal? EstimatedWeight { get; set; }

    /// <summary>
    /// Время доставки (строка, например "сегодня 18:00-20:00")
    /// </summary>
    public string? DeliveryTime { get; set; }

    /// <summary>
    /// Стоимость доставки
    /// </summary>
    public string? DeliveryPrice { get; set; }

    /// <summary>
    /// Процент найденных товаров
    /// </summary>
    public double FoundPercent => ItemsTotal > 0 ? (double)ItemsFound / ItemsTotal * 100 : 0;
}

/// <summary>
/// Товар в сформированной корзине.
/// Связывает DraftItem с выбранным ProductMatch.
/// </summary>
public class PlannedItem
{
    /// <summary>
    /// ID товара из черновика
    /// </summary>
    public Guid DraftItemId { get; set; }

    /// <summary>
    /// Название товара из черновика (для отображения)
    /// </summary>
    public string DraftItemName { get; set; } = "";

    /// <summary>
    /// Выбранный товар из магазина (null если не найден)
    /// </summary>
    public ProductMatch? Match { get; set; }

    /// <summary>
    /// Количество для заказа
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Сумма по позиции (Price * Quantity)
    /// </summary>
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Обоснование выбора AI (почему выбран этот товар и это количество)
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// Товар найден и в наличии
    /// </summary>
    public bool IsAvailable => Match != null && Match.InStock;
}
