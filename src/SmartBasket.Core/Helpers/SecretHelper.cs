using System.Text;

namespace SmartBasket.Core.Helpers;

/// <summary>
/// Простое шифрование секретов через Base64.
/// TODO: Заменить на DPAPI или Azure Key Vault для production.
/// </summary>
public static class SecretHelper
{
    private const string Prefix = "enc:";

    /// <summary>
    /// Зашифровать значение (если ещё не зашифровано)
    /// </summary>
    public static string Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        // Already encrypted
        if (plainText.StartsWith(Prefix))
            return plainText;

        var bytes = Encoding.UTF8.GetBytes(plainText);
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Расшифровать значение (если зашифровано)
    /// </summary>
    public static string Decrypt(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        // Not encrypted
        if (!encryptedText.StartsWith(Prefix))
            return encryptedText;

        try
        {
            var base64 = encryptedText[Prefix.Length..];
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Return as-is if decryption fails
            return encryptedText;
        }
    }

    /// <summary>
    /// Проверить, зашифровано ли значение
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(Prefix);
    }
}
