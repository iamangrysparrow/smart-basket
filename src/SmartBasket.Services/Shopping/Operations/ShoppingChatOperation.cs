using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Llm;
using SmartBasket.Services.Tools;

namespace SmartBasket.Services.Shopping.Operations;

/// <summary>
/// Реализация операции чата для модуля закупок.
/// Использует AI провайдер с tool calling для формирования списка покупок.
/// </summary>
public class ShoppingChatOperation : IShoppingChatOperation
{
    private readonly IAiProviderFactory _providerFactory;
    private readonly IToolExecutor _tools;
    private readonly ILogger<ShoppingChatOperation> _logger;

    private readonly List<LlmChatMessage> _history = new();
    private ShoppingSession? _session;
    private ILlmProvider? _provider;
    private string? _systemPrompt;

    private const int MaxIterations = 10;

    public string? ConversationId => _session?.ConversationId;
    public ShoppingSession? CurrentSession => _session;

    public ShoppingChatOperation(
        IAiProviderFactory providerFactory,
        IToolExecutor tools,
        ILogger<ShoppingChatOperation> logger)
    {
        _providerFactory = providerFactory;
        _tools = tools;
        _logger = logger;
    }

    public async Task<(bool IsAvailable, string Message)> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[ShoppingChatOperation] Checking Shopping provider availability");

        try
        {
            var provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);

            if (provider == null)
            {
                return (false, "Провайдер для закупок не настроен. Укажите его в Настройки → AI Операции → Закупки");
            }

            var (success, message) = await provider.TestConnectionAsync(ct);
            if (!success)
            {
                return (false, $"Провайдер недоступен: {message}");
            }

            _logger.LogInformation("[ShoppingChatOperation] Shopping provider available: {Name}", provider.Name);
            return (true, $"Провайдер готов ({provider.Name})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShoppingChatOperation] Error checking availability");
            return (false, $"Ошибка проверки: {ex.Message}");
        }
    }

    public Task<string> StartConversationAsync(ShoppingSession session, CancellationToken ct = default)
    {
        _logger.LogInformation("[ShoppingChatOperation] Starting conversation for session {SessionId}", session.Id);

        _session = session;
        _history.Clear();

        _provider = _providerFactory.GetProviderForOperation(AiOperation.Shopping);

        if (_provider == null)
        {
            throw new InvalidOperationException("Провайдер для закупок не настроен. Укажите его в Настройки → AI Операции → Закупки");
        }

        if (_provider.SupportsConversationReset)
        {
            _provider.ResetConversation();
        }

        _systemPrompt = LoadPromptTemplate("prompt_shopping_system.txt");

        var conversationId = $"shopping-{session.Id:N}";
        _session.ConversationId = conversationId;

        _logger.LogInformation("[ShoppingChatOperation] Conversation started: {ConversationId}, Provider: {Provider}",
            conversationId, _provider.Name);

        return Task.FromResult(conversationId);
    }

    public async IAsyncEnumerable<WorkflowProgress> SendInitialPromptAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("[ShoppingChatOperation] Sending initial prompt");

        var initialPrompt = LoadPromptTemplate("prompt_shopping_initial.txt");

        await foreach (var progress in ProcessMessageAsync(initialPrompt, ct))
        {
            yield return progress;
        }
    }

    public async IAsyncEnumerable<WorkflowProgress> ProcessMessageAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_session == null || _provider == null)
        {
            yield return new ChatErrorProgress("Conversation not started. Call StartConversationAsync first.");
            yield break;
        }

        _logger.LogInformation("[ShoppingChatOperation] ========================================");
        _logger.LogInformation("[ShoppingChatOperation] >>> NEW MESSAGE");
        _logger.LogInformation("[ShoppingChatOperation] User message ({Length} chars)", message.Length);

        // Добавляем сообщение пользователя в историю
        _history.Add(new LlmChatMessage { Role = "user", Content = message });

        // Получаем инструменты для Shopping модуля
        var allTools = _tools.GetToolDefinitions();
        var shoppingTools = allTools
            .Where(t => t.Name is "update_basket" or "query" or "describe_data" or "get_current_datetime")
            .ToList();

        _logger.LogDebug("[ShoppingChatOperation] Tools: {Tools}",
            string.Join(", ", shoppingTools.Select(t => t.Name)));

        var fullResponseText = new System.Text.StringBuilder();

        // Tool-use loop
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            _logger.LogInformation("[ShoppingChatOperation] --- Iteration {Iteration}/{Max} ---",
                iteration + 1, MaxIterations);

            var messages = BuildMessagesWithSystem();

            // Streaming через callback
            bool hadStreamingText = false;
            var streamingBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();

            var streamingProgress = new ThreadSafeProgress<string>(delta =>
            {
                if (IsServiceMessage(delta)) return;

                var cleanDelta = delta;
                if (delta != null && delta.StartsWith("  "))
                {
                    cleanDelta = delta.Substring(2);
                }

                if (!string.IsNullOrEmpty(cleanDelta))
                {
                    hadStreamingText = true;
                    streamingBuffer.Enqueue(cleanDelta);
                }
            });

            // Запускаем LLM вызов
            var llmTask = _provider.ChatAsync(
                messages,
                shoppingTools,
                progress: streamingProgress,
                cancellationToken: ct);

            // Отдаём streaming дельты по мере поступления
            while (!llmTask.IsCompleted)
            {
                while (streamingBuffer.TryDequeue(out var delta))
                {
                    fullResponseText.Append(delta);
                    yield return new TextDeltaProgress(delta);
                }
                await Task.Delay(10, ct);
            }

            // Отдаём оставшиеся дельты
            while (streamingBuffer.TryDequeue(out var delta))
            {
                fullResponseText.Append(delta);
                yield return new TextDeltaProgress(delta);
            }

            var result = await llmTask;

            if (!result.IsSuccess)
            {
                _logger.LogError("[ShoppingChatOperation] LLM error: {Error}", result.ErrorMessage);
                yield return new ChatErrorProgress(result.ErrorMessage ?? "Unknown error");
                yield break;
            }

            // Если нет tool calls — финальный ответ
            if (!result.HasToolCalls)
            {
                var response = result.Response ?? "";
                _history.Add(new LlmChatMessage
                {
                    Role = "assistant",
                    Content = response
                });

                // Если текст не был отправлен через streaming
                if (!hadStreamingText && !string.IsNullOrEmpty(response))
                {
                    fullResponseText.Append(response);
                    yield return new TextDeltaProgress(response);
                }

                yield return new ChatCompleteProgress(fullResponseText.ToString());

                _logger.LogInformation("[ShoppingChatOperation] <<< FINAL RESPONSE ({Length} chars) in {Iterations} iteration(s)",
                    response.Length, iteration + 1);
                _logger.LogInformation("[ShoppingChatOperation] ========================================");

                yield break;
            }

            // Обрабатываем tool calls
            _logger.LogDebug("[ShoppingChatOperation] Processing {Count} tool call(s)", result.ToolCalls!.Count);

            // Текст вместе с tool calls (рассуждения)
            if (!string.IsNullOrEmpty(result.Response) && !hadStreamingText)
            {
                fullResponseText.Append(result.Response);
                yield return new TextDeltaProgress(result.Response);
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
                yield return new ToolCallProgress(call.Name, call.Arguments ?? "{}");

                _logger.LogInformation("[ShoppingChatOperation] Executing tool: {Name}", call.Name);

                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments, ct);

                _logger.LogInformation("[ShoppingChatOperation] Tool {Name} completed, success={Success}",
                    call.Name, toolResult.Success);

                yield return new ToolResultProgress(call.Name, toolResult.JsonData, toolResult.Success);

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

        _logger.LogWarning("[ShoppingChatOperation] Max iterations ({Max}) exceeded", MaxIterations);
        _logger.LogInformation("[ShoppingChatOperation] ========================================");

        yield return new ChatErrorProgress("Превышено максимальное количество итераций");
    }

    public void Reset()
    {
        _logger.LogInformation("[ShoppingChatOperation] Resetting conversation");

        _session = null;
        _history.Clear();
        _systemPrompt = null;

        if (_provider?.SupportsConversationReset == true)
        {
            _provider.ResetConversation();
        }
        _provider = null;
    }

    private List<LlmChatMessage> BuildMessagesWithSystem()
    {
        var messages = new List<LlmChatMessage>();

        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = _systemPrompt
            });
        }

        messages.AddRange(_history);

        return messages;
    }

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
                _logger.LogDebug("[ShoppingChatOperation] Loading prompt template from: {Path}", path);
                return File.ReadAllText(path);
            }
        }

        _logger.LogWarning("[ShoppingChatOperation] Prompt template not found: {FileName}", fileName);

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

    private static bool IsServiceMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return true;

        var trimmed = message.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        return trimmed.StartsWith("[YandexGPT") ||
               trimmed.StartsWith("[YandexAgent") ||
               trimmed.StartsWith("[Ollama") ||
               trimmed.Contains("=== STREAMING") ||
               trimmed.Contains(">>> ЗАПРОС") ||
               trimmed.Contains("=== END");
    }
}
