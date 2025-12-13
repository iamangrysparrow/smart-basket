using System;
using System.Collections.Generic;
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

        // Find and remove current theme color dictionary, Brushes.xaml, and HandyControlOverrides.xaml
        var toRemove = new List<ResourceDictionary>();
        foreach (var dict in resources)
        {
            var source = dict.Source?.OriginalString;
            if (source != null && (
                source.Contains("Colors.Light") ||
                source.Contains("Colors.Dark") ||
                source.Contains("LightTheme") ||
                source.Contains("DarkTheme") ||
                source.Contains("Brushes.xaml") ||
                source.Contains("HandyControlOverrides.xaml")))
            {
                toRemove.Add(dict);
            }
        }

        foreach (var dict in toRemove)
        {
            resources.Remove(dict);
        }

        // Add new theme colors dictionary first
        var newThemeUri = theme switch
        {
            AppTheme.Light => new Uri("Themes/Colors.Light.xaml", UriKind.Relative),
            AppTheme.Dark => new Uri("Themes/Colors.Dark.xaml", UriKind.Relative),
            _ => throw new ArgumentOutOfRangeException(nameof(theme))
        };

        resources.Insert(0, new ResourceDictionary { Source = newThemeUri });

        // Re-add Brushes.xaml AFTER colors so it picks up new color values
        resources.Insert(1, new ResourceDictionary { Source = new Uri("Themes/Brushes.xaml", UriKind.Relative) });

        // Add HandyControlOverrides at the END to override everything
        resources.Add(new ResourceDictionary { Source = new Uri("Themes/HandyControlOverrides.xaml", UriKind.Relative) });

        ThemeChanged?.Invoke(null, theme);
    }

    public static void ToggleTheme()
    {
        ApplyTheme(_currentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }
}
