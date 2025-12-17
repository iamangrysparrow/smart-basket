using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartBasket.WPF.Logging;
using SmartBasket.WPF.Models;

namespace SmartBasket.WPF.ViewModels;

/// <summary>
/// ViewModel для окна логов с поддержкой:
/// - Мультивыбора источников
/// - Поиска с навигацией
/// - Копирования выделенных строк
/// </summary>
public partial class LogWindowViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;

    public LogWindowViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;

        // Подписываемся на изменение источников
        LogViewerSink.Instance.SourcesChanged += OnSourcesChanged;

        // Инициализируем источники
        RefreshAvailableSources();

        // Создаём отфильтрованное представление
        FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogEntries.Filter = LogEntryFilter;
    }

    #region Properties from MainViewModel

    public ObservableCollection<LogEntry> LogEntries => LogViewerSink.Instance.LogEntries;

    // Level filters - delegate to MainViewModel
    public bool ShowDebug
    {
        get => _mainViewModel.ShowDebug;
        set
        {
            _mainViewModel.ShowDebug = value;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public bool ShowInfo
    {
        get => _mainViewModel.ShowInfo;
        set
        {
            _mainViewModel.ShowInfo = value;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public bool ShowWarning
    {
        get => _mainViewModel.ShowWarning;
        set
        {
            _mainViewModel.ShowWarning = value;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public bool ShowError
    {
        get => _mainViewModel.ShowError;
        set
        {
            _mainViewModel.ShowError = value;
            OnPropertyChanged();
            RefreshFilter();
        }
    }

    public bool AutoScrollEnabled
    {
        get => _mainViewModel.AutoScrollEnabled;
        set
        {
            _mainViewModel.AutoScrollEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsProcessing => _mainViewModel.IsProcessing;
    public string StatusText => _mainViewModel.StatusText;

    #endregion

    #region Source Filter (Multi-select)

    /// <summary>
    /// Элемент списка источников
    /// </summary>
    public class SourceFilterItem : ObservableObject
    {
        private bool _isSelected;
        private readonly Action _onChanged;

        public string Name { get; }
        public bool IsSpecial { get; } // "Все" или "Нет источника"

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _onChanged();
                }
            }
        }

        public SourceFilterItem(string name, bool isSpecial, Action onChanged)
        {
            Name = name;
            IsSpecial = isSpecial;
            _onChanged = onChanged;
            _isSelected = name == "Все"; // "Все" выбран по умолчанию
        }
    }

    /// <summary>
    /// Список источников с чекбоксами
    /// </summary>
    public ObservableCollection<SourceFilterItem> SourceFilters { get; } = new();

    /// <summary>
    /// Текст для отображения выбранных источников
    /// </summary>
    [ObservableProperty]
    private string _selectedSourcesText = "Все";

    private void RefreshAvailableSources()
    {
        SourceFilters.Clear();

        // Специальные элементы
        SourceFilters.Add(new SourceFilterItem("Все", true, OnSourceFilterChanged));
        SourceFilters.Add(new SourceFilterItem("Нет источника", true, OnSourceFilterChanged));

        // Динамические источники
        foreach (var source in LogViewerSink.Instance.KnownSources.OrderBy(s => s))
        {
            SourceFilters.Add(new SourceFilterItem(source, false, OnSourceFilterChanged));
        }
    }

    private void OnSourcesChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            foreach (var source in LogViewerSink.Instance.KnownSources)
            {
                if (!SourceFilters.Any(s => s.Name == source))
                {
                    SourceFilters.Add(new SourceFilterItem(source, false, OnSourceFilterChanged));
                }
            }
        });
    }

    private void OnSourceFilterChanged()
    {
        // Обновляем текст
        var selected = SourceFilters.Where(s => s.IsSelected && s.Name != "Все").ToList();

        if (SourceFilters.FirstOrDefault(s => s.Name == "Все")?.IsSelected == true || selected.Count == 0)
        {
            SelectedSourcesText = "Все";
        }
        else if (selected.Count == 1)
        {
            SelectedSourcesText = selected[0].Name;
        }
        else
        {
            SelectedSourcesText = $"{selected.Count} выбрано";
        }

        RefreshFilter();
    }

    [RelayCommand]
    private void ResetSourceFilter()
    {
        foreach (var item in SourceFilters)
        {
            item.IsSelected = item.Name == "Все";
        }
    }

    #endregion

    #region Search

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _searchMatchCount;

    [ObservableProperty]
    private int _currentMatchIndex;

    [ObservableProperty]
    private string _searchStatus = string.Empty;

    private List<LogEntry> _searchMatches = new();

    partial void OnSearchTextChanged(string value)
    {
        PerformSearch();
    }

    private void PerformSearch()
    {
        _searchMatches.Clear();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchMatchCount = 0;
            CurrentMatchIndex = 0;
            SearchStatus = string.Empty;
            return;
        }

        var searchLower = SearchText.ToLowerInvariant();

        foreach (var entry in FilteredLogEntries.Cast<LogEntry>())
        {
            if (entry.Message.ToLowerInvariant().Contains(searchLower))
            {
                _searchMatches.Add(entry);
            }
        }

        SearchMatchCount = _searchMatches.Count;
        CurrentMatchIndex = SearchMatchCount > 0 ? 1 : 0;
        UpdateSearchStatus();

        // Автоматически переходим к первому совпадению
        if (SearchMatchCount > 0)
        {
            CurrentSearchMatch = _searchMatches[0];
        }
    }

    private void UpdateSearchStatus()
    {
        if (SearchMatchCount == 0)
        {
            SearchStatus = string.IsNullOrWhiteSpace(SearchText) ? "" : "Не найдено";
        }
        else
        {
            SearchStatus = $"{CurrentMatchIndex} / {SearchMatchCount}";
        }
    }

    /// <summary>
    /// Текущее совпадение для прокрутки к нему
    /// </summary>
    [ObservableProperty]
    private LogEntry? _currentSearchMatch;

    [RelayCommand]
    private void SearchFirst()
    {
        if (SearchMatchCount > 0)
        {
            CurrentMatchIndex = 1;
            CurrentSearchMatch = _searchMatches[0];
            UpdateSearchStatus();
        }
    }

    [RelayCommand]
    private void SearchPrevious()
    {
        if (SearchMatchCount > 0)
        {
            CurrentMatchIndex = CurrentMatchIndex > 1 ? CurrentMatchIndex - 1 : SearchMatchCount;
            CurrentSearchMatch = _searchMatches[CurrentMatchIndex - 1];
            UpdateSearchStatus();
        }
    }

    [RelayCommand]
    private void SearchNext()
    {
        if (SearchMatchCount > 0)
        {
            CurrentMatchIndex = CurrentMatchIndex < SearchMatchCount ? CurrentMatchIndex + 1 : 1;
            CurrentSearchMatch = _searchMatches[CurrentMatchIndex - 1];
            UpdateSearchStatus();
        }
    }

    [RelayCommand]
    private void SearchLast()
    {
        if (SearchMatchCount > 0)
        {
            CurrentMatchIndex = SearchMatchCount;
            CurrentSearchMatch = _searchMatches[^1];
            UpdateSearchStatus();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    #endregion

    #region Filtering

    public ICollectionView FilteredLogEntries { get; }

    private void RefreshFilter()
    {
        FilteredLogEntries.Refresh();
        PerformSearch(); // Обновляем поиск после фильтрации
    }

    private bool LogEntryFilter(object item)
    {
        if (item is not LogEntry entry)
            return true;

        // Фильтр по уровню
        var levelMatch = entry.Level switch
        {
            LogLevel.Debug => ShowDebug,
            LogLevel.Info => ShowInfo,
            LogLevel.Warning => ShowWarning,
            LogLevel.Error => ShowError,
            _ => true
        };

        if (!levelMatch)
            return false;

        // Фильтр по источнику (мультивыбор)
        var allSelected = SourceFilters.FirstOrDefault(s => s.Name == "Все")?.IsSelected ?? false;
        if (allSelected)
            return true;

        var noSourceSelected = SourceFilters.FirstOrDefault(s => s.Name == "Нет источника")?.IsSelected ?? false;
        var selectedSources = SourceFilters.Where(s => s.IsSelected && !s.IsSpecial).Select(s => s.Name).ToHashSet();

        if (string.IsNullOrEmpty(entry.Source))
        {
            return noSourceSelected;
        }

        return selectedSources.Contains(entry.Source);
    }

    #endregion

    #region Copy Commands

    [RelayCommand]
    private void CopyLog()
    {
        _mainViewModel.CopyLogCommand.Execute(null);
    }

    [RelayCommand]
    private void CopySelectedLog(object? parameter)
    {
        _mainViewModel.CopySelectedLogCommand.Execute(parameter);
    }

    [RelayCommand]
    private void ClearLog()
    {
        _mainViewModel.ClearLogCommand.Execute(null);
    }

    #endregion

    public void Cleanup()
    {
        LogViewerSink.Instance.SourcesChanged -= OnSourcesChanged;
    }
}
