using System.Text.RegularExpressions;

namespace SmartBasket.Services.Parsing;

/// <summary>
/// Утилита для работы с HTML
/// </summary>
public static class HtmlHelper
{
    /// <summary>
    /// Очищает HTML от тегов и конвертирует в plain text
    /// </summary>
    public static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Удалить style и script теги с содержимым
        var result = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);

        // Заменить <br>, <p>, <div>, <tr> на переносы строк
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</?(p|div|tr|li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</(td|th)>", " | ", RegexOptions.IgnoreCase);

        // Удалить все остальные HTML теги
        result = Regex.Replace(result, @"<[^>]+>", "");

        // Декодировать HTML entities
        result = System.Net.WebUtility.HtmlDecode(result);

        // Убрать множественные пробелы и переносы
        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n\s*\n", "\n\n");

        return result.Trim();
    }

    /// <summary>
    /// Проверяет, содержит ли текст HTML теги
    /// </summary>
    public static bool IsHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<table", StringComparison.OrdinalIgnoreCase);
    }
}
