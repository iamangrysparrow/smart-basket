# Задача: Расширение ChatAsync для поддержки Tools

## Контекст

В проекте уже есть:
- `ILlmProvider` с методом `ChatAsync`
- `LlmChatMessage`, `LlmGenerationResult`
- `AiChatViewModel` использующий `ChatAsync`
- Провайдеры: `OllamaLlmProvider`, `YandexGptLlmProvider`, `YandexAgentLlmProvider`

Нужно расширить `ChatAsync` для поддержки tool calling.

## Прочитай перед началом

1. `CLAUDE.md`, `WPF_RULES.md`
2. `src/SmartBasket.Services/Llm/ILlmProvider.cs` — текущий интерфейс
3. `src/SmartBasket.Services/Llm/OllamaLlmProvider.cs` — реализация Ollama
4. `src/SmartBasket.Services/Llm/YandexGptLlmProvider.cs` — реализация YandexGPT
5. `src/SmartBasket.Services/Tools/` — реализация Tools (из PROMPT-01)
6. `docs/AiTools/TOOLS-SPEC.md` — спецификация инструментов

## Часть 1: Расширение моделей в ILlmProvider.cs

### Добавить LlmToolCall

```csharp
/// <summary>
/// Вызов инструмента от LLM
/// </summary>
public class LlmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";  // JSON
}
```

### Расширить LlmChatMessage

```csharp
public class LlmChatMessage
{
    public string Role { get; set; } = "user";  // user, assistant, system, tool
    public string Content { get; set; } = string.Empty;
    
    // NEW: для tool calls
    public List<LlmToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }  // для role="tool"
}
```

### Расширить LlmGenerationResult

```csharp
public class LlmGenerationResult
{
    public bool IsSuccess { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseId { get; set; }
    
    // NEW
    public List<LlmToolCall>? ToolCalls { get; set; }
    public bool HasToolCalls => ToolCalls?.Count > 0;
}
```

## Часть 2: Расширение ILlmProvider

```csharp
public interface ILlmProvider
{
    string Name { get; }
    
    Task<(bool Success, string Message)> TestConnectionAsync(...);
    
    Task<LlmGenerationResult> GenerateAsync(...);
    
    // NEW: поддержка tools
    bool SupportsTools { get; }
    
    // ИЗМЕНИТЬ сигнатуру — добавить tools
    Task<LlmGenerationResult> ChatAsync(
        IEnumerable<LlmChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,  // NEW
        int maxTokens = 2000,
        double temperature = 0.7,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    
    bool SupportsConversationReset { get; }
    void ResetConversation();
}
```

## Часть 3: Реализация в OllamaLlmProvider

Ollama поддерживает native tool calling через `/api/chat`:

```csharp
public bool SupportsTools => true;  // Зависит от модели, но API поддерживает

public async Task<LlmGenerationResult> ChatAsync(
    IEnumerable<LlmChatMessage> messages,
    IEnumerable<ToolDefinition>? tools = null,
    ...)
{
    var request = new
    {
        model = _config.Model,
        messages = ConvertMessages(messages),
        tools = tools != null ? ConvertTools(tools) : null,
        stream = true,
        options = new { temperature, num_predict = maxTokens }
    };
    
    // POST /api/chat
    // Парсить ответ: message.tool_calls или message.content
}
```

**Формат tools для Ollama:**
```json
{
  "tools": [{
    "type": "function",
    "function": {
      "name": "get_receipts",
      "description": "...",
      "parameters": { "type": "object", "properties": {...} }
    }
  }]
}
```

**Ответ с tool_calls:**
```json
{
  "message": {
    "role": "assistant",
    "content": "",
    "tool_calls": [{
      "function": { "name": "get_receipts", "arguments": {"limit": 5} }
    }]
  }
}
```

## Часть 4: Реализация в YandexGptLlmProvider

YandexGPT поддерживает tools (beta, rc модели):

```csharp
public bool SupportsTools => _config.Model?.Contains("/rc") ?? false;
```

**Формат запроса:**
```json
{
  "modelUri": "gpt://folder/yandexgpt/rc",
  "messages": [...],
  "tools": [{
    "function": {
      "name": "get_receipts",
      "description": "...",
      "parameters": {...}
    }
  }]
}
```

**Ответ:**
```json
{
  "result": {
    "alternatives": [{
      "message": {
        "toolCalls": [{
          "functionCall": { "name": "...", "arguments": {...} }
        }]
      }
    }]
  }
}
```

## Часть 5: Fallback для провайдеров без native tools

Если `SupportsTools = false`, но tools переданы:

```csharp
if (tools != null && !SupportsTools)
{
    // Добавить tools в system prompt как JSON
    var systemMessage = BuildSystemPromptWithTools(tools);
    messages = PrependSystemMessage(messages, systemMessage);
    
    // После получения ответа — парсить JSON
    var toolCall = TryParseToolCallFromText(result.Response);
    if (toolCall != null)
    {
        result.ToolCalls = new List<LlmToolCall> { toolCall };
        result.Response = null;
    }
}
```

## Часть 6: ChatService с tool-use loop

### Структура

```
src/SmartBasket.Services/Chat/
├── IChatService.cs
├── ChatService.cs
```

### IChatService

```csharp
public interface IChatService
{
    Task<ChatResponse> SendAsync(
        string userMessage,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
    
    IReadOnlyList<LlmChatMessage> History { get; }
    void ClearHistory();
}

public record ChatResponse(string Content, bool Success, string? ErrorMessage = null);
```

### ChatService — tool-use loop

```csharp
public class ChatService : IChatService
{
    private readonly IToolExecutor _tools;
    private readonly IAiProviderFactory _providerFactory;
    private readonly List<LlmChatMessage> _history = new();
    
    private const int MaxIterations = 5;
    
    public async Task<ChatResponse> SendAsync(string userMessage, ...)
    {
        _history.Add(new LlmChatMessage { Role = "user", Content = userMessage });
        
        var provider = _providerFactory.GetProvider(GetChatProviderKey());
        var tools = _tools.GetToolDefinitions();
        
        for (int i = 0; i < MaxIterations; i++)
        {
            var result = await provider.ChatAsync(_history, tools, ...);
            
            if (!result.IsSuccess)
                return new ChatResponse("", false, result.ErrorMessage);
            
            if (!result.HasToolCalls)
            {
                // Финальный ответ
                _history.Add(new LlmChatMessage 
                { 
                    Role = "assistant", 
                    Content = result.Response 
                });
                return new ChatResponse(result.Response!, true);
            }
            
            // Выполнить tools
            _history.Add(new LlmChatMessage 
            { 
                Role = "assistant", 
                ToolCalls = result.ToolCalls 
            });
            
            foreach (var call in result.ToolCalls)
            {
                progress?.Report($"Выполняю {call.Name}...");
                
                var toolResult = await _tools.ExecuteAsync(call.Name, call.Arguments);
                
                _history.Add(new LlmChatMessage
                {
                    Role = "tool",
                    Content = toolResult.JsonData,
                    ToolCallId = call.Id
                });
            }
        }
        
        return new ChatResponse("", false, "Превышено количество итераций");
    }
}
```

## Часть 7: Обновление AiChatViewModel

Существующий `AiChatViewModel` уже использует `ChatAsync`. Нужно:
1. Добавить передачу tools в вызов
2. Обработать `HasToolCalls` в ответе
3. Или заменить на использование `IChatService`

**Вариант А:** Использовать новый `IChatService` (рекомендуется)
**Вариант Б:** Добавить tool-use loop прямо в ViewModel

## Часть 8: Регистрация в DI

```csharp
// App.xaml.cs
services.AddTools();  // из PROMPT-01
services.AddTransient<IChatService, ChatService>();
```

## Часть 9: Настройки

**appsettings.json — добавить Chat в AiOperations:**
```json
{
  "AiOperations": {
    "Classification": "YandexGPT",
    "Labels": "Ollama - mistral",
    "Chat": "Ollama - llama3.1"
  }
}
```

**AiOperation enum — добавить:**
```csharp
public enum AiOperation
{
    Classification,
    Labels,
    Chat  // NEW
}
```

## Порядок выполнения

1. Расширить модели в `ILlmProvider.cs`
2. Добавить `SupportsTools` и изменить `ChatAsync` в `ILlmProvider`
3. Реализовать в `OllamaLlmProvider`
4. Реализовать в `YandexGptLlmProvider`
5. Создать `IChatService` и `ChatService`
6. Добавить `Chat` в `AiOperation`
7. Зарегистрировать в DI
8. Обновить `AiChatViewModel` для использования `IChatService`

## Проверка

```bash
dotnet build src/SmartBasket.sln
```

## Критерии готовности

- [ ] `ILlmProvider.ChatAsync` принимает tools
- [ ] `OllamaLlmProvider` поддерживает native tools
- [ ] `YandexGptLlmProvider` поддерживает native tools (rc модели)
- [ ] `ChatService` реализован с tool-use loop
- [ ] Компилируется без ошибок

## Тестирование

```
User: "Покажи последний чек"
→ ChatAsync с tools
→ LLM возвращает tool_call: get_receipts(limit=1)
→ ChatService выполняет tool
→ LLM формирует ответ
→ "Ваш последний чек от 10 декабря из Kuper на 2,450₽"
```
