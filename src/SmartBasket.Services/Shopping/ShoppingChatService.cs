using System.IO;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools;
using SmartBasket.Services.Tools.Models;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Специализированный сервис чата для модуля закупок.
/// Использует YandexAgent для stateful conversation с tool calling.
/// </summary>
public class ShoppingChatService : IShoppingChatService
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IToolExecutor _tools;
    private readonly IShoppingSessionService _sessionService;
    private readonly ILogger<ShoppingChatService> _logger;

    private readonly List<LlmChatMessage> _history = new();
    private ShoppingSession? _session;
    private ILlmProvider? _provider;
    private string? _systemPrompt;

    private const int MaxIterations = 10;

    public string? ConversationId => _session?.ConversationId;
    public ShoppingSession? CurrentSession => _session;

    public ShoppingChatService(
        IAiProviderFactory providerFactory,
        IToolExecutor tools,
        IShoppingSessionService sessionService,
        ILogger<ShoppingChatService> logger)
    {
        _providerFactory = providerFactory;
        _tools = tools;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<(bool IsAvailable, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[ShoppingChatService] Checking Shopping provider availability");

        try
        {
            // Получаем провайдер из конфигурации AiOperations.Shopping
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);

            if (provider == null)
            {
                return (false, "Провайдер для закупок не настроен. Укажите его в Настройки → AI Операции → Закупки");
            }

            // Проверяем подключение
            var (success, message) = await provider.TestConnectionAsync(cancellationToken);
            if (!success)
            {
                return (false, $"Провайдер недоступен: {message}");
            }

            _logger.LogInformation("[ShoppingChatService] Shopping provider available: {Name}", provider.Name);
            return (true, $"Провайдер готов ({provider.Name})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingChatService] Error checking availability");
            return (false, $"Ошибка проверки: {ex.Message}");
        }
    }

    public Task<string> StartConversationAsync(ShoppingSession session, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[ShoppingChatService] Starting conversation for session {SessionId}", session.Id);

        _session = session;
        _history.Clear();

        // Получаем провайдер из конфигурации AiOperations.Shopping
        _provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);

        if (_provider == null)
        {
            throw new InvalidOperationException("Провайдер для закупок не настроен. Укажите его в Настройки → AI Операции → Закупки");
        }

        // Сбрасываем предыдущую conversation если провайдер поддерживает это
        if (_provider.SupportsConversationReset)
        {
            _provider.ResetConversation();
        }

        // Загружаем системный промпт для Shopping модуля
        _systemPrompt = LoadPromptTemplate("prompt_shopping_system.txt");

        // Генерируем уникальный ID conversation
        var conversationId = $"shopping-{session.Id:N}";
        _session.ConversationId = conversationId;

        _logger.LogInformation("[ShoppingChatService] Conversation started: {ConversationId}, Provider: {Provider}",
            conversationId, _provider.Name);

        return Task.FromResult(conversationId);
    }

    public async Task<ChatResponse> SendInitialPromptAsync(
        IProgress<ChatProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[ShoppingChatService] Sending initial prompt");

        // Загружаем инициализирующий промпт
        var initialPrompt = LoadPromptTemplate("prompt_shopping_initial.txt");

        return await SendAsync(initialPrompt, progress, cancellationToken);
    }

    public async Task<ChatResponse> SendAsync(
        string message,
        IProgress<ChatProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_session == null || _provider == null)
        {
            throw new InvalidOperationException("Conversation not started. Call StartConversationAsync first.");
        }

        _logger.LogInformation("[ShoppingChatService] ========================================");
        _logger.LogInformation("[ShoppingChatService] >>> NEW MESSAGE");
        _logger.LogInformation("[ShoppingChatService] User message ({Length} chars)", message.Length);

        // Добавляем сообщение пользователя в историю
        _history.Add(new LlmChatMessage { Role = "user", Content = message });

        // Получаем только нужные инструменты для Shopping модуля
        var allTools = _tools.GetToolDefinitions();
        var shoppingTools = allTools
            .Where(t => t.Name is "update_basket" or "query" or "describe_data")
            .ToList();

        _logger.LogDebug("[ShoppingChatService] Tools: {Tools}",
            string.Join(", ", shoppingTools.Select(t => t.Name)));

        // Tool-use loop
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            _logger.LogInformation("[ShoppingChatService] --- Iteration {Iteration}/{Max} ---",
                iteration + 1, MaxIterations);

            // Подготавливаем сообщения
            var messages = BuildMessagesWithSystem();

            // Progress adapter для streaming
            var streamingProgress = progress != null
                ? new Progress<string>(delta =>
                {
                    // Фильтруем служебные сообщения провайдера
                    if (IsServiceMessage(delta)) return;

                    var cleanDelta = delta?.TrimStart();
                    if (!string.IsNullOrEmpty(cleanDelta))
                    {
                        progress.Report(new ChatProgress(ChatProgressType.TextDelta, Text: cleanDelta));
                    }
                })
                : null;

            // Вызов LLM
            var result = await _provider.ChatAsync(
                messages,
                shoppingTools,
                progress: streamingProgress,
                cancellationToken: cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("[ShoppingChatService] LLM error: {Error}", result.ErrorMessage);
                return new ChatResponse("", false, result.ErrorMessage);
            }

            // Если нет tool calls — это финальный ответ
            if (!result.HasToolCalls)
            {
                var response = result.Response ?? "";
                _history.Add(new LlmChatMessage
                {
                    Role = "assistant",
                    Content = response
                });

                // Уведомляем UI о завершении
                progress?.Report(new ChatProgress(ChatProgressType.Complete));

                _logger.LogInformation("[ShoppingChatService] <<< FINAL RESPONSE ({Length} chars) in {Iterations} iteration(s)",
                    response.Length, iteration + 1);
                _logger.LogInformation("[ShoppingChatService] ========================================");

                return new ChatResponse(response, true);
            }

            // Обрабатываем tool calls
            _logger.LogDebug("[ShoppingChatService] Processing {Count} tool call(s)", result.ToolCalls!.Count);

            // Если модель вернула текст вместе с tool calls (рассуждения) — отправляем через TextDelta
            if (!string.IsNullOrEmpty(result.Response))
            {
                progress?.Report(new ChatProgress(ChatProgressType.TextDelta, Text: result.Response));
            }

            // Добавляем assistant сообщение с tool calls
            _history.Add(new LlmChatMessage
            {
                Role = "assistant",
                Content = result.Response ?? "",
                ToolCalls = result.ToolCalls
            });

            // Выполняем каждый tool call
            foreach (var call in result.ToolCalls)
            {
                // Уведомляем UI о вызове инструмента
                progress?.Report(new ChatProgress(
                    ChatProgressType.ToolCall,
                    ToolName: call.Name,
                    ToolArgs: call.Arguments
                ));

                _logger.LogInformation("[ShoppingChatService] Executing tool: {Name}", call.Name);

                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments, cancellationToken);

                _logger.LogInformation("[ShoppingChatService] Tool {Name} completed, success={Success}, result={ResultLength} chars",
                    call.Name, toolResult.Success, toolResult.JsonData.Length);

                // Уведомляем UI о результате инструмента
                progress?.Report(new ChatProgress(
                    ChatProgressType.ToolResult,
                    ToolName: call.Name,
                    ToolResult: toolResult.JsonData,
                    ToolSuccess: toolResult.Success
                ));

                // Добавляем результат tool в историю
                _history.Add(new LlmChatMessage
                {
                    Role = "tool",
                    Content = toolResult.JsonData,
                    ToolCallId = call.Id,
                    IsToolError = !toolResult.Success
                });
            }
        }

        _logger.LogWarning("[ShoppingChatService] Max iterations ({Max}) exceeded", MaxIterations);
        _logger.LogInformation("[ShoppingChatService] ========================================");

        return new ChatResponse("", false, "Превышено максимальное количество итераций");
    }

    public void Reset()
    {
        _logger.LogInformation("[ShoppingChatService] Resetting conversation");

        _session = null;
        _history.Clear();
        _systemPrompt = null;

        // Сбрасываем провайдер
        if (_provider?.SupportsConversationReset == true)
        {
            _provider.ResetConversation();
        }
        _provider = null;
    }

    private List<LlmChatMessage> BuildMessagesWithSystem()
    {
        var messages = new List<LlmChatMessage>();

        // Добавляем системный промпт
        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = _systemPrompt
            });
        }

        // Добавляем историю
        messages.AddRange(_history);

        return messages;
    }

    /// <summary>
    /// Загружает шаблон промпта из файла
    /// </summary>
    private string LoadPromptTemplate(string fileName)
    {
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(AppContext.BaseDirectory, "..", fileName),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("[ShoppingChatService] Loading prompt template from: {Path}", path);
                return File.ReadAllText(path);
            }
        }

        _logger.LogWarning("[ShoppingChatService] Prompt template not found: {FileName}", fileName);

        // Fallback prompts
        return fileName switch
        {
            "prompt_shopping_system.txt" => GetDefaultSystemPrompt(),
            "prompt_shopping_initial.txt" => GetDefaultInitialPrompt(),
            _ => ""
        };
    }

    private static string GetDefaultSystemPrompt()
    {
        return """
            Ты — умный помощник для формирования списка покупок.

            ## Твои возможности:
            1. Анализировать историю покупок пользователя
            2. Предлагать товары на основе частоты покупок
            3. Добавлять, удалять и изменять товары в списке

            ## Доступные инструменты:

            ### update_basket
            Добавляет, удаляет или изменяет товары в списке покупок.
            Параметры:
            - operations: массив операций
              - action: "add" | "remove" | "update"
              - name: название товара
              - quantity: количество (для add/update)
              - unit: единица измерения (шт, кг, л, г, мл)
              - category: категория для группировки

            ### query
            Запрос к базе данных для анализа истории покупок.
            Используй для получения информации о прошлых чеках.

            ## Правила:
            1. При добавлении товаров сразу вызывай update_basket
            2. Группируй товары по категориям (Молочные продукты, Овощи, Хлеб и т.д.)
            3. Предлагай подходящие единицы измерения
            4. Отвечай кратко и по делу
            """;
    }

    private static string GetDefaultInitialPrompt()
    {
        return """
            Привет! Я помогу сформировать список покупок.

            Напиши что нужно купить, например:
            - "Молоко, хлеб, яйца"
            - "2 кг яблок и 1 л кефира"
            - "Продукты на неделю"

            Или я могу проанализировать твои чеки и предложить список на основе прошлых покупок.
            """;
    }

    /// <summary>
    /// Проверяет, является ли сообщение служебным
    /// </summary>
    private static bool IsServiceMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return true;

        var trimmed = message.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;

        return trimmed.StartsWith("[YandexGPT") ||
               trimmed.StartsWith("[YandexAgent") ||
               trimmed.StartsWith("[Ollama") ||
               trimmed.Contains("=== STREAMING") ||
               trimmed.Contains(">>> ЗАПРОС") ||
               trimmed.Contains("=== END");
    }
}
