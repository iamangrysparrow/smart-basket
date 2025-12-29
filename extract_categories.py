import json

# Исключаем маркетинговые/промо категории верхнего уровня
EXCLUDE_CATEGORY_IDS = {
    11372526,  # Больше билетиков за заказ
    11319371,  # Новый год
    11378741,  # Бритвы BIC к Новому году
    11379406,  # Забота о себе с «Дав»
    11256837,  # Без наценки на бренд
    62983,     # Спецпредложения Магнит
    7107,      # Приготовить дома (рецепты - дублирует товары)
    69086,     # Скидки
    11213812,  # Скидки недели
}

# Названия служебных/маркетинговых подкатегорий для исключения
EXCLUDE_NAMES = {
    "Все товары категории",
    "Все товары раздела",
    # Маркетинговые подкатегории
    "С Петелинкой на ужин",
    "Мандариновая сказка",
    "Идеально для запекания",
    "Залить кипятком",
    "Домашняя выпечка",
}

def extract_category(cat):
    """Извлекает категорию с children рекурсивно"""
    result = {"name": cat["name"]}

    if cat.get("children"):
        children = []
        for child in cat["children"]:
            # Пропускаем служебные категории по названию
            if child["name"] in EXCLUDE_NAMES:
                continue
            children.append(extract_category(child))
        if children:
            result["children"] = children

    return result

def main():
    with open("categories.json", "r", encoding="utf-8") as f:
        data = json.load(f)

    result = []

    for cat in data["categories"]:
        # Пропускаем маркетинговые категории
        if cat["id"] in EXCLUDE_CATEGORY_IDS:
            continue
        # Берём только корневые категории (depth: 0, parent_id: 0)
        if cat.get("parent_id") == 0:
            result.append(extract_category(cat))

    # Выводим в виде дерева в текстовый файл
    def print_tree(cats, level=0, file=None):
        prefix = "-" * (level + 1)
        for cat in cats:
            line = f"{prefix}{cat['name']}"
            if file:
                file.write(line + "\n")
            print(line)
            if "children" in cat:
                print_tree(cat["children"], level + 1, file)

    with open("product_categories.txt", "w", encoding="utf-8") as f:
        print_tree(result, file=f)

    print(f"\nСохранено в product_categories.txt")

if __name__ == "__main__":
    main()
