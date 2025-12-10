using System.Windows;
using System.Windows.Controls;

namespace SmartBasket.WPF.Controls;

public partial class PasswordBoxWithReveal : UserControl
{
    private bool _isUpdating;

    public PasswordBoxWithReveal()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(PasswordBoxWithReveal),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBoxWithReveal control && !control._isUpdating)
        {
            control._isUpdating = true;
            var newValue = e.NewValue as string ?? string.Empty;
            control.PasswordHidden.Password = newValue;
            control.PasswordVisible.Text = newValue;
            control._isUpdating = false;
        }
    }

    private void PasswordHidden_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUpdating)
        {
            _isUpdating = true;
            Password = PasswordHidden.Password;
            _isUpdating = false;
        }
    }

    private void PasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUpdating)
        {
            _isUpdating = true;
            PasswordHidden.Password = PasswordVisible.Text;
            Password = PasswordVisible.Text; // Sync to DependencyProperty
            _isUpdating = false;
        }
    }

    private void RevealButton_Checked(object sender, RoutedEventArgs e)
    {
        // Show password
        PasswordHidden.Visibility = Visibility.Collapsed;
        PasswordVisible.Visibility = Visibility.Visible;
        EyeOpen.Visibility = Visibility.Collapsed;
        EyeClosed.Visibility = Visibility.Visible;
    }

    private void RevealButton_Unchecked(object sender, RoutedEventArgs e)
    {
        // Hide password
        PasswordHidden.Visibility = Visibility.Visible;
        PasswordVisible.Visibility = Visibility.Collapsed;
        EyeOpen.Visibility = Visibility.Visible;
        EyeClosed.Visibility = Visibility.Collapsed;
    }
}
