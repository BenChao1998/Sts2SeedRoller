using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
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
    private RelayCommand? _addHighProbabilityRelicFilterCommand;
    private RelayCommand? _removeHighProbabilityRelicFilterCommand;
    private bool _includePoolFilter;
    private string _poolFilterSummary = "分析池：关闭";
    private string _act1EventPoolCatalogFilter = string.Empty;
    private string _act2EventPoolCatalogFilter = string.Empty;
    private string _act3EventPoolCatalogFilter = string.Empty;
    private string _highProbabilityRelicCatalogFilter = string.Empty;
    private string _highProbabilitySeenThresholdPercentText = FormatThresholdPercent(Sts2PoolFilter.DefaultHighProbabilitySeenThreshold);
    private string _highProbabilityNonShopThresholdPercentText = string.Empty;
    private string _highProbabilityShopThresholdPercentText = string.Empty;
    private string _highProbabilityEarlyThresholdPercentText = string.Empty;
    private string _highProbabilityAverageFirstOpportunityMaxText = string.Empty;
    private string _highProbabilityMostCommonSourceText = string.Empty;
    private CatalogItem? _selectedAct1EventPoolCatalogItem;
    private CatalogItem? _selectedAct2EventPoolCatalogItem;
    private CatalogItem? _selectedAct3EventPoolCatalogItem;
    private CatalogItem? _selectedHighProbabilityRelicCatalogItem;
    private IReadOnlyList<CatalogItem> _poolEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _poolRelicCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct1EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct2EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredAct3EventPoolCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredHighProbabilityRelicCatalog = Array.Empty<CatalogItem>();

    public ObservableCollection<FilterChipViewModel> Act1EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act2EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act3EventPoolFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> HighProbabilityRelicFilterChips { get; } = new();

    public IEnumerable<CatalogItem> Act1EventPoolCatalogView => _filteredAct1EventPoolCatalog;

    public IEnumerable<CatalogItem> Act2EventPoolCatalogView => _filteredAct2EventPoolCatalog;

    public IEnumerable<CatalogItem> Act3EventPoolCatalogView => _filteredAct3EventPoolCatalog;

    public IEnumerable<CatalogItem> HighProbabilityRelicCatalogView => _filteredHighProbabilityRelicCatalog;

    public IReadOnlyList<RelicVisibilitySourceFilterOption> HighProbabilityMostCommonSourceOptions => _highProbabilityMostCommonSourceOptions;

    public RelayCommand AddAct1EventPoolFilterCommand => _addAct1EventPoolFilterCommand ??= new RelayCommand(AddAct1EventPoolFilter);

    public RelayCommand RemoveAct1EventPoolFilterCommand => _removeAct1EventPoolFilterCommand ??= new RelayCommand(RemoveAct1EventPoolFilter);

    public RelayCommand AddAct2EventPoolFilterCommand => _addAct2EventPoolFilterCommand ??= new RelayCommand(AddAct2EventPoolFilter);

    public RelayCommand RemoveAct2EventPoolFilterCommand => _removeAct2EventPoolFilterCommand ??= new RelayCommand(RemoveAct2EventPoolFilter);

    public RelayCommand AddAct3EventPoolFilterCommand => _addAct3EventPoolFilterCommand ??= new RelayCommand(AddAct3EventPoolFilter);

    public RelayCommand RemoveAct3EventPoolFilterCommand => _removeAct3EventPoolFilterCommand ??= new RelayCommand(RemoveAct3EventPoolFilter);

    public RelayCommand AddHighProbabilityRelicFilterCommand => _addHighProbabilityRelicFilterCommand ??= new RelayCommand(AddHighProbabilityRelicFilter);

    public RelayCommand RemoveHighProbabilityRelicFilterCommand => _removeHighProbabilityRelicFilterCommand ??= new RelayCommand(RemoveHighProbabilityRelicFilter);

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

    public CatalogItem? SelectedHighProbabilityRelicCatalogItem
    {
        get => _selectedHighProbabilityRelicCatalogItem;
        set => SetProperty(ref _selectedHighProbabilityRelicCatalogItem, value);
    }

    public string HighProbabilitySeenThresholdPercentText
    {
        get => _highProbabilitySeenThresholdPercentText;
        set
        {
            if (SetProperty(ref _highProbabilitySeenThresholdPercentText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HighProbabilitySeenThresholdDescription));
                UpdatePoolFilterSummary();
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
                UpdatePoolFilterSummary();
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
                UpdatePoolFilterSummary();
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
                UpdatePoolFilterSummary();
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
                UpdatePoolFilterSummary();
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
                UpdatePoolFilterSummary();
            }
        }
    }

    public string HighProbabilitySeenThresholdDescription =>
        $"命中规则：任一路线画像下，所选遗物需满足“出现”阈值；下方其它条件留空表示不限制。当前出现阈值为 {FormatThresholdPercent(GetHighProbabilitySeenThreshold())}%。";

    private void InitializePoolFilter()
    {
        Act1EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act2EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        Act3EventPoolFilterChips.CollectionChanged += OnPoolFilterChipsChanged;
        HighProbabilityRelicFilterChips.CollectionChanged += OnPoolFilterChipsChanged;

        RefreshPoolEventCatalog();
        RefreshPoolRelicCatalog();
        ApplyHighProbabilityRelicFilter();
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

        ApplyHighProbabilityRelicFilter();
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
        parts.Add($"高概率阈值：{FormatThresholdPercent(GetHighProbabilitySeenThreshold())}%");
        AppendOptionalSummary(parts, "非商店≥", GetOptionalThresholdPercent(HighProbabilityNonShopThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
        AppendOptionalSummary(parts, "商店≥", GetOptionalThresholdPercent(HighProbabilityShopThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
        AppendOptionalSummary(parts, "前期≥", GetOptionalThresholdPercent(HighProbabilityEarlyThresholdPercentText), value => $"{FormatThresholdPercent(value)}%");
        AppendOptionalSummary(parts, "平均首次机会≤", GetOptionalPositiveDouble(HighProbabilityAverageFirstOpportunityMaxText), value => value.ToString("0.##", CultureInfo.InvariantCulture));

        var mostCommonSource = GetHighProbabilityMostCommonSource();
        if (mostCommonSource.HasValue)
        {
            parts.Add($"最常来源：{GetRelicVisibilitySourceDisplayName(mostCommonSource.Value)}");
        }

        AppendChipSummary(parts, "第一幕事件", Act1EventPoolFilterChips);
        AppendChipSummary(parts, "第二幕事件", Act2EventPoolFilterChips);
        AppendChipSummary(parts, "第三幕事件", Act3EventPoolFilterChips);
        AppendChipSummary(parts, "高概率出现遗物", HighProbabilityRelicFilterChips);

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

    private void OnPoolFilterChipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdatePoolFilterSummary();
    }

    private double GetHighProbabilitySeenThreshold()
    {
        return GetOptionalThresholdPercent(HighProbabilitySeenThresholdPercentText)
            ?? Sts2PoolFilter.DefaultHighProbabilitySeenThreshold;
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

    public sealed record RelicVisibilitySourceFilterOption(string Value, string DisplayName);
}
