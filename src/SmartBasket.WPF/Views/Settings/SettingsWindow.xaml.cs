using System.Windows;
using System.Windows.Controls;
using SmartBasket.WPF.ViewModels.Settings;

namespace SmartBasket.WPF.Views.Settings;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel == null) return;

        switch (e.NewValue)
        {
            case SettingsCategoryItem category:
                _viewModel.SelectCategory(category.Category);
                break;
            case SettingsItemViewModel item:
                _viewModel.SelectItem(item);
                break;
        }
    }
}
