using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;
using SmartBasket.Services.Email;

namespace SmartBasket.Services.Sources;

/// <summary>
/// Источник чеков из email через IMAP
/// </summary>
public class EmailReceiptSource : IReceiptSource
{
    private readonly ReceiptSourceConfig _config;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailReceiptSource> _logger;

    public EmailReceiptSource(
        ReceiptSourceConfig config,
        IEmailService emailService,
        ILogger<EmailReceiptSource> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (config.Type != SourceType.Email)
            throw new ArgumentException($"Expected Email source type, got {config.Type}", nameof(config));

        if (config.Email == null)
            throw new ArgumentException("Email configuration is required for Email source type", nameof(config));
    }

    public string Name => _config.Name;
    public SourceType Type => SourceType.Email;
    public string ParserName => _config.Parser;

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var emailSettings = ToEmailSettings(_config.Email!);
        return await _emailService.TestConnectionAsync(emailSettings, cancellationToken);
    }

    public async Task<IReadOnlyList<RawReceipt>> FetchAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching receipts from email source '{Name}'", Name);

        var emailSettings = ToEmailSettings(_config.Email!);
        var emails = await _emailService.FetchEmailsAsync(emailSettings, progress, cancellationToken);

        var rawReceipts = new List<RawReceipt>();

        foreach (var email in emails)
        {
            var content = email.Body;
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Email '{Subject}' has no body, skipping", email.Subject);
                continue;
            }

            var contentType = !string.IsNullOrWhiteSpace(email.HtmlBody)
                ? "text/html"
                : "text/plain";

            rawReceipts.Add(new RawReceipt(
                Content: content,
                ContentType: contentType,
                Date: email.Date,
                ExternalId: email.MessageId
            ));
        }

        _logger.LogInformation("Fetched {Count} raw receipts from '{Name}'", rawReceipts.Count, Name);
        return rawReceipts;
    }

    /// <summary>
    /// Конвертирует новый EmailSourceConfig в legacy EmailSettings для совместимости
    /// </summary>
    private static EmailSettings ToEmailSettings(EmailSourceConfig config)
    {
        return new EmailSettings
        {
            ImapServer = config.ImapServer,
            ImapPort = config.ImapPort,
            UseSsl = config.UseSsl,
            Username = config.Username,
            Password = config.Password,
            SenderFilter = config.SenderFilter,
            SubjectFilter = config.SubjectFilter,
            Folder = config.Folder,
            SearchDaysBack = config.SearchDaysBack
        };
    }
}
