using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Email;

public interface IEmailService
{
    /// <summary>
    /// Проверить подключение к почтовому серверу
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync(EmailSourceConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить письма по фильтру
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> FetchEmailsAsync(
        EmailSourceConfig config,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
