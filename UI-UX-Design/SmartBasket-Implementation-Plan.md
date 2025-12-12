# Smart Basket — Design System Implementation Plan

> Пошаговый план внедрения дизайн-системы для Claude Code
> Референс: SmartBasket-DesignGuide.md

---

## Общие правила для Claude Code

1. **Не ломай существующий функционал** — каждый шаг должен оставлять приложение рабочим
2. **Один шаг = один коммит** — после каждого шага код должен компилироваться и запускаться
3. **Сначала структура, потом детали** — создаём файлы ресурсов, потом наполняем
4. **DynamicResource везде** — для поддержки переключения тем в runtime

---

## Шаг 1: Структура файлов ресурсов

### Задача
Создать структуру папок и пустые файлы ResourceDictionary для дизайн-системы.

### Действия
```
SmartBasket.WPF/
└── Themes/
    ├── Colors.Light.xaml      # Цвета светлой темы
    ├── Colors.Dark.xaml       # Цвета тёмной темы
    ├── Brushes.xaml           # Кисти (ссылаются на цвета)
    ├── Sizes.xaml             # Размеры, отступы, шрифты
    ├── Controls/
    │   ├── ButtonStyles.xaml
    │   ├── TextBlockStyles.xaml
    │   ├── TextBoxStyles.xaml
    │   ├── ListBoxStyles.xaml
    │   ├── TreeViewStyles.xaml
    │   ├── DataGridStyles.xaml
    │   ├── TabControlStyles.xaml
    │   └── TagStyles.xaml
    └── ThemeManager.cs        # Класс для переключения тем
```

### Создать файлы

**Themes/Colors.Light.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Background -->
    <Color x:Key="BackgroundBase">#FFFFFF</Color>
    <Color x:Key="BackgroundLayer1">#F8F8F8</Color>
    <Color x:Key="BackgroundLayer2">#F0F0F0</Color>
    <Color x:Key="BackgroundElevated">#FFFFFF</Color>
    
    <!-- Foreground -->
    <Color x:Key="ForegroundPrimary">#1E1E1E</Color>
    <Color x:Key="ForegroundSecondary">#5C5C5C</Color>
    <Color x:Key="ForegroundTertiary">#9E9E9E</Color>
    <Color x:Key="ForegroundInverse">#FFFFFF</Color>
    
    <!-- Border -->
    <Color x:Key="BorderDefault">#E0E0E0</Color>
    <Color x:Key="BorderStrong">#BDBDBD</Color>
    <Color x:Key="BorderSubtle">#EEEEEE</Color>
    
    <!-- Accent (Purple) -->
    <Color x:Key="AccentDefault">#7C4DFF</Color>
    <Color x:Key="AccentHover">#651FFF</Color>
    <Color x:Key="AccentPressed">#5414CC</Color>
    <Color x:Key="AccentSubtle">#EDE7F6</Color>
    <Color x:Key="AccentMuted">#B39DDB</Color>
    
    <!-- Semantic -->
    <Color x:Key="Success">#4CAF50</Color>
    <Color x:Key="SuccessSubtle">#E8F5E9</Color>
    <Color x:Key="Warning">#FF9800</Color>
    <Color x:Key="WarningSubtle">#FFF3E0</Color>
    <Color x:Key="Error">#F44336</Color>
    <Color x:Key="ErrorSubtle">#FFEBEE</Color>
    <Color x:Key="Info">#2196F3</Color>
    <Color x:Key="InfoSubtle">#E3F2FD</Color>
    
    <!-- Category Tags -->
    <Color x:Key="CategoryDairyBg">#E3F2FD</Color>
    <Color x:Key="CategoryDairyFg">#1565C0</Color>
    <Color x:Key="CategoryMeatBg">#FFEBEE</Color>
    <Color x:Key="CategoryMeatFg">#C62828</Color>
    <Color x:Key="CategoryVegetablesBg">#E8F5E9</Color>
    <Color x:Key="CategoryVegetablesFg">#2E7D32</Color>
    <Color x:Key="CategoryBakeryBg">#FFF3E0</Color>
    <Color x:Key="CategoryBakeryFg">#E65100</Color>
    <Color x:Key="CategoryDrinksBg">#E0F7FA</Color>
    <Color x:Key="CategoryDrinksFg">#00838F</Color>
    <Color x:Key="CategoryGroceryBg">#F3E5F5</Color>
    <Color x:Key="CategoryGroceryFg">#7B1FA2</Color>
    <Color x:Key="CategoryFrozenBg">#E8EAF6</Color>
    <Color x:Key="CategoryFrozenFg">#3949AB</Color>
    <Color x:Key="CategoryHouseholdBg">#EFEBE9</Color>
    <Color x:Key="CategoryHouseholdFg">#5D4037</Color>
    <Color x:Key="CategoryOtherBg">#FAFAFA</Color>
    <Color x:Key="CategoryOtherFg">#616161</Color>
    
</ResourceDictionary>
```

**Themes/Colors.Dark.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Background -->
    <Color x:Key="BackgroundBase">#1E1E1E</Color>
    <Color x:Key="BackgroundLayer1">#252526</Color>
    <Color x:Key="BackgroundLayer2">#2D2D30</Color>
    <Color x:Key="BackgroundElevated">#3C3C3C</Color>
    
    <!-- Foreground -->
    <Color x:Key="ForegroundPrimary">#E0E0E0</Color>
    <Color x:Key="ForegroundSecondary">#A0A0A0</Color>
    <Color x:Key="ForegroundTertiary">#6E6E6E</Color>
    <Color x:Key="ForegroundInverse">#1E1E1E</Color>
    
    <!-- Border -->
    <Color x:Key="BorderDefault">#3C3C3C</Color>
    <Color x:Key="BorderStrong">#505050</Color>
    <Color x:Key="BorderSubtle">#2D2D30</Color>
    
    <!-- Accent (Purple - lighter for dark theme) -->
    <Color x:Key="AccentDefault">#B388FF</Color>
    <Color x:Key="AccentHover">#D1C4E9</Color>
    <Color x:Key="AccentPressed">#9575CD</Color>
    <Color x:Key="AccentSubtle">#332940</Color>
    <Color x:Key="AccentMuted">#7E57C2</Color>
    
    <!-- Semantic -->
    <Color x:Key="Success">#81C784</Color>
    <Color x:Key="SuccessSubtle">#1B3D1F</Color>
    <Color x:Key="Warning">#FFB74D</Color>
    <Color x:Key="WarningSubtle">#3D2E14</Color>
    <Color x:Key="Error">#E57373</Color>
    <Color x:Key="ErrorSubtle">#3D1F1F</Color>
    <Color x:Key="Info">#64B5F6</Color>
    <Color x:Key="InfoSubtle">#1A2D3D</Color>
    
    <!-- Category Tags (Dark variants) -->
    <Color x:Key="CategoryDairyBg">#1A2D3D</Color>
    <Color x:Key="CategoryDairyFg">#64B5F6</Color>
    <Color x:Key="CategoryMeatBg">#3D1F1F</Color>
    <Color x:Key="CategoryMeatFg">#E57373</Color>
    <Color x:Key="CategoryVegetablesBg">#1B3D1F</Color>
    <Color x:Key="CategoryVegetablesFg">#81C784</Color>
    <Color x:Key="CategoryBakeryBg">#3D2814</Color>
    <Color x:Key="CategoryBakeryFg">#FFB74D</Color>
    <Color x:Key="CategoryDrinksBg">#14333D</Color>
    <Color x:Key="CategoryDrinksFg">#4DD0E1</Color>
    <Color x:Key="CategoryGroceryBg">#2D1B3D</Color>
    <Color x:Key="CategoryGroceryFg">#CE93D8</Color>
    <Color x:Key="CategoryFrozenBg">#1A1F3D</Color>
    <Color x:Key="CategoryFrozenFg">#9FA8DA</Color>
    <Color x:Key="CategoryHouseholdBg">#2D2519</Color>
    <Color x:Key="CategoryHouseholdFg">#BCAAA4</Color>
    <Color x:Key="CategoryOtherBg">#2D2D30</Color>
    <Color x:Key="CategoryOtherFg">#9E9E9E</Color>
    
</ResourceDictionary>
```

**Themes/Brushes.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Background Brushes -->
    <SolidColorBrush x:Key="BackgroundBaseBrush" Color="{DynamicResource BackgroundBase}"/>
    <SolidColorBrush x:Key="BackgroundLayer1Brush" Color="{DynamicResource BackgroundLayer1}"/>
    <SolidColorBrush x:Key="BackgroundLayer2Brush" Color="{DynamicResource BackgroundLayer2}"/>
    <SolidColorBrush x:Key="BackgroundElevatedBrush" Color="{DynamicResource BackgroundElevated}"/>
    
    <!-- Foreground Brushes -->
    <SolidColorBrush x:Key="ForegroundPrimaryBrush" Color="{DynamicResource ForegroundPrimary}"/>
    <SolidColorBrush x:Key="ForegroundSecondaryBrush" Color="{DynamicResource ForegroundSecondary}"/>
    <SolidColorBrush x:Key="ForegroundTertiaryBrush" Color="{DynamicResource ForegroundTertiary}"/>
    <SolidColorBrush x:Key="ForegroundInverseBrush" Color="{DynamicResource ForegroundInverse}"/>
    
    <!-- Border Brushes -->
    <SolidColorBrush x:Key="BorderDefaultBrush" Color="{DynamicResource BorderDefault}"/>
    <SolidColorBrush x:Key="BorderStrongBrush" Color="{DynamicResource BorderStrong}"/>
    <SolidColorBrush x:Key="BorderSubtleBrush" Color="{DynamicResource BorderSubtle}"/>
    
    <!-- Accent Brushes -->
    <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentDefault}"/>
    <SolidColorBrush x:Key="AccentHoverBrush" Color="{DynamicResource AccentHover}"/>
    <SolidColorBrush x:Key="AccentPressedBrush" Color="{DynamicResource AccentPressed}"/>
    <SolidColorBrush x:Key="AccentSubtleBrush" Color="{DynamicResource AccentSubtle}"/>
    <SolidColorBrush x:Key="AccentMutedBrush" Color="{DynamicResource AccentMuted}"/>
    
    <!-- Semantic Brushes -->
    <SolidColorBrush x:Key="SuccessBrush" Color="{DynamicResource Success}"/>
    <SolidColorBrush x:Key="SuccessSubtleBrush" Color="{DynamicResource SuccessSubtle}"/>
    <SolidColorBrush x:Key="WarningBrush" Color="{DynamicResource Warning}"/>
    <SolidColorBrush x:Key="WarningSubtleBrush" Color="{DynamicResource WarningSubtle}"/>
    <SolidColorBrush x:Key="ErrorBrush" Color="{DynamicResource Error}"/>
    <SolidColorBrush x:Key="ErrorSubtleBrush" Color="{DynamicResource ErrorSubtle}"/>
    <SolidColorBrush x:Key="InfoBrush" Color="{DynamicResource Info}"/>
    <SolidColorBrush x:Key="InfoSubtleBrush" Color="{DynamicResource InfoSubtle}"/>
    
</ResourceDictionary>
```

**Themes/Sizes.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:system="clr-namespace:System;assembly=mscorlib">
    
    <!-- Spacing (base unit: 4px) -->
    <system:Double x:Key="SpacingXS">4</system:Double>
    <system:Double x:Key="SpacingS">8</system:Double>
    <system:Double x:Key="SpacingM">12</system:Double>
    <system:Double x:Key="SpacingL">16</system:Double>
    <system:Double x:Key="SpacingXL">24</system:Double>
    <system:Double x:Key="SpacingXXL">32</system:Double>
    
    <!-- Thickness shortcuts -->
    <Thickness x:Key="PaddingS">8</Thickness>
    <Thickness x:Key="PaddingM">12</Thickness>
    <Thickness x:Key="PaddingL">16</Thickness>
    <Thickness x:Key="PaddingXL">24</Thickness>
    
    <!-- Font Sizes -->
    <system:Double x:Key="FontSizeCaption">11</system:Double>
    <system:Double x:Key="FontSizeBody">13</system:Double>
    <system:Double x:Key="FontSizeSubtitle">14</system:Double>
    <system:Double x:Key="FontSizeTitle">18</system:Double>
    <system:Double x:Key="FontSizeHeader">24</system:Double>
    
    <!-- Font Weights -->
    <FontWeight x:Key="FontWeightNormal">Normal</FontWeight>
    <FontWeight x:Key="FontWeightMedium">Medium</FontWeight>
    <FontWeight x:Key="FontWeightSemiBold">SemiBold</FontWeight>
    <FontWeight x:Key="FontWeightBold">Bold</FontWeight>
    
    <!-- Border Radius -->
    <CornerRadius x:Key="RadiusS">2</CornerRadius>
    <CornerRadius x:Key="RadiusM">4</CornerRadius>
    <CornerRadius x:Key="RadiusL">6</CornerRadius>
    <CornerRadius x:Key="RadiusXL">8</CornerRadius>
    
    <!-- Component Heights -->
    <system:Double x:Key="ToolbarHeight">48</system:Double>
    <system:Double x:Key="StatusBarHeight">28</system:Double>
    <system:Double x:Key="ButtonHeight">32</system:Double>
    <system:Double x:Key="TextBoxHeight">32</system:Double>
    <system:Double x:Key="ListItemHeight">48</system:Double>
    <system:Double x:Key="TableRowHeight">40</system:Double>
    
    <!-- Sidebar -->
    <system:Double x:Key="SidebarWidth">300</system:Double>
    
</ResourceDictionary>
```

**Themes/ThemeManager.cs:**
```csharp
using System;
using System.Windows;

namespace SmartBasket.WPF.Themes;

public enum AppTheme
{
    Light,
    Dark
}

public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Light;
    
    public static AppTheme CurrentTheme => _currentTheme;
    
    public static event EventHandler<AppTheme>? ThemeChanged;
    
    public static void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        
        _currentTheme = theme;
        
        var app = Application.Current;
        var resources = app.Resources.MergedDictionaries;
        
        // Find and remove current theme dictionary
        ResourceDictionary? themeDict = null;
        foreach (var dict in resources)
        {
            if (dict.Source?.OriginalString.Contains("Colors.Light") == true ||
                dict.Source?.OriginalString.Contains("Colors.Dark") == true)
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
        SetTheme(_currentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }
}
```

### Критерий готовности (Definition of Done)
- [ ] Папка `Themes/` создана в проекте SmartBasket.WPF
- [ ] Все файлы .xaml созданы и компилируются без ошибок
- [ ] ThemeManager.cs добавлен и компилируется
- [ ] Приложение запускается (пока без видимых изменений)

---

## Шаг 2: Подключение ресурсов в App.xaml

### Задача
Подключить созданные ResourceDictionary к приложению.

### Действия

**Изменить App.xaml:**
```xml
<Application x:Class="SmartBasket.WPF.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Theme Colors (Light by default) -->
                <ResourceDictionary Source="Themes/Colors.Light.xaml"/>
                
                <!-- Brushes (reference colors) -->
                <ResourceDictionary Source="Themes/Brushes.xaml"/>
                
                <!-- Sizes and Typography -->
                <ResourceDictionary Source="Themes/Sizes.xaml"/>
                
                <!-- Control Styles (добавим позже) -->
                <!-- <ResourceDictionary Source="Themes/Controls/ButtonStyles.xaml"/> -->
                
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### Критерий готовности
- [ ] App.xaml обновлён
- [ ] Приложение запускается без ошибок
- [ ] В XAML-редакторе доступны ресурсы (например, `{DynamicResource BackgroundBaseBrush}`)

---

## Шаг 3: Установка HandyControl

### Задача
Установить и настроить HandyControl для сложных контролов (DatePicker, NumericUpDown и др.).

### Гибридный подход

| Контрол | Источник |
|---------|----------|
| Button, TextBox, ListBox, панели, метки | Свои стили |
| DatePicker, TimePicker, NumericUpDown | HandyControl |
| ComboBox, TreeView | Свои стили (или HandyControl если нужна функциональность) |

### Действия

**1. Установить пакет:**
```bash
dotnet add D:\AI\smart-basket\src\SmartBasket.WPF\SmartBasket.WPF.csproj package HandyControl
```

**2. Добавить в Colors.Light.xaml (для интеграции цветов):**
```xml
<!-- HandyControl использует эти ключи для акцентного цвета -->
<Color x:Key="PrimaryColor">#7C4DFF</Color>
<SolidColorBrush x:Key="PrimaryBrush" Color="{DynamicResource PrimaryColor}"/>
<SolidColorBrush x:Key="DarkPrimaryBrush" Color="{DynamicResource AccentPressed}"/>
<SolidColorBrush x:Key="LightPrimaryBrush" Color="{DynamicResource AccentSubtle}"/>
```

**3. Добавить в Colors.Dark.xaml:**
```xml
<!-- HandyControl - тёмная тема -->
<Color x:Key="PrimaryColor">#B388FF</Color>
<SolidColorBrush x:Key="PrimaryBrush" Color="{DynamicResource PrimaryColor}"/>
<SolidColorBrush x:Key="DarkPrimaryBrush" Color="{DynamicResource AccentPressed}"/>
<SolidColorBrush x:Key="LightPrimaryBrush" Color="{DynamicResource AccentSubtle}"/>
```

**4. Обновить App.xaml:**
```xml
<Application x:Class="SmartBasket.WPF.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:hc="https://handyorg.github.io/handycontrol"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- 1. Наши цвета (ПЕРВЫМИ - задают PrimaryBrush для HC) -->
                <ResourceDictionary Source="Themes/Colors.Light.xaml"/>
                <ResourceDictionary Source="Themes/Brushes.xaml"/>
                <ResourceDictionary Source="Themes/Sizes.xaml"/>
                
                <!-- 2. HandyControl темы -->
                <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml"/>
                <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/Theme.xaml"/>
                
                <!-- 3. Наши стили контролов (последними - перезаписывают) -->
                <!-- <ResourceDictionary Source="Themes/Controls/ButtonStyles.xaml"/> -->
                
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### Использование HandyControl контролов

```xml
<!-- Namespace в XAML файле -->
xmlns:hc="https://handyorg.github.io/handycontrol"

<!-- DatePicker (заменяет стандартный) -->
<hc:DatePicker SelectedDate="{Binding StartDate}" 
               Style="{StaticResource DatePickerExtend}"
               hc:InfoElement.Placeholder="Выберите дату"/>

<!-- DateTimePicker -->
<hc:DateTimePicker SelectedDateTime="{Binding DateTime}"
                   Style="{StaticResource DateTimePickerExtend}"/>

<!-- NumericUpDown -->
<hc:NumericUpDown Value="{Binding Count}" 
                  Minimum="0" 
                  Maximum="1000"
                  Style="{StaticResource NumericUpDownExtend}"/>

<!-- SearchBar -->
<hc:SearchBar Text="{Binding SearchText}"
              Style="{StaticResource SearchBarExtend}"
              hc:InfoElement.Placeholder="Поиск..."/>
```

### Критерий готовности
- [ ] Пакет HandyControl установлен
- [ ] App.xaml обновлён с подключением HC
- [ ] Приложение компилируется и запускается
- [ ] DatePicker из HandyControl отображается корректно (проверить на любом View)

---

## Шаг 4: Базовые стили кнопок

### Задача
Создать стили для всех типов кнопок: Primary, Secondary, Ghost, Danger.

### Действия

**Создать Themes/Controls/ButtonStyles.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Base Button Style -->
    <Style x:Key="ButtonBase" TargetType="Button">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightMedium}"/>
        <Setter Property="Padding" Value="16,8"/>
        <Setter Property="MinHeight" Value="{DynamicResource ButtonHeight}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    </Style>
    
    <!-- Primary Button (Accent) -->
    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundInverseBrush}"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource RadiusM}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center" 
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource AccentHoverBrush}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{DynamicResource AccentPressedBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- Secondary Button (Outlined) -->
    <Style x:Key="SecondaryButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource RadiusM}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center" 
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource AccentSubtleBrush}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{DynamicResource AccentSubtleBrush}"/>
                            <Setter Property="BorderBrush" Value="{DynamicResource AccentPressedBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- Ghost Button (Toolbar) -->
    <Style x:Key="GhostButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundSecondaryBrush}"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Setter Property="Padding" Value="8"/>
        <Setter Property="MinWidth" Value="{DynamicResource ButtonHeight}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource RadiusM}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center" 
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource BackgroundLayer2Brush}"/>
                            <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{DynamicResource BorderDefaultBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- Danger Button (Delete) -->
    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource ButtonBase}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource ErrorBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource ErrorBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource RadiusM}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="Center" 
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{DynamicResource ErrorSubtleBrush}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Background" Value="{DynamicResource ErrorBrush}"/>
                            <Setter Property="Foreground" Value="{DynamicResource ForegroundInverseBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
</ResourceDictionary>
```

**Добавить в App.xaml:**
```xml
<ResourceDictionary Source="Themes/Controls/ButtonStyles.xaml"/>
```

### Тестирование
Добавь временно кнопки в MainWindow для проверки:
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="20">
    <Button Style="{StaticResource PrimaryButton}" Content="Primary" Margin="4"/>
    <Button Style="{StaticResource SecondaryButton}" Content="Secondary" Margin="4"/>
    <Button Style="{StaticResource GhostButton}" Content="👁" Margin="4"/>
    <Button Style="{StaticResource DangerButton}" Content="Delete" Margin="4"/>
</StackPanel>
```

### Критерий готовности
- [ ] ButtonStyles.xaml создан и подключен
- [ ] Все 4 стиля кнопок работают
- [ ] Hover и Pressed состояния корректно отображаются
- [ ] Кнопки выглядят как в мокапе (проверить визуально)

---

## Шаг 5: Стили текста

### Задача
Создать стили для всех типов текста: Caption, Body, Subtitle, Title, Header.

### Действия

**Создать Themes/Controls/TextBlockStyles.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Caption (11px) - подписи, хинты -->
    <Style x:Key="CaptionText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundSecondaryBrush}"/>
    </Style>
    
    <Style x:Key="CaptionTextTertiary" TargetType="TextBlock" BasedOn="{StaticResource CaptionText}">
        <Setter Property="Foreground" Value="{DynamicResource ForegroundTertiaryBrush}"/>
    </Style>
    
    <!-- Body (13px) - основной текст -->
    <Style x:Key="BodyText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
    </Style>
    
    <Style x:Key="BodyTextStrong" TargetType="TextBlock" BasedOn="{StaticResource BodyText}">
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
    </Style>
    
    <Style x:Key="BodyTextSecondary" TargetType="TextBlock" BasedOn="{StaticResource BodyText}">
        <Setter Property="Foreground" Value="{DynamicResource ForegroundSecondaryBrush}"/>
    </Style>
    
    <!-- Subtitle (14px) - заголовки секций -->
    <Style x:Key="SubtitleText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeSubtitle}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
    </Style>
    
    <!-- Title (18px) - заголовки панелей -->
    <Style x:Key="TitleText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeTitle}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
    </Style>
    
    <!-- Header (24px) - заголовки окон -->
    <Style x:Key="HeaderText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeHeader}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
    </Style>
    
    <!-- Accent Text -->
    <Style x:Key="AccentText" TargetType="TextBlock" BasedOn="{StaticResource BodyText}">
        <Setter Property="Foreground" Value="{DynamicResource AccentBrush}"/>
    </Style>
    
    <!-- Table Header (uppercase) -->
    <Style x:Key="TableHeaderText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundSecondaryBrush}"/>
        <Setter Property="TextTransform" Value="Uppercase"/>
    </Style>
    
    <!-- Monospace (для чисел) -->
    <Style x:Key="MonoText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Cascadia Code, Consolas, monospace"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
    </Style>
    
    <Style x:Key="MonoTextStrong" TargetType="TextBlock" BasedOn="{StaticResource MonoText}">
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightSemiBold}"/>
    </Style>
    
</ResourceDictionary>
```

### Критерий готовности
- [ ] TextBlockStyles.xaml создан и подключен
- [ ] Стили доступны в XAML
- [ ] Визуально соответствует гайду (размеры, цвета)

---

## Шаг 6: Стили TextBox и поля ввода

### Задача
Создать стили для полей ввода с правильными состояниями.

### Действия

**Создать Themes/Controls/TextBoxStyles.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Default TextBox -->
    <Style x:Key="DefaultTextBox" TargetType="TextBox">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeBody}"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundPrimaryBrush}"/>
        <Setter Property="Background" Value="{DynamicResource BackgroundBaseBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderDefaultBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="MinHeight" Value="{DynamicResource TextBoxHeight}"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="CaretBrush" Value="{DynamicResource ForegroundPrimaryBrush}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource AccentSubtleBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource RadiusM}"
                            SnapsToDevicePixels="True">
                        <ScrollViewer x:Name="PART_ContentHost"
                                      Focusable="False"
                                      HorizontalScrollBarVisibility="Hidden"
                                      VerticalScrollBarVisibility="Hidden"
                                      Margin="{TemplateBinding Padding}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" 
                                    Value="{DynamicResource BorderStrongBrush}"/>
                        </Trigger>
                        <Trigger Property="IsFocused" Value="True">
                            <Setter TargetName="border" Property="BorderBrush" 
                                    Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- Search TextBox (with icon placeholder) -->
    <Style x:Key="SearchTextBox" TargetType="TextBox" BasedOn="{StaticResource DefaultTextBox}">
        <Setter Property="Background" Value="{DynamicResource BackgroundBaseBrush}"/>
        <Setter Property="Padding" Value="32,6,10,6"/>
    </Style>
    
</ResourceDictionary>
```

### Критерий готовности
- [ ] TextBoxStyles.xaml создан и подключен
- [ ] Focus состояние показывает акцентную рамку
- [ ] Hover состояние работает

---

## Шаг 7: Стили ListBox (список чеков)

### Задача
Создать стиль для списка чеков с выделением активного элемента.

### Действия

**Создать Themes/Controls/ListBoxStyles.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- ListBox Container -->
    <Style x:Key="SidebarListBox" TargetType="ListBox">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="0"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="ItemContainerStyle" Value="{DynamicResource SidebarListBoxItem}"/>
    </Style>
    
    <!-- ListBox Item (Receipt Item) -->
    <Style x:Key="SidebarListBoxItem" TargetType="ListBoxItem">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="3,0,0,0"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Setter Property="Padding" Value="16,12"/>
        <Setter Property="Margin" Value="0"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="Background" 
                                    Value="{DynamicResource BackgroundLayer2Brush}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="border" Property="Background" 
                                    Value="{DynamicResource AccentSubtleBrush}"/>
                            <Setter TargetName="border" Property="BorderBrush" 
                                    Value="{DynamicResource AccentBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
</ResourceDictionary>
```

### Критерий готовности
- [ ] ListBoxStyles.xaml создан и подключен
- [ ] Выделенный элемент имеет акцентную полоску слева
- [ ] Hover подсветка работает
- [ ] Визуально соответствует мокапу

---

## Шаг 8: Стили Tag (метки категорий)

### Задача
Создать стили для меток с приглушёнными цветами.

### Действия

**Создать Themes/Controls/TagStyles.xaml:**
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Base Tag Style -->
    <Style x:Key="TagBase" TargetType="Border">
        <Setter Property="CornerRadius" Value="{DynamicResource RadiusM}"/>
        <Setter Property="Padding" Value="8,2"/>
        <Setter Property="SnapsToDevicePixels" Value="True"/>
    </Style>
    
    <!-- Tag TextBlock -->
    <Style x:Key="TagText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
        <Setter Property="FontWeight" Value="{DynamicResource FontWeightMedium}"/>
    </Style>
    
    <!-- Category-specific Tags -->
    <Style x:Key="TagDairy" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryDairyBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagMeat" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryMeatBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagVegetables" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryVegetablesBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagBakery" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryBakeryBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagDrinks" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryDrinksBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagGrocery" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryGroceryBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagHousehold" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryHouseholdBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <Style x:Key="TagOther" TargetType="Border" BasedOn="{StaticResource TagBase}">
        <Setter Property="Background">
            <Setter.Value>
                <SolidColorBrush Color="{DynamicResource CategoryOtherBg}"/>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- Empty category indicator -->
    <Style x:Key="TagEmpty" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="Segoe UI"/>
        <Setter Property="FontSize" Value="{DynamicResource FontSizeCaption}"/>
        <Setter Property="FontStyle" Value="Italic"/>
        <Setter Property="Foreground" Value="{DynamicResource ForegroundTertiaryBrush}"/>
        <Setter Property="Text" Value="Не задана"/>
    </Style>
    
</ResourceDictionary>
```

### Пример использования тега:
```xml
<Border Style="{StaticResource TagDairy}">
    <TextBlock Style="{StaticResource TagText}" 
               Foreground="{DynamicResource CategoryDairyFg}"
               Text="Молочные"/>
</Border>
```

### Критерий готовности
- [ ] TagStyles.xaml создан и подключен
- [ ] Метки отображаются с приглушёнными цветами
- [ ] В тёмной теме цвета корректные

---

## Шаг 9: Применение к MainWindow (Toolbar)

### Задача
Переработать верхнюю панель MainWindow с новыми стилями.

### Действия
1. Применить фон `BackgroundLayer1Brush` к toolbar
2. Использовать новые стили кнопок
3. Добавить группировку элементов
4. Использовать стили текста для статистики

### Пример структуры:
```xml
<!-- Toolbar -->
<Border Background="{DynamicResource BackgroundLayer1Brush}"
        BorderBrush="{DynamicResource BorderDefaultBrush}"
        BorderThickness="0,0,0,1"
        Height="{DynamicResource ToolbarHeight}">
    <DockPanel Margin="16,0">
        <!-- Logo -->
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Left" Margin="0,0,24,0">
            <Border Background="{DynamicResource AccentBrush}" 
                    CornerRadius="6" Width="24" Height="24" Margin="0,0,8,0">
                <TextBlock Text="🛒" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <TextBlock Text="Smart Basket" 
                       Style="{StaticResource SubtitleText}"
                       Foreground="{DynamicResource AccentBrush}"
                       VerticalAlignment="Center"/>
        </StackPanel>
        
        <!-- Tabs -->
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
            <!-- TabButton style нужно создать -->
        </StackPanel>
        
        <!-- Actions (Right) -->
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Right" VerticalAlignment="Center">
            <Button Style="{StaticResource PrimaryButton}" Content="✓ Collect"/>
            <Button Style="{StaticResource GhostButton}" Content="⚙" Margin="8,0,0,0"/>
        </StackPanel>
        
        <!-- Stats (Center) -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,16,0">
            <TextBlock Style="{StaticResource CaptionText}">
                <Run Text="6" Foreground="{DynamicResource AccentBrush}" FontWeight="SemiBold"/>
                <Run Text=" чеков · "/>
                <Run Text="31,541 ₽" Foreground="{DynamicResource AccentBrush}" FontWeight="SemiBold"/>
            </TextBlock>
        </StackPanel>
    </DockPanel>
</Border>
```

### Критерий готовности
- [ ] Toolbar имеет новый фон и границу
- [ ] Кнопки используют новые стили
- [ ] Элементы сгруппированы логично
- [ ] Визуально соответствует мокапу

---

## Шаг 10: Применение к списку чеков (Sidebar)

### Задача
Переработать левую панель со списком чеков.

### Действия
1. Применить фон `BackgroundLayer1Brush`
2. Использовать `SidebarListBox` стиль
3. Создать DataTemplate для элемента чека
4. Добавить поле поиска

### Критерий готовности
- [ ] Sidebar имеет корректный фон
- [ ] Выделение работает с акцентной полоской
- [ ] Hover подсветка работает
- [ ] Шрифты соответствуют гайду

---

## Шаг 11: Применение к деталям чека (Content)

### Задача
Переработать правую панель с деталями чека.

### Действия
1. Применить фон `BackgroundBaseBrush`
2. Использовать стили текста для заголовка
3. Применить стили к таблице товаров
4. Использовать новые метки для категорий

### Критерий готовности
- [ ] Заголовок чека соответствует мокапу
- [ ] Таблица товаров стилизована
- [ ] Метки отображаются с новыми цветами
- [ ] "Не задана" отображается курсивом серым

---

## Шаг 12: Применение к экрану "Продукты"

### Задача
Переработать экран со списком продуктов и категорий.

### Действия
1. Создать стиль для TreeView
2. Применить стили к таблице товаров
3. Стилизовать верхнюю панель фильтров

### Критерий готовности
- [ ] TreeView соответствует мокапу
- [ ] Таблица товаров стилизована
- [ ] Фильтры выглядят аккуратно

---

## Шаг 13: Status Bar

### Задача
Стилизовать нижнюю панель статуса.

### Действия
```xml
<Border Background="{DynamicResource BackgroundLayer1Brush}"
        BorderBrush="{DynamicResource BorderDefaultBrush}"
        BorderThickness="0,1,0,0"
        Height="{DynamicResource StatusBarHeight}">
    <DockPanel Margin="16,0">
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Left" VerticalAlignment="Center">
            <Ellipse Width="8" Height="8" Fill="{DynamicResource SuccessBrush}" Margin="0,0,6,0"/>
            <TextBlock Style="{StaticResource CaptionText}" Text="Ollama: 16 моделей"/>
        </StackPanel>
        <TextBlock Style="{StaticResource CaptionText}" 
                   DockPanel.Dock="Right"
                   Text="Продуктов: 71 | Товаров: 118"/>
    </DockPanel>
</Border>
```

### Критерий готовности
- [ ] StatusBar имеет корректный фон и границу
- [ ] Индикатор статуса отображается
- [ ] Текст читается

---

## Шаг 14: Переключение темы

### Задача
Добавить UI для переключения светлой/тёмной темы.

### Действия
1. Добавить кнопку переключения в toolbar или settings
2. Подключить ThemeManager
3. Сохранять выбор в настройках

### Пример:
```csharp
// В ViewModel или code-behind
private void ToggleTheme()
{
    ThemeManager.ToggleTheme();
    // Сохранить в настройки
}
```

### Критерий готовности
- [ ] Кнопка переключения работает
- [ ] Все цвета меняются корректно
- [ ] Выбор темы сохраняется между сессиями

---

## Шаг 15: Тестирование и полировка

### Задача
Проверить всё приложение на соответствие дизайн-гайду.

### Чеклист
- [ ] Все тексты читаемы (контраст минимум 4.5:1)
- [ ] Все интерактивные элементы имеют hover state
- [ ] Focus visible для keyboard navigation
- [ ] Тёмная тема проверена на всех экранах
- [ ] Нет элементов со старыми стилями
- [ ] Размеры окон адекватны при разных DPI
- [ ] Приложение не падает при переключении темы

---

## Порядок выполнения (Summary)

| Шаг | Название | Время | Зависит от |
|-----|----------|-------|------------|
| 1 | Структура файлов | 30 мин | — |
| 2 | Подключение в App.xaml | 15 мин | 1 |
| 3 | Установка HandyControl | 20 мин | 2 |
| 4 | Стили кнопок | 1 час | 3 |
| 5 | Стили текста | 30 мин | 3 |
| 6 | Стили TextBox | 30 мин | 3 |
| 7 | Стили ListBox | 45 мин | 3 |
| 8 | Стили Tag | 30 мин | 3 |
| 9 | Toolbar | 1-2 часа | 4,5 |
| 10 | Sidebar (чеки) | 1-2 часа | 5,7 |
| 11 | Content (детали) | 1-2 часа | 5,8 |
| 12 | Экран "Продукты" | 2-3 часа | 5,7,8 |
| 13 | Status Bar | 30 мин | 5 |
| 14 | Переключение темы | 1 час | 1-13 |
| 15 | Тестирование | 2-3 часа | 1-14 |

**Общее время: ~16-22 часа**

---

## Файлы для референса

- `SmartBasket-DesignGuide.md` — полный дизайн-гайд с цветами и правилами
- `SmartBasket-Mockup.html` — визуальный референс экрана "Чеки"
- `SmartBasket-Products-Mockup.html` — визуальный референс экрана "Продукты"

---

*Этот документ предназначен для использования с Claude Code. Выполняй шаги последовательно, проверяя критерии готовности после каждого.*
