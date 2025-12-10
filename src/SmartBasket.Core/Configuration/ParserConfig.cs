namespace SmartBasket.Core.Configuration;

/// <summary>
/// Конфигурация парсера чеков
/// </summary>
public class ParserConfig
{
    /// <summary>
    /// Уникальное имя парсера
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Тип парсера (Regex, LLM)
    /// </summary>
    public ParserType Type { get; set; } = ParserType.Regex;

    /// <summary>
    /// Требуется ли AI для работы парсера
    /// </summary>
    public bool RequiresAI { get; set; } = false;

    /// <summary>
    /// Ключ AI провайдера (если RequiresAI == true)
    /// Формат: "Provider/Model", например "Ollama/qwen2.5:1.5b"
    /// </summary>
    public string? AiProvider { get; set; }

    /// <summary>
    /// Описание парсера (что парсит)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Поддерживаемые магазины/источники
    /// </summary>
    public List<string> SupportedShops { get; set; } = new();

    /// <summary>
    /// Поддерживаемые форматы входных данных (html, text, json)
    /// </summary>
    public List<string> SupportedFormats { get; set; } = new();

    /// <summary>
    /// Встроенный парсер (нельзя удалить, только отключить)
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Включен ли парсер
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
