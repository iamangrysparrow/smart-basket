using System.Windows.Controls;

namespace SmartBasket.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the PasswordBox for password binding from code-behind
    /// </summary>
    public PasswordBox GetPasswordBox() => PasswordBox;
}
