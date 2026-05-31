using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SeedModel.Neow;
using SeedModel.Seeds;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private const int DefaultExactRouteMaxResults = 3;
    private const long DefaultExactRouteMaxChecks = 200;
    private const int DefaultExactRouteFastShopOutputLimit = 12;
    private const int MaxExactRouteResults = 20;
    private const long MaxExactRouteChecks = 1_000_000;
    private const int MaxExactRouteFastShopOutputLimit = 200;

    private AsyncRelayCommand? _analyzeSeedRoutesCommand;
    private RelayCommand? _cancelSeedAnalysisRouteCommand;
    private RelayCommand? _addSeedAnalysisRouteEventTargetCommand;
    private RelayCommand? _removeSeedAnalysisRouteEventTargetCommand;
    private RelayCommand? _addSeedAnalysisRouteRelicTargetCommand;
    private RelayCommand? _removeSeedAnalysisRouteRelicTargetCommand;
    private RelayCommand? _addSeedAnalysisRouteCoverageTargetCommand;
    private string _seedAnalysisRouteSummary = "选择目标事件或遗物后，搜索命中路线。";
    private string _seedAnalysisRouteEventCatalogFilter = string.Empty;
    private string _seedAnalysisRouteRelicCatalogFilter = string.Empty;
    private string _seedAnalysisRouteMaxResultsText = DefaultExactRouteMaxResults.ToString(CultureInfo.InvariantCulture);
    private string _seedAnalysisRouteMaxChecksText = DefaultExactRouteMaxChecks.ToString(CultureInfo.InvariantCulture);
    private bool _isSeedAnalysisRouteFastMode = true;
    private string _seedAnalysisRouteShopOutputLimitText = DefaultExactRouteFastShopOutputLimit.ToString(CultureInfo.InvariantCulture);
    private CatalogItem? _selectedSeedAnalysisRouteEventCatalogItem;
    private CatalogItem? _selectedSeedAnalysisRouteRelicCatalogItem;
    private SeedAnalysisRouteAct1OptionViewModel? _selectedSeedAnalysisRouteAct1Option;
    private RouteActOption? _selectedSeedAnalysisRouteEventActOption;
    private RouteActOption? _selectedSeedAnalysisRouteRelicActOption;
    private IReadOnlyList<CatalogItem> _filteredSeedAnalysisRouteEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredSeedAnalysisRouteRelicCatalog = Array.Empty<CatalogItem>();
    private bool _hasSeedAnalysisRouteResult;
    private bool _isSeedAnalysisRouteAnalyzing;
    private CancellationTokenSource? _seedAnalysisRouteCancellation;
    private string _seedAnalysisRouteProgressText = "尚未开始路线模拟。";
    private double _seedAnalysisRouteProgressValue;
    private string _seedAnalysisRouteCoverageSummary = "路线覆盖率会在分析后生成。";

    public ObservableCollection<SeedAnalysisRouteMatchViewModel> SeedAnalysisRouteMatches { get; } = new();

    public ObservableCollection<SeedAnalysisRouteTargetChipViewModel> SeedAnalysisRouteEventTargetChips { get; } = new();

    public ObservableCollection<SeedAnalysisRouteTargetChipViewModel> SeedAnalysisRouteRelicTargetChips { get; } = new();

    public ObservableCollection<SeedAnalysisRouteAct1OptionViewModel> SeedAnalysisRouteAct1Options { get; } = new();

    public AsyncRelayCommand AnalyzeSeedRoutesCommand => _analyzeSeedRoutesCommand ??= new AsyncRelayCommand(AnalyzeSeedRoutesAsync, () => !IsSeedAnalysisRouteAnalyzing);

    public RelayCommand CancelSeedAnalysisRouteCommand =>
        _cancelSeedAnalysisRouteCommand ??= new RelayCommand(CancelSeedAnalysisRoute, () => IsSeedAnalysisRouteAnalyzing);

    public RelayCommand AddSeedAnalysisRouteEventTargetCommand =>
        _addSeedAnalysisRouteEventTargetCommand ??= new RelayCommand(AddSeedAnalysisRouteEventTarget);

    public RelayCommand RemoveSeedAnalysisRouteEventTargetCommand =>
        _removeSeedAnalysisRouteEventTargetCommand ??= new RelayCommand(RemoveSeedAnalysisRouteEventTarget);

    public RelayCommand AddSeedAnalysisRouteRelicTargetCommand =>
        _addSeedAnalysisRouteRelicTargetCommand ??= new RelayCommand(AddSeedAnalysisRouteRelicTarget);

    public RelayCommand RemoveSeedAnalysisRouteRelicTargetCommand =>
        _removeSeedAnalysisRouteRelicTargetCommand ??= new RelayCommand(RemoveSeedAnalysisRouteRelicTarget);

    public RelayCommand AddSeedAnalysisRouteCoverageTargetCommand =>
        _addSeedAnalysisRouteCoverageTargetCommand ??= new RelayCommand(AddSeedAnalysisRouteCoverageTarget);

    public IReadOnlyList<RouteActOption> SeedAnalysisRouteEventActOptions { get; } =
    [
        new(null, "任意幕"),
        new(1, "第一幕"),
        new(2, "第二幕"),
        new(3, "第三幕")
    ];

    public IReadOnlyList<RouteActOption> SeedAnalysisRouteRelicActOptions { get; } =
    [
        new(null, "任意幕"),
        new(1, "第一幕"),
        new(2, "第二幕"),
        new(3, "第三幕")
    ];

    public IEnumerable<CatalogItem> SeedAnalysisRouteEventCatalogView => _filteredSeedAnalysisRouteEventCatalog;

    public IEnumerable<CatalogItem> SeedAnalysisRouteRelicCatalogView => _filteredSeedAnalysisRouteRelicCatalog;

    public string SeedAnalysisRouteSummary
    {
        get => _seedAnalysisRouteSummary;
        private set => SetProperty(ref _seedAnalysisRouteSummary, value);
    }

    public bool HasSeedAnalysisRouteResult
    {
        get => _hasSeedAnalysisRouteResult;
        private set => SetProperty(ref _hasSeedAnalysisRouteResult, value);
    }

    public bool HasSeedAnalysisRouteMatches => SeedAnalysisRouteMatches.Count > 0;

    public ObservableCollection<SeedAnalysisRouteCoverageItemViewModel> SeedAnalysisRouteCoverageEvents { get; } = new();

    public ObservableCollection<SeedAnalysisRouteCoverageItemViewModel> SeedAnalysisRouteCoverageRelics { get; } = new();

    public bool IsSeedAnalysisRouteAnalyzing
    {
        get => _isSeedAnalysisRouteAnalyzing;
        private set
        {
            if (SetProperty(ref _isSeedAnalysisRouteAnalyzing, value))
            {
                _analyzeSeedRoutesCommand?.RaiseCanExecuteChanged();
                _cancelSeedAnalysisRouteCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string SeedAnalysisRouteProgressText
    {
        get => _seedAnalysisRouteProgressText;
        private set => SetProperty(ref _seedAnalysisRouteProgressText, value);
    }

    public double SeedAnalysisRouteProgressValue
    {
        get => _seedAnalysisRouteProgressValue;
        private set => SetProperty(ref _seedAnalysisRouteProgressValue, Math.Clamp(value, 0d, 1d));
    }

    public string SeedAnalysisRouteCoverageSummary
    {
        get => _seedAnalysisRouteCoverageSummary;
        private set => SetProperty(ref _seedAnalysisRouteCoverageSummary, value);
    }

    public string SeedAnalysisRouteEventCatalogFilter
    {
        get => _seedAnalysisRouteEventCatalogFilter;
        set
        {
            if (SetProperty(ref _seedAnalysisRouteEventCatalogFilter, value ?? string.Empty))
            {
                ApplySeedAnalysisRouteEventFilter();
            }
        }
    }

    public string SeedAnalysisRouteRelicCatalogFilter
    {
        get => _seedAnalysisRouteRelicCatalogFilter;
        set
        {
            if (SetProperty(ref _seedAnalysisRouteRelicCatalogFilter, value ?? string.Empty))
            {
                ApplySeedAnalysisRouteRelicFilter();
            }
        }
    }

    public string SeedAnalysisRouteMaxResultsText
    {
        get => _seedAnalysisRouteMaxResultsText;
        set => SetProperty(ref _seedAnalysisRouteMaxResultsText, value ?? string.Empty);
    }

    public string SeedAnalysisRouteMaxChecksText
    {
        get => _seedAnalysisRouteMaxChecksText;
        set => SetProperty(ref _seedAnalysisRouteMaxChecksText, value ?? string.Empty);
    }

    public bool IsSeedAnalysisRouteStrictMode
    {
        get => !_isSeedAnalysisRouteFastMode;
        set
        {
            if (value)
            {
                IsSeedAnalysisRouteFastMode = false;
            }
        }
    }

    public bool IsSeedAnalysisRouteFastMode
    {
        get => _isSeedAnalysisRouteFastMode;
        set
        {
            if (SetProperty(ref _isSeedAnalysisRouteFastMode, value))
            {
                RaisePropertyChanged(nameof(IsSeedAnalysisRouteStrictMode));
                RaisePropertyChanged(nameof(SeedAnalysisRouteSimulationModeText));
            }
        }
    }

    public string SeedAnalysisRouteShopOutputLimitText
    {
        get => _seedAnalysisRouteShopOutputLimitText;
        set
        {
            if (SetProperty(ref _seedAnalysisRouteShopOutputLimitText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SeedAnalysisRouteSimulationModeText));
            }
        }
    }

    public string SeedAnalysisRouteSimulationModeText =>
        IsSeedAnalysisRouteFastMode
            ? $"快速模式：商店输出上限 {GetSeedAnalysisRouteShopOutputLimit()}，会裁剪低优先级商店分支，结果为近似。"
            : "严格模式：不裁剪商店分支，结果优先保证完整性。";

    public CatalogItem? SelectedSeedAnalysisRouteEventCatalogItem
    {
        get => _selectedSeedAnalysisRouteEventCatalogItem;
        set => SetProperty(ref _selectedSeedAnalysisRouteEventCatalogItem, value);
    }

    public CatalogItem? SelectedSeedAnalysisRouteRelicCatalogItem
    {
        get => _selectedSeedAnalysisRouteRelicCatalogItem;
        set => SetProperty(ref _selectedSeedAnalysisRouteRelicCatalogItem, value);
    }

    public SeedAnalysisRouteAct1OptionViewModel? SelectedSeedAnalysisRouteAct1Option
    {
        get => _selectedSeedAnalysisRouteAct1Option;
        set => SetProperty(ref _selectedSeedAnalysisRouteAct1Option, value);
    }

    public RouteActOption? SelectedSeedAnalysisRouteEventActOption
    {
        get => _selectedSeedAnalysisRouteEventActOption;
        set => SetProperty(ref _selectedSeedAnalysisRouteEventActOption, value);
    }

    public RouteActOption? SelectedSeedAnalysisRouteRelicActOption
    {
        get => _selectedSeedAnalysisRouteRelicActOption;
        set => SetProperty(ref _selectedSeedAnalysisRouteRelicActOption, value);
    }

    private void InitializeSeedAnalysisRouteSearch()
    {
        SelectedSeedAnalysisRouteEventActOption = SeedAnalysisRouteEventActOptions.First();
        SelectedSeedAnalysisRouteRelicActOption = SeedAnalysisRouteRelicActOptions.First();
        RefreshSeedAnalysisRouteAct1Options();
        ApplySeedAnalysisRouteEventFilter();
        ApplySeedAnalysisRouteRelicFilter();
    }

    private void RefreshSeedAnalysisRouteCatalogs()
    {
        RefreshSeedAnalysisRouteAct1Options();
        ApplySeedAnalysisRouteEventFilter();
        ApplySeedAnalysisRouteRelicFilter();
    }

    private void RefreshSeedAnalysisRouteAct1Options()
    {
        var previousSelectionId = SelectedSeedAnalysisRouteAct1Option?.OptionId;
        SeedAnalysisRouteAct1Options.Clear();

        if (!SeedFormatter.TryNormalize(SeedText, out var normalizedSeed, out _))
        {
            SelectedSeedAnalysisRouteAct1Option = null;
            return;
        }

        var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
        foreach (var option in BuildAct1OpeningOptionResults(seedValue))
        {
            SeedAnalysisRouteAct1Options.Add(SeedAnalysisRouteAct1OptionViewModel.FromOption(option));
        }

        SelectedSeedAnalysisRouteAct1Option = SeedAnalysisRouteAct1Options
            .FirstOrDefault(option => string.Equals(option.OptionId, previousSelectionId, StringComparison.OrdinalIgnoreCase))
            ?? SeedAnalysisRouteAct1Options.FirstOrDefault();
    }

    private void ApplySeedAnalysisRouteEventFilter()
    {
        _filteredSeedAnalysisRouteEventCatalog = FilterCatalog(_eventVisibilityCatalog, _seedAnalysisRouteEventCatalogFilter);
        RaisePropertyChanged(nameof(SeedAnalysisRouteEventCatalogView));
    }

    private void ApplySeedAnalysisRouteRelicFilter()
    {
        _filteredSeedAnalysisRouteRelicCatalog = FilterCatalog(_poolRelicCatalog, _seedAnalysisRouteRelicCatalogFilter);
        RaisePropertyChanged(nameof(SeedAnalysisRouteRelicCatalogView));
    }

    private void AddSeedAnalysisRouteEventTarget()
    {
        AddSeedAnalysisRouteEventTargetFromItem(SelectedSeedAnalysisRouteEventCatalogItem, SelectedSeedAnalysisRouteEventActOption);
    }

    private void AddSeedAnalysisRouteEventTargetFromItem(CatalogItem? selectedItem, RouteActOption? selectedActOption)
    {
        if (TryAddSeedAnalysisRouteTargetChip(
                SeedAnalysisRouteEventTargetChips,
                selectedItem,
                selectedActOption,
                "事件"))
        {
            SelectedSeedAnalysisRouteEventCatalogItem = null;
        }
    }

    private void RemoveSeedAnalysisRouteEventTarget(object? parameter)
    {
        RemoveRouteTargetChipById(SeedAnalysisRouteEventTargetChips, parameter as string);
    }

    private void AddSeedAnalysisRouteRelicTarget()
    {
        AddSeedAnalysisRouteRelicTargetFromItem(SelectedSeedAnalysisRouteRelicCatalogItem, SelectedSeedAnalysisRouteRelicActOption);
    }

    private void AddSeedAnalysisRouteRelicTargetFromItem(CatalogItem? selectedItem, RouteActOption? selectedActOption)
    {
        if (TryAddSeedAnalysisRouteTargetChip(
                SeedAnalysisRouteRelicTargetChips,
                selectedItem,
                selectedActOption,
                "遗物"))
        {
            SelectedSeedAnalysisRouteRelicCatalogItem = null;
        }
    }

    private void RemoveSeedAnalysisRouteRelicTarget(object? parameter)
    {
        RemoveRouteTargetChipById(SeedAnalysisRouteRelicTargetChips, parameter as string);
    }

    private bool TryAddSeedAnalysisRouteTargetChip(
        ObservableCollection<SeedAnalysisRouteTargetChipViewModel> chips,
        CatalogItem? selectedItem,
        RouteActOption? selectedActOption,
        string itemName)
    {
        if (selectedItem == null)
        {
            LogWarn($"请选择要添加的{itemName}。");
            return false;
        }

        var actNumber = selectedActOption?.ActNumber;
        if (chips.Any(chip =>
                chip.ActNumber == actNumber &&
                string.Equals(chip.Value, selectedItem.Value, StringComparison.OrdinalIgnoreCase)))
        {
            LogWarn($"该{itemName}条件已存在。");
            return false;
        }

        chips.Add(SeedAnalysisRouteTargetChipViewModel.FromCatalog(selectedItem, actNumber, GetRouteActLabel(actNumber)));
        return true;
    }

    private static void RemoveRouteTargetChipById(
        ObservableCollection<SeedAnalysisRouteTargetChipViewModel> chips,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var chip = chips.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (chip != null)
        {
            chips.Remove(chip);
        }
    }

    private Task AnalyzeSeedRoutesAsync()
    {
        _seedAnalysisRouteCancellation?.Dispose();
        _seedAnalysisRouteCancellation = new CancellationTokenSource();
        return AnalyzeSeedRoutesCoreAsync(useDefaultRouteWhenNoTargets: false, _seedAnalysisRouteCancellation.Token);
    }

    private void CancelSeedAnalysisRoute()
    {
        _seedAnalysisRouteCancellation?.Cancel();
        SeedAnalysisRouteSummary = "正在停止精确路线分析...";
        SeedAnalysisRouteProgressText = "正在停止路线模拟...";
        StatusMessage = SeedAnalysisRouteSummary;
    }

    private void AddSeedAnalysisRouteCoverageTarget(object? parameter)
    {
        if (parameter is not SeedAnalysisRouteCoverageItemViewModel item)
        {
            return;
        }

        var catalogItem = new CatalogItem(item.Id, item.Name, item.Name);
        var actOption = new RouteActOption(item.ActNumber, GetRouteActLabel(item.ActNumber));
        if (item.IsEvent)
        {
            AddSeedAnalysisRouteEventTargetFromItem(catalogItem, actOption);
        }
        else
        {
            AddSeedAnalysisRouteRelicTargetFromItem(catalogItem, actOption);
        }
    }

    private Task AnalyzeDefaultSeedRouteAsync(CancellationToken cancellationToken = default)
    {
        _seedAnalysisRouteCancellation?.Dispose();
        _seedAnalysisRouteCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return AnalyzeSeedRoutesCoreAsync(useDefaultRouteWhenNoTargets: true, _seedAnalysisRouteCancellation.Token);
    }

    private async Task AnalyzeSeedRoutesCoreAsync(bool useDefaultRouteWhenNoTargets, CancellationToken routeCancellationToken)
    {
        if (_ancientPreviewer == null)
        {
            SeedAnalysisRouteSummary = "当前未加载种子分析所需的 StS2 数据。";
            return;
        }

        if (!SeedFormatter.TryNormalize(SeedText, out var normalizedSeed, out var error))
        {
            SeedAnalysisRouteSummary = error;
            return;
        }

        try
        {
            IsSeedAnalysisRouteAnalyzing = true;
            SeedAnalysisRouteProgressText = "正在准备路线模拟...";
            SeedAnalysisRouteProgressValue = 0;
            var dataset = EnsureSeedAnalysisDataset();
            if (dataset == null)
            {
                SeedAnalysisRouteSummary = "种子分析所需的数据尚未成功加载。";
                return;
            }

            var hasTargets = SeedAnalysisRouteEventTargetChips.Count > 0 || SeedAnalysisRouteRelicTargetChips.Count > 0;
            if (!hasTargets && !useDefaultRouteWhenNoTargets)
            {
                SeedAnalysisRouteSummary = "请至少选择一个目标事件或目标遗物。";
                return;
            }

            var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
            var unlockedCharacters = GetConfiguredUnlockedCharacters();
            var ancientAvailability = ResolveEffectiveAncientAvailability("精确路线分析");
            RefreshSeedAnalysisRouteAct1Options();
            var routeAnalyses = await AnalyzeSeedRoutesForAllAct1OptionsAsync(
                dataset,
                normalizedSeed,
                seedValue,
                unlockedCharacters,
                ancientAvailability,
                useDefaultRouteWhenNoTargets,
                progress =>
                {
                    SeedAnalysisRouteProgressText = progress.Text;
                    SeedAnalysisRouteProgressValue = progress.Value;
                },
                routeCancellationToken);
            await Task.Yield();
            if (routeCancellationToken.IsCancellationRequested)
            {
                SeedAnalysisRouteSummary = "精确路线分析已停止。";
                SeedAnalysisRouteProgressText = SeedAnalysisRouteSummary;
                StatusMessage = SeedAnalysisRouteSummary;
                return;
            }

            ApplySeedAnalysisRoutes(routeAnalyses);
            var matchCount = routeAnalyses.Sum(result => result.Analysis.Matches.Count);
            var checkedRoutes = routeAnalyses.Sum(result => result.Analysis.CheckedRoutes);
            var wasTruncated = routeAnalyses.Any(result => result.Analysis.WasTruncated);
            var droppedShopBranches = routeAnalyses.Sum(result => result.Analysis.ShopOutputBranchesDropped);
            var openingCount = routeAnalyses.Count;
            var modeSummary = BuildSeedAnalysisRouteModeSummary(droppedShopBranches);

            if (!hasTargets && useDefaultRouteWhenNoTargets)
            {
                SeedAnalysisRouteSummary = matchCount > 0
                    ? $"已按当前种子的 {openingCount} 个实际开局选项展示 {matchCount} 条真实路线。已检查 {checkedRoutes} 条路线组合{(wasTruncated ? "（至少一个开局已达到检查上限）" : string.Empty)}。{modeSummary}你也可以再选择目标事件或遗物，继续反查命中路线。"
                    : "暂时没能生成默认路线图。";
            }
            else
            {
                var targetSummary = string.Join(
                    " + ",
                    SeedAnalysisRouteEventTargetChips.Select(chip => $"{chip.ActLabel}事件：{FormatEventId(chip.Value)}")
                        .Concat(SeedAnalysisRouteRelicTargetChips.Select(chip => $"{chip.ActLabel}遗物：{FormatRelicId(chip.Value)}")));

                SeedAnalysisRouteSummary = matchCount > 0
                    ? $"已在 {openingCount} 个实际开局选项中找到 {matchCount} 条命中路线，目标：{targetSummary}。已检查 {checkedRoutes} 条路线组合{(wasTruncated ? "（至少一个开局已达到检查上限）" : string.Empty)}。{modeSummary}"
                    : $"未找到命中路线，目标：{targetSummary}。已检查 {checkedRoutes} 条路线组合{(wasTruncated ? "（至少一个开局已达到检查上限）" : string.Empty)}。{modeSummary}";
            }

            StatusMessage = "精确路线分析完成。";
            SeedAnalysisRouteProgressText = $"路线模拟完成：已遍历 {openingCount} 个开局，检查 {checkedRoutes:N0} 条路线组合。";
            SeedAnalysisRouteProgressValue = 1;
        }
        catch (OperationCanceledException) when (routeCancellationToken.IsCancellationRequested)
        {
            SeedAnalysisRouteSummary = "精确路线分析已停止。";
            SeedAnalysisRouteProgressText = SeedAnalysisRouteSummary;
            StatusMessage = SeedAnalysisRouteSummary;
        }
        catch (Exception ex)
        {
            ClearSeedAnalysisRouteResults();
            SeedAnalysisRouteSummary = $"精确路线分析失败：{ex.Message}";
            SeedAnalysisRouteProgressText = SeedAnalysisRouteSummary;
            StatusMessage = SeedAnalysisRouteSummary;
            LogError(SeedAnalysisRouteSummary);
        }
        finally
        {
            IsSeedAnalysisRouteAnalyzing = false;
            _seedAnalysisRouteCancellation?.Dispose();
            _seedAnalysisRouteCancellation = null;
        }
    }

    private async Task<IReadOnlyList<SeedAnalysisRouteAnalysisResult>> AnalyzeSeedRoutesForAllAct1OptionsAsync(
        NeowOptionDataset dataset,
        string normalizedSeed,
        uint seedValue,
        IReadOnlyList<CharacterId> unlockedCharacters,
        Sts2AncientAvailability ancientAvailability,
        bool useDefaultRouteWhenNoTargets,
        Action<SeedAnalysisRouteProgressUpdate>? reportProgress = null)
    {
        return await AnalyzeSeedRoutesForAllAct1OptionsAsync(
            dataset,
            normalizedSeed,
            seedValue,
            unlockedCharacters,
            ancientAvailability,
            useDefaultRouteWhenNoTargets,
            reportProgress,
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<SeedAnalysisRouteAnalysisResult>> AnalyzeSeedRoutesForAllAct1OptionsAsync(
        NeowOptionDataset dataset,
        string normalizedSeed,
        uint seedValue,
        IReadOnlyList<CharacterId> unlockedCharacters,
        Sts2AncientAvailability ancientAvailability,
        bool useDefaultRouteWhenNoTargets,
        Action<SeedAnalysisRouteProgressUpdate>? reportProgress,
        CancellationToken routeCancellationToken)
    {
        var act1Options = SeedAnalysisRouteAct1Options.ToList();
        if (act1Options.Count == 0)
        {
            act1Options.Add(SeedAnalysisRouteAct1OptionViewModel.Empty());
        }

        var eventTargets = SeedAnalysisRouteEventTargetChips
            .Select(chip => new Sts2ExactRouteEventTargetRequest(chip.ActNumber, chip.Value))
            .ToList();
        var relicTargets = SeedAnalysisRouteRelicTargetChips
            .Select(chip => new Sts2ExactRouteRelicTargetRequest(chip.ActNumber, chip.Value))
            .ToList();
        var hasTargets = eventTargets.Count > 0 || relicTargets.Count > 0;
        var coverageOnly = useDefaultRouteWhenNoTargets && !hasTargets;
        var maxResults = GetSeedAnalysisRouteMaxResults();
        var maxRouteChecks = GetSeedAnalysisRouteMaxChecks();
        var simulationMode = IsSeedAnalysisRouteFastMode
            ? Sts2ExactRouteSimulationMode.FastShopLimited
            : Sts2ExactRouteSimulationMode.Strict;
        var shopOutputLimit = IsSeedAnalysisRouteFastMode ? GetSeedAnalysisRouteShopOutputLimit() : 0;
        var totalMaxChecks = maxRouteChecks;
        long totalCheckedRoutes = 0;
        var results = new List<SeedAnalysisRouteAnalysisResult>(act1Options.Count);

        for (var index = 0; index < act1Options.Count; index++)
        {
            if (routeCancellationToken.IsCancellationRequested)
            {
                break;
            }

            var allocatedRouteChecks = GetAllocatedRouteChecks(maxRouteChecks, act1Options.Count, index);
            if (allocatedRouteChecks <= 0)
            {
                continue;
            }

            var act1Option = act1Options[index];
            var completedBeforeOpening = totalCheckedRoutes;
            reportProgress?.Invoke(CreateRouteProgressUpdate(
                $"正在模拟路线：已检查 {totalCheckedRoutes:N0}/{totalMaxChecks:N0} 条路线组合，当前开局：{act1Option.Display}",
                totalCheckedRoutes,
                totalMaxChecks));
            var progress = new Progress<Sts2ExactRouteProgress>(update =>
            {
                var currentCheckedRoutes = completedBeforeOpening + update.CheckedRoutes;
                var startedRoutes = completedBeforeOpening + update.StartedRoutes;
                reportProgress?.Invoke(CreateRouteProgressUpdate(
                    $"正在模拟路线：正在检查第 {startedRoutes:N0}/{totalMaxChecks:N0} 条，已完成 {currentCheckedRoutes:N0} 条，当前开局：{act1Option.Display}，当前命中 {results.Sum(result => result.Analysis.FoundRouteCount) + update.FoundRoutes:N0} 条。",
                    Math.Max(startedRoutes, currentCheckedRoutes),
                    totalMaxChecks));
            });
            var request = new Sts2ExactRouteAnalysisRequest
            {
                SeedText = normalizedSeed,
                SeedValue = seedValue,
                Character = SelectedCharacter,
                UnlockedCharacters = unlockedCharacters,
                AscensionLevel = SelectedAscensionLevel,
                PlayerCount = 1,
                AncientAvailability = ancientAvailability,
                Act1OpeningOption = act1Option.Option,
                EventTargets = eventTargets,
                RelicTargets = relicTargets,
                ShopStrategy = coverageOnly ? Sts2ExactRouteShopStrategy.NoPurchase : Sts2ExactRouteShopStrategy.TargetRelicsOnly,
                SimulationMode = simulationMode,
                ShopOutputLimit = shopOutputLimit,
                Preference = Sts2ExactRoutePreference.None,
                MaxResults = maxResults,
                MaxRouteChecks = allocatedRouteChecks,
                Progress = progress,
                CancellationToken = routeCancellationToken
            };

            LogInfo(
                $"[精确路线分析] 请求: seed={normalizedSeed}, act1={request.Act1OpeningOption?.RelicId ?? "(none)"}, events={string.Join(",", request.EventTargets.Select(target => $"{target.ActNumber}:{target.EventId}"))}, relics={string.Join(",", request.RelicTargets.Select(target => $"{target.ActNumber}:{target.RelicId}"))}, shopStrategy={request.ShopStrategy}, mode={request.SimulationMode}, shopOutputLimit={request.ShopOutputLimit}, preference={request.Preference}, maxResults={request.MaxResults}, maxChecks={request.MaxRouteChecks}, default={useDefaultRouteWhenNoTargets}");

            results.Add(new SeedAnalysisRouteAnalysisResult(
                act1Option,
                await Task.Run(() => _ancientPreviewer!.AnalyzeExactRoutes(dataset, request))));
            var latest = results[^1].Analysis;
            totalCheckedRoutes += latest.CheckedRoutes;
            reportProgress?.Invoke(CreateRouteProgressUpdate(
                $"已检查 {totalCheckedRoutes:N0}/{totalMaxChecks:N0} 条路线组合，当前命中 {results.Sum(result => result.Analysis.FoundRouteCount):N0} 条。",
                totalCheckedRoutes,
                totalMaxChecks));
        }

        return results;
    }

    private void ApplySeedAnalysisRoutes(IReadOnlyList<SeedAnalysisRouteAnalysisResult> routeAnalyses)
    {
        ClearSeedAnalysisRouteResults();
        ApplySeedAnalysisRouteCoverage(routeAnalyses);

        var routeIndex = 1;
        foreach (var result in routeAnalyses)
        {
            var mapActs = result.Analysis.MapActs.ToDictionary(act => act.ActNumber, act => act, EqualityComparer<int>.Default);
            foreach (var match in result.Analysis.Matches)
            {
                var acts = match.Acts
                    .OrderBy(act => act.ActNumber)
                    .Select(act => BuildSeedAnalysisRouteAct(act, mapActs.GetValueOrDefault(act.ActNumber)))
                    .ToList();
                var hitSummary = BuildRouteHitSummary(match);
                SeedAnalysisRouteMatches.Add(new SeedAnalysisRouteMatchViewModel(
                    $"路线 {routeIndex:D2} · {result.Act1Option.Display}",
                    hitSummary,
                    acts,
                    routeIndex == 1));
                routeIndex++;
            }
        }

        HasSeedAnalysisRouteResult = true;
        RaisePropertyChanged(nameof(HasSeedAnalysisRouteMatches));
    }

    private void ApplySeedAnalysisRouteCoverage(IReadOnlyList<SeedAnalysisRouteAnalysisResult> routeAnalyses)
    {
        SeedAnalysisRouteCoverageEvents.Clear();
        SeedAnalysisRouteCoverageRelics.Clear();

        var eventItems = MergeCoverage(
                routeAnalyses,
                result => result.Analysis.Coverage.EventCoverage,
                FormatEventId,
                isEvent: true)
            .Take(16)
            .ToList();
        var relicItems = MergeCoverage(
                routeAnalyses,
                result => result.Analysis.Coverage.RelicCoverage,
                FormatRelicId,
                isEvent: false)
            .Take(16)
            .ToList();

        foreach (var item in eventItems)
        {
            SeedAnalysisRouteCoverageEvents.Add(item);
        }

        foreach (var item in relicItems)
        {
            SeedAnalysisRouteCoverageRelics.Add(item);
        }

        var checkedRoutes = routeAnalyses.Sum(result => result.Analysis.CheckedRoutes);
        var coveredRoutes = routeAnalyses.Sum(result => result.Analysis.Coverage.TotalRoutes);
        SeedAnalysisRouteCoverageSummary =
            checkedRoutes <= 0
                ? "路线覆盖率会在分析后生成。"
                : $"基于当前策略已检查 {checkedRoutes:N0} 条路线组合，其中 {coveredRoutes:N0} 条可完整模拟；下表按出现率、覆盖路线数和首次层数排序。";
    }

    private IEnumerable<SeedAnalysisRouteCoverageItemViewModel> MergeCoverage(
        IReadOnlyList<SeedAnalysisRouteAnalysisResult> routeAnalyses,
        Func<SeedAnalysisRouteAnalysisResult, IReadOnlyList<Sts2ExactRouteCoverageItem>> selector,
        Func<string, string> displayNameFormatter,
        bool isEvent)
    {
        return routeAnalyses
            .SelectMany(result => selector(result).Select(item => (result, item)))
            .GroupBy(entry => (entry.item.ActNumber, entry.item.Id), RouteCoverageKeyComparer.Instance)
            .Select(group =>
            {
                var entries = group.ToList();
                var seen = entries.Sum(entry => entry.item.SeenRouteCount);
                var total = entries.Sum(entry => entry.item.TotalRouteCount);
                var firstRows = entries
                    .SelectMany(entry => new[] { entry.item.FirstRowMin, entry.item.FirstRowMax })
                    .Where(row => row.HasValue)
                    .Select(row => row!.Value)
                    .ToList();
                var sources = entries
                    .SelectMany(entry => entry.item.Sources)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(FormatRouteRoomType)
                    .ToList();
                var openings = entries
                    .Select(entry => entry.result.Act1Option.Display)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new SeedAnalysisRouteCoverageItemViewModel(
                    group.Key.Id,
                    displayNameFormatter(group.Key.Id),
                    group.Key.ActNumber,
                    GetRouteActLabel(group.Key.ActNumber),
                    seen,
                    total,
                    firstRows.Count > 0 ? firstRows.Min() : null,
                    firstRows.Count > 0 ? firstRows.Max() : null,
                    sources,
                    openings,
                    isEvent);
            })
            .OrderByDescending(item => item.CoverageRatio)
            .ThenByDescending(item => item.SeenRouteCount)
            .ThenBy(item => item.FirstRowMin ?? int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
    }

    private SeedAnalysisRouteActViewModel BuildSeedAnalysisRouteAct(Sts2ExactRouteAct act, Sts2ExactMapAct? mapAct)
    {
        var roomViewModels = act.Rooms
            .Select(room =>
            {
                var eventMatched = SeedAnalysisRouteEventTargetChips.Any(chip =>
                    (!chip.ActNumber.HasValue || chip.ActNumber.Value == act.ActNumber) &&
                    RouteIdEquals(room.EventId, chip.Value));
                var relicMatched = SeedAnalysisRouteRelicTargetChips.Any(chip =>
                    (!chip.ActNumber.HasValue || chip.ActNumber.Value == act.ActNumber) &&
                    room.RelicIds.Any(relicId => RouteIdEquals(relicId, chip.Value)));

                var roomSummary = $"第 {room.Row} 层 / 列 {room.Col} · {FormatRoutePointType(room.PointType)} -> {FormatRouteRoomType(room.RoomType)}";
                var detailParts = new List<string>();
                if (string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase) &&
                    room.DisplayRelicIds.Count > 0)
                {
                    detailParts.Add("商店陈列：" + string.Join("、", room.DisplayRelicIds.Select(FormatRelicId)));
                }

                if (!string.IsNullOrWhiteSpace(room.EventId))
                {
                    detailParts.Add($"事件：{FormatEventId(room.EventId)}");
                }

                if (room.RelicIds.Count > 0)
                {
                    detailParts.Add("实际获得：" + string.Join("、", room.RelicIds.Select(FormatRelicId)));
                }

                return new SeedAnalysisRouteRoomViewModel(
                    roomSummary,
                    string.Join(" | ", detailParts),
                    eventMatched || relicMatched,
                    room.Row,
                    room.Col,
                    room.PointType,
                    room.RoomType,
                    room.EventId,
                    room.RelicIds);
            })
            .ToList();

        var ancientParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(act.AncientId))
        {
            ancientParts.Add($"古神：{AncientDisplayCatalog.GetDisplayText(act.AncientId, act.AncientId)}");
        }

        if (act.AncientRelics.Count > 0)
        {
            ancientParts.Add("古神遗物：" + string.Join("、", act.AncientRelics.Select(FormatRelicId)));
        }

        var map = BuildRouteMap(mapAct, roomViewModels, act.Steps);
        return new SeedAnalysisRouteActViewModel(
            $"第 {act.ActNumber} 幕",
            string.Join(" | ", ancientParts),
            roomViewModels,
            map.Nodes,
            map.Links,
            map.Width,
            map.Height);
    }

    private static (IReadOnlyList<SeedAnalysisRouteMapNodeViewModel> Nodes, IReadOnlyList<SeedAnalysisRouteMapLinkViewModel> Links, double Width, double Height)
        BuildRouteMap(
            Sts2ExactMapAct? mapAct,
            IReadOnlyList<SeedAnalysisRouteRoomViewModel> rooms,
            IReadOnlyList<Sts2ExactRouteStep> steps)
    {
        const double margin = 24d;
        const double colStep = 72d;
        const double rowStep = 68d;
        const double nodeSize = 28d;

        var roomByCoord = rooms.ToDictionary(room => (room.Row, room.Col));
        var highlightedNodes = new HashSet<(int Row, int Col)>(rooms.Select(room => (room.Row, room.Col)));
        var highlightedLinks = new HashSet<(int FromRow, int FromCol, int ToRow, int ToCol)>(
            steps.Select(step => (step.FromRow, step.FromCol, step.ToRow, step.ToCol)));
        var sourceNodes = (mapAct?.Nodes
            ?? rooms.Select(room => new Sts2ExactMapNode
            {
                Row = room.Row,
                Col = room.Col,
                PointType = room.PointType
            }).ToList())
            .ToList();
        var sourceLinks = (mapAct?.Links
            ?? steps.Select(step => new Sts2ExactMapLink
            {
                FromRow = step.FromRow,
                FromCol = step.FromCol,
                ToRow = step.ToRow,
                ToCol = step.ToCol
            }).ToList())
            .ToList();

        AddSyntheticStartNode(sourceNodes, sourceLinks);

        var maxCol = sourceNodes.Count == 0 ? 0 : sourceNodes.Max(node => node.Col);
        var maxRow = sourceNodes.Count == 0 ? 0 : sourceNodes.Max(node => node.Row);
        var minRow = sourceNodes.Count == 0 ? 0 : sourceNodes.Min(node => node.Row);
        var nodes = sourceNodes
            .OrderBy(node => node.Row)
            .ThenBy(node => node.Col)
            .Select(node =>
            {
                var centerX = margin + (node.Col * colStep);
                var centerY = margin + ((maxRow - node.Row) * rowStep);
                roomByCoord.TryGetValue((node.Row, node.Col), out var routeRoom);
                var isHighlighted = highlightedNodes.Contains((node.Row, node.Col));
                var isMatched = routeRoom?.IsMatched == true;
                return new SeedAnalysisRouteMapNodeViewModel(
                    node.Row,
                    node.Col,
                    centerX - (nodeSize / 2d),
                    centerY - (nodeSize / 2d),
                    centerX,
                    centerY,
                    nodeSize,
                    GetRouteNodeFill(node.PointType, routeRoom?.RoomType, isHighlighted, isMatched),
                    isMatched ? "#C68A00" : isHighlighted ? "#7A7A7A" : "#D6D6D6",
                    GetRouteNodeLabel(node.PointType, routeRoom?.RoomType),
                    routeRoom?.Summary ?? $"第 {node.Row} 层 / 列 {node.Col} · {FormatRoutePointType(node.PointType)}",
                    routeRoom?.Detail ?? string.Empty,
                    isMatched,
                    isHighlighted);
            })
            .ToList();

        var nodeCenterMap = nodes.ToDictionary(node => (node.Row, node.Col));
        var links = new List<SeedAnalysisRouteMapLinkViewModel>();
        foreach (var link in sourceLinks)
        {
            if (!nodeCenterMap.TryGetValue((link.FromRow, link.FromCol), out var fromNode) ||
                !nodeCenterMap.TryGetValue((link.ToRow, link.ToCol), out var toNode))
            {
                continue;
            }

            var isHighlighted = highlightedLinks.Contains((link.FromRow, link.FromCol, link.ToRow, link.ToCol));
            var isMatched = (fromNode.IsMatched || toNode.IsMatched) && isHighlighted;
            links.Add(new SeedAnalysisRouteMapLinkViewModel(
                fromNode.CenterX,
                fromNode.CenterY,
                toNode.CenterX,
                toNode.CenterY,
                isMatched ? "#D9A441" : isHighlighted ? "#6E6E6E" : "#DADADA",
                isMatched ? 4d : isHighlighted ? 3d : 1.5d,
                isHighlighted));
        }

        return (
            nodes,
            links,
            Math.Max(220d, (maxCol + 1) * colStep + (margin * 2)),
            Math.Max(120d, Math.Max(1, maxRow - minRow + 1) * rowStep + (margin * 2)));
    }

    private static void AddSyntheticStartNode(
        IList<Sts2ExactMapNode> sourceNodes,
        IList<Sts2ExactMapLink> sourceLinks)
    {
        if (sourceNodes.Count == 0 || sourceNodes.Any(node => node.Row <= 0))
        {
            return;
        }

        var firstRowNumber = sourceNodes.Min(node => node.Row);
        var firstRowNodes = sourceNodes
            .Where(node => node.Row == firstRowNumber)
            .OrderBy(node => node.Col)
            .ToList();
        if (firstRowNodes.Count == 0)
        {
            return;
        }

        var startCol = (int)Math.Round(firstRowNodes.Average(node => node.Col), MidpointRounding.AwayFromZero);
        sourceNodes.Add(new Sts2ExactMapNode
        {
            Row = 0,
            Col = startCol,
            PointType = "Start"
        });

        foreach (var node in firstRowNodes)
        {
            sourceLinks.Add(new Sts2ExactMapLink
            {
                FromRow = 0,
                FromCol = startCol,
                ToRow = node.Row,
                ToCol = node.Col
            });
        }
    }

    private static string BuildMapNodeTitle(Sts2ExactMapNode node)
    {
        return string.Equals(node.PointType, "Start", StringComparison.OrdinalIgnoreCase)
            ? "起点"
            : $"第 {node.Row} 层 / 列 {node.Col} · {FormatRoutePointType(node.PointType)}";
    }

    private string BuildRouteHitSummary(Sts2ExactRouteMatch match)
    {
        var parts = new List<string>();
        foreach (var act in match.Acts)
        {
            foreach (var room in act.Rooms)
            {
                foreach (var chip in SeedAnalysisRouteEventTargetChips.Where(chip =>
                             (!chip.ActNumber.HasValue || chip.ActNumber.Value == act.ActNumber) &&
                             RouteIdEquals(room.EventId, chip.Value)))
                {
                    parts.Add($"{chip.ActLabel}事件：{FormatEventId(room.EventId!)}（第 {room.Row} 层）");
                }

                foreach (var chip in SeedAnalysisRouteRelicTargetChips.Where(chip =>
                             !chip.ActNumber.HasValue || chip.ActNumber.Value == act.ActNumber))
                {
                    foreach (var relicId in room.RelicIds.Where(relicId => RouteIdEquals(relicId, chip.Value)))
                    {
                        parts.Add($"{chip.ActLabel}遗物：{FormatRelicId(relicId)}（第 {room.Row} 层）");
                    }
                }
            }
        }

        return parts.Count > 0
            ? string.Join("；", parts.Distinct(StringComparer.OrdinalIgnoreCase))
            : "该路线命中目标。";
    }

    private static bool RouteIdEquals(string? left, string? right)
    {
        return string.Equals(RouteNormalizeId(left), RouteNormalizeId(right), StringComparison.Ordinal);
    }

    private static string RouteNormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static string GetRouteActLabel(int? actNumber)
    {
        return actNumber switch
        {
            1 => "第一幕",
            2 => "第二幕",
            3 => "第三幕",
            _ => "任意幕"
        };
    }

    private static string FormatRoutePointType(string pointType)
    {
        return pointType switch
        {
            "Start" => "起点",
            "Unknown" => "问号房",
            "Monster" => "战斗",
            "Elite" => "精英",
            "Treasure" => "宝箱",
            "Shop" => "商店",
            "RestSite" => "营火",
            "Ancient" => "古神",
            _ => pointType
        };
    }

    private static string FormatRouteRoomType(string roomType)
    {
        return roomType switch
        {
            "Event" => "事件",
            "Monster" => "战斗",
            "Elite" => "精英",
            "Treasure" => "宝箱",
            "Shop" => "商店",
            "RestSite" => "营火",
            _ => roomType
        };
    }

    private static string GetRouteNodeFill(string pointType, string? roomType, bool isHighlighted, bool isMatched)
    {
        if (!isHighlighted)
        {
            return pointType switch
            {
                "Start" => "#FFFFFF",
                "Unknown" => "#F1F1F1",
                "Monster" => "#ECECEC",
                "Elite" => "#F3E1DB",
                "Treasure" => "#F6EAB9",
                "Shop" => "#E4EFFA",
                "RestSite" => "#E2F0E6",
                _ => "#F0F0F0"
            };
        }

        return roomType switch
        {
            "Event" => isMatched ? "#F4C542" : "#F6E8B1",
            "Monster" => "#D9D9D9",
            "Elite" => isMatched ? "#E07A5F" : "#E8B4A8",
            "Treasure" => isMatched ? "#E0B020" : "#F3D77C",
            "Shop" => isMatched ? "#6FA8DC" : "#B7D3F2",
            "RestSite" => "#A8D5BA",
            _ => string.Equals(pointType, "Start", StringComparison.OrdinalIgnoreCase) ? "#FFFFFF" : "#E5E5E5"
        };
    }

    private static string GetRouteNodeLabel(string pointType, string? roomType)
    {
        if (string.Equals(pointType, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(roomType))
        {
            return "?";
        }

        return roomType switch
        {
            "Event" => "?",
            "Monster" => "M",
            "Elite" => "E",
            "Treasure" => "T",
            "Shop" => "$",
            "RestSite" => "R",
            _ => pointType switch
            {
                "Start" => "起",
                "Unknown" => "?",
                "Monster" => "M",
                "Elite" => "E",
                "Treasure" => "T",
                "Shop" => "$",
                "RestSite" => "R",
                _ => !string.IsNullOrWhiteSpace(pointType) ? pointType[..1] : string.Empty
            }
        };
    }

    private int GetSeedAnalysisRouteMaxResults()
    {
        return int.TryParse(SeedAnalysisRouteMaxResultsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, MaxExactRouteResults)
            : DefaultExactRouteMaxResults;
    }

    private static long GetAllocatedRouteChecks(long totalMaxRouteChecks, int openingCount, int openingIndex)
    {
        if (totalMaxRouteChecks <= 0 || openingCount <= 0 || openingIndex < 0 || openingIndex >= openingCount)
        {
            return 0;
        }

        var baseAllocation = totalMaxRouteChecks / openingCount;
        var remainder = totalMaxRouteChecks % openingCount;
        return baseAllocation + (openingIndex < remainder ? 1 : 0);
    }

    private long GetSeedAnalysisRouteMaxChecks()
    {
        return long.TryParse(SeedAnalysisRouteMaxChecksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, MaxExactRouteChecks)
            : DefaultExactRouteMaxChecks;
    }

    private int GetSeedAnalysisRouteShopOutputLimit()
    {
        return int.TryParse(SeedAnalysisRouteShopOutputLimitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, MaxExactRouteFastShopOutputLimit)
            : DefaultExactRouteFastShopOutputLimit;
    }

    private string BuildSeedAnalysisRouteModeSummary(long droppedShopBranches)
    {
        if (!IsSeedAnalysisRouteFastMode)
        {
            return "严格模式未裁剪商店分支。";
        }

        return $"快速模式商店输出上限 {GetSeedAnalysisRouteShopOutputLimit()}，已裁剪 {droppedShopBranches:N0} 条商店分支，结果为近似。";
    }

    private void ClearSeedAnalysisRouteResults()
    {
        SeedAnalysisRouteMatches.Clear();
        SeedAnalysisRouteCoverageEvents.Clear();
        SeedAnalysisRouteCoverageRelics.Clear();
        SeedAnalysisRouteCoverageSummary = "路线覆盖率会在分析后生成。";
        SeedAnalysisRouteProgressText = "尚未开始路线模拟。";
        SeedAnalysisRouteProgressValue = 0;
        HasSeedAnalysisRouteResult = false;
        RaisePropertyChanged(nameof(HasSeedAnalysisRouteMatches));
    }

    private void ResetSeedAnalysisRoutes()
    {
        SeedAnalysisRouteSummary = "选择目标事件或遗物后，搜索命中路线。";
        SeedAnalysisRouteMaxResultsText = DefaultExactRouteMaxResults.ToString(CultureInfo.InvariantCulture);
        SeedAnalysisRouteMaxChecksText = DefaultExactRouteMaxChecks.ToString(CultureInfo.InvariantCulture);
        IsSeedAnalysisRouteFastMode = true;
        SeedAnalysisRouteShopOutputLimitText = DefaultExactRouteFastShopOutputLimit.ToString(CultureInfo.InvariantCulture);
        SelectedSeedAnalysisRouteEventCatalogItem = null;
        SelectedSeedAnalysisRouteRelicCatalogItem = null;
        SelectedSeedAnalysisRouteAct1Option = null;
        SelectedSeedAnalysisRouteEventActOption = SeedAnalysisRouteEventActOptions.First();
        SelectedSeedAnalysisRouteRelicActOption = SeedAnalysisRouteRelicActOptions.First();
        SeedAnalysisRouteAct1Options.Clear();
        SeedAnalysisRouteEventTargetChips.Clear();
        SeedAnalysisRouteRelicTargetChips.Clear();
        ClearSeedAnalysisRouteResults();
        RefreshSeedAnalysisRouteAct1Options();
    }

    internal sealed record RouteActOption(int? ActNumber, string DisplayName);

    private sealed record SeedAnalysisRouteAnalysisResult(
        SeedAnalysisRouteAct1OptionViewModel Act1Option,
        Sts2ExactRouteAnalysis Analysis);

    private sealed record SeedAnalysisRouteProgressUpdate(string Text, double Value);

    private static SeedAnalysisRouteProgressUpdate CreateRouteProgressUpdate(
        string text,
        long checkedRoutes,
        long totalMaxChecks)
    {
        var denominator = Math.Max(1, totalMaxChecks);
        return new SeedAnalysisRouteProgressUpdate(text, (double)Math.Clamp(checkedRoutes, 0, denominator) / denominator);
    }

    private sealed class RouteCoverageKeyComparer : IEqualityComparer<(int ActNumber, string Id)>
    {
        public static RouteCoverageKeyComparer Instance { get; } = new();

        public bool Equals((int ActNumber, string Id) x, (int ActNumber, string Id) y) =>
            x.ActNumber == y.ActNumber && string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int ActNumber, string Id) obj) =>
            HashCode.Combine(obj.ActNumber, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id));
    }

    internal sealed class SeedAnalysisRouteCoverageItemViewModel
    {
        public SeedAnalysisRouteCoverageItemViewModel(
            string id,
            string name,
            int actNumber,
            string actLabel,
            int seenRouteCount,
            int totalRouteCount,
            int? firstRowMin,
            int? firstRowMax,
            IReadOnlyList<string> sources,
            IReadOnlyList<string> openings,
            bool isEvent)
        {
            Id = id;
            Name = name;
            ActNumber = actNumber;
            ActLabel = actLabel;
            SeenRouteCount = seenRouteCount;
            TotalRouteCount = Math.Max(1, totalRouteCount);
            FirstRowMin = firstRowMin;
            FirstRowMax = firstRowMax;
            Sources = sources;
            Openings = openings;
            IsEvent = isEvent;
        }

        public string Id { get; }

        public string Name { get; }

        public int ActNumber { get; }

        public string ActLabel { get; }

        public int SeenRouteCount { get; }

        public int TotalRouteCount { get; }

        public int? FirstRowMin { get; }

        public int? FirstRowMax { get; }

        public IReadOnlyList<string> Sources { get; }

        public IReadOnlyList<string> Openings { get; }

        public bool IsEvent { get; }

        public double CoverageRatio => TotalRouteCount <= 0 ? 0d : (double)SeenRouteCount / TotalRouteCount;

        public string CoverageText => $"{CoverageRatio:P1} ({SeenRouteCount:N0}/{TotalRouteCount:N0})";

        public string FirstRowText =>
            (FirstRowMin, FirstRowMax) switch
            {
                (int min, int max) when min == max => $"第 {min} 层",
                (int min, int max) => $"第 {min}-{max} 层",
                _ => "-"
            };

        public string SourceText => Sources.Count == 0 ? "-" : string.Join("、", Sources);

        public string OpeningText => Openings.Count == 0 ? "-" : string.Join("、", Openings.Take(3)) + (Openings.Count > 3 ? $" 等 {Openings.Count} 个" : string.Empty);
    }

    internal sealed class SeedAnalysisRouteAct1OptionViewModel
    {
        public SeedAnalysisRouteAct1OptionViewModel(
            NeowOptionResult? option,
            string optionId,
            string display,
            string description)
        {
            Option = option;
            OptionId = optionId;
            Display = display;
            Description = description;
        }

        public NeowOptionResult? Option { get; }

        public string OptionId { get; }

        public string Display { get; }

        public string Description { get; }

        public static SeedAnalysisRouteAct1OptionViewModel FromOption(NeowOptionResult option)
        {
            var title = SanitizeLocalizationText(option.Title ?? option.RelicId);
            var note = FormatNeowOptionNote(option);
            var details = option.Details
                .Select(FormatRewardDetail)
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .ToList();
            return new SeedAnalysisRouteAct1OptionViewModel(
                option,
                option.Id,
                $"{title} [{note}]",
                string.Join(" | ", details));
        }

        public static SeedAnalysisRouteAct1OptionViewModel Empty() =>
            new(null, string.Empty, "无可用开局选项", string.Empty);
    }

    internal sealed class SeedAnalysisRouteTargetChipViewModel
    {
        public SeedAnalysisRouteTargetChipViewModel(string value, string label, int? actNumber, string actLabel)
        {
            Id = Guid.NewGuid().ToString("N");
            Value = value;
            Label = label;
            ActNumber = actNumber;
            ActLabel = actLabel;
        }

        public string Id { get; }

        public string Value { get; }

        public string Label { get; }

        public int? ActNumber { get; }

        public string ActLabel { get; }

        public string Display => $"{ActLabel} 路 {Label}";

        public static SeedAnalysisRouteTargetChipViewModel FromCatalog(CatalogItem item, int? actNumber, string actLabel) =>
            new(item.Value, item.Display, actNumber, actLabel);
    }

    internal sealed class SeedAnalysisRouteMatchViewModel
    {
        public SeedAnalysisRouteMatchViewModel(
            string title,
            string hitSummary,
            IReadOnlyList<SeedAnalysisRouteActViewModel> acts,
            bool isExpandedByDefault)
        {
            Title = title;
            HitSummary = hitSummary;
            Acts = acts;
            IsExpandedByDefault = isExpandedByDefault;
        }

        public string Title { get; }

        public string HitSummary { get; }

        public IReadOnlyList<SeedAnalysisRouteActViewModel> Acts { get; }

        public bool IsExpandedByDefault { get; set; }
    }

    internal sealed class SeedAnalysisRouteActViewModel
    {
        public SeedAnalysisRouteActViewModel(
            string title,
            string ancientSummary,
            IReadOnlyList<SeedAnalysisRouteRoomViewModel> rooms,
            IReadOnlyList<SeedAnalysisRouteMapNodeViewModel> mapNodes,
            IReadOnlyList<SeedAnalysisRouteMapLinkViewModel> mapLinks,
            double mapWidth,
            double mapHeight)
        {
            Title = title;
            AncientSummary = ancientSummary;
            Rooms = rooms;
            MapNodes = mapNodes;
            MapLinks = mapLinks;
            MapWidth = mapWidth;
            MapHeight = mapHeight;
        }

        public string Title { get; }

        public string AncientSummary { get; }

        public IReadOnlyList<SeedAnalysisRouteRoomViewModel> Rooms { get; }

        public IReadOnlyList<SeedAnalysisRouteMapNodeViewModel> MapNodes { get; }

        public IReadOnlyList<SeedAnalysisRouteMapLinkViewModel> MapLinks { get; }

        public double MapWidth { get; }

        public double MapHeight { get; }

        public bool HasAncientSummary => !string.IsNullOrWhiteSpace(AncientSummary);
    }

    internal sealed class SeedAnalysisRouteRoomViewModel
    {
        public SeedAnalysisRouteRoomViewModel(
            string summary,
            string detail,
            bool isMatched,
            int row,
            int col,
            string pointType,
            string roomType,
            string? eventId,
            IReadOnlyList<string> relicIds)
        {
            Summary = summary;
            Detail = detail;
            IsMatched = isMatched;
            Row = row;
            Col = col;
            PointType = pointType;
            RoomType = roomType;
            EventId = eventId;
            RelicIds = relicIds;
        }

        public string Summary { get; }

        public string Detail { get; }

        public bool IsMatched { get; }

        public int Row { get; }

        public int Col { get; }

        public string PointType { get; }

        public string RoomType { get; }

        public string? EventId { get; }

        public IReadOnlyList<string> RelicIds { get; }

        public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    }

    internal sealed class SeedAnalysisRouteMapNodeViewModel
    {
        public SeedAnalysisRouteMapNodeViewModel(
            int row,
            int col,
            double left,
            double top,
            double centerX,
            double centerY,
            double size,
            string fill,
            string stroke,
            string label,
            string title,
            string detail,
            bool isMatched,
            bool isHighlighted)
        {
            Row = row;
            Col = col;
            Left = left;
            Top = top;
            CenterX = centerX;
            CenterY = centerY;
            Size = size;
            Fill = fill;
            Stroke = stroke;
            Label = label;
            Title = title;
            Detail = detail;
            IsMatched = isMatched;
            IsHighlighted = isHighlighted;
        }

        public int Row { get; }

        public int Col { get; }

        public double Left { get; }

        public double Top { get; }

        public double CenterX { get; }

        public double CenterY { get; }

        public double Size { get; }

        public string Fill { get; }

        public string Stroke { get; }

        public string Label { get; }

        public string Title { get; }

        public string Detail { get; }

        public bool IsMatched { get; }

        public bool IsHighlighted { get; }

        public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    }

    internal sealed class SeedAnalysisRouteMapLinkViewModel
    {
        public SeedAnalysisRouteMapLinkViewModel(double x1, double y1, double x2, double y2, string stroke, double strokeThickness, bool isHighlighted)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Stroke = stroke;
            StrokeThickness = strokeThickness;
            IsHighlighted = isHighlighted;
        }

        public double X1 { get; }

        public double Y1 { get; }

        public double X2 { get; }

        public double Y2 { get; }

        public string Stroke { get; }

        public double StrokeThickness { get; }

        public bool IsHighlighted { get; }
    }
}
