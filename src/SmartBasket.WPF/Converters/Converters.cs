using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace SmartBasket.WPF.Converters;

/// <summary>
/// Converts bool to Visibility (inverse: true = Collapsed, false = Visible)
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        if (value is int intValue)
        {
            return intValue > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Converts int to Visibility (int > 0 = Visible, 0 = Collapsed)
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts boolean value (true = false, false = true)
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts null to Visibility (null = Collapsed, not null = Visible)
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null to Visibility (null = Visible, not null = Collapsed)
/// Inverse of NullToVisibilityConverter
/// </summary>
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts nullable bool to Visibility based on parameter
/// Parameter "True" or "true": shows when ToolSuccess == true
/// Parameter "False" or "false": shows when ToolSuccess == false
/// If ToolSuccess is null, always Collapsed
/// </summary>
public class NullableBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return Visibility.Collapsed;

        var expectedValue = parameter?.ToString()?.ToLowerInvariant() == "true";
        return boolValue == expectedValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool to expand/collapse icon Geometry
/// true = IconCollapse (down arrow), false = IconExpand (right arrow)
/// </summary>
public class BoolToExpandIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            // Return the key name - binding will resolve it through DynamicResource
            var iconKey = isExpanded ? "IconCollapse" : "IconExpand";
            return Application.Current?.TryFindResource(iconKey);
        }
        return Application.Current?.TryFindResource("IconExpand");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts double to string with invariant culture (supports both . and , as input)
/// </summary>
public class DoubleToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return d.ToString("0.##", CultureInfo.InvariantCulture);
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            // Support both comma and dot as decimal separator
            s = s.Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }
        return 0.0;
    }
}

/// <summary>
/// Converts string to Color
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Color color = Colors.Gray;

        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                color = Colors.Gray;
            }
        }

        // Always return Brush for Background binding (targetType is often Object)
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }
        if (value is Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        return "#808080";
    }
}

/// <summary>
/// Converts boolean to string values
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "True";
    public string FalseValue { get; set; } = "False";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }
        return FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            return stringValue == TrueValue;
        }
        return false;
    }
}

/// <summary>
/// Converts non-empty string to Visibility (non-empty = Visible, empty = Collapsed)
/// </summary>
public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts string to Visibility based on inequality with parameter
/// If string != parameter, returns Visible; otherwise Collapsed
/// </summary>
public class StringNotEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value as string ?? string.Empty;
        var compareWith = parameter as string ?? string.Empty;
        return str != compareWith ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to FontWeight (true = Bold, false = Normal)
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue)
        {
            return FontWeights.SemiBold;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ActualHeight to percentage value for MaxHeight binding.
/// Parameter is percentage as double (e.g., 0.25 for 25%)
/// </summary>
public class HeightPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height && parameter is double percentage)
        {
            return Math.Max(100, height * percentage); // Min 100px
        }
        if (value is double h && parameter is string percentStr &&
            double.TryParse(percentStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            return Math.Max(100, h * pct); // Min 100px
        }
        return 200.0; // Default max height
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts three booleans (IsProductsMode, IsCategoriesMode, IsLabelsMode) to panel header text
/// </summary>
public class ViewModeToTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 &&
            values[0] is bool isProductsMode &&
            values[1] is bool isCategoriesMode &&
            values[2] is bool isLabelsMode)
        {
            if (isProductsMode) return "ПРОДУКТЫ";
            if (isCategoriesMode) return "КАТЕГОРИИ";
            if (isLabelsMode) return "МЕТКИ";
        }
        return "ПРОДУКТЫ";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Attached behavior for highlighting search text in TextBlock
/// </summary>
public static class TextBlockHighlight
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(TextBlockHighlight),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty HighlightTextProperty =
        DependencyProperty.RegisterAttached(
            "HighlightText",
            typeof(string),
            typeof(TextBlockHighlight),
            new PropertyMetadata(string.Empty, OnHighlightTextChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.RegisterAttached(
            "HighlightBrush",
            typeof(Brush),
            typeof(TextBlockHighlight),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(255, 235, 59)), OnHighlightBrushChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    public static string GetHighlightText(DependencyObject obj) => (string)obj.GetValue(HighlightTextProperty);
    public static void SetHighlightText(DependencyObject obj, string value) => obj.SetValue(HighlightTextProperty, value);

    public static Brush GetHighlightBrush(DependencyObject obj) => (Brush)obj.GetValue(HighlightBrushProperty);
    public static void SetHighlightBrush(DependencyObject obj, Brush value) => obj.SetValue(HighlightBrushProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            UpdateHighlighting(textBlock);
    }

    private static void OnHighlightTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            UpdateHighlighting(textBlock);
    }

    private static void OnHighlightBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            UpdateHighlighting(textBlock);
    }

    private static void UpdateHighlighting(TextBlock textBlock)
    {
        var text = GetText(textBlock);
        var highlightText = GetHighlightText(textBlock);
        var highlightBrush = GetHighlightBrush(textBlock);

        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(text))
            return;

        if (string.IsNullOrEmpty(highlightText))
        {
            textBlock.Inlines.Add(new Run(text));
            return;
        }

        // Find all occurrences (case insensitive)
        var currentIndex = 0;
        var searchIndex = 0;

        while ((searchIndex = text.IndexOf(highlightText, currentIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Add text before match
            if (searchIndex > currentIndex)
            {
                textBlock.Inlines.Add(new Run(text.Substring(currentIndex, searchIndex - currentIndex)));
            }

            // Add highlighted match (use original case from text)
            var matchText = text.Substring(searchIndex, highlightText.Length);
            textBlock.Inlines.Add(new Run(matchText)
            {
                Background = highlightBrush,
                FontWeight = FontWeights.SemiBold
            });

            currentIndex = searchIndex + highlightText.Length;
        }

        // Add remaining text
        if (currentIndex < text.Length)
        {
            textBlock.Inlines.Add(new Run(text.Substring(currentIndex)));
        }
    }
}
