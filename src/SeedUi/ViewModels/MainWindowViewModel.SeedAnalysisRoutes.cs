using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Seeds;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private const int DefaultExactRouteMaxResults = 3;
    private const long DefaultExactRouteMaxChecks = 20_000;
    private const int MaxExactRouteResults = 20;
    private const long MaxExactRouteChecks = 1_000_000;

    private RelayCommand? _analyzeSeedRoutesCommand;
    private RelayCommand? _addSeedAnalysisRouteEventTargetCommand;
    private RelayCommand? _removeSeedAnalysisRouteEventTargetCommand;
    private RelayCommand? _addSeedAnalysisRouteRelicTargetCommand;
    private RelayCommand? _removeSeedAnalysisRouteRelicTargetCommand;
    private string _seedAnalysisRouteSummary = "选择目标事件或遗物后，搜索命中路线。";
    private string _seedAnalysisRouteEventCatalogFilter = string.Empty;
    private string _seedAnalysisRouteRelicCatalogFilter = string.Empty;
    private string _seedAnalysisRouteMaxResultsText = DefaultExactRouteMaxResults.ToString(CultureInfo.InvariantCulture);
    private string _seedAnalysisRouteMaxChecksText = DefaultExactRouteMaxChecks.ToString(CultureInfo.InvariantCulture);
    private CatalogItem? _selectedSeedAnalysisRouteEventCatalogItem;
    private CatalogItem? _selectedSeedAnalysisRouteRelicCatalogItem;
    private SeedAnalysisRouteAct1OptionViewModel? _selectedSeedAnalysisRouteAct1Option;
    private RouteActOption? _selectedSeedAnalysisRouteEventActOption;
    private RouteActOption? _selectedSeedAnalysisRouteRelicActOption;
    private SeedAnalysisRouteShopStrategyOption? _selectedSeedAnalysisRouteShopStrategyOption;
    private SeedAnalysisRoutePreferenceOption? _selectedSeedAnalysisRoutePreferenceOption;
    private IReadOnlyList<CatalogItem> _filteredSeedAnalysisRouteEventCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredSeedAnalysisRouteRelicCatalog = Array.Empty<CatalogItem>();
    private bool _hasSeedAnalysisRouteResult;

    public ObservableCollection<SeedAnalysisRouteMatchViewModel> SeedAnalysisRouteMatches { get; } = new();

    public ObservableCollection<SeedAnalysisRouteTargetChipViewModel> SeedAnalysisRouteEventTargetChips { get; } = new();

    public ObservableCollection<SeedAnalysisRouteTargetChipViewModel> SeedAnalysisRouteRelicTargetChips { get; } = new();

    public ObservableCollection<SeedAnalysisRouteAct1OptionViewModel> SeedAnalysisRouteAct1Options { get; } = new();

    public RelayCommand AnalyzeSeedRoutesCommand => _analyzeSeedRoutesCommand ??= new RelayCommand(AnalyzeSeedRoutes);

    public RelayCommand AddSeedAnalysisRouteEventTargetCommand =>
        _addSeedAnalysisRouteEventTargetCommand ??= new RelayCommand(AddSeedAnalysisRouteEventTarget);

    public RelayCommand RemoveSeedAnalysisRouteEventTargetCommand =>
        _removeSeedAnalysisRouteEventTargetCommand ??= new RelayCommand(RemoveSeedAnalysisRouteEventTarget);

    public RelayCommand AddSeedAnalysisRouteRelicTargetCommand =>
        _addSeedAnalysisRouteRelicTargetCommand ??= new RelayCommand(AddSeedAnalysisRouteRelicTarget);

    public RelayCommand RemoveSeedAnalysisRouteRelicTargetCommand =>
        _removeSeedAnalysisRouteRelicTargetCommand ??= new RelayCommand(RemoveSeedAnalysisRouteRelicTarget);

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

    public IReadOnlyList<SeedAnalysisRouteShopStrategyOption> SeedAnalysisRouteShopStrategyOptions { get; } =
    [
        new(Sts2ExactRouteShopStrategy.TargetRelicsOnly, "只买目标遗物"),
        new(Sts2ExactRouteShopStrategy.NoPurchase, "不进行任何购买"),
        new(Sts2ExactRouteShopStrategy.CardRemovalOnly, "金币够就只删牌")
    ];

    public IReadOnlyList<SeedAnalysisRoutePreferenceOption> SeedAnalysisRoutePreferenceOptions { get; } =
    [
        new(Sts2ExactRoutePreference.None, "无偏好"),
        new(Sts2ExactRoutePreference.MostElites, "最多精英"),
        new(Sts2ExactRoutePreference.FewestElites, "最少精英"),
        new(Sts2ExactRoutePreference.MostRestSites, "最多火堆"),
        new(Sts2ExactRoutePreference.MostQuestionMarks, "最多问号房")
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

    public SeedAnalysisRouteShopStrategyOption? SelectedSeedAnalysisRouteShopStrategyOption
    {
        get => _selectedSeedAnalysisRouteShopStrategyOption;
        set => SetProperty(ref _selectedSeedAnalysisRouteShopStrategyOption, value);
    }

    public SeedAnalysisRoutePreferenceOption? SelectedSeedAnalysisRoutePreferenceOption
    {
        get => _selectedSeedAnalysisRoutePreferenceOption;
        set => SetProperty(ref _selectedSeedAnalysisRoutePreferenceOption, value);
    }

    private void InitializeSeedAnalysisRouteSearch()
    {
        SelectedSeedAnalysisRouteEventActOption = SeedAnalysisRouteEventActOptions.First();
        SelectedSeedAnalysisRouteRelicActOption = SeedAnalysisRouteRelicActOptions.First();
        SelectedSeedAnalysisRouteShopStrategyOption = SeedAnalysisRouteShopStrategyOptions.First();
        SelectedSeedAnalysisRoutePreferenceOption = SeedAnalysisRoutePreferenceOptions.First();
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
        if (TryAddSeedAnalysisRouteTargetChip(
                SeedAnalysisRouteEventTargetChips,
                SelectedSeedAnalysisRouteEventCatalogItem,
                SelectedSeedAnalysisRouteEventActOption,
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
        if (TryAddSeedAnalysisRouteTargetChip(
                SeedAnalysisRouteRelicTargetChips,
                SelectedSeedAnalysisRouteRelicCatalogItem,
                SelectedSeedAnalysisRouteRelicActOption,
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

    private void AnalyzeSeedRoutes()
    {
        AnalyzeSeedRoutesCore(useDefaultRouteWhenNoTargets: false);
    }

    private void AnalyzeDefaultSeedRoute()
    {
        AnalyzeSeedRoutesCore(useDefaultRouteWhenNoTargets: true);
    }

    private void AnalyzeSeedRoutesCore(bool useDefaultRouteWhenNoTargets)
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
            var request = new Sts2ExactRouteAnalysisRequest
            {
                SeedText = normalizedSeed,
                SeedValue = seedValue,
                Character = SelectedCharacter,
                UnlockedCharacters = unlockedCharacters,
                AscensionLevel = SelectedAscensionLevel,
                PlayerCount = 1,
                AncientAvailability = ancientAvailability,
                Act1OpeningOption = SelectedSeedAnalysisRouteAct1Option?.Option,
                EventTargets = SeedAnalysisRouteEventTargetChips
                    .Select(chip => new Sts2ExactRouteEventTargetRequest(chip.ActNumber, chip.Value))
                    .ToList(),
                RelicTargets = SeedAnalysisRouteRelicTargetChips
                    .Select(chip => new Sts2ExactRouteRelicTargetRequest(chip.ActNumber, chip.Value))
                    .ToList(),
                ShopStrategy = SelectedSeedAnalysisRouteShopStrategyOption?.Strategy ?? Sts2ExactRouteShopStrategy.TargetRelicsOnly,
                Preference = SelectedSeedAnalysisRoutePreferenceOption?.Preference ?? Sts2ExactRoutePreference.None,
                MaxResults = useDefaultRouteWhenNoTargets && !hasTargets ? 1 : GetSeedAnalysisRouteMaxResults(),
                MaxRouteChecks = useDefaultRouteWhenNoTargets && !hasTargets ? 1 : GetSeedAnalysisRouteMaxChecks()
            };

            LogInfo(
                $"[精确路线分析] 请求: seed={normalizedSeed}, act1={request.Act1OpeningOption?.RelicId ?? "(none)"}, events={string.Join(",", request.EventTargets.Select(target => $"{target.ActNumber}:{target.EventId}"))}, relics={string.Join(",", request.RelicTargets.Select(target => $"{target.ActNumber}:{target.RelicId}"))}, shopStrategy={request.ShopStrategy}, preference={request.Preference}, maxResults={request.MaxResults}, maxChecks={request.MaxRouteChecks}, default={useDefaultRouteWhenNoTargets}");

            var analysis = _ancientPreviewer.AnalyzeExactRoutes(dataset, request);
            ApplySeedAnalysisRoutes(analysis);

            if (!hasTargets && useDefaultRouteWhenNoTargets)
            {
                SeedAnalysisRouteSummary = analysis.Matches.Count > 0
                    ? "已默认展示这颗种子的首条路线图。你也可以再选择目标事件或遗物，继续反查命中路线。"
                    : "暂时没能生成默认路线图。";
            }
            else
            {
                var targetSummary = string.Join(
                    " + ",
                    SeedAnalysisRouteEventTargetChips.Select(chip => $"{chip.ActLabel}事件：{FormatEventId(chip.Value)}")
                        .Concat(SeedAnalysisRouteRelicTargetChips.Select(chip => $"{chip.ActLabel}遗物：{FormatRelicId(chip.Value)}")));

                SeedAnalysisRouteSummary = analysis.Matches.Count > 0
                    ? $"已找到 {analysis.Matches.Count} 条命中路线，目标：{targetSummary}。路线偏好：{(SelectedSeedAnalysisRoutePreferenceOption?.DisplayName ?? "无偏好")}。已检查 {analysis.CheckedRoutes} 条路线组合{(analysis.WasTruncated ? "（已达到检查上限）" : string.Empty)}。"
                    : $"未找到命中路线，目标：{targetSummary}。路线偏好：{(SelectedSeedAnalysisRoutePreferenceOption?.DisplayName ?? "无偏好")}。已检查 {analysis.CheckedRoutes} 条路线组合{(analysis.WasTruncated ? "（已达到检查上限）" : string.Empty)}。";
            }

            StatusMessage = "精确路线分析完成。";
        }
        catch (Exception ex)
        {
            ClearSeedAnalysisRouteResults();
            SeedAnalysisRouteSummary = $"精确路线分析失败：{ex.Message}";
            StatusMessage = SeedAnalysisRouteSummary;
            LogError(SeedAnalysisRouteSummary);
        }
    }

    private void ApplySeedAnalysisRoutes(Sts2ExactRouteAnalysis analysis)
    {
        ClearSeedAnalysisRouteResults();
        var mapActs = analysis.MapActs.ToDictionary(act => act.ActNumber, act => act, EqualityComparer<int>.Default);

        var routeIndex = 1;
        foreach (var match in analysis.Matches)
        {
            var acts = match.Acts
                .OrderBy(act => act.ActNumber)
                .Select(act => BuildSeedAnalysisRouteAct(act, mapActs.GetValueOrDefault(act.ActNumber)))
                .ToList();
            var hitSummary = BuildRouteHitSummary(match);
            SeedAnalysisRouteMatches.Add(new SeedAnalysisRouteMatchViewModel(
                $"路线 {routeIndex:D2}",
                hitSummary,
                acts,
                routeIndex == 1));
            routeIndex++;
        }

        HasSeedAnalysisRouteResult = true;
        RaisePropertyChanged(nameof(HasSeedAnalysisRouteMatches));
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

    private long GetSeedAnalysisRouteMaxChecks()
    {
        return long.TryParse(SeedAnalysisRouteMaxChecksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, MaxExactRouteChecks)
            : DefaultExactRouteMaxChecks;
    }

    private void ClearSeedAnalysisRouteResults()
    {
        SeedAnalysisRouteMatches.Clear();
        HasSeedAnalysisRouteResult = false;
        RaisePropertyChanged(nameof(HasSeedAnalysisRouteMatches));
    }

    private void ResetSeedAnalysisRoutes()
    {
        SeedAnalysisRouteSummary = "选择目标事件或遗物后，搜索命中路线。";
        SeedAnalysisRouteMaxResultsText = DefaultExactRouteMaxResults.ToString(CultureInfo.InvariantCulture);
        SeedAnalysisRouteMaxChecksText = DefaultExactRouteMaxChecks.ToString(CultureInfo.InvariantCulture);
        SelectedSeedAnalysisRouteEventCatalogItem = null;
        SelectedSeedAnalysisRouteRelicCatalogItem = null;
        SelectedSeedAnalysisRouteAct1Option = null;
        SelectedSeedAnalysisRouteEventActOption = SeedAnalysisRouteEventActOptions.First();
        SelectedSeedAnalysisRouteRelicActOption = SeedAnalysisRouteRelicActOptions.First();
        SelectedSeedAnalysisRouteShopStrategyOption = SeedAnalysisRouteShopStrategyOptions.First();
        SelectedSeedAnalysisRoutePreferenceOption = SeedAnalysisRoutePreferenceOptions.First();
        SeedAnalysisRouteAct1Options.Clear();
        SeedAnalysisRouteEventTargetChips.Clear();
        SeedAnalysisRouteRelicTargetChips.Clear();
        ClearSeedAnalysisRouteResults();
        RefreshSeedAnalysisRouteAct1Options();
    }

    internal sealed record RouteActOption(int? ActNumber, string DisplayName);

    internal sealed record SeedAnalysisRouteShopStrategyOption(
        Sts2ExactRouteShopStrategy Strategy,
        string DisplayName);

    internal sealed record SeedAnalysisRoutePreferenceOption(
        Sts2ExactRoutePreference Preference,
        string DisplayName);

    internal sealed class SeedAnalysisRouteAct1OptionViewModel
    {
        public SeedAnalysisRouteAct1OptionViewModel(
            NeowOptionResult option,
            string optionId,
            string display,
            string description)
        {
            Option = option;
            OptionId = optionId;
            Display = display;
            Description = description;
        }

        public NeowOptionResult Option { get; }

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
