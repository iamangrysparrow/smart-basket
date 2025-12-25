using System.Text;
using AiWebSniffer.Core.Models;
using Microsoft.Extensions.Logging;
using SmartBasket.Core.Shopping;
using SmartBasket.Services.Chat;
using SmartBasket.Services.Tools.Handlers;

namespace SmartBasket.Services.Shopping;

/// <summary>
/// Реализация сервиса AI-выбора товаров.
/// Использует ShoppingChatService для отправки запроса AI в контексте текущей беседы.
/// </summary>
public class ProductSelectorService : IProductSelectorService
{
    private readonly Lazy<IShoppingChatService> _chatServiceLazy;
    private readonly SelectProductHandler _selectProductHandler;
    private readonly ILogger<ProductSelectorService> _logger;

    private IShoppingChatService ChatService => _chatServiceLazy.Value;

    public ProductSelectorService(
        Lazy<IShoppingChatService> chatService,
        SelectProductHandler selectProductHandler,
        ILogger<ProductSelectorService> logger)
    {
        _chatServiceLazy = chatService;
        _selectProductHandler = selectProductHandler;
        _logger = logger;
    }

    public async Task<ProductSelection?> SelectBestProductAsync(
        DraftItem draftItem,
        List<ProductSearchResult> searchResults,
        string storeId,
        string storeName,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[ProductSelectorService] Selecting product for '{DraftItem}' from {Count} results in {Store}",
            draftItem.Name, searchResults.Count, storeName);

        // Если нет результатов — сразу возвращаем null
        if (searchResults.Count == 0)
        {
            _logger.LogWarning("[ProductSelectorService] No search results for '{DraftItem}'", draftItem.Name);
            return null;
        }

        try
        {
            // Загружаем шаблон промпта
            var template = LoadPromptTemplate("prompt_shopping_select_product.txt");

            // Форматируем результаты поиска
            var resultsText = FormatSearchResults(searchResults);

            // Подставляем плейсхолдеры
            var prompt = template
                .Replace("{{DRAFT_ITEM_ID}}", draftItem.Id.ToString())
                .Replace("{{DRAFT_ITEM_NAME}}", draftItem.Name)
                .Replace("{{DRAFT_ITEM_QUANTITY}}", draftItem.Quantity.ToString("0.##"))
                .Replace("{{DRAFT_ITEM_UNIT}}", draftItem.Unit)
                .Replace("{{DRAFT_ITEM_CATEGORY}}", draftItem.Category ?? "Не указана")
                .Replace("{{STORE_NAME}}", storeName)
                .Replace("{{SEARCH_RESULTS}}", resultsText);

            _logger.LogDebug("[ProductSelectorService] Prompt length: {Length} chars", prompt.Length);

            // Очищаем предыдущий выбор
            _selectProductHandler.ClearLastSelection();

            // Отправляем в чат (AI вызовет select_product)
            var response = await ChatService.SendAsync(prompt, progress, ct);

            // Получаем результат из хендлера
            var selection = _selectProductHandler.LastSelection;

            if (selection == null)
            {
                _logger.LogWarning(
                    "[ProductSelectorService] AI did not call select_product for '{DraftItem}'",
                    draftItem.Name);
                return null;
            }

            _logger.LogInformation(
                "[ProductSelectorService] Selected: ProductId={ProductId}, Qty={Qty}, Reason={Reason}",
                selection.SelectedProductId ?? "null",
                selection.Quantity,
                selection.Reasoning);

            return selection;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[ProductSelectorService] Selection cancelled for '{DraftItem}'", draftItem.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ProductSelectorService] Error selecting product for '{DraftItem}'", draftItem.Name);
            return null;
        }
    }

    private string FormatSearchResults(List<ProductSearchResult> results)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] ID: {r.Id}");
            sb.AppendLine($"    Название: {r.Name}");
            sb.AppendLine($"    Цена: {r.Price:N2}₽");

            if (r.Quantity > 0 && !string.IsNullOrEmpty(r.Unit))
            {
                sb.AppendLine($"    Фасовка: {r.Quantity:0.##} {r.Unit}");
            }

            sb.AppendLine($"    В наличии: {(r.InStock ? "Да" : "Нет")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string LoadPromptTemplate(string fileName)
    {
        // Ищем в папке приложения
        var path = Path.Combine(AppContext.BaseDirectory, fileName);

        if (!File.Exists(path))
        {
            // Fallback: ищем рядом с exe
            path = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                fileName);
        }

        if (!File.Exists(path))
        {
            _logger.LogError("[ProductSelectorService] Prompt template not found: {FileName}", fileName);
            throw new FileNotFoundException($"Prompt template not found: {fileName}", fileName);
        }

        return File.ReadAllText(path);
    }
}
