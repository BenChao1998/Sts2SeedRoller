using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal sealed class RollResultViewModel
{
    public RollResultViewModel(
        string seedString,
        uint seedValue,
        CharacterId character,
        string characterName,
        IEnumerable<NeowOptionResult> act1Options,
        Sts2SeedAnalysis? poolAnalysis,
        Sts2EventVisibilityAnalysis? eventVisibilityAnalysis,
        Sts2RelicVisibilityAnalysis? relicVisibilityAnalysis,
        Sts2PoolFilter? poolFilter,
        Sts2RunPreview? ancientPreview,
        bool requiresAct2,
        bool requiresAct3,
        int ascensionLevel,
        ShopPreview? shopPreview = null,
        bool shopFilterMatched = false)
    {
        SeedString = seedString;
        SeedValue = seedValue;
        Character = character;
        CharacterName = characterName;
        RequiresAct2 = requiresAct2;
        RequiresAct3 = requiresAct3;
        AscensionLevel = ascensionLevel;
        var optionList = act1Options.ToList();
        RawOptions = optionList;
        Options = optionList.Select(o => new OptionDisplayViewModel(o)).ToList();

        var act1EventIds = ToEventIdSet(poolFilter?.Act1EventIds);
        var act2EventIds = ToEventIdSet(poolFilter?.Act2EventIds);
        var act3EventIds = ToEventIdSet(poolFilter?.Act3EventIds);
        var highProbabilityEventSelections = poolFilter?.GetHighProbabilityEventSelections()
            ?? Array.Empty<Sts2ActScopedEventId>();
        var highProbabilityEventSeenThreshold = poolFilter?.HighProbabilityEventSeenThreshold
            ?? Sts2PoolFilter.DefaultHighProbabilityEventSeenThreshold;
        var highProbabilityRelicIds = ToIdSet(poolFilter?.HighProbabilityRelicIds);
        var highProbabilitySeenThreshold = poolFilter?.HighProbabilitySeenThreshold
            ?? Sts2PoolFilter.DefaultHighProbabilitySeenThreshold;

        PoolActs = poolAnalysis == null
            ? Array.Empty<PoolActViewModel>()
            : poolAnalysis.Acts
                .Select(act => new PoolActViewModel(act, GetRequiredEventIds(act.ActNumber, act1EventIds, act2EventIds, act3EventIds)))
                .ToList();

        EventVisibilityProfiles = eventVisibilityAnalysis == null
            ? Array.Empty<PoolEventVisibilityProfileViewModel>()
            : eventVisibilityAnalysis.Profiles
                .Select(profile => new PoolEventVisibilityProfileViewModel(profile, poolFilter, highProbabilityEventSelections, highProbabilityEventSeenThreshold))
                .Where(profile => highProbabilityEventSelections.Count == 0 || profile.HasSelectedConditionsMatched)
                .Where(profile => profile.HasEvents)
                .ToList();

        RelicVisibilityProfiles = relicVisibilityAnalysis == null
            ? Array.Empty<PoolRelicVisibilityProfileViewModel>()
            : relicVisibilityAnalysis.Profiles
                .Select(profile => new PoolRelicVisibilityProfileViewModel(profile, highProbabilityRelicIds, highProbabilitySeenThreshold))
                .Where(profile => profile.HasRelics)
                .ToList();

        AncientActs = ancientPreview == null
            ? Array.Empty<AncientActViewModel>()
            : ancientPreview.Acts.Select(act => new AncientActViewModel(act)).ToList();

        ShopPreview = shopPreview;
        ShopFilterMatched = shopFilterMatched;
        HasShopPreview = shopPreview != null;
        if (shopPreview != null)
        {
            var colored = shopPreview.ColoredCards;
            ShopCardEntries = colored.Select((c, i) => new ShopItemViewModel(c.Id, c.Price, IsDiscounted(shopPreview.DiscountedColoredSlot, i), MainWindowViewModel.GetCardDisplayName(c.Id))).ToList();
            ShopColorlessEntries = shopPreview.ColorlessCards.Select(c => new ShopItemViewModel(c.Id, c.Price, false, MainWindowViewModel.GetCardDisplayName(c.Id))).ToList();
            ShopRelicEntries = shopPreview.Relics.Select(r => new ShopItemViewModel(r.Id, r.Price, false, MainWindowViewModel.GetRelicDisplayName(r.Id))).ToList();
            ShopPotionEntries = shopPreview.Potions.Select(p => new ShopItemViewModel(p.Id, p.Price, false, MainWindowViewModel.GetPotionDisplayName(p.Id))).ToList();
        }
        else
        {
            ShopCardEntries = Array.Empty<ShopItemViewModel>();
            ShopColorlessEntries = Array.Empty<ShopItemViewModel>();
            ShopRelicEntries = Array.Empty<ShopItemViewModel>();
            ShopPotionEntries = Array.Empty<ShopItemViewModel>();
        }
    }

    private static bool IsDiscounted(int discountedSlot, int index) => discountedSlot == index && discountedSlot >= 0;

    public string SeedString { get; }

    public uint SeedValue { get; }

    public CharacterId Character { get; }

    public string CharacterName { get; }

    public IReadOnlyList<OptionDisplayViewModel> Options { get; }

    public IReadOnlyList<NeowOptionResult> RawOptions { get; }

    public string SeedText => SeedString;

    public IReadOnlyList<PoolActViewModel> PoolActs { get; }

    public IReadOnlyList<PoolEventVisibilityProfileViewModel> EventVisibilityProfiles { get; }

    public IReadOnlyList<PoolRelicVisibilityProfileViewModel> RelicVisibilityProfiles { get; }

    public bool HasPoolActs => PoolActs.Count > 0;

    public bool HasEventVisibilityProfiles => EventVisibilityProfiles.Count > 0;

    public bool HasRelicVisibilityProfiles => RelicVisibilityProfiles.Count > 0;

    public bool HasPoolAnalysis => HasPoolActs || HasEventVisibilityProfiles || HasRelicVisibilityProfiles;

    public IReadOnlyList<AncientActViewModel> AncientActs { get; }

    public bool HasAncientActs => AncientActs.Count > 0;

    public bool RequiresAct2 { get; }

    public bool RequiresAct3 { get; }

    public int AscensionLevel { get; }

    public bool RequiresAncientMatch => RequiresAct2 || RequiresAct3;

    public int Act1MatchCount => Options.Count;

    public bool HasAct2 => AncientActs.Any(a => a.ActNumber == 2);

    public bool HasAct3 => AncientActs.Any(a => a.ActNumber == 3);

    public string Act2AncientDisplay => AncientActs.FirstOrDefault(a => a.ActNumber == 2)?.AncientDisplay ?? "—";

    public string Act3AncientDisplay => AncientActs.FirstOrDefault(a => a.ActNumber == 3)?.AncientDisplay ?? "—";

    public bool MatchesRequiredActs => (!RequiresAct2 || HasAct2) && (!RequiresAct3 || HasAct3);

    public ShopPreview? ShopPreview { get; }
    public bool ShopFilterMatched { get; }
    public bool HasShopPreview { get; }
    public IReadOnlyList<ShopItemViewModel> ShopCardEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopColorlessEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopRelicEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopPotionEntries { get; }

    internal sealed class PoolActViewModel
    {
        public PoolActViewModel(Sts2ActPoolPreview act, HashSet<string> requiredIds)
        {
            Title = $"第{act.ActNumber}幕事件池 (优先候选{act.PriorityEventCount}个 / 共{act.TotalEventCount}个)";
            Events = act.EventPool
                .Select((eventId, index) => new PoolEventItemViewModel(
                    MainWindowViewModel.CreateSeedAnalysisEventDisplayItem(eventId, index + 1),
                    requiredIds.Contains(NormalizeEventId(eventId))))
                .ToList();
            Monsters = act.MonsterPool
                .Select(MainWindowViewModel.GetSeedAnalysisEncounterDisplayName)
                .ToList();
            Elites = act.ElitePool
                .Select(MainWindowViewModel.GetSeedAnalysisEncounterDisplayName)
                .ToList();
        }

        public string Title { get; }

        public IReadOnlyList<PoolEventItemViewModel> Events { get; }

        public IReadOnlyList<string> Monsters { get; }

        public IReadOnlyList<string> Elites { get; }

        public bool HasMonsters => Monsters.Count > 0;

        public bool HasElites => Elites.Count > 0;
    }

    internal sealed class PoolEventVisibilityProfileViewModel
    {
        public PoolEventVisibilityProfileViewModel(
            Sts2EventVisibilityProfileResult profile,
            Sts2PoolFilter? poolFilter,
            IReadOnlyList<Sts2ActScopedEventId> requiredSelections,
            double seenThreshold)
        {
            Title = TranslateEventVisibilityProfileTitle(profile.Id, profile.Title);
            Description = profile.IsComposite
                ? $"{TranslateEventVisibilityProfileDescription(profile.Id, profile.Description)} 阅读时建议先看这一栏；Roll 命中仍以具体路线画像为准。"
                : $"{TranslateEventVisibilityProfileDescription(profile.Id, profile.Description)} 事件整局阈值 >= {seenThreshold:P0}。";

            var thresholdEvents = profile.SeenEvents
                .Where(item => item.SeenProbability >= seenThreshold)
                .ToList();

            var selectedEvents = profile.SeenEvents
                .Where(item => requiredSelections.Any(selection => MatchesScopedEventSelection(item, selection)))
                .ToList();

            var selectedMatchedEvents = selectedEvents
                .Where(item => poolFilter?.MatchesHighProbabilityEvent(item) == true)
                .ToList();

            var selectedUnmatchedEvents = selectedEvents
                .Where(item => poolFilter?.MatchesHighProbabilityEvent(item) != true)
                .ToList();

            var selectedSeenEventIds = selectedEvents
                .Select(item => BuildScopedEventKey(item.ActNumber, item.EventId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingSelectedEventIds = requiredSelections
                .Where(selection => !selectedSeenEventIds.Contains(BuildScopedEventKey(selection.ActNumber, selection.EventId)))
                .ToList();

            var visibleEvents = new List<Sts2EventVisibilityRankedEvent>();
            AppendDistinctEvents(visibleEvents, selectedMatchedEvents);
            AppendDistinctEvents(visibleEvents, selectedUnmatchedEvents);
            AppendDistinctEvents(
                visibleEvents,
                thresholdEvents.Take(profile.IsRecommended ? 16 : 12));

            var eventItems = new List<PoolEventVisibilityItemViewModel>();
            foreach (var item in visibleEvents)
            {
                eventItems.Add(new PoolEventVisibilityItemViewModel(
                    rank: eventItems.Count + 1,
                    item,
                    isSelected: requiredSelections.Any(selection => MatchesScopedEventSelection(item, selection)),
                    matchesCriteria: poolFilter?.MatchesHighProbabilityEvent(item) == true));
            }

            foreach (var missingEvent in missingSelectedEventIds)
            {
                eventItems.Add(new PoolEventVisibilityItemViewModel(
                    rank: eventItems.Count + 1,
                    missingEvent.ActNumber,
                    missingEvent.EventId,
                    "该路线画像下未进入高概率事件结果，可视为未命中。"));
            }

            var selectedEventLookup = selectedEvents.ToDictionary(
                item => BuildScopedEventKey(item.ActNumber, item.EventId),
                item => item,
                StringComparer.OrdinalIgnoreCase);
            SelectedConditions = requiredSelections
                .Select(selection =>
                {
                    var eventKey = BuildScopedEventKey(selection.ActNumber, selection.EventId);
                    if (selectedEventLookup.TryGetValue(eventKey, out var item))
                    {
                        var matched = poolFilter?.MatchesHighProbabilityEvent(item) == true;
                        return new PoolEventVisibilityConditionViewModel(
                            selection.ActNumber,
                            selection.EventId,
                            matched,
                            matched ? "已命中" : "未命中",
                            FormatEventMetrics(item));
                    }

                    return new PoolEventVisibilityConditionViewModel(
                        selection.ActNumber,
                        selection.EventId,
                        false,
                        "未出现",
                        "该路线画像下未进入高概率事件结果，可视为未命中。");
                })
                .ToList();

            Events = eventItems;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<PoolEventVisibilityConditionViewModel> SelectedConditions { get; }

        public IReadOnlyList<PoolEventVisibilityItemViewModel> Events { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasSelectedConditions => SelectedConditions.Count > 0;

        public bool HasSelectedConditionsMatched => SelectedConditions.Count == 0 || SelectedConditions.All(item => item.IsMatched);

        public bool HasEvents => Events.Count > 0;
    }

    internal sealed class PoolRelicVisibilityProfileViewModel
    {
        public PoolRelicVisibilityProfileViewModel(
            Sts2RelicVisibilityProfileResult profile,
            HashSet<string> requiredIds,
            double seenThreshold)
        {
            Title = $"{TranslateRelicVisibilityProfileTitle(profile.Title)} 路线画像";
            Description = $"{TranslateRelicVisibilityProfileDescription(profile.Description)} 高概率阈值：出现概率 >= {seenThreshold:P0}。";
            var filteredRelics = profile.SeenRelics
                .Where(item => item.SeenProbability >= seenThreshold)
                .ToList();

            var visibleRelics = filteredRelics
                .Take(12)
                .ToList();

            foreach (var matchedRelic in filteredRelics.Where(item => requiredIds.Contains(item.RelicId)))
            {
                if (visibleRelics.Any(item => string.Equals(item.RelicId, matchedRelic.RelicId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                visibleRelics.Add(matchedRelic);
            }

            Relics = visibleRelics
                .Select((item, index) => new PoolRelicVisibilityItemViewModel(
                    $"{index + 1:D2}. {FormatRelicLine(item)}",
                    requiredIds.Contains(item.RelicId)))
                .ToList();
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<PoolRelicVisibilityItemViewModel> Relics { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasRelics => Relics.Count > 0;
    }

    internal sealed class PoolEventItemViewModel
    {
        public PoolEventItemViewModel(MainWindowViewModel.SeedAnalysisDisplayItemViewModel item, bool isMatched)
        {
            Title = item.Title;
            Description = item.Description;
            Options = item.Options;
            IsMatched = isMatched;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<MainWindowViewModel.SeedAnalysisOptionDisplayItemViewModel> Options { get; }

        public bool IsMatched { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasOptions => Options.Count > 0;

        public bool HasTooltipContent => HasDescription || HasOptions;

        public bool HasDescriptionAndOptions => HasDescription && HasOptions;
    }

    internal sealed class PoolEventVisibilityItemViewModel
    {
        public PoolEventVisibilityItemViewModel(int rank, Sts2EventVisibilityRankedEvent item, bool isSelected, bool matchesCriteria)
        {
            var displayItem = MainWindowViewModel.CreateSeedAnalysisEventDisplayItem(item.EventId);
            Rank = rank;
            Title = $"第{item.ActNumber}幕 · {displayItem.Title}";
            Description = displayItem.Description;
            Options = displayItem.Options;
            MetricsText = FormatEventMetrics(item);
            IsSelected = isSelected;
            MatchesCriteria = matchesCriteria;
        }

        public PoolEventVisibilityItemViewModel(int rank, int actNumber, string eventId, string metricsText)
        {
            var displayItem = MainWindowViewModel.CreateSeedAnalysisEventDisplayItem(eventId);
            Rank = rank;
            Title = $"第{actNumber}幕 · {displayItem.Title}";
            Description = displayItem.Description;
            Options = displayItem.Options;
            MetricsText = metricsText;
            IsSelected = true;
            MatchesCriteria = false;
        }

        public int Rank { get; }

        public string RankText => $"{Rank:D2}.";

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<MainWindowViewModel.SeedAnalysisOptionDisplayItemViewModel> Options { get; }

        public string MetricsText { get; }

        public bool IsSelected { get; }

        public bool MatchesCriteria { get; }

        public bool IsMatched => IsSelected && MatchesCriteria;

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasOptions => Options.Count > 0;

        public bool HasTooltipContent => HasDescription || HasOptions;

        public bool HasDescriptionAndOptions => HasDescription && HasOptions;
    }

    internal sealed class PoolEventVisibilityConditionViewModel
    {
        public PoolEventVisibilityConditionViewModel(int actNumber, string eventId, bool isMatched, string statusText, string detailText)
        {
            var displayItem = MainWindowViewModel.CreateSeedAnalysisEventDisplayItem(eventId);
            Title = $"第{actNumber}幕 · {displayItem.Title}";
            IsMatched = isMatched;
            StatusText = statusText;
            DetailText = detailText;
        }

        public string Title { get; }

        public bool IsMatched { get; }

        public string StatusText { get; }

        public string DetailText { get; }
    }

    internal sealed class PoolRelicVisibilityItemViewModel
    {
        public PoolRelicVisibilityItemViewModel(string displayName, bool isMatched)
        {
            DisplayName = displayName;
            IsMatched = isMatched;
        }

        public string DisplayName { get; }

        public bool IsMatched { get; }
    }

    internal sealed class AncientActViewModel
    {
        public AncientActViewModel(Sts2ActPreview preview)
        {
            ActNumber = preview.ActNumber;
            AncientId = preview.AncientId ?? string.Empty;
            AncientName = AncientDisplayCatalog.GetLocalizedName(AncientId, preview.AncientName ?? AncientId);
            AncientDisplay = AncientDisplayCatalog.GetDisplayText(AncientId, preview.AncientName ?? AncientId);
            Options = preview.AncientOptions.Select(option => new AncientOptionDisplayViewModel(option)).ToList();
        }

        public int ActNumber { get; }

        public string AncientId { get; }

        public string AncientName { get; }

        public string AncientDisplay { get; }

        public string ActTitle => string.Format(
            CultureInfo.InvariantCulture,
            "第{0}幕古神",
            ActNumber);

        public bool HasAncientId => !string.IsNullOrWhiteSpace(AncientId);

        public string AncientIdLabel => HasAncientId ? $"ID: {AncientId}" : string.Empty;

        public IReadOnlyList<AncientOptionDisplayViewModel> Options { get; }

        public bool HasOptions => Options.Count > 0;
    }

    internal sealed class AncientOptionDisplayViewModel
    {
        private static readonly Regex ColorMarkupPattern = new(@"\[/?(gold|blue|red|purple|orange|gray|green)\]", RegexOptions.Compiled);
        private static readonly Regex ConditionalPattern = new(@"\{[^}]*:cond:[^{}]*(?:\{[^}]*\}[^{}]*)*\|[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex VariablePattern = new(@"\{[^}]+\}", RegexOptions.Compiled);

        public AncientOptionDisplayViewModel(Sts2AncientOption option)
        {
            OptionId = option.OptionId;
            Title = StripMarkup(option.Title ?? option.OptionId);
            Description = StripMarkup(option.Description ?? string.Empty);
            Note = StripMarkup(option.Note ?? string.Empty);
        }

        public string OptionId { get; }

        public string Title { get; }

        public string Description { get; }

        public string Note { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        private static string StripMarkup(string text)
        {
            text = ColorMarkupPattern.Replace(text, string.Empty);
            text = ConditionalPattern.Replace(text, string.Empty);
            text = VariablePattern.Replace(text, string.Empty);
            return text;
        }
    }

    internal sealed class ShopItemViewModel
    {
        public ShopItemViewModel(string id, int price, bool isDiscounted, string displayName)
        {
            Id = id;
            Price = price;
            IsDiscounted = isDiscounted;
            DisplayName = displayName;
        }

        public string Id { get; }
        public int Price { get; }
        public bool IsDiscounted { get; }
        public string DisplayName { get; }
        public string PriceDisplay => Price.ToString();
        public string DiscountLabel => IsDiscounted ? " (折)" : string.Empty;
    }

    private static HashSet<string> ToIdSet(IReadOnlyList<string>? values)
    {
        return values == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ToEventIdSet(IReadOnlyList<string>? values)
    {
        return values == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeEventId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetRequiredEventIds(
        int actNumber,
        HashSet<string> act1EventIds,
        HashSet<string> act2EventIds,
        HashSet<string> act3EventIds)
    {
        return actNumber switch
        {
            1 => act1EventIds,
            2 => act2EventIds,
            3 => act3EventIds,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesScopedEventSelection(
        Sts2EventVisibilityRankedEvent item,
        Sts2ActScopedEventId selection)
    {
        return item.ActNumber == selection.ActNumber &&
               string.Equals(NormalizeEventId(item.EventId), NormalizeEventId(selection.EventId), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildScopedEventKey(int actNumber, string eventId)
    {
        return $"{actNumber}:{NormalizeEventId(eventId)}";
    }

    private static string NormalizeEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current is '_' or ' ' or '-')
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                continue;
            }

            if (i > 0)
            {
                var previous = value[i - 1];
                var next = i + 1 < value.Length ? value[i + 1] : '\0';
                var shouldInsertUnderscore =
                    (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous))) ||
                    (char.IsUpper(current) && char.IsUpper(previous) && next != '\0' && char.IsLower(next)) ||
                    (char.IsDigit(current) && char.IsLetter(previous)) ||
                    (char.IsLetter(current) && char.IsDigit(previous));

                if (shouldInsertUnderscore && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString().Trim('_');
    }

    private static string FormatEventLine(Sts2EventVisibilityRankedEvent item)
    {
        var displayName = MainWindowViewModel.GetEventVisibilityDisplayName(item.EventId);
        if (item.RouteCount > 1)
        {
            return $"{displayName} | 前期 {item.EarlyProbability:P1}（区间 {item.MinEarlyProbability:P0}-{item.MaxEarlyProbability:P0}） | 整局 {item.SeenProbability:P1}（区间 {item.MinSeenProbability:P0}-{item.MaxSeenProbability:P0}） | 首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventSource(item.MostCommonSource)}";
        }

        return $"{displayName} | 前期 {item.EarlyProbability:P1} | 整局 {item.SeenProbability:P1} | 首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventSource(item.MostCommonSource)}";
    }

    private static string FormatEventMetrics(Sts2EventVisibilityRankedEvent item)
    {
        if (item.RouteCount > 1)
        {
            return $"第{item.ActNumber}幕前5事件位 {item.EarlyProbability:P1}（区间 {item.MinEarlyProbability:P0}-{item.MaxEarlyProbability:P0}） | 第{item.ActNumber}幕出现 {item.SeenProbability:P1}（区间 {item.MinSeenProbability:P0}-{item.MaxSeenProbability:P0}） | 第{item.ActNumber}幕首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventSource(item.MostCommonSource)}";
        }

        return $"第{item.ActNumber}幕前5事件位 {item.EarlyProbability:P1} | 第{item.ActNumber}幕出现 {item.SeenProbability:P1} | 第{item.ActNumber}幕首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventSource(item.MostCommonSource)}";
    }

    private static string FormatEventSource(Sts2EventVisibilitySource source)
    {
        return source switch
        {
            Sts2EventVisibilitySource.Unknown => "问号房",
            Sts2EventVisibilitySource.AncientAct2 => "第二幕开场古神",
            Sts2EventVisibilitySource.AncientAct3 => "第三幕开场古神",
            _ => source.ToString()
        };
    }

    private static void AppendDistinctEvents(
        ICollection<Sts2EventVisibilityRankedEvent> target,
        IEnumerable<Sts2EventVisibilityRankedEvent> source)
    {
        foreach (var item in source)
        {
            if (target.Any(existing => string.Equals(
                    NormalizeEventId(existing.EventId),
                    NormalizeEventId(item.EventId),
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(item);
        }
    }

    private static string FormatRelicLine(Sts2RelicVisibilityRankedRelic item)
    {
        return $"{MainWindowViewModel.GetRelicDisplayName(item.RelicId)} | 出现 {item.SeenProbability:P1} | 非商店 {item.NonShopSeenProbability:P1} | 商店 {item.ShopSeenProbability:P1} | 前期 {item.EarlyProbability:P1} | 平均首次机会 {item.AverageFirstOpportunity:F2} | 最常来源 {FormatSource(item.MostCommonSource)}";
    }

    private static string FormatSource(Sts2RelicVisibilitySource source)
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

    private static string TranslateEventVisibilityProfileTitle(string profileId, string fallbackTitle)
    {
        return profileId switch
        {
            "consensus" => "综合预测（推荐）",
            "balanced" => "均衡推进",
            "aggressive" => "前压打精英",
            "shopper" => "偏商店补强",
            "explorer" => "问号优先",
            _ => fallbackTitle
        };
    }

    private static string TranslateEventVisibilityProfileDescription(string profileId, string fallbackDescription)
    {
        return profileId switch
        {
            "consensus" => "把多条路线画像揉成一份综合结果，用来回答只给种子时大概率会看到什么。",
            "balanced" => "按普通实战路线估算。",
            "aggressive" => "按更早打精英、问号更少的路线估算。",
            "shopper" => "按更愿意进店、事件量略低的路线估算。",
            "explorer" => "按更愿意绕路踩问号、事件量更高的路线估算。",
            _ => fallbackDescription
        };
    }

    private static string TranslateRelicVisibilityProfileTitle(string title)
    {
        return title switch
        {
            "Balanced" => "均衡",
            "Aggressive" => "激进",
            "Shopper" => "商店优先",
            _ => title
        };
    }

    private static string TranslateRelicVisibilityProfileDescription(string description)
    {
        return description switch
        {
            "Balanced route: a moderate number of elites, a few shops, and a medium chance to visit ancients."
                => "均衡路线：精英数量适中，商店较少，遇到古神的概率中等。",
            "Aggressive route: earlier elites, fewer shops, and a slightly lower ancient chance."
                => "激进路线：更早打精英，商店更少，遇到古神的概率略低。",
            "Shop-heavy route: more shop visibility, fewer elites, and a slightly higher ancient chance."
                => "商店优先路线：更容易经过商店，精英更少，遇到古神的概率略高。",
            _ => description
        };
    }
}
