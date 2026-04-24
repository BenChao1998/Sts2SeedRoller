using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private RelayCommand? _addAct1EventPoolFilterCommand;
    private RelayCommand? _removeAct1EventPoolFilterCommand;
    private RelayCommand? _addAct2EventPoolFilterCommand;
    private RelayCommand? _removeAct2EventPoolFilterCommand;
    private RelayCommand? _addAct3EventPoolFilterCommand;
    private RelayCommand? _removeAct3EventPoolFilterCommand;
    private RelayCommand? _addSharedRelicPoolFilterCommand;
    private RelayCommand? _removeSharedRelicPoolFilterCommand;
    private RelayCommand? _addPlayerRelicPoolFilterCommand;
    private RelayCommand? _removePlayerRelicPoolFilterCommand;
    private bool _includePoolFilter;
    private string _poolFilterSummary = "分析池：关闭";
    private string _act1EventPoolCatalogFilter = string.Empty;
    private string _act2EventPoolCatalogFilter = string.Empty;
    private string _act3EventPoolCatalogFilter = string.Empty;
    private string _sharedRelicPoolCatalogFilter = string.Empty;
    private string _playerRelicPoolCatalogFilter = string.Empty;
    private CatalogItem? _selectedAct1EventPoolCatalogItem;
    private CatalogItem? _selectedAct2EventPoolCatalogItem;
    private CatalogItem? _selectedAct3EventPoolCatalogItem;
    private CatalogItem? _selectedSharedRelicPoolCatalogItem;
    private CatalogItem? _selectedPlayerRelicPoolCatalogItem;
    private IReadOnlyList<CatalogItem> _poolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _poolRelicCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct1EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct2EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct3EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredSharedRelicPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredPlayerRelicPoolCatalog = Array.Empty<CatalogItem>();

    public ObservableCollection<FilterChipViewModel> Act1EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act2EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act3EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> SharedRelicPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> PlayerRelicPoolFilterChips { get; } = new();

    public IEnumerable<CatalogItem> Act1EventPoolCatalogView => _filteredAct1EventPoolCatalog;

    public IEnumerable<CatalogItem> Act2EventPoolCatalogView => _filteredAct2EventPoolCatalog;

    public IEnumerable<CatalogItem> Act3EventPoolCatalogView => _filteredAct3EventPoolCatalog;

    public IEnumerable<CatalogItem> SharedRelicPoolCatalogView => _filteredSharedRelicPoolCatalog;

    public IEnumerable<CatalogItem> PlayerRelicPoolCatalogView => _filteredPlayerRelicPoolCatalog;

    public RelayCommand AddAct1EventPoolFilterCommand => _addAct1EventPoolFilterCommand ??= new RelayCommand(AddAct1EventPoolFilter);

    public RelayCommand RemoveAct1EventPoolFilterCommand => _removeAct1EventPoolFilterCommand ??= new RelayCommand(RemoveAct1EventPoolFilter);

    public RelayCommand AddAct2EventPoolFilterCommand => _addAct2EventPoolFilterCommand ??= new RelayCommand(AddAct2EventPoolFilter);

    public RelayCommand RemoveAct2EventPoolFilterCommand => _removeAct2EventPoolFilterCommand ??= new RelayCommand(RemoveAct2EventPoolFilter);

    public RelayCommand AddAct3EventPoolFilterCommand => _addAct3EventPoolFilterCommand ??= new RelayCommand(AddAct3EventPoolFilter);

    public RelayCommand RemoveAct3EventPoolFilterCommand => _removeAct3EventPoolFilterCommand ??= new RelayCommand(RemoveAct3EventPoolFilter);

    public RelayCommand AddSharedRelicPoolFilterCommand => _addSharedRelicPoolFilterCommand ??= new RelayCommand(AddSharedRelicPoolFilter);

    public RelayCommand RemoveSharedRelicPoolFilterCommand => _removeSharedRelicPoolFilterCommand ??= new RelayCommand(RemoveSharedRelicPoolFilter);

    public RelayCommand AddPlayerRelicPoolFilterCommand => _addPlayerRelicPoolFilterCommand ??= new RelayCommand(AddPlayerRelicPoolFilter);

    public RelayCommand RemovePlayerRelicPoolFilterCommand => _removePlayerRelicPoolFilterCommand ??= new RelayCommand(RemovePlayerRelicPoolFilter);

    public bool IncludePoolFilter
    {
        get => _includePoolFilter;
        set
        {
            if (SetProperty(ref _includePoolFilter, value))
            {
                UpdatePoolFilterSummary();
            }
        }
    }

    public string PoolFilterSummary
    {
        get => _poolFilterSummary;
        private set => SetProperty(ref _poolFilterSummary, value);
    }

    public string Act1EventPoolCatalogFilter
    {
        get => _act1EventPoolCatalogFilter;
        set
        {
            if (SetProperty(ref _act1EventPoolCatalogFilter, value ?? string.Empty))
            {
                ApplyAct1EventPoolFilter();
            }
        }
    }

    public string Act2EventPoolCatalogFilter
    {
        get => _act2EventPoolCatalogFilter;
        set
        {
            if (SetProperty(ref _act2EventPoolCatalogFilter, value ?? string.Empty))
            {
                ApplyAct2EventPoolFilter();
            }
        }
    }

    public string Act3EventPoolCatalogFilter
    {
        get => _act3EventPoolCatalogFilter;
        set
        {
            if (SetProperty(ref _act3EventPoolCatalogFilter, value ?? string.Empty))
            {
                ApplyAct3EventPoolFilter();
            }
        }
    }

    public string SharedRelicPoolCatalogFilter
    {
        get => _sharedRelicPoolCatalogFilter;
        set
        {
            if (SetProperty(ref _sharedRelicPoolCatalogFilter, value ?? string.Empty))
            {
                ApplySharedRelicPoolFilter();
            }
        }
    }

    public string PlayerRelicPoolCatalogFilter
    {
        get => _playerRelicPoolCatalogFilter;
        set
        {
            if (SetProperty(ref _playerRelicPoolCatalogFilter, value ?? string.Empty))
            {
                ApplyPlayerRelicPoolFilter();
            }
        }
    }

    public CatalogItem? SelectedAct1EventPoolCatalogItem
    {
        get => _selectedAct1EventPoolCatalogItem;
        set => SetProperty(ref _selectedAct1EventPoolCatalogItem, value);
    }

    public CatalogItem? SelectedAct2EventPoolCatalogItem
    {
        get => _selectedAct2EventPoolCatalogItem;
        set => SetProperty(ref _selectedAct2EventPoolCatalogItem, value);
    }

    public CatalogItem? SelectedAct3EventPoolCatalogItem
    {
        get => _selectedAct3EventPoolCatalogItem;
        set => SetProperty(ref _selectedAct3EventPoolCatalogItem, value);
    }

    public CatalogItem? SelectedSharedRelicPoolCatalogItem
    {
        get => _selectedSharedRelicPoolCatalogItem;
        set => SetProperty(ref _selectedSharedRelicPoolCatalogItem, value);
    }

    public CatalogItem? SelectedPlayerRelicPoolCatalogItem
    {
        get => _selectedPlayerRelicPoolCatalogItem;
        set => SetProperty(ref _selectedPlayerRelicPoolCatalogItem, value);
    }

    private void InitializePoolFilter()
    {
        Act1EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act2EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act3EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        SharedRelicPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        PlayerRelicPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;

        RefreshPoolEventCatalog();
        RefreshPoolRelicCatalog();
        ApplySharedRelicPoolFilter();
        ApplyPlayerRelicPoolFilter();
        UpdatePoolFilterSummary();
    }

    private void RefreshPoolEventCatalog()
    {
        _poolEventCatalog = _seedAnalysisEventLocalization.Keys
            .Where(key => key.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
            .Select(key => key.Split('.')[0])
            .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(eventId =>
            {
                var displayName = FormatEventId(eventId);
                var display = $"{displayName} ({eventId})";
                return new CatalogItem(eventId, display, BuildSearchKey(displayName, eventId));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyAct1EventPoolFilter();
        ApplyAct2EventPoolFilter();
        ApplyAct3EventPoolFilter();
    }

    private void RefreshPoolRelicCatalog()
    {
        _poolRelicCatalog = _relicLocalizationTable.Keys
            .Where(key => key.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
            .Select(key => key.Split('.')[0])
            .Concat(_relicCatalog.Select(item => item.Value))
            .Where(relicId => !string.IsNullOrWhiteSpace(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(relicId =>
            {
                var displayName = GetPoolRelicTitle(relicId);
                var display = $"{displayName} ({relicId})";
                return new CatalogItem(relicId, display, BuildSearchKey(displayName, relicId));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplySharedRelicPoolFilter();
        ApplyPlayerRelicPoolFilter();
    }

    private void ApplyAct1EventPoolFilter()
    {
        _filteredAct1EventPoolCatalog = FilterCatalog(_poolEventCatalog, _act1EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act1EventPoolCatalogView));
    }

    private void ApplyAct2EventPoolFilter()
    {
        _filteredAct2EventPoolCatalog = FilterCatalog(_poolEventCatalog, _act2EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act2EventPoolCatalogView));
    }

    private void ApplyAct3EventPoolFilter()
    {
        _filteredAct3EventPoolCatalog = FilterCatalog(_poolEventCatalog, _act3EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act3EventPoolCatalogView));
    }

    private void ApplySharedRelicPoolFilter()
    {
        _filteredSharedRelicPoolCatalog = FilterCatalog(_poolRelicCatalog, _sharedRelicPoolCatalogFilter);
        RaisePropertyChanged(nameof(SharedRelicPoolCatalogView));
    }

    private void ApplyPlayerRelicPoolFilter()
    {
        _filteredPlayerRelicPoolCatalog = FilterCatalog(_poolRelicCatalog, _playerRelicPoolCatalogFilter);
        RaisePropertyChanged(nameof(PlayerRelicPoolCatalogView));
    }

    private string GetPoolRelicTitle(string relicId)
    {
        var localizedTitle = GetLocalizedRelicTitle(relicId);
        if (!string.IsNullOrWhiteSpace(localizedTitle))
        {
            return localizedTitle;
        }

        var catalogItem = _relicCatalog.FirstOrDefault(item =>
            string.Equals(item.Value, relicId, StringComparison.OrdinalIgnoreCase));
        if (catalogItem != null)
        {
            var suffix = $" ({relicId})";
            if (catalogItem.Display.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return catalogItem.Display[..^suffix.Length];
            }

            return catalogItem.Display;
        }

        return relicId;
    }

    private void AddAct1EventPoolFilter()
    {
        if (TryAddCatalogChip(Act1EventPoolFilterChips, SelectedAct1EventPoolCatalogItem, "事件"))
        {
            SelectedAct1EventPoolCatalogItem = null;
        }
    }

    private void RemoveAct1EventPoolFilter(object? parameter)
    {
        RemoveChipById(Act1EventPoolFilterChips, parameter as string);
    }

    private void AddAct2EventPoolFilter()
    {
        if (TryAddCatalogChip(Act2EventPoolFilterChips, SelectedAct2EventPoolCatalogItem, "事件"))
        {
            SelectedAct2EventPoolCatalogItem = null;
        }
    }

    private void RemoveAct2EventPoolFilter(object? parameter)
    {
        RemoveChipById(Act2EventPoolFilterChips, parameter as string);
    }

    private void AddAct3EventPoolFilter()
    {
        if (TryAddCatalogChip(Act3EventPoolFilterChips, SelectedAct3EventPoolCatalogItem, "事件"))
        {
            SelectedAct3EventPoolCatalogItem = null;
        }
    }

    private void RemoveAct3EventPoolFilter(object? parameter)
    {
        RemoveChipById(Act3EventPoolFilterChips, parameter as string);
    }

    private void AddSharedRelicPoolFilter()
    {
        if (TryAddCatalogChip(SharedRelicPoolFilterChips, SelectedSharedRelicPoolCatalogItem, "遗物"))
        {
            SelectedSharedRelicPoolCatalogItem = null;
        }
    }

    private void RemoveSharedRelicPoolFilter(object? parameter)
    {
        RemoveChipById(SharedRelicPoolFilterChips, parameter as string);
    }

    private void AddPlayerRelicPoolFilter()
    {
        if (TryAddCatalogChip(PlayerRelicPoolFilterChips, SelectedPlayerRelicPoolCatalogItem, "遗物"))
        {
            SelectedPlayerRelicPoolCatalogItem = null;
        }
    }

    private void RemovePlayerRelicPoolFilter(object? parameter)
    {
        RemoveChipById(PlayerRelicPoolFilterChips, parameter as string);
    }

    private bool TryAddCatalogChip(
        ObservableCollection<FilterChipViewModel> chips,
        CatalogItem? selectedItem,
        string itemName)
    {
        if (selectedItem == null)
        {
            LogWarn($"请选择要添加的{itemName}。");
            return false;
        }

        if (chips.Any(chip => string.Equals(chip.Value, selectedItem.Value, StringComparison.OrdinalIgnoreCase)))
        {
            LogWarn($"该{itemName}已在筛选条件中。");
            return false;
        }

        chips.Add(FilterChipViewModel.FromCatalog(selectedItem));
        return true;
    }

    private void UpdatePoolFilterSummary()
    {
        if (!IncludePoolFilter)
        {
            PoolFilterSummary = "分析池：关闭";
            return;
        }

        var parts = new List<string>();
        AppendChipSummary(parts, "第一幕事件", Act1EventPoolFilterChips);
        AppendChipSummary(parts, "第二幕事件", Act2EventPoolFilterChips);
        AppendChipSummary(parts, "第三幕事件", Act3EventPoolFilterChips);
        AppendChipSummary(parts, "共享遗物", SharedRelicPoolFilterChips);
        AppendChipSummary(parts, "角色遗物", PlayerRelicPoolFilterChips);

        PoolFilterSummary = parts.Count > 0
            ? string.Join(" | ", parts)
            : "分析池：任意";
    }

    private static void AppendChipSummary(
        ICollection<string> parts,
        string label,
        IEnumerable<FilterChipViewModel> chips)
    {
        var values = chips.Select(chip => chip.Label).ToList();
        if (values.Count > 0)
        {
            parts.Add($"{label}：{string.Join(", ", values)}");
        }
    }

    private void OnPoolFilterChipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdatePoolFilterSummary();
    }
}
