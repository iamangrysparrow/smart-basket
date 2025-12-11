using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Email;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        EmailSourceConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use timeout to prevent hanging
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var token = linkedCts.Token;

            using var client = new ImapClient();
            client.Timeout = 15000; // 15 seconds socket timeout

            _logger.LogInformation("Connecting to {Server}:{Port}...", config.ImapServer, config.ImapPort);

            await client.ConnectAsync(
                config.ImapServer,
                config.ImapPort,
                config.UseSsl,
                token).ConfigureAwait(false);

            _logger.LogInformation("Authenticating as {Username}...", config.Username);

            await client.AuthenticateAsync(config.Username, config.Password, token).ConfigureAwait(false);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, token).ConfigureAwait(false);

            var message = $"Connected successfully. Inbox contains {inbox.Count} messages.";
            _logger.LogInformation(message);

            await client.DisconnectAsync(true, token).ConfigureAwait(false);

            return (true, message);
        }
        catch (OperationCanceledException)
        {
            return (false, "Connection timed out after 30 seconds");
        }
        catch (Exception ex)
        {
            var message = $"Connection failed: {ex.Message}";
            _logger.LogError(ex, message);
            return (false, message);
        }
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchEmailsAsync(
        EmailSourceConfig config,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<EmailMessage>();

        try
        {
            using var client = new ImapClient();
            client.Timeout = 30000; // 30 seconds socket timeout

            progress?.Report($"Connecting to {config.ImapServer}:{config.ImapPort}...");
            _logger.LogInformation("Connecting to {Server}:{Port}...", config.ImapServer, config.ImapPort);

            await client.ConnectAsync(
                config.ImapServer,
                config.ImapPort,
                config.UseSsl,
                cancellationToken).ConfigureAwait(false);

            progress?.Report($"Authenticating as {config.Username}...");
            _logger.LogInformation("Authenticating as {Username}...", config.Username);

            await client.AuthenticateAsync(config.Username, config.Password, cancellationToken).ConfigureAwait(false);

            // Открыть папку
            var folder = string.IsNullOrEmpty(config.Folder) || config.Folder == "INBOX"
                ? client.Inbox
                : await client.GetFolderAsync(config.Folder, cancellationToken).ConfigureAwait(false);

            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            progress?.Report($"Opened folder '{folder.Name}' with {folder.Count} messages");
            _logger.LogInformation("Opened folder '{Folder}' with {Count} messages", folder.Name, folder.Count);

            // Построить поисковый запрос
            var query = BuildSearchQuery(config);

            progress?.Report("Searching for messages...");
            _logger.LogInformation("Executing search query...");

            var uids = await folder.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            progress?.Report($"Found {uids.Count} messages matching filter");
            _logger.LogInformation("Found {Count} messages matching filter", uids.Count);

            // Получить письма
            for (int i = 0; i < uids.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var uid = uids[i];
                progress?.Report($"Fetching message {i + 1}/{uids.Count}...");

                try
                {
                    var message = await folder.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);

                    var emailMessage = new EmailMessage
                    {
                        MessageId = message.MessageId ?? uid.ToString(),
                        From = message.From?.ToString(),
                        Subject = message.Subject,
                        Date = message.Date.UtcDateTime,
                        TextBody = message.TextBody,
                        HtmlBody = message.HtmlBody
                    };

                    messages.Add(emailMessage);

                    _logger.LogDebug("Fetched message: {Subject} from {From}",
                        emailMessage.Subject, emailMessage.From);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch message with UID {Uid}", uid);
                    progress?.Report($"Warning: Failed to fetch message {uid}: {ex.Message}");
                }
            }

            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

            progress?.Report($"Successfully fetched {messages.Count} messages");
            _logger.LogInformation("Successfully fetched {Count} messages", messages.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Email fetch operation was cancelled");
            progress?.Report("Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch emails");
            progress?.Report($"Error: {ex.Message}");
            throw;
        }

        return messages;
    }

    private SearchQuery BuildSearchQuery(EmailSourceConfig config)
    {
        var queries = new List<SearchQuery>();

        // Фильтр по дате
        if (config.SearchDaysBack > 0)
        {
            var sinceDate = DateTime.Now.AddDays(-config.SearchDaysBack);
            queries.Add(SearchQuery.DeliveredAfter(sinceDate));
        }

        // Фильтр по отправителю
        if (!string.IsNullOrWhiteSpace(config.SenderFilter))
        {
            queries.Add(SearchQuery.FromContains(config.SenderFilter));
        }

        // Фильтр по теме
        if (!string.IsNullOrWhiteSpace(config.SubjectFilter))
        {
            queries.Add(SearchQuery.SubjectContains(config.SubjectFilter));
        }

        // Объединить запросы через AND
        if (queries.Count == 0)
        {
            return SearchQuery.All;
        }

        var result = queries[0];
        for (int i = 1; i < queries.Count; i++)
        {
            result = result.And(queries[i]);
        }

        return result;
    }
}
