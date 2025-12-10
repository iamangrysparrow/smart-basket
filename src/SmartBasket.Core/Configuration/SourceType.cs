namespace SmartBasket.Core.Configuration;

/// <summary>
/// Тип источника чеков
/// </summary>
public enum SourceType
{
    /// <summary>
    /// Получение чеков из email через IMAP
    /// </summary>
    Email,

    /// <summary>
    /// Получение чеков через REST API
    /// </summary>
    REST,

    /// <summary>
    /// Получение чеков из файловой системы (фото, PDF)
    /// </summary>
    FileSystem
}
