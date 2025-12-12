using System;
using System.Windows;

namespace SmartBasket.WPF.Themes;

public enum AppTheme
{
    Light,
    Dark
}

/// <summary>
/// Управление темами приложения.
/// Переключает ResourceDictionary с цветами в App.Resources.
/// </summary>
public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Light;

    public static AppTheme CurrentTheme => _currentTheme;

    public static event EventHandler<AppTheme>? ThemeChanged;

    public static void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        ApplyTheme(theme);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;

        var app = Application.Current;
        if (app == null) return;

        var resources = app.Resources.MergedDictionaries;

        // Find and remove current theme color dictionary (both old and new format)
        ResourceDictionary? themeDict = null;
        foreach (var dict in resources)
        {
            var source = dict.Source?.OriginalString;
            if (source != null && (
                source.Contains("Colors.Light") ||
                source.Contains("Colors.Dark") ||
                source.Contains("LightTheme") ||
                source.Contains("DarkTheme")))
            {
                themeDict = dict;
                break;
            }
        }

        if (themeDict != null)
        {
            resources.Remove(themeDict);
        }

        // Add new theme dictionary
        var newThemeUri = theme switch
        {
            AppTheme.Light => new Uri("Themes/Colors.Light.xaml", UriKind.Relative),
            AppTheme.Dark => new Uri("Themes/Colors.Dark.xaml", UriKind.Relative),
            _ => throw new ArgumentOutOfRangeException(nameof(theme))
        };

        resources.Insert(0, new ResourceDictionary { Source = newThemeUri });

        ThemeChanged?.Invoke(null, theme);
    }

    public static void ToggleTheme()
    {
        ApplyTheme(_currentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }
}
