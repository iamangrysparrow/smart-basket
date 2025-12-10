using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace SmartBasket.WPF.Views.Settings;

public partial class AiProvidersSettingsView : UserControl
{
    public AiProvidersSettingsView()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
