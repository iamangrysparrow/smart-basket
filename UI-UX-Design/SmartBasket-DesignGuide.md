# Smart Basket — Design Guide

> Дизайн-система для WPF приложения учёта покупок

---

## 1. Философия дизайна

### Кто пользователь
Занятые семейные люди 30–50 лет, которые еженедельно закупают продукты. Они не хотят разбираться в интерфейсе — хотят **быстро увидеть** и **быстро сделать**.

### Принципы

| Принцип | Что это значит |
|---------|----------------|
| **Невидимый инструмент** | Интерфейс не должен привлекать внимание к себе. Фокус на данных, не на UI |
| **Иерархия важности** | Глаз сразу находит главное. Второстепенное — тише, но доступно |
| **Спокойная плотность** | Много информации, но без ощущения тесноты. Как в VS Code |
| **Консистентность** | Одинаковые действия выглядят одинаково везде |

### Референс
Visual Studio / VS Code — профессиональные IDE с высокой информационной плотностью, но без визуального шума.

---

## 2. Цветовая система

### 2.1. Структура палитры

```
┌─────────────────────────────────────────────────────────┐
│  BACKGROUND LAYERS (фоны — создают глубину)            │
│  ├── Background.Base      — основной фон окна          │
│  ├── Background.Layer1    — панели, карточки           │
│  ├── Background.Layer2    — вложенные элементы         │
│  └── Background.Elevated  — всплывающие окна, dropdown │
├─────────────────────────────────────────────────────────┤
│  FOREGROUND (текст и иконки)                           │
│  ├── Foreground.Primary   — основной текст             │
│  ├── Foreground.Secondary — второстепенный текст       │
│  ├── Foreground.Tertiary  — подсказки, disabled        │
│  └── Foreground.Inverse   — текст на акцентном фоне    │
├─────────────────────────────────────────────────────────┤
│  BORDER (границы)                                       │
│  ├── Border.Default       — обычные границы            │
│  ├── Border.Strong        — акцентированные границы    │
│  └── Border.Subtle        — едва заметные разделители  │
├─────────────────────────────────────────────────────────┤
│  ACCENT (акцентный цвет — фиолетовый)                  │
│  ├── Accent.Default       — кнопки, ссылки, выделение  │
│  ├── Accent.Hover         — при наведении              │
│  ├── Accent.Pressed       — при нажатии                │
│  ├── Accent.Subtle        — фон выделенной строки      │
│  └── Accent.Muted         — неактивный акцент          │
├─────────────────────────────────────────────────────────┤
│  SEMANTIC (смысловые цвета)                            │
│  ├── Success              — успех, доход, положительное│
│  ├── Warning              — предупреждение             │
│  ├── Error                — ошибка, расход             │
│  └── Info                 — информация                 │
├─────────────────────────────────────────────────────────┤
│  CATEGORY (категории товаров — приглушённые)           │
│  └── 8-10 приглушённых оттенков для меток              │
└─────────────────────────────────────────────────────────┘
```

### 2.2. Светлая тема (Light)

```
╔═══════════════════════════════════════════════════════════════╗
║  LIGHT THEME                                                   ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  BACKGROUNDS                                                   ║
║  ──────────────────────────────────────────────────────────── ║
║  Base .............. #FFFFFF  (белый — основной фон)          ║
║  Layer1 ............ #F8F8F8  (чуть серее — панели)           ║
║  Layer2 ............ #F0F0F0  (ещё серее — вложенные)         ║
║  Elevated .......... #FFFFFF  (белый + тень для popup)        ║
║                                                                ║
║  FOREGROUND                                                    ║
║  ──────────────────────────────────────────────────────────── ║
║  Primary ........... #1E1E1E  (почти чёрный)                  ║
║  Secondary ......... #5C5C5C  (тёмно-серый)                   ║
║  Tertiary .......... #9E9E9E  (серый)                         ║
║  Inverse ........... #FFFFFF  (белый на акценте)              ║
║                                                                ║
║  BORDER                                                        ║
║  ──────────────────────────────────────────────────────────── ║
║  Default ........... #E0E0E0  (светло-серый)                  ║
║  Strong ............ #BDBDBD  (серый)                         ║
║  Subtle ............ #EEEEEE  (едва видимый)                  ║
║                                                                ║
║  ACCENT (фиолетовый)                                          ║
║  ──────────────────────────────────────────────────────────── ║
║  Default ........... #7C4DFF  (основной фиолетовый)           ║
║  Hover ............. #651FFF  (темнее при наведении)          ║
║  Pressed ........... #5414CC  (ещё темнее)                    ║
║  Subtle ............ #EDE7F6  (очень светлый — фон строки)    ║
║  Muted ............. #B39DDB  (приглушённый)                  ║
║                                                                ║
║  SEMANTIC                                                      ║
║  ──────────────────────────────────────────────────────────── ║
║  Success ........... #4CAF50  (зелёный)                       ║
║  Success.Subtle .... #E8F5E9  (фон)                           ║
║  Warning ........... #FF9800  (оранжевый)                     ║
║  Warning.Subtle .... #FFF3E0  (фон)                           ║
║  Error ............. #F44336  (красный)                       ║
║  Error.Subtle ...... #FFEBEE  (фон)                           ║
║  Info .............. #2196F3  (синий)                         ║
║  Info.Subtle ....... #E3F2FD  (фон)                           ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 2.3. Тёмная тема (Dark)

**Важно:** Тёмная тема — это НЕ инверсия светлой. Это отдельная палитра с другими правилами:
- Контрасты ниже (глаза устают от яркого текста на тёмном)
- Акценты светлее (чтобы выделяться на тёмном фоне)
- Больше уровней серого (для глубины)

```
╔═══════════════════════════════════════════════════════════════╗
║  DARK THEME                                                    ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  BACKGROUNDS                                                   ║
║  ──────────────────────────────────────────────────────────── ║
║  Base .............. #1E1E1E  (тёмно-серый — как VS Code)     ║
║  Layer1 ............ #252526  (чуть светлее — панели)         ║
║  Layer2 ............ #2D2D30  (ещё светлее — вложенные)       ║
║  Elevated .......... #3C3C3C  (popup, dropdown)               ║
║                                                                ║
║  FOREGROUND                                                    ║
║  ──────────────────────────────────────────────────────────── ║
║  Primary ........... #E0E0E0  (светло-серый, НЕ белый!)       ║
║  Secondary ......... #A0A0A0  (серый)                         ║
║  Tertiary .......... #6E6E6E  (тёмно-серый)                   ║
║  Inverse ........... #1E1E1E  (тёмный на светлом акценте)     ║
║                                                                ║
║  BORDER                                                        ║
║  ──────────────────────────────────────────────────────────── ║
║  Default ........... #3C3C3C  (тёмно-серый)                   ║
║  Strong ............ #505050  (серый)                         ║
║  Subtle ............ #2D2D30  (едва видимый)                  ║
║                                                                ║
║  ACCENT (фиолетовый — светлее чем в Light!)                   ║
║  ──────────────────────────────────────────────────────────── ║
║  Default ........... #B388FF  (светлый фиолетовый)            ║
║  Hover ............. #D1C4E9  (ещё светлее)                   ║
║  Pressed ........... #9575CD  (темнее)                        ║
║  Subtle ............ #332940  (тёмный фиолетовый — фон)       ║
║  Muted ............. #7E57C2  (приглушённый)                  ║
║                                                                ║
║  SEMANTIC                                                      ║
║  ──────────────────────────────────────────────────────────── ║
║  Success ........... #81C784  (светло-зелёный)                ║
║  Success.Subtle .... #1B3D1F  (тёмный фон)                    ║
║  Warning ........... #FFB74D  (светло-оранжевый)              ║
║  Warning.Subtle .... #3D2E14  (тёмный фон)                    ║
║  Error ............. #E57373  (светло-красный)                ║
║  Error.Subtle ...... #3D1F1F  (тёмный фон)                    ║
║  Info .............. #64B5F6  (светло-синий)                  ║
║  Info.Subtle ....... #1A2D3D  (тёмный фон)                    ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 2.4. Цвета категорий (метки товаров)

Проблема текущих меток: слишком яркие, кричащие. Решение: **приглушённые пастельные тона**.

```
LIGHT THEME — фон метки / текст метки
──────────────────────────────────────
Bakery .......... #FFF3E0 / #E65100   (персиковый)
Dairy ........... #E3F2FD / #1565C0   (голубой)
Meat ............ #FFEBEE / #C62828   (розовый)
Vegetables ...... #E8F5E9 / #2E7D32   (мятный)
Fruits .......... #FFF8E1 / #F57F17   (лимонный)
Drinks .......... #E0F7FA / #00838F   (бирюзовый)
Grocery ......... #F3E5F5 / #7B1FA2   (лавандовый)
Frozen .......... #E8EAF6 / #3949AB   (индиго)
Household ....... #EFEBE9 / #5D4037   (кофейный)
Other ........... #FAFAFA / #616161   (серый)

DARK THEME — фон метки / текст метки
──────────────────────────────────────
Bakery .......... #3D2814 / #FFB74D
Dairy ........... #1A2D3D / #64B5F6
Meat ............ #3D1F1F / #E57373
Vegetables ...... #1B3D1F / #81C784
Fruits .......... #3D3314 / #FFD54F
Drinks .......... #14333D / #4DD0E1
Grocery ......... #2D1B3D / #CE93D8
Frozen .......... #1A1F3D / #9FA8DA
Household ....... #2D2519 / #BCAAA4
Other ........... #2D2D30 / #9E9E9E
```

---

## 3. Типографика

### Шрифты

```
Основной шрифт:     Segoe UI (системный Windows)
Моноширинный:       Cascadia Code / Consolas (для чисел в таблицах)
```

### Размеры

```
╔════════════════════════════════════════════════════════════╗
║  TYPOGRAPHY SCALE                                           ║
╠════════════════════════════════════════════════════════════╣
║                                                             ║
║  Caption ........... 11px / Regular   — подписи, хинты     ║
║  Body .............. 13px / Regular   — основной текст     ║
║  Body.Strong ....... 13px / SemiBold  — акцентированный    ║
║  Subtitle .......... 14px / SemiBold  — заголовки секций   ║
║  Title ............. 18px / SemiBold  — заголовки панелей  ║
║  Header ............ 24px / SemiBold  — заголовки окон     ║
║                                                             ║
║  Tabular Numbers ... Cascadia Code    — цены, суммы        ║
║                                                             ║
╚════════════════════════════════════════════════════════════╝
```

### Правила использования

| Элемент | Стиль | Цвет |
|---------|-------|------|
| Название товара | Body | Foreground.Primary |
| Цена за единицу | Caption + Tabular | Foreground.Secondary |
| Итоговая сумма | Body.Strong + Tabular | Foreground.Primary |
| Магазин в списке | Subtitle | Foreground.Primary |
| Дата чека | Caption | Foreground.Secondary |
| "Не задана" (пустая категория) | Caption + Italic | Foreground.Tertiary |

---

## 4. Сетка и отступы

### Базовая единица: 4px

Все отступы кратны 4px. Это создаёт ритм и порядок.

```
╔═══════════════════════════════════════════════════════════════╗
║  SPACING SCALE                                                 ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  XS ............. 4px    — между иконкой и текстом            ║
║  S .............. 8px    — внутри компактных элементов        ║
║  M .............. 12px   — стандартный padding                ║
║  L .............. 16px   — между группами                     ║
║  XL ............. 24px   — между секциями                     ║
║  XXL ............ 32px   — крупные отступы                    ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### Применение

```
┌──────────────────────────────────────────────────────────┐
│ ← L(16) →                                      ← L(16) → │
│          ┌────────────────────────────────────┐          │
│          │  Toolbar                           │          │
│          │  padding: M(12)                    │          │
│          └────────────────────────────────────┘          │
│                        ↕ S(8)                            │
│          ┌────────────────────────────────────┐          │
│          │  Content                           │          │
│          │  padding: L(16)                    │          │
│          │                                    │          │
│          │  ┌─────────┐ ←S(8)→ ┌─────────┐   │          │
│          │  │  Card   │        │  Card   │   │          │
│          │  │  p: M   │        │  p: M   │   │          │
│          │  └─────────┘        └─────────┘   │          │
│          │                                    │          │
│          └────────────────────────────────────┘          │
└──────────────────────────────────────────────────────────┘
```

---

## 5. Компоненты

### 5.1. Кнопки

```
╔═══════════════════════════════════════════════════════════════╗
║  BUTTONS                                                       ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  PRIMARY (главное действие — одна на экран)                   ║
║  ─────────────────────────────────────────────                ║
║  Background:    Accent.Default                                 ║
║  Foreground:    Foreground.Inverse                            ║
║  Border:        none                                           ║
║  Padding:       12px 20px                                      ║
║  BorderRadius:  4px                                            ║
║  Hover:         Accent.Hover                                   ║
║  Pressed:       Accent.Pressed                                 ║
║                                                                ║
║  SECONDARY (второстепенные действия)                          ║
║  ─────────────────────────────────────────────                ║
║  Background:    transparent                                    ║
║  Foreground:    Accent.Default                                 ║
║  Border:        1px Accent.Default                             ║
║  Hover:         Background: Accent.Subtle                      ║
║                                                                ║
║  GHOST (toolbar, иконки)                                       ║
║  ─────────────────────────────────────────────                ║
║  Background:    transparent                                    ║
║  Foreground:    Foreground.Secondary                          ║
║  Border:        none                                           ║
║  Hover:         Background: Background.Layer2                  ║
║                                                                ║
║  DANGER (удаление)                                            ║
║  ─────────────────────────────────────────────                ║
║  Background:    transparent                                    ║
║  Foreground:    Error                                         ║
║  Border:        1px Error                                      ║
║  Hover:         Background: Error.Subtle                       ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 5.2. Панели и карточки

```
╔═══════════════════════════════════════════════════════════════╗
║  PANELS                                                        ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  SIDEBAR (левая панель — список чеков)                        ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Layer1                              ║
║  Border-right:  1px Border.Default                             ║
║  Width:         280-320px (фиксированная)                     ║
║                                                                ║
║  MAIN CONTENT (правая панель — детали)                        ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Base                                ║
║                                                                ║
║  CARD (карточка товара, если нужна)                           ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Layer1                              ║
║  Border:        1px Border.Subtle                              ║
║  BorderRadius:  6px                                            ║
║  Shadow:        none (плоский дизайн)                         ║
║                                                                ║
║  MODAL / DIALOG                                                ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Elevated                            ║
║  Border:        1px Border.Default                             ║
║  Shadow:        0 8px 32px rgba(0,0,0,0.15)                   ║
║  BorderRadius:  8px                                            ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 5.3. Списки и таблицы

```
╔═══════════════════════════════════════════════════════════════╗
║  LISTS                                                         ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  LIST ITEM (чек в списке слева)                               ║
║  ─────────────────────────────────────────────                ║
║  Height:        60-72px                                        ║
║  Padding:       12px 16px                                      ║
║  Hover:         Background.Layer2                              ║
║  Selected:      Background: Accent.Subtle                      ║
║                 Border-left: 3px Accent.Default                ║
║                                                                ║
║  TABLE ROW (товар в чеке)                                     ║
║  ─────────────────────────────────────────────                ║
║  Height:        40-48px                                        ║
║  Padding:       8px 16px                                       ║
║  Border-bottom: 1px Border.Subtle                              ║
║  Hover:         Background.Layer1                              ║
║  Alternate:     не использовать (zebra stripes устарели)      ║
║                                                                ║
║  TABLE HEADER                                                  ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Layer1                              ║
║  Foreground:    Foreground.Secondary                          ║
║  Font:          Caption + SemiBold + UPPERCASE                ║
║  Border-bottom: 2px Border.Strong                              ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 5.4. Метки (Tags)

```
╔═══════════════════════════════════════════════════════════════╗
║  TAGS (категории товаров)                                      ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  Принцип: метки должны быть ТИХИМИ, не кричащими              ║
║                                                                ║
║  Height:        22-24px                                        ║
║  Padding:       2px 8px                                        ║
║  BorderRadius:  4px                                            ║
║  Font:          Caption (11px)                                 ║
║  Background:    Category.Subtle (приглушённый)                ║
║  Foreground:    Category.Text (читаемый, но не яркий)         ║
║  Border:        none                                           ║
║                                                                ║
║  ПЛОХО:   ┌────────────┐  яркий фон, белый текст              ║
║           │ 🔴 Мясо    │  кричит, отвлекает                   ║
║           └────────────┘                                       ║
║                                                                ║
║  ХОРОШО:  ┌────────────┐  приглушённый фон, тёмный текст      ║
║           │   Мясо     │  видно, но не отвлекает              ║
║           └────────────┘                                       ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

### 5.5. Toolbar (верхняя панель)

```
╔═══════════════════════════════════════════════════════════════╗
║  TOOLBAR                                                       ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  Проблема: сейчас слишком много визуального шума              ║
║                                                                ║
║  Решение:                                                      ║
║  ─────────────────────────────────────────────                ║
║  Background:    Background.Layer1                              ║
║  Height:        48px                                           ║
║  Border-bottom: 1px Border.Default                             ║
║  Padding:       0 16px                                         ║
║                                                                ║
║  Группировка элементов:                                        ║
║  ┌──────────────────────────────────────────────────────────┐ ║
║  │ [Logo]  │  Tab Tab Tab  │        │ Filter │ Search │ ⚙️  │ ║
║  │         │  ─────        │        │        │        │     │ ║
║  │ fixed   │  navigation   │ spacer │      actions          │ ║
║  └──────────────────────────────────────────────────────────┘ ║
║                                                                ║
║  Tabs:                                                         ║
║  - Ghost buttons                                               ║
║  - Active: Foreground.Primary + border-bottom 2px Accent      ║
║  - Inactive: Foreground.Secondary                             ║
║                                                                ║
║  Разделители:                                                  ║
║  - Вертикальная линия 1px Border.Subtle между группами        ║
║  - Или просто пространство 24px                               ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

---

## 6. Иконки

### Стиль
- **Outline** (контурные), не filled
- **Толщина линии:** 1.5px
- **Размеры:** 16px (inline), 20px (buttons), 24px (headers)

### Рекомендуемые наборы
- **Fluent UI System Icons** (Microsoft, бесплатно)
- **Phosphor Icons** (бесплатно)
- **Feather Icons** (бесплатно)

### Цвета иконок
```
В тексте:         Foreground.Secondary (не Primary!)
В кнопках:        наследует цвет кнопки
Акцентные:        Accent.Default (редко, для важного)
Semantic:         Success/Warning/Error (статусы)
```

---

## 7. Состояния интерактивных элементов

```
╔═══════════════════════════════════════════════════════════════╗
║  INTERACTIVE STATES                                            ║
╠═══════════════════════════════════════════════════════════════╣
║                                                                ║
║  Default → Hover → Pressed → Focused → Disabled               ║
║                                                                ║
║  HOVER                                                         ║
║  - Background становится на 1 уровень светлее/темнее          ║
║  - Transition: 100-150ms ease-out                             ║
║  - Курсор: pointer                                            ║
║                                                                ║
║  PRESSED                                                       ║
║  - Background ещё на 1 уровень                                ║
║  - Без scale/transform (не мобильное приложение)              ║
║                                                                ║
║  FOCUSED (Tab navigation)                                      ║
║  - Outline: 2px Accent.Default                                ║
║  - Offset: 2px                                                ║
║                                                                ║
║  DISABLED                                                      ║
║  - Opacity: 0.5                                               ║
║  - Курсор: not-allowed                                        ║
║  - НЕ менять цвета — только opacity                           ║
║                                                                ║
╚═══════════════════════════════════════════════════════════════╝
```

---

## 8. Специфичные экраны

### 8.1. Список чеков (левая панель)

```
┌─────────────────────────────────────┐
│  🔍 Поиск...                        │  ← Search field
├─────────────────────────────────────┤
│                                     │
│  ┌─────────────────────────────────┐│
│  │▌ АШАН                    6,130 ₽││  ← Selected (accent left border)
│  │  03.12.2025 · 30 items         ││
│  └─────────────────────────────────┘│
│  ┌─────────────────────────────────┐│
│  │  АШАН                    5,805 ₽││  ← Normal
│  │  26.11.2025 · 32 items         ││
│  └─────────────────────────────────┘│
│  ┌─────────────────────────────────┐│
│  │  Магнит                  4,183 ₽││
│  │  06.09.2025 · 22 items         ││
│  └─────────────────────────────────┘│
│                                     │
└─────────────────────────────────────┘

Правила:
- Магазин: Body.Strong, Primary
- Сумма: Body.Strong, Primary, выравнивание справа
- Дата + items: Caption, Secondary
- Нет рамок у элементов — только hover/selected фон
- Разделитель: Border.Subtle или просто отступ
```

### 8.2. Детали чека (правая панель)

```
┌──────────────────────────────────────────────────────────────┐
│  АШАН  03.12.2025  #H0325S764114                    6,130 ₽  │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  НАЗВАНИЕ                           КОЛ-ВО    ЦЕНА    СУММА │
│  ──────────────────────────────────────────────────────────  │
│  Морковь мытая                      0.478 кг   59.99  28.68  │
│  Не задана                                                   │
│  ────────────────────────────────────────────────────────    │
│  Шампиньоны АШАН Красная птица      1 г      129.99  129.99  │
│  Грибы                                                       │
│  ────────────────────────────────────────────────────────    │
│  Борщ АШАН Красная птица с кур...   4 шт     109.99  439.96  │
│  Не задана                                                   │
│                                                              │
└──────────────────────────────────────────────────────────────┘

Правила:
- Заголовок: магазин (Title), дата (Body, Secondary), номер (Caption, Tertiary)
- Сумма чека: Title, Primary, справа
- Заголовки колонок: Caption, Secondary, UPPERCASE
- Название товара: Body, Primary
- Категория под названием: Caption, Tertiary (или метка)
- Числа: Tabular font, выравнивание по правому краю
- "Не задана": Caption, Tertiary, Italic — показывает что нужно внимание
```

### 8.3. Окно настроек

Текущее окно настроек хорошее! Оставить структуру, применить новую палитру.

---

## 9. Анимации и переходы

### Принцип: быстро и незаметно

```
Hover эффекты:     100-150ms  ease-out
Раскрытие панели:  200-250ms  ease-out
Появление modal:   150ms      ease-out
Смена темы:        300ms      ease-in-out (все цвета одновременно)
```

### Что НЕ анимировать
- Смену данных в таблицах
- Скролл
- Набор текста

---

## 10. План внедрения

### Фаза 1: Фундамент (1-2 дня)
```
□ Создать ResourceDictionary с цветами как DynamicResource
  - Colors.Light.xaml
  - Colors.Dark.xaml
  - Colors.Shared.xaml (семантические алиасы)
  
□ Создать ResourceDictionary с размерами
  - Sizes.xaml (spacing, font sizes)
  
□ Настроить переключение тем
  - Сменить MergedDictionary в runtime
```

### Фаза 2: Базовые стили (2-3 дня)
```
□ Переопределить стили базовых контролов:
  - ButtonStyles.xaml
  - TextBlockStyles.xaml
  - TextBoxStyles.xaml
  - ListBoxStyles.xaml
  - ComboBoxStyles.xaml
  
□ Создать стили для переиспользуемых компонентов:
  - TagStyle.xaml
  - CardStyle.xaml
```

### Фаза 3: Экраны (3-5 дней)
```
□ Toolbar
  - Убрать лишнее, сгруппировать элементы
  - Применить новые стили кнопок
  
□ Список чеков (левая панель)
  - Новый ItemTemplate
  - Selected state с accent border
  
□ Детали чека (правая панель)
  - Новый layout
  - Tabular numbers для сумм
  
□ Окно продуктов
  - TreeView styling
  - Новые метки
  
□ Окно настроек
  - Применить новую палитру (минимальные изменения)
```

### Фаза 4: Полировка (1-2 дня)
```
□ Проверить все состояния (hover, disabled, focus)
□ Проверить тёмную тему
□ Проверить на разных DPI
□ Убрать визуальные артефакты
```

---

## 11. Примеры XAML

### Определение цветов

```xml
<!-- Colors.Light.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Background -->
    <Color x:Key="BackgroundBaseColor">#FFFFFF</Color>
    <Color x:Key="BackgroundLayer1Color">#F8F8F8</Color>
    <Color x:Key="BackgroundLayer2Color">#F0F0F0</Color>
    <Color x:Key="BackgroundElevatedColor">#FFFFFF</Color>
    
    <!-- Foreground -->
    <Color x:Key="ForegroundPrimaryColor">#1E1E1E</Color>
    <Color x:Key="ForegroundSecondaryColor">#5C5C5C</Color>
    <Color x:Key="ForegroundTertiaryColor">#9E9E9E</Color>
    <Color x:Key="ForegroundInverseColor">#FFFFFF</Color>
    
    <!-- Accent -->
    <Color x:Key="AccentDefaultColor">#7C4DFF</Color>
    <Color x:Key="AccentHoverColor">#651FFF</Color>
    <Color x:Key="AccentPressedColor">#5414CC</Color>
    <Color x:Key="AccentSubtleColor">#EDE7F6</Color>
    
    <!-- Border -->
    <Color x:Key="BorderDefaultColor">#E0E0E0</Color>
    <Color x:Key="BorderStrongColor">#BDBDBD</Color>
    <Color x:Key="BorderSubtleColor">#EEEEEE</Color>
    
    <!-- Semantic -->
    <Color x:Key="SuccessColor">#4CAF50</Color>
    <Color x:Key="WarningColor">#FF9800</Color>
    <Color x:Key="ErrorColor">#F44336</Color>
    
</ResourceDictionary>
```

### Семантические кисти

```xml
<!-- Colors.Shared.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Brushes that reference theme colors -->
    <SolidColorBrush x:Key="BackgroundBaseBrush" Color="{DynamicResource BackgroundBaseColor}"/>
    <SolidColorBrush x:Key="BackgroundLayer1Brush" Color="{DynamicResource BackgroundLayer1Color}"/>
    <SolidColorBrush x:Key="ForegroundPrimaryBrush" Color="{DynamicResource ForegroundPrimaryColor}"/>
    <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentDefaultColor}"/>
    <!-- ... -->
    
</ResourceDictionary>
```

### Стиль кнопки

```xml
<!-- ButtonStyles.xaml -->
<Style x:Key="PrimaryButton" TargetType="Button">
    <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
    <Setter Property="Foreground" Value="{DynamicResource ForegroundInverseBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="20,12"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}"
                        CornerRadius="4"
                        Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Center" 
                                      VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Background" 
                                Value="{DynamicResource AccentHoverBrush}"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter Property="Background" 
                                Value="{DynamicResource AccentPressedBrush}"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.5"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

---

## 12. Чеклист перед релизом

```
□ Все тексты читаемы (контраст минимум 4.5:1)
□ Все интерактивные элементы имеют hover state
□ Focus visible для keyboard navigation
□ Тёмная тема проверена на всех экранах
□ Нет "сирот" — элементов со старыми стилями
□ Размеры окон адекватны при разных DPI
□ Все иконки в едином стиле
```

---

## Приложение А: Быстрый референс цветов

### Когда какой цвет использовать

| Ситуация | Light Theme | Dark Theme |
|----------|-------------|------------|
| Фон окна | #FFFFFF | #1E1E1E |
| Фон панели | #F8F8F8 | #252526 |
| Основной текст | #1E1E1E | #E0E0E0 |
| Второстепенный текст | #5C5C5C | #A0A0A0 |
| Подсказки/disabled | #9E9E9E | #6E6E6E |
| Акцент (кнопки) | #7C4DFF | #B388FF |
| Выделенная строка | #EDE7F6 | #332940 |
| Граница | #E0E0E0 | #3C3C3C |
| Ошибка | #F44336 | #E57373 |
| Успех | #4CAF50 | #81C784 |

---

*Документ создан для проекта Smart Basket. Версия 1.0*
