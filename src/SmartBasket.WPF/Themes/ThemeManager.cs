using System;
using System.Windows;
using System.Windows.Media;

namespace SmartBasket.WPF.Themes;

public enum AppTheme
{
    Light,
    Dark
}

/// <summary>
/// Управление темами приложения.
/// Переключает ResourceDictionary в App.Resources.
/// </summary>
public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Light;

    public static AppTheme CurrentTheme => _currentTheme;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;

        var app = Application.Current;
        if (app == null) return;

        // Найти и удалить текущую тему
        ResourceDictionary? themeToRemove = null;
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict.Source?.OriginalString.Contains("Theme.xaml") == true)
            {
                themeToRemove = dict;
                break;
            }
        }

        if (themeToRemove != null)
        {
            app.Resources.MergedDictionaries.Remove(themeToRemove);
        }

        // Добавить новую тему
        var themePath = theme switch
        {
            AppTheme.Dark => "Themes/DarkTheme.xaml",
            _ => "Themes/LightTheme.xaml"
        };

        var newTheme = new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative)
        };

        app.Resources.MergedDictionaries.Insert(0, newTheme);

        ThemeChanged?.Invoke(null, theme);
    }

    public static void ToggleTheme()
    {
        ApplyTheme(_currentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }
}
