using SmartBasket.Core.Configuration;

namespace SmartBasket.Services.Llm;

/// <summary>
/// Интерфейс для LLM провайдеров поддерживающих режим рассуждений (reasoning)
/// </summary>
public interface IReasoningProvider
{
    /// <summary>
    /// Поддерживает ли провайдер режим рассуждений
    /// </summary>
    bool SupportsReasoning { get; }

    /// <summary>
    /// Текущий режим рассуждений
    /// </summary>
    ReasoningMode CurrentReasoningMode { get; }

    /// <summary>
    /// Текущий уровень рассуждений
    /// </summary>
    ReasoningEffort CurrentReasoningEffort { get; }

    /// <summary>
    /// Устанавливает параметры режима рассуждений для текущей сессии
    /// </summary>
    /// <param name="mode">Режим рассуждений (null = использовать из конфигурации)</param>
    /// <param name="effort">Уровень рассуждений (null = использовать из конфигурации)</param>
    void SetReasoningParameters(ReasoningMode? mode, ReasoningEffort? effort);

    /// <summary>
    /// Сбрасывает runtime override для reasoning параметров
    /// </summary>
    void ResetReasoningParameters();
}
