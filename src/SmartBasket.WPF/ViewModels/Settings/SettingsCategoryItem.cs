using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// Элемент дерева категорий настроек
/// </summary>
public partial class SettingsCategoryItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private SettingsCategory _category;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Дочерние элементы (конкретные источники/провайдеры)
    /// </summary>
    public ObservableCollection<SettingsItemViewModel> Items { get; } = new();
}

/// <summary>
/// Элемент в списке настроек (конкретный источник, парсер, провайдер)
/// </summary>
public partial class SettingsItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// Ключ/идентификатор элемента
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Категория родителя
    /// </summary>
    public SettingsCategory ParentCategory { get; set; }
}
