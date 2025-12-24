using Microsoft.EntityFrameworkCore;
using SmartBasket.Core.Entities;
using SmartBasket.Data;

namespace SmartBasket.Services.Units;

/// <summary>
/// Сервис конвертации единиц измерения
/// </summary>
public interface IUnitConversionService
{
    /// <summary>
    /// Вычислить количество в базовых единицах
    /// </summary>
    /// <param name="quantity">Количество</param>
    /// <param name="unitId">Единица измерения</param>
    /// <returns>Количество в базовых единицах (кг, л, шт, м, м²)</returns>
    Task<decimal> ConvertToBaseUnitAsync(decimal quantity, string unitId);

    /// <summary>
    /// Получить коэффициент пересчёта для единицы измерения
    /// </summary>
    Task<decimal> GetCoefficientAsync(string unitId);

    /// <summary>
    /// Получить базовую единицу для указанной единицы
    /// </summary>
    Task<string> GetBaseUnitIdAsync(string unitId);

    /// <summary>
    /// Проверить, является ли единица базовой
    /// </summary>
    Task<bool> IsBaseUnitAsync(string unitId);

    /// <summary>
    /// Загрузить все единицы измерения для промпта
    /// </summary>
    Task<IReadOnlyList<UnitOfMeasure>> GetAllUnitsAsync();

    /// <summary>
    /// Нормализовать единицу измерения (привести к стандартному формату из справочника)
    /// </summary>
    /// <param name="unitId">Единица измерения (может быть null или пустой)</param>
    /// <returns>Нормализованная единица или "шт" по умолчанию</returns>
    Task<string> NormalizeUnitAsync(string? unitId);
}

/// <summary>
/// Реализация сервиса конвертации единиц измерения
/// </summary>
public class UnitConversionService : IUnitConversionService
{
    private readonly SmartBasketDbContext _dbContext;
    private Dictionary<string, UnitOfMeasure>? _unitsCache;

    public UnitConversionService(SmartBasketDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<decimal> ConvertToBaseUnitAsync(decimal quantity, string unitId)
    {
        var coefficient = await GetCoefficientAsync(unitId);
        return quantity * coefficient;
    }

    public async Task<decimal> GetCoefficientAsync(string unitId)
    {
        await EnsureCacheLoadedAsync();

        if (_unitsCache!.TryGetValue(unitId, out var unit))
        {
            return unit.Coefficient;
        }

        // Если единица не найдена, возвращаем 1 (без конвертации)
        return 1m;
    }

    public async Task<string> GetBaseUnitIdAsync(string unitId)
    {
        await EnsureCacheLoadedAsync();

        if (_unitsCache!.TryGetValue(unitId, out var unit))
        {
            return unit.BaseUnitId;
        }

        // Если единица не найдена, возвращаем саму себя
        return unitId;
    }

    public async Task<bool> IsBaseUnitAsync(string unitId)
    {
        await EnsureCacheLoadedAsync();

        if (_unitsCache!.TryGetValue(unitId, out var unit))
        {
            return unit.IsBase;
        }

        return false;
    }

    public async Task<IReadOnlyList<UnitOfMeasure>> GetAllUnitsAsync()
    {
        await EnsureCacheLoadedAsync();
        return _unitsCache!.Values.ToList();
    }

    public async Task<string> NormalizeUnitAsync(string? unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId))
            return "шт";

        await EnsureCacheLoadedAsync();

        // Прямое совпадение
        if (_unitsCache!.ContainsKey(unitId))
            return unitId;

        // Попробуем нормализовать (lowercase, убрать пробелы)
        var normalized = unitId.Trim().ToLowerInvariant();

        // Поиск по нормализованному значению
        foreach (var unit in _unitsCache.Values)
        {
            if (unit.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return unit.Id;
        }

        // Алиасы для единиц измерения
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "грамм", "г" },
            { "гр", "г" },
            { "килограмм", "кг" },
            { "миллилитр", "мл" },
            { "литр", "л" },
            { "штука", "шт" },
            { "штук", "шт" },
            { "шт.", "шт" },
            { "метр", "м" },
            { "сантиметр", "см" },
            { "миллиметр", "мм" }
        };

        if (aliases.TryGetValue(normalized, out var aliasUnit))
            return aliasUnit;

        // Единица не найдена, возвращаем "шт" по умолчанию
        return "шт";
    }

    private async Task EnsureCacheLoadedAsync()
    {
        if (_unitsCache != null)
            return;

        var units = await _dbContext.UnitOfMeasures.ToListAsync();
        _unitsCache = units.ToDictionary(u => u.Id, u => u);
    }
}
