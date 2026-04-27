using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private static readonly IReadOnlyList<EventVisibilitySourceFilterOption> _highProbabilityEventMostCommonSourceOptions =
    [
        new(string.Empty, "不限制"),
        new(nameof(Sts2EventVisibilitySource.Unknown), "问号房"),
        new(nameof(Sts2EventVisibilitySource.AncientAct2), "第二幕开场古神"),
        new(nameof(Sts2EventVisibilitySource.AncientAct3), "第三幕开场古神")
    ];

    private static readonly IReadOnlyList<RelicVisibilitySourceFilterOption> _highProbabilityMostCommonSourceOptions =
    [
        new(string.Empty, "不限制"),
        new(nameof(Sts2RelicVisibilitySource.Treasure), "宝箱"),
        new(nameof(Sts2RelicVisibilitySource.Elite), "精英"),
        new(nameof(Sts2RelicVisibilitySource.Shop), "商店"),
        new(nameof(Sts2RelicVisibilitySource.AncientAct2), "第二幕古神"),
        new(nameof(Sts2RelicVisibilitySource.AncientAct3), "第三幕古神")
    ];

    private RelayCommand? _addAct1EventPoolFilterCommand;
    private RelayCommand? _removeAct1EventPoolFilterCommand;
    private RelayCommand? _addAct2EventPoolFilterCommand;
    private RelayCommand? _removeAct2EventPoolFilterCommand;
    private RelayCommand? _addAct3EventPoolFilterCommand;
    private RelayCommand? _removeAct3EventPoolFilterCommand;
    private RelayCommand? _addHighProbabilityEventFilterCommand;
    private RelayCommand? _removeHighProbabilityEventFilterCommand;
    private RelayCommand? _addHighProbabilityRelicFilterCommand;
    private RelayCommand? _removeHighProbabilityRelicFilterCommand;
    private bool _includePoolFilter;
    private string _poolFilterSummary = "分析池：关闭";
    private string _act1EventPoolCatalogFilter = string.Empty;
    private string _act2EventPoolCatalogFilter = string.Empty;
    private string _act3EventPoolCatalogFilter = string.Empty;
    private string _highProbabilityEventCatalogFilter = string.Empty;
    private string _highProbabilityRelicCatalogFilter = string.Empty;
    private string _highProbabilityEventSeenThresholdPercentText = FormatThresholdPercent(Sts2PoolFilter.DefaultHighProbabilityEventSeenThreshold);
    private string _highProbabilityEventEarlyThresholdPercentText = string.Empty;
    private string _highProbabilityEventAverageFirstOpportunityMaxText = string.Empty;
    private string _highProbabilityEventMostCommonSourceText = string.Empty;
    private string _highProbabilitySeenThresholdPercentText = FormatThresholdPercent(Sts2PoolFilter.DefaultHighProbabilitySeenThreshold);
    private string _highProbabilityNonShopThresholdPercentText = string.Empty;
    private string _highProbabilityShopThresholdPercentText = string.Empty;
    private string _highProbabilityEarlyThresholdPercentText = string.Empty;
    private string _highProbabilityAverageFirstOpportunityMaxText = string.Empty;
    private string _highProbabilityMostCommonSourceText = string.Empty;
    private string _eventPoolConflictMessage = string.Empty;
    private CatalogItem? _selectedAct1EventPoolCatalogItem;
    private CatalogItem? _selectedAct2EventPoolCatalogItem;
    private CatalogItem? _selectedAct3EventPoolCatalogItem;
    private CatalogItem? _selectedHighProbabilityEventCatalogItem;
    private CatalogItem? _selectedHighProbabilityRelicCatalogItem;
    private IReadOnlyDictionary<int, IReadOnlyList<HashSet<string>>> _actEventPoolsByAct =
        new Dictionary<int, IReadOnlyList<HashSet<string>>>();
    private IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> _actEventBranchLabelByAct =
        new Dictionary<int, IReadOnlyDictionary<string, string>>();
    private IReadOnlyList<CatalogItem> _poolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _act1PoolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _act2PoolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _act3PoolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _eventVisibilityCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _poolRelicCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct1EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct2EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct3EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredHighProbabilityEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredHighProbabilityRelicCatalog = Array.Empty<CatalogItem>();

    public ObservableCollection<FilterChipViewModel> Act1EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act2EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act3EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> HighProbabilityEventFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> HighProbabilityRelicFilterChips { get; } = new();

    public IEnumerable<CatalogItem> Act1EventPoolCatalogView => _filteredAct1EventPoolCatalog;

    public IEnumerable<CatalogItem> Act2EventPoolCatalogView => _filteredAct2EventPoolCatalog;

    public IEnumerable<CatalogItem> Act3EventPoolCatalogView => _filteredAct3EventPoolCatalog;

    public IEnumerable<CatalogItem> HighProbabilityEventCatalogView => _filteredHighProbabilityEventCatalog;

    public IEnumerable<CatalogItem> HighProbabilityRelicCatalogView => _filteredHighProbabilityRelicCatalog;

    public IReadOnlyList<EventVisibilitySourceFilterOption> HighProbabilityEventMostCommonSourceOptions => _highProbabilityEventMostCommonSourceOptions;

    public IReadOnlyList<RelicVisibilitySourceFilterOption> HighProbabilityMostCommonSourceOptions => _highProbabilityMostCommonSourceOptions;

    public RelayCommand AddAct1EventPoolFilterCommand => _addAct1EventPoolFilterCommand ??= new RelayCommand(AddAct1EventPoolFilter);

    public RelayCommand RemoveAct1EventPoolFilterCommand => _removeAct1EventPoolFilterCommand ??= new RelayCommand(RemoveAct1EventPoolFilter);

    public RelayCommand AddAct2EventPoolFilterCommand => _addAct2EventPoolFilterCommand ??= new RelayCommand(AddAct2EventPoolFilter);

    public RelayCommand RemoveAct2EventPoolFilterCommand => _removeAct2EventPoolFilterCommand ??= new RelayCommand(RemoveAct2EventPoolFilter);

    public RelayCommand AddAct3EventPoolFilterCommand => _addAct3EventPoolFilterCommand ??= new RelayCommand(AddAct3EventPoolFilter);

    public RelayCommand RemoveAct3EventPoolFilterCommand => _removeAct3EventPoolFilterCommand ??= new RelayCommand(RemoveAct3EventPoolFilter);

    public RelayCommand AddHighProbabilityEventFilterCommand => _addHighProbabilityEventFilterCommand ??= new RelayCommand(AddHighProbabilityEventFilter);

    public RelayCommand RemoveHighProbabilityEventFilterCommand => _removeHighProbabilityEventFilterCommand ??= new RelayCommand(RemoveHighProbabilityEventFilter);

    public RelayCommand AddHighProbabilityRelicFilterCommand => _addHighProbabilityRelicFilterCommand ??= new RelayCommand(AddHighProbabilityRelicFilter);

    public RelayCommand RemoveHighProbabilityRelicFilterCommand => _removeHighProbabilityRelicFilterCommand ??= new RelayCommand(RemoveHighProbabilityRelicFilter);

    public bool IncludePoolFilter
    {
        get => _includePoolFilter;
        set
        {
            if (SetProperty(ref _includePoolFilter, value))
            {
                if (!value)
                {
                    ClearEventPoolConflictMessage();
                }

                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string PoolFilterSummary
    {
        get => _poolFilterSummary;
        private set => SetProperty(ref _poolFilterSummary, value);
    }

    public string EventPoolConflictMessage
    {
        get => _eventPoolConflictMessage;
        private set
        {
            if (SetProperty(ref _eventPoolConflictMessage, value))
            {
                RaisePropertyChanged(nameof(HasEventPoolConflictMessage));
            }
        }
    }

    public bool HasEventPoolConflictMessage => !string.IsNullOrWhiteSpace(EventPoolConflictMessage);

    public int HighProbabilityEventFilterCount =>
        Act1EventPoolFilterChips.Count + Act2EventPoolFilterChips.Count + Act3EventPoolFilterChips.Count;

    public bool HasHighProbabilityEventFilters => HighProbabilityEventFilterCount > 0;

    public string HighProbabilityEventFilterStatusText => HighProbabilityEventFilterCount > 0
        ? $"已添加 {HighProbabilityEventFilterCount} 个事件条件"
        : "当前未添加任何高概率事件条件。";

    public int HighProbabilityRelicFilterCount => HighProbabilityRelicFilterChips.Count;

    public bool HasHighProbabilityRelicFilters => HighProbabilityRelicFilterChips.Count > 0;

    public string HighProbabilityRelicFilterStatusText => HighProbabilityRelicFilterChips.Count > 0
        ? $"已添加 {HighProbabilityRelicFilterChips.Count} 个遗物条件"
        : "当前未添加任何高概率遗物条件。";

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

    public string HighProbabilityEventCatalogFilter
    {
        get => _highProbabilityEventCatalogFilter;
        set
        {
            if (SetProperty(ref _highProbabilityEventCatalogFilter, value ?? string.Empty))
            {
                ApplyHighProbabilityEventFilter();
            }
        }
    }

    public string HighProbabilityRelicCatalogFilter
    {
        get => _highProbabilityRelicCatalogFilter;
        set
        {
            if (SetProperty(ref _highProbabilityRelicCatalogFilter, value ?? string.Empty))
            {
                ApplyHighProbabilityRelicFilter();
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

    public CatalogItem? SelectedHighProbabilityEventCatalogItem
    {
        get => _selectedHighProbabilityEventCatalogItem;
        set => SetProperty(ref _selectedHighProbabilityEventCatalogItem, value);
    }

    public CatalogItem? SelectedHighProbabilityRelicCatalogItem
    {
        get => _selectedHighProbabilityRelicCatalogItem;
        set => SetProperty(ref _selectedHighProbabilityRelicCatalogItem, value);
    }

    public string HighProbabilityEventSeenThresholdPercentText
    {
        get => _highProbabilityEventSeenThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilityEventSeenThresholdPercentText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HighProbabilityEventSeenThresholdDescription));
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityEventEarlyThresholdPercentText
    {
        get => _highProbabilityEventEarlyThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilityEventEarlyThresholdPercentText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityEventAverageFirstOpportunityMaxText
    {
        get => _highProbabilityEventAverageFirstOpportunityMaxText;
        set
        {
            if (SetProperty(ref _highProbabilityEventAverageFirstOpportunityMaxText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityEventMostCommonSourceText
    {
        get => _highProbabilityEventMostCommonSourceText;
        set
        {
            if (SetProperty(ref _highProbabilityEventMostCommonSourceText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilitySeenThresholdPercentText
    {
        get => _highProbabilitySeenThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilitySeenThresholdPercentText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HighProbabilitySeenThresholdDescription));
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityNonShopThresholdPercentText
    {
        get => _highProbabilityNonShopThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilityNonShopThresholdPercentText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityShopThresholdPercentText
    {
        get => _highProbabilityShopThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilityShopThresholdPercentText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityEarlyThresholdPercentText
    {
        get => _highProbabilityEarlyThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilityEarlyThresholdPercentText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityAverageFirstOpportunityMaxText
    {
        get => _highProbabilityAverageFirstOpportunityMaxText;
        set
        {
            if (SetProperty(ref _highProbabilityAverageFirstOpportunityMaxText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilityMostCommonSourceText
    {
        get => _highProbabilityMostCommonSourceText;
        set
        {
            if (SetProperty(ref _highProbabilityMostCommonSourceText, value ?? string.Empty))
            {
                UpdatePoolFilterSummaryCore();
            }
        }
    }

    public string HighProbabilitySeenThresholdDescription =>
        $"命中规则：任一路线画像下，所选遗物需满足“出现”阈值；下方其它条件留空表示不限制。当前出现阈值为 {FormatThresholdPercent(GetHighProbabilitySeenThreshold())}%。";

    public string HighProbabilityEventSeenThresholdDescription =>
        $"命中规则：三幕里选中的事件会统一按“整局概率”筛选；下方其它条件留空表示不限制。当前整局概率阈值为 {FormatThresholdPercent(GetHighProbabilityEventSeenThreshold())}%。";

    private void InitializePoolFilter()
    {
        Act1EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act2EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act3EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        HighProbabilityEventFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        HighProbabilityRelicFilterChips.CollectionChanged += OnPoolFilterChipsChanged;

        RefreshPoolEventCatalog();
        RefreshPoolRelicCatalog();
        ApplyHighProbabilityEventFilter();
        ApplyHighProbabilityRelicFilter();
        UpdatePoolFilterSummaryCore();
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

        var actEventIdsByAct = LoadActEventIdsByAct();
        _act1PoolEventCatalog = BuildActEventCatalog(1, actEventIdsByAct.GetValueOrDefault(1));
        _act2PoolEventCatalog = BuildActEventCatalog(2, actEventIdsByAct.GetValueOrDefault(2));
        _act3PoolEventCatalog = BuildActEventCatalog(3, actEventIdsByAct.GetValueOrDefault(3));

        _eventVisibilityCatalog = _poolEventCatalog
            .Concat(
                AncientDisplayCatalog.AllowedForAct2
                    .Concat(AncientDisplayCatalog.AllowedForAct3)
                    .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(option => new CatalogItem(
                        option.Id,
                        option.DisplayText,
                        BuildSearchKey(option.Name, option.DisplayText, option.Id))))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyAct1EventPoolFilter();
        ApplyAct2EventPoolFilter();
        ApplyAct3EventPoolFilter();
        ApplyHighProbabilityEventFilter();
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

        ApplyHighProbabilityRelicFilter();
    }

    private void ApplyAct1EventPoolFilter()
    {
        _filteredAct1EventPoolCatalog = FilterCatalog(_act1PoolEventCatalog, _act1EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act1EventPoolCatalogView));
    }

    private void ApplyAct2EventPoolFilter()
    {
        _filteredAct2EventPoolCatalog = FilterCatalog(_act2PoolEventCatalog, _act2EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act2EventPoolCatalogView));
    }

    private void ApplyAct3EventPoolFilter()
    {
        _filteredAct3EventPoolCatalog = FilterCatalog(_act3PoolEventCatalog, _act3EventPoolCatalogFilter);
        RaisePropertyChanged(nameof(Act3EventPoolCatalogView));
    }

    private void ApplyHighProbabilityEventFilter()
    {
        _filteredHighProbabilityEventCatalog = FilterCatalog(_eventVisibilityCatalog, _highProbabilityEventCatalogFilter);
        RaisePropertyChanged(nameof(HighProbabilityEventCatalogView));
    }

    private void ApplyHighProbabilityRelicFilter()
    {
        _filteredHighProbabilityRelicCatalog = FilterCatalog(_poolRelicCatalog, _highProbabilityRelicCatalogFilter);
        RaisePropertyChanged(nameof(HighProbabilityRelicCatalogView));
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
        ClearEventPoolConflictMessage();
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
        ClearEventPoolConflictMessage();
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
        ClearEventPoolConflictMessage();
    }

    private void AddHighProbabilityEventFilter()
    {
        if (TryAddCatalogChip(HighProbabilityEventFilterChips, SelectedHighProbabilityEventCatalogItem, "事件"))
        {
            SelectedHighProbabilityEventCatalogItem = null;
        }
    }

    private void RemoveHighProbabilityEventFilter(object? parameter)
    {
        RemoveChipById(HighProbabilityEventFilterChips, parameter as string);
    }

    private void AddHighProbabilityRelicFilter()
    {
        if (TryAddCatalogChip(HighProbabilityRelicFilterChips, SelectedHighProbabilityRelicCatalogItem, "遗物"))
        {
            SelectedHighProbabilityRelicCatalogItem = null;
        }
    }

    private void RemoveHighProbabilityRelicFilter(object? parameter)
    {
        RemoveChipById(HighProbabilityRelicFilterChips, parameter as string);
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
            LogWarn($"{itemName}已在筛选条件中。");
            return false;
        }

        chips.Add(FilterChipViewModel.FromCatalog(selectedItem));

        if (!ValidateActEventPoolSelection(chips, selectedItem))
        {
            return false;
        }

        if (ReferenceEquals(chips, Act1EventPoolFilterChips)
            || ReferenceEquals(chips, Act2EventPoolFilterChips)
            || ReferenceEquals(chips, Act3EventPoolFilterChips))
        {
            ClearEventPoolConflictMessage();
        }

        return true;
    }

    private bool ValidateActEventPoolSelection(
        ObservableCollection<FilterChipViewModel> chips,
        CatalogItem selectedItem)
    {
        int? actNumber = ReferenceEquals(chips, Act1EventPoolFilterChips)
            ? 1
            : ReferenceEquals(chips, Act2EventPoolFilterChips)
                ? 2
                : ReferenceEquals(chips, Act3EventPoolFilterChips)
                    ? 3
                    : null;

        if (!actNumber.HasValue || HasCompatibleActEventPool(actNumber.Value, chips.Select(chip => chip.Value)))
        {
            return true;
        }

        var addedChipId = chips.LastOrDefault(chip =>
            string.Equals(chip.Value, selectedItem.Value, StringComparison.OrdinalIgnoreCase))?.Id;
        RemoveChipById(chips, addedChipId);

        EventPoolConflictMessage = string.Format(
            CultureInfo.InvariantCulture,
            "第{0}幕当前选择的事件不可能同时出现在同一个事件池里，请检查是否混选了不同地图分支的事件。",
            actNumber.Value);
        StatusMessage = EventPoolConflictMessage;
        LogWarn(StatusMessage);
        return false;
    }

    private void ClearEventPoolConflictMessage()
    {
        EventPoolConflictMessage = string.Empty;
    }

    private void UpdatePoolFilterSummaryCore()
    {
        if (!IncludePoolFilter)
        {
            PoolFilterSummary = "分析池：关闭";
            return;
        }

        var parts = new List<string>();

        AppendChipSummary(parts, "第一幕高概率事件", Act1EventPoolFilterChips);
        AppendChipSummary(parts, "第二幕高概率事件", Act2EventPoolFilterChips);
        AppendChipSummary(parts, "第三幕高概率事件", Act3EventPoolFilterChips);

        if (HasHighProbabilityEventFilters)
        {
            parts.Add($"事件整局概率：{FormatThresholdPercent(GetHighProbabilityEventSeenThreshold())}%");
            AppendOptionalSummary(
                parts,
                "事件平均首次事件位≤",
                GetOptionalPositiveDouble(HighProbabilityEventAverageFirstOpportunityMaxText),
                value => value.ToString("0.##", CultureInfo.InvariantCulture));

            var eventMostCommonSource = GetHighProbabilityEventMostCommonSource();
            if (eventMostCommonSource.HasValue)
            {
                parts.Add($"事件多来自：{GetEventVisibilitySourceDisplayName(eventMostCommonSource.Value)}");
            }
        }

        if (HighProbabilityRelicFilterChips.Count > 0)
        {
            AppendChipSummary(parts, "高概率遗物", HighProbabilityRelicFilterChips);
            parts.Add($"遗物出现概率：{FormatThresholdPercent(GetHighProbabilitySeenThreshold())}%");
            AppendOptionalSummary(parts, "非商店≥", GetOptionalThresholdPercent(HighProbabilityNonShopThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
            AppendOptionalSummary(parts, "商店≥", GetOptionalThresholdPercent(HighProbabilityShopThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
            AppendOptionalSummary(parts, "遗物前期≥", GetOptionalThresholdPercent(HighProbabilityEarlyThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
            AppendOptionalSummary(
                parts,
                "遗物平均首次机会≤",
                GetOptionalPositiveDouble(HighProbabilityAverageFirstOpportunityMaxText),
                value => value.ToString("0.##", CultureInfo.InvariantCulture));

            var mostCommonSource = GetHighProbabilityMostCommonSource();
            if (mostCommonSource.HasValue)
            {
                parts.Add($"遗物多来自：{GetRelicVisibilitySourceDisplayName(mostCommonSource.Value)}");
            }
        }

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

    private static void AppendOptionalSummary<T>(
        ICollection<string> parts,
        string label,
        T? value,
        Func<T, string> formatter)
        where T : struct
    {
        if (value.HasValue)
        {
            parts.Add($"{label}{formatter(value.Value)}");
        }
    }

    private Dictionary<int, HashSet<string>> LoadActEventIdsByAct()
    {
        try
        {
            using var stream = OpenActsDataStream(SelectedGameVersion.Id);
            if (stream == null)
            {
                return new Dictionary<int, HashSet<string>>();
            }

            var model = JsonSerializer.Deserialize<Sts2ActsFileModel>(stream);
            if (model?.Acts == null)
            {
                _actEventPoolsByAct = new Dictionary<int, IReadOnlyList<HashSet<string>>>();
                _actEventBranchLabelByAct = new Dictionary<int, IReadOnlyDictionary<string, string>>();
                return new Dictionary<int, HashSet<string>>();
            }

            var actNameLookup = LoadActNameLookup(SelectedGameVersion.Id);

            _actEventPoolsByAct = model.Acts
                .Where(act => act.Number is >= 1 and <= 3)
                .GroupBy(act => act.Number)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<HashSet<string>>)group
                        .Select(act => (act.Events ?? Enumerable.Empty<string>())
                            .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                            .Select(ToLocalizationToken)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase))
                        .Where(pool => pool.Count > 0)
                        .ToList());

            _actEventBranchLabelByAct = model.Acts
                .Where(act => act.Number is >= 1 and <= 3)
                .GroupBy(act => act.Number)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyDictionary<string, string>)group
                        .SelectMany(act =>
                        {
                            var branchName = GetActBranchDisplayName(act, actNameLookup);
                            return (act.Events ?? Enumerable.Empty<string>())
                                .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                                .Select(ToLocalizationToken)
                                .Select(eventId => new { EventId = eventId, BranchName = branchName });
                        })
                        .GroupBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            itemGroup => itemGroup.Key,
                            itemGroup =>
                            {
                                var branchNames = itemGroup
                                    .Select(item => item.BranchName)
                                    .Where(name => !string.IsNullOrWhiteSpace(name))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                                return branchNames.Count switch
                                {
                                    0 => string.Empty,
                                    1 => branchNames[0],
                                    _ => string.Join(" / ", branchNames)
                                };
                            },
                            StringComparer.OrdinalIgnoreCase));

            return model.Acts
                .Where(act => act.Number is >= 1 and <= 3)
                .GroupBy(act => act.Number)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .SelectMany(act => act.Events ?? Enumerable.Empty<string>())
                        .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                        .Select(ToLocalizationToken)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            LogWarn($"加载分幕事件目录失败：{ex.Message}");
            return new Dictionary<int, HashSet<string>>();
        }
    }

    private IReadOnlyDictionary<string, string> LoadActNameLookup(string version)
    {
        try
        {
            var path = UiDataPathResolver.ResolveVersionedDataFilePath(version, "sts2", "localization", "zhs", "acts.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return model == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(model, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetActBranchDisplayName(
        Sts2ActCatalogEntry act,
        IReadOnlyDictionary<string, string> actNameLookup)
    {
        if (string.IsNullOrWhiteSpace(act.Name))
        {
            return $"第{act.Number}幕";
        }

        var localizationKey = $"{act.Name.ToUpperInvariant()}.title";
        return actNameLookup.TryGetValue(localizationKey, out var displayName) && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : act.Name;
    }

    private Stream? OpenActsDataStream(string version)
    {
        var path = UiDataPathResolver.ResolveVersionedDataFilePath(version, "sts2", "acts.json");
        if (File.Exists(path))
        {
            return File.OpenRead(path);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.Contains("SeedUi.Data.sts2.") &&
                name.EndsWith(".acts.json", StringComparison.OrdinalIgnoreCase));

        return resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private IReadOnlyList<CatalogItem> BuildActEventCatalog(
        int actNumber,
        HashSet<string>? allowedIds)
    {
        if (allowedIds == null || allowedIds.Count == 0)
        {
            return Array.Empty<CatalogItem>();
        }

        _actEventBranchLabelByAct.TryGetValue(actNumber, out var branchLabels);

        return _poolEventCatalog
            .Where(item => allowedIds.Contains(item.Value))
            .Select(item =>
            {
                var display = item.Display;
                var branchLabel = string.Empty;
                if (branchLabels != null &&
                    branchLabels.TryGetValue(item.Value, out var resolvedBranchLabel) &&
                    !string.IsNullOrWhiteSpace(resolvedBranchLabel))
                {
                    branchLabel = resolvedBranchLabel;
                    display = $"{item.Display} [{branchLabel}]";
                }

                return new CatalogItem(item.Value, display, BuildSearchKey(item.SearchText, branchLabel));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool HasCompatibleActEventPool(int actNumber, IEnumerable<string> selectedEventIds)
    {
        if (!_actEventPoolsByAct.TryGetValue(actNumber, out var pools) || pools.Count == 0)
        {
            return true;
        }

        var selected = selectedEventIds
            .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
            .ToList();

        if (selected.Count <= 1)
        {
            return true;
        }

        return pools.Any(pool => selected.All(pool.Contains));
    }

    private void OnPoolFilterChipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HighProbabilityEventFilterCount));
        RaisePropertyChanged(nameof(HasHighProbabilityEventFilters));
        RaisePropertyChanged(nameof(HighProbabilityEventFilterStatusText));
        RaisePropertyChanged(nameof(HighProbabilityRelicFilterCount));
        RaisePropertyChanged(nameof(HasHighProbabilityRelicFilters));
        RaisePropertyChanged(nameof(HighProbabilityRelicFilterStatusText));
        UpdatePoolFilterSummaryCore();
    }

    private void WarnIfProbabilitySelectionsAreNotAdded()
    {
        if (SelectedHighProbabilityEventCatalogItem != null && HighProbabilityEventFilterChips.Count == 0)
        {
            LogWarn($"你当前只是在下拉框里选中了“{SelectedHighProbabilityEventCatalogItem.Display}”，但还没有点击“添加”，因此这个旧的独立事件筛选不会生效。现在请直接使用上方的三幕事件筛选。");
        }

        if (SelectedHighProbabilityRelicCatalogItem != null && HighProbabilityRelicFilterChips.Count == 0)
        {
            LogWarn($"你当前只是在下拉框里选中了“{SelectedHighProbabilityRelicCatalogItem.Display}”，但还没有点击“添加”，因此“高概率出现遗物”筛选不会生效。");
        }
    }

    private double GetHighProbabilitySeenThreshold()
    {
        return GetOptionalThresholdPercent(HighProbabilitySeenThresholdPercentText)
            ?? Sts2PoolFilter.DefaultHighProbabilitySeenThreshold;
    }

    private double GetHighProbabilityEventSeenThreshold()
    {
        return GetOptionalThresholdPercent(HighProbabilityEventSeenThresholdPercentText)
            ?? Sts2PoolFilter.DefaultHighProbabilityEventSeenThreshold;
    }

    private static string FormatThresholdPercent(double threshold)
    {
        var percent = threshold * 100d;
        return percent.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatOptionalPercentInput(double? percent)
    {
        return percent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatOptionalNumberInput(double? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static double? GetOptionalThresholdPercent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var percent))
        {
            return null;
        }

        percent = Math.Clamp(percent, 0d, 100d);
        return percent / 100d;
    }

    private static double? GetOptionalPositiveDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0d)
        {
            return null;
        }

        return value;
    }

    private Sts2RelicVisibilitySource? GetHighProbabilityMostCommonSource()
    {
        return string.IsNullOrWhiteSpace(HighProbabilityMostCommonSourceText) ||
               !Enum.TryParse<Sts2RelicVisibilitySource>(
                   HighProbabilityMostCommonSourceText,
                   ignoreCase: true,
                   out var source)
            ? null
            : source;
    }

    private Sts2EventVisibilitySource? GetHighProbabilityEventMostCommonSource()
    {
        return string.IsNullOrWhiteSpace(HighProbabilityEventMostCommonSourceText) ||
               !Enum.TryParse<Sts2EventVisibilitySource>(
                   HighProbabilityEventMostCommonSourceText,
                   ignoreCase: true,
                   out var source)
            ? null
            : source;
    }

    private static string GetRelicVisibilitySourceDisplayName(Sts2RelicVisibilitySource source)
    {
        return source switch
        {
            Sts2RelicVisibilitySource.Treasure => "宝箱",
            Sts2RelicVisibilitySource.Elite => "精英",
            Sts2RelicVisibilitySource.Shop => "商店",
            Sts2RelicVisibilitySource.AncientAct2 => "第二幕古神",
            Sts2RelicVisibilitySource.AncientAct3 => "第三幕古神",
            _ => source.ToString()
        };
    }

    private static string GetEventVisibilitySourceDisplayName(Sts2EventVisibilitySource source)
    {
        return source switch
        {
            Sts2EventVisibilitySource.Unknown => "问号房",
            Sts2EventVisibilitySource.AncientAct2 => "第二幕开场古神",
            Sts2EventVisibilitySource.AncientAct3 => "第三幕开场古神",
            _ => source.ToString()
        };
    }

    public sealed record RelicVisibilitySourceFilterOption(string Value, string DisplayName);

    public sealed record EventVisibilitySourceFilterOption(string Value, string DisplayName);

    private sealed class Sts2ActsFileModel
    {
        [JsonPropertyName("acts")]
        public List<Sts2ActCatalogEntry>? Acts { get; init; }
    }

    private sealed class Sts2ActCatalogEntry
    {
        [JsonPropertyName("ancients")]
        public List<string>? Ancients { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("events")]
        public List<string>? Events { get; init; }
    }
}
