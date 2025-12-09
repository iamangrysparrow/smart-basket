# UI Specification: Products & Items Tab

## Overview

Вкладка для управления продуктами, товарами и метками. Заменяет существующую вкладку "Категории".

## Layout

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ TOOLBAR                                                                         │
│ [Продукты|Метки]  [Магазин: ▼Все]  [☐ Показывать все]  [➕][✏️][🗑️][↻]         │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  MASTER (TreeView)              │  DETAIL (DataGrid)                            │
│  ┌───────────────────────────┐  │  ┌─────────────────────────────────────────┐  │
│  │ [🔍 Поиск продуктов...]   │  │  │ [🔍 Поиск товаров...]                   │  │
│  ├───────────────────────────┤  │  ├─────────────────────────────────────────┤  │
│  │ 📁 Все (127)              │  │  │ ☐ │ Название       │ Ед. │ Магазин │ # │  │
│  │                           │  │  ├─────────────────────────────────────────┤  │
│  │ 📦 Суп (15)               │  │  │ ☐ │ Молоко 3.2%    │ л   │ Ашан   │12 │  │
│  │   └─ 🥣 Борщ (5)          │  │  │ ☐ │ Молоко 2.5%    │ л   │ Пятер. │ 8 │  │
│  │   └─ 🥣 Куриный (7)       │  │  │ ☐ │ Кефир 1%       │ л   │ Ашан   │ 5 │  │
│  │                           │  │  │                                         │  │
│  │ 📦 Мясо (23)              │  │  │         [Drag & Drop на продукт]        │  │
│  │   └─ 🥩 Говядина (8)      │  │  │         [Right-click → меню]            │  │
│  │   └─ 🥩 Свинина (15)      │  │  │                                         │  │
│  │      └─ Шея (9)           │  │  │                                         │  │
│  │      └─ Лопатка (6)       │  │  │                                         │  │
│  │                           │  │  │                                         │  │
│  │ [Right-click → меню]      │  │  │                                         │  │
│  └───────────────────────────┘  │  └─────────────────────────────────────────┘  │
│                                                                                 │
├─────────────────────────────────────────────────────────────────────────────────┤
│ STATUSBAR                                                                       │
│ Продуктов: 24 │ Товаров: 127 │ Выбрано: 3                                       │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

## 1. Toolbar

### 1.1 Mode Switcher (ToggleButton Group)
- **[Продукты]** - режим фильтрации по иерархии продуктов
- **[Метки]** - режим фильтрации по меткам

### 1.2 Shop Filter (ComboBox)
- Dropdown со списком магазинов
- Значения: "Все", "Ашан", "Пятёрочка", ... (из БД)
- Применяется к списку товаров в Detail

### 1.3 Include Children Toggle (CheckBox)
- **[ ] Показывать все** - когда OFF: показывать только товары выбранного продукта
- **[✓] Показывать все** - когда ON: показывать товары выбранного продукта + всех потомков
- Видим только в режиме "Продукты"

### 1.4 Action Buttons
| Кнопка | Действие | Enabled |
|--------|----------|---------|
| **➕ Add** | Создать продукт/метку | Всегда |
| **✏️ Edit** | Редактировать выбранный продукт/метку | Выбран продукт/метка |
| **🗑️ Delete** | Удалить выбранный продукт/метку | Выбран продукт/метка БЕЗ связанных товаров и детей |
| **↻ Refresh** | Обновить данные | Всегда |

### 1.5 Add Button Behavior (Context-Dependent)

**В режиме "Продукты":**
- Если ничего не выбрано или выбран "Все" → создать корневой продукт
- Если выбран продукт → показать dropdown:
  - "Добавить на том же уровне" (sibling)
  - "Добавить дочерний" (child)

**В режиме "Метки":**
- Создать новую метку (диалог с именем и цветом)

---

## 2. Master Panel (Left)

### 2.1 Режим "Продукты" - TreeView

```
📁 Все (127)                    ← Специальный узел, всегда первый
📦 Суп (15)                     ← Корневой продукт
   └─ 🥣 Борщ (5)               ← Дочерний продукт
   └─ 🥣 Куриный (7)
   └─ 🥣 Щи (3)
📦 Мясо (23)
   └─ 🥩 Говядина (8)
   └─ 🥩 Свинина (15)
      └─ Шея (9)                ← Вложенность любой глубины
      └─ Лопатка (6)
📦 Молочка (34)
```

**TreeView Item Template:**
```
[Icon] [Name] ([ItemCount])
```

**Search Box** (над TreeView):
- Фильтрует дерево по названию продукта
- При вводе раскрывает ветки с совпадениями

**Selection:**
- Single selection
- При выборе → загрузка товаров в Detail

**Double-Click:**
- Открывает диалог редактирования продукта

**Context Menu (Right-Click на продукте):**
| Пункт | Действие | Enabled |
|-------|----------|---------|
| ➕ Добавить дочерний | Создать дочерний продукт | Всегда |
| ➕ Добавить на том же уровне | Создать sibling | Не для "Все" |
| ✏️ Редактировать | Открыть диалог редактирования | Не для "Все" |
| 🗑️ Удалить | Удалить продукт | Нет товаров и детей |
| --- | --- | --- |
| 🏷️ Назначить метку → | Submenu с метками | Не для "Все" |

**Drag & Drop:**
- Товар на продукт → перемещение товара в этот продукт
- Продукт на продукт → изменение иерархии (становится дочерним)
- Visual feedback: highlight drop target

---

### 2.2 Режим "Метки" - ListView

```
🏷️ Все (127)                    ← Специальный узел
🔵 Сытая семья (45)             ← Метка с цветом
🟢 Чистый дом (23)
🟡 Папа доволен (8)
🔴 Для гостей (12)
⚪ Без метки (39)               ← Специальный узел
```

**ListView Item Template:**
```
[ColorCircle] [Name] ([ItemCount])
```

**Search Box:**
- Фильтрует список по названию метки

**Double-Click:**
- Открывает диалог редактирования метки

**Context Menu (Right-Click на метке):**
| Пункт | Действие | Enabled |
|-------|----------|---------|
| ✏️ Редактировать | Открыть диалог редактирования | Не для "Все"/"Без метки" |
| 🗑️ Удалить | Удалить метку (с подтверждением) | Не для "Все"/"Без метки" |

---

## 3. Detail Panel (Right) - DataGrid

### 3.1 Columns

| Column | Header | Width | Sortable | Description |
|--------|--------|-------|----------|-------------|
| Selection | ☐ | 30px | No | Checkbox для множественного выбора |
| Name | Название | * | Yes | Название товара |
| UnitOfMeasure | Ед.изм | 60px | Yes | л, кг, шт, г, мл |
| Shop | Магазин | 100px | Yes | Название магазина |
| PurchaseCount | # | 50px | Yes | Количество покупок (ReceiptItems.Count) |
| Product | Продукт | 120px | Yes | Название продукта (только в режиме "Метки") |

### 3.2 Search Box (над DataGrid)
- Фильтрует товары по названию
- Instant search (по мере ввода)

### 3.3 Sorting
- Click на заголовок → сортировка ASC
- Повторный click → сортировка DESC
- Третий click → без сортировки

### 3.4 Multiple Selection
- Checkbox в первой колонке
- Shift+Click для диапазона
- Ctrl+Click для добавления/удаления из выбора
- "Select All" checkbox в header

### 3.5 Context Menu (Right-Click на товаре/товарах)

**Single selection:**
| Пункт | Действие |
|-------|----------|
| 📦 Переместить в продукт → | Submenu с деревом продуктов |
| 🏷️ Назначить метку → | Submenu с метками (checkbox для каждой) |
| 🏷️ Снять все метки | Удалить все метки с товара |

**Multiple selection:**
| Пункт | Действие |
|-------|----------|
| 📦 Переместить в продукт → | Submenu с деревом продуктов (для всех выбранных) |
| 🏷️ Добавить метку → | Submenu с метками (добавить ко всем) |
| 🏷️ Снять метку → | Submenu с метками (снять со всех) |
| 🏷️ Снять все метки | Удалить все метки со всех выбранных |

### 3.6 Drag & Drop
- Drag товар(ы) на продукт в TreeView → перемещение
- Visual feedback: ghost image при перетаскивании

---

## 4. Dialogs

### 4.1 Product Dialog (Create/Edit)

```
┌─────────────────────────────────────┐
│ Создать продукт          [X]       │
├─────────────────────────────────────┤
│                                     │
│  Название: [________________]       │
│                                     │
│  Родитель: [▼ Нет (корневой) ]     │
│            ├─ Нет (корневой)        │
│            ├─ Суп                   │
│            │   └─ Борщ              │
│            ├─ Мясо                  │
│            └─ ...                   │
│                                     │
│           [Отмена]  [Сохранить]     │
└─────────────────────────────────────┘
```

**Fields:**
- **Название** (required): TextBox, max 255 chars
- **Родитель** (optional): ComboBox с деревом продуктов

**Validation:**
- Название не пустое
- Название уникально среди siblings

---

### 4.2 Label Dialog (Create/Edit)

```
┌─────────────────────────────────────┐
│ Создать метку            [X]       │
├─────────────────────────────────────┤
│                                     │
│  Название: [________________]       │
│                                     │
│  Цвет:     [🔵][🟢][🟡][🟠][🔴]    │
│            [🟣][⚫][⚪][🟤][🩷]    │
│                                     │
│           [Отмена]  [Сохранить]     │
└─────────────────────────────────────┘
```

**Fields:**
- **Название** (required): TextBox, max 255 chars
- **Цвет** (required): Preset color palette (10 colors)

**Preset Colors:**
| Name | HEX |
|------|-----|
| Blue | #3498db |
| Green | #2ecc71 |
| Yellow | #f1c40f |
| Orange | #e67e22 |
| Red | #e74c3c |
| Purple | #9b59b6 |
| Black | #2c3e50 |
| White/Gray | #95a5a6 |
| Brown | #8b4513 |
| Pink | #ff69b4 |

---

### 4.3 Delete Confirmation Dialog

**For Product with items/children:**
```
┌─────────────────────────────────────┐
│ Удаление невозможно      [X]       │
├─────────────────────────────────────┤
│                                     │
│  ⚠️ Продукт "Мясо" нельзя удалить:  │
│                                     │
│  • Связано товаров: 23              │
│  • Дочерних продуктов: 2            │
│                                     │
│  Сначала переместите товары и       │
│  удалите дочерние продукты.         │
│                                     │
│                        [OK]         │
└─────────────────────────────────────┘
```

**For Label with items:**
```
┌─────────────────────────────────────┐
│ Удалить метку?           [X]       │
├─────────────────────────────────────┤
│                                     │
│  ⚠️ Метка "Папа доволен" назначена  │
│  на 8 товаров.                      │
│                                     │
│  При удалении метка будет снята     │
│  со всех товаров.                   │
│                                     │
│           [Отмена]  [Удалить]       │
└─────────────────────────────────────┘
```

---

## 5. Status Bar

**Режим "Продукты":**
```
Продуктов: 24 │ Товаров: 127 │ Выбрано: 3
```

**Режим "Метки":**
```
Меток: 6 │ Товаров с метками: 88 │ Без меток: 39 │ Выбрано: 3
```

---

## 6. Data Model Changes

### 6.1 Add Shop to Item

```csharp
public class Item : BaseEntity
{
    // ... existing fields ...

    /// <summary>
    /// Магазин-источник (для фильтрации)
    /// </summary>
    public string? Shop { get; set; }
}
```

### 6.2 DbContext Update

```csharp
modelBuilder.Entity<Item>(entity =>
{
    // ... existing config ...
    entity.Property(e => e.Shop).HasMaxLength(255);
    entity.HasIndex(e => e.Shop);
});
```

---

## 7. ViewModel Requirements

### 7.1 New Properties

```csharp
// Mode
[ObservableProperty] private bool _isProductsMode = true;
[ObservableProperty] private bool _isLabelsMode;

// Filters
[ObservableProperty] private string _selectedShopFilter = "Все";
[ObservableProperty] private bool _includeChildrenItems = true;
[ObservableProperty] private string _productSearchText = "";
[ObservableProperty] private string _itemSearchText = "";

// Selection
[ObservableProperty] private ProductTreeItemViewModel? _selectedProduct;
[ObservableProperty] private LabelViewModel? _selectedLabel;
public ObservableCollection<ItemViewModel> SelectedItems { get; }

// Collections
public ObservableCollection<ProductTreeItemViewModel> ProductTree { get; }
public ObservableCollection<LabelViewModel> Labels { get; }
public ObservableCollection<ItemViewModel> Items { get; }
public ObservableCollection<string> ShopFilters { get; }

// Statistics
[ObservableProperty] private int _totalProductsCount;
[ObservableProperty] private int _totalItemsCount;
[ObservableProperty] private int _totalLabelsCount;
[ObservableProperty] private int _itemsWithLabelsCount;
[ObservableProperty] private int _itemsWithoutLabelsCount;
[ObservableProperty] private int _selectedItemsCount;
```

### 7.2 Commands

```csharp
// CRUD
IRelayCommand AddProductCommand { get; }
IRelayCommand EditProductCommand { get; }
IRelayCommand DeleteProductCommand { get; }
IRelayCommand AddLabelCommand { get; }
IRelayCommand EditLabelCommand { get; }
IRelayCommand DeleteLabelCommand { get; }

// Actions
IRelayCommand RefreshCommand { get; }
IRelayCommand<ProductTreeItemViewModel> MoveItemsToProductCommand { get; }
IRelayCommand<LabelViewModel> AssignLabelCommand { get; }
IRelayCommand<LabelViewModel> RemoveLabelCommand { get; }
IRelayCommand RemoveAllLabelsCommand { get; }

// Drag & Drop
IRelayCommand<(ItemViewModel Item, ProductTreeItemViewModel Target)> DropItemOnProductCommand { get; }
IRelayCommand<(ProductTreeItemViewModel Source, ProductTreeItemViewModel Target)> DropProductOnProductCommand { get; }
```

---

## 8. Open Questions / Future Considerations

1. **Иконки продуктов** - можно ли выбирать иконку для продукта? (пока используем стандартные)

2. **Экспорт/Импорт** - нужен ли экспорт иерархии продуктов в JSON/CSV?

3. **Undo/Redo** - нужна ли отмена действий?

4. **Keyboard shortcuts** - Del для удаления, F2 для редактирования?

5. **Bulk operations** - "Назначить продукт всем товарам без продукта"?

---

## 9. Implementation Checklist

- [ ] Добавить поле `Shop` в `Item` entity
- [ ] Создать миграцию БД
- [ ] Создать `ProductTreeItemViewModel` с поддержкой иерархии
- [ ] Обновить `ItemViewModel` (добавить Shop, IsSelected)
- [ ] Обновить `LabelViewModel` (добавить ItemCount)
- [ ] Создать XAML для вкладки
- [ ] Реализовать TreeView с иерархией
- [ ] Реализовать ListView для меток
- [ ] Реализовать DataGrid с множественным выбором
- [ ] Реализовать Drag & Drop
- [ ] Создать диалоги (Product, Label, Delete confirmation)
- [ ] Реализовать контекстные меню
- [ ] Реализовать поиск и фильтрацию
- [ ] Реализовать сортировку
- [ ] Покрыть тестами
