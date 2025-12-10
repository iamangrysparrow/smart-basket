using System.Windows.Controls;

namespace SmartBasket.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the PasswordBox for email password binding from code-behind
    /// </summary>
    public PasswordBox GetPasswordBox() => PasswordBox;

    /// <summary>
    /// Gets the PasswordBox for YandexGPT API key binding from code-behind
    /// </summary>
    public PasswordBox GetYandexApiKeyBox() => YandexApiKeyBox;
}
