using System.Windows;
using System.Windows.Controls;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class CategoriesView : UserControl
{
    public CategoriesView()
    {
        InitializeComponent();
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CategoryTreeItemViewModel item && DataContext is MainViewModel vm)
        {
            vm.OnCategoryTreeItemSelected(item);
        }
    }
}
