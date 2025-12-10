using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования связки AI операций с провайдерами
/// </summary>
public partial class AiOperationsViewModel : ObservableObject
{
    public AiOperationsViewModel() { }

    public AiOperationsViewModel(AiOperationsConfig config)
    {
        Classification = config.Classification ?? string.Empty;
        Labels = config.Labels ?? string.Empty;
    }

    [ObservableProperty]
    private string _classification = string.Empty;

    [ObservableProperty]
    private string _labels = string.Empty;

    /// <summary>
    /// Преобразование обратно в конфигурацию
    /// </summary>
    public AiOperationsConfig ToConfig()
    {
        return new AiOperationsConfig
        {
            Classification = string.IsNullOrWhiteSpace(Classification) ? null : Classification,
            Labels = string.IsNullOrWhiteSpace(Labels) ? null : Labels
        };
    }
}
