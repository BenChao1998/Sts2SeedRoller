using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2;

internal sealed class Sts2EventVisibilityAnalyzer
{
    private const double CompositeEarlySupportThreshold = 0.10;
    private const double CompositeSeenSupportThreshold = 0.15;

    private readonly string? _workspaceRoot;

    internal Sts2EventVisibilityAnalyzer(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot;
    }

    internal Sts2EventVisibilityAnalysis Analyze(
        Sts2EventVisibilityRequest request,
        NeowOptionDataset dataset,
        IReadOnlyList<Generation.Sts2RunSimulator.ActPoolResult> actPools,
        Sts2RunPreview ancientPreview)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(actPools);
        ArgumentNullException.ThrowIfNull(ancientPreview);

        if (request.Samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Samples must be positive.");
        }

        if (request.EarlyWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Early window must be positive.");
        }

        var playerCount = Math.Max(1, request.PlayerCount);
        var eventPools = actPools.ToDictionary(
            act => act.ActNumber,
            act => (IReadOnlyList<string>)act.Events
                .Select(Sts2EventIdNormalizer.FromPoolItem)
                .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                .ToList(),
            EqualityComparer<int>.Default);
        var ancients = ancientPreview.Acts.ToDictionary(
            act => act.ActNumber,
            act => Sts2EventIdNormalizer.FromAny(act.AncientId),
            EqualityComparer<int>.Default);
        var simulationModel = Sts2EventVisibilitySimulationModel.Create(
            dataset,
            request.Character,
            request.UnlockedCharacters,
            playerCount,
            request.AscensionLevel,
            _workspaceRoot);

        var routeRuns = RouteProfile.All
            .Select(profile => new RouteProfileRun(
                profile,
                RunProfile(request, profile, eventPools, ancients, simulationModel)))
            .ToList();
        var profiles = new List<Sts2EventVisibilityProfileResult>(routeRuns.Count + 1)
        {
            BuildCompositeProfile(request, routeRuns)
        };
        profiles.AddRange(routeRuns.Select(run => run.Result));

        return new Sts2EventVisibilityAnalysis
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            Character = request.Character,
            PlayerCount = playerCount,
            Samples = request.Samples,
            EarlyWindow = request.EarlyWindow,
            Profiles = profiles
        };
    }

    private static Sts2EventVisibilityProfileResult BuildCompositeProfile(
        Sts2EventVisibilityRequest request,
        IReadOnlyList<RouteProfileRun> routeRuns)
    {
        var totalWeight = routeRuns.Sum(run => run.Profile.Weight);
        var eventIds = routeRuns
            .SelectMany(run => run.Result.SeenEvents.Select(item => item.EventId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var itemLookupByProfile = routeRuns
            .ToDictionary(
                run => run.Profile.Id,
                run => run.Result.SeenEvents.ToDictionary(item => item.EventId, item => item, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var aggregatedEvents = eventIds
            .Select(eventId => AggregateEvent(routeRuns, itemLookupByProfile, totalWeight, eventId))
            .ToList();

        var earlyRanked = aggregatedEvents
            .OrderByDescending(item => item.MaxEarlyProbability)
            .ThenByDescending(item => item.EarlyRouteSupportCount)
            .ThenByDescending(item => item.EarlyProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenByDescending(item => item.MaxSeenProbability)
            .ThenBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seenRanked = aggregatedEvents
            .OrderByDescending(item => item.MaxSeenProbability)
            .ThenByDescending(item => item.SeenRouteSupportCount)
            .ThenByDescending(item => item.SeenProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenByDescending(item => item.MaxEarlyProbability)
            .ThenBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actSummaries = Enumerable.Range(1, routeRuns.Max(run => run.Result.Acts.Count))
            .Select(actNumber => BuildCompositeActSummary(routeRuns, actNumber, totalWeight))
            .ToList();

        var earlySamples = routeRuns
            .SelectMany(run => run.Result.EarlySamples)
            .Where(sample => sample.Count > 0)
            .GroupBy(
                sample => string.Join("|", sample),
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.First().Count)
            .Take(4)
            .Select(group => (IReadOnlyList<string>)group.First().ToList())
            .ToList();

        return new Sts2EventVisibilityProfileResult
        {
            Id = "consensus",
            Title = "Consensus",
            Description = "Fused best-effort view: blends every route portrait, then keeps the shared front-runners while surfacing route-sensitive candidates through probability ranges.",
            Acts = actSummaries,
            EarlyEvents = earlyRanked,
            SeenEvents = seenRanked,
            EarlySamples = earlySamples,
            IsComposite = true,
            IsRecommended = true
        };
    }

    private static Sts2EventVisibilityProfileResult RunProfile(
        Sts2EventVisibilityRequest request,
        RouteProfile profile,
        IReadOnlyDictionary<int, IReadOnlyList<string>> eventPools,
        IReadOnlyDictionary<int, string> ancients,
        Sts2EventVisibilitySimulationModel simulationModel)
    {
        var routeRng = new GameRng(request.SeedValue, $"event_visibility_{profile.Id}");
        var stats = new Dictionary<string, AppearanceStats>(StringComparer.OrdinalIgnoreCase);
        var earlySamples = new List<IReadOnlyList<string>>(capacity: 3);

        for (var sample = 0; sample < request.Samples; sample++)
        {
            var state = Sts2EventProgressState.Create(simulationModel, request.Character, request.PlayerCount);
            var seenThisSample = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstSource = new Dictionary<string, Sts2EventVisibilitySource>(StringComparer.OrdinalIgnoreCase);
            var earlyThisSample = new List<string>();
            var globalIndex = 0;

            for (var actNumber = 1; actNumber <= profile.Acts.Count; actNumber++)
            {
                var actProfile = profile.Acts[actNumber - 1];
                var actOpeningAncientId = ancients.GetValueOrDefault(actNumber);
                var visitsActOpeningAncient = actNumber > 1 &&
                                              !string.IsNullOrWhiteSpace(actOpeningAncientId) &&
                                              routeRng.NextDouble() < actProfile.AncientVisitChance;
                var startsWithEventRoom = actNumber == 1 || visitsActOpeningAncient;
                state.StartAct(actNumber, startsWithEventRoom ? 1 : 0);

                if (actNumber == 1)
                {
                    // Neow is always an event room and advances eventsVisited before Act 1 routing begins.
                    state.TotalFloor++;
                }
                else if (visitsActOpeningAncient)
                {
                    // Ancient rooms are outside the act pool, but still advance the act event visit counter.
                    state.TotalFloor++;
                    globalIndex++;
                    NoteSeen(
                        actOpeningAncientId!,
                        actNumber == 2 ? Sts2EventVisibilitySource.AncientAct2 : Sts2EventVisibilitySource.AncientAct3,
                        globalIndex,
                        request.EarlyWindow,
                        seenThisSample,
                        firstSeen,
                        firstSource,
                        earlyThisSample);
                }

                var unknownCount = actProfile.UnknownCounts.Sample(routeRng);
                var eventCount = Math.Min(actProfile.EventCounts.Sample(routeRng), unknownCount);
                var eventSlots = PickSlots(routeRng, unknownCount, eventCount);
                var majorStops = actProfile.BuildMajorStops(routeRng);
                var gaps = DistributeStopsAcrossGaps(majorStops, unknownCount + 1, biasEarlyUnknowns: actNumber == 1);
                var pool = eventPools.GetValueOrDefault(actNumber, Array.Empty<string>());

                for (var gap = 0; gap < gaps.Count; gap++)
                {
                    foreach (var stop in gaps[gap])
                    {
                        switch (stop)
                        {
                            case MajorStopKind.Treasure:
                                state.ConsumeTreasureStop(routeRng);
                                state.TotalFloor++;
                                break;
                            case MajorStopKind.Elite:
                                state.ConsumeEliteStop(routeRng);
                                state.TotalFloor++;
                                break;
                            case MajorStopKind.Shop:
                                state.ConsumeShopStop(routeRng);
                                state.TotalFloor++;
                                break;
                        }
                    }

                    if (gap >= unknownCount)
                    {
                        continue;
                    }

                    var regularCombats = actProfile.BetweenUnknownCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        state.ConsumeRegularCombat(routeRng);
                        state.TotalFloor++;
                    }

                    state.TotalFloor++;
                    if (!eventSlots.Contains(gap))
                    {
                        state.ConsumeUnknownNonEvent(routeRng);
                        continue;
                    }

                    if (pool.Count == 0)
                    {
                        continue;
                    }

                    Sts2EventPullEngine.EnsureNextEventIsValid(pool, state);
                    var eventId = Sts2EventPullEngine.PeekNextAllowedEvent(pool, state);
                    if (string.IsNullOrWhiteSpace(eventId))
                    {
                        continue;
                    }

                    globalIndex++;
                    NoteSeen(
                        eventId,
                        Sts2EventVisibilitySource.Unknown,
                        globalIndex,
                        request.EarlyWindow,
                        seenThisSample,
                        firstSeen,
                        firstSource,
                        earlyThisSample);
                    Sts2EventPullEngine.ConsumeShownEvent(pool, eventId, state);
                    state.ApplyShownEvent(eventId, routeRng);
                }
            }

            if (earlySamples.Count < 3)
            {
                earlySamples.Add(earlyThisSample);
            }

            foreach (var (eventId, opportunityIndex) in firstSeen)
            {
                if (!stats.TryGetValue(eventId, out var eventStats))
                {
                    eventStats = new AppearanceStats();
                    stats[eventId] = eventStats;
                }

                eventStats.SeenCount++;
                eventStats.FirstOpportunityTotal += opportunityIndex;
                if (opportunityIndex <= request.EarlyWindow)
                {
                    eventStats.EarlyCount++;
                }

                eventStats.FirstSourceCounts[firstSource[eventId]] =
                    eventStats.FirstSourceCounts.GetValueOrDefault(firstSource[eventId]) + 1;
            }
        }

        var earlyRanked = stats
            .Select(pair => ToRankedEvent(pair.Key, pair.Value, request.Samples))
            .OrderByDescending(item => item.EarlyProbability)
            .ThenByDescending(item => item.SeenProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seenRanked = earlyRanked
            .OrderByDescending(item => item.SeenProbability)
            .ThenByDescending(item => item.EarlyProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Sts2EventVisibilityProfileResult
        {
            Id = profile.Id,
            Title = profile.Title,
            Description = profile.Description,
            Acts = profile.Acts
                .Select((act, index) => act.ToModel(index + 1))
                .ToList(),
            EarlyEvents = earlyRanked,
            SeenEvents = seenRanked,
            EarlySamples = earlySamples,
            IsComposite = false,
            IsRecommended = false
        };
    }

    private static List<List<MajorStopKind>> DistributeStopsAcrossGaps(
        IReadOnlyList<MajorStopKind> stops,
        int gapCount,
        bool biasEarlyUnknowns)
    {
        var gaps = Enumerable.Range(0, Math.Max(1, gapCount))
            .Select(_ => new List<MajorStopKind>())
            .ToList();

        if (stops.Count == 0)
        {
            return gaps;
        }

        if (stops.Count == 1)
        {
            gaps[stops[0] == MajorStopKind.Ancient ? 0 : Math.Min(gaps.Count - 1, 1)].Add(stops[0]);
            return gaps;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            var gap = biasEarlyUnknowns
                ? (int)Math.Round((double)(i + 1) * (gaps.Count - 1) / Math.Max(2, stops.Count + 1))
                : (int)Math.Round((double)i * (gaps.Count - 1) / Math.Max(1, stops.Count - 1));
            gap = Math.Clamp(gap, 0, gaps.Count - 1);
            gaps[gap].Add(stop);
        }

        return gaps;
    }

    private static HashSet<int> PickSlots(GameRng rng, int total, int count)
    {
        if (count <= 0 || total <= 0)
        {
            return new HashSet<int>();
        }

        if (count >= total)
        {
            return Enumerable.Range(0, total).ToHashSet();
        }

        var slots = Enumerable.Range(0, total).ToArray();
        for (var i = 0; i < count; i++)
        {
            var swapIndex = i + rng.NextInt(total - i);
            (slots[i], slots[swapIndex]) = (slots[swapIndex], slots[i]);
        }

        return slots
            .Take(count)
            .ToHashSet();
    }

    private static void NoteSeen(
        string eventId,
        Sts2EventVisibilitySource source,
        int opportunityIndex,
        int earlyWindow,
        HashSet<string> seenThisSample,
        Dictionary<string, int> firstSeen,
        Dictionary<string, Sts2EventVisibilitySource> firstSource,
        List<string> earlyThisSample)
    {
        if (string.IsNullOrWhiteSpace(eventId) || !seenThisSample.Add(eventId))
        {
            return;
        }

        firstSeen[eventId] = opportunityIndex;
        firstSource[eventId] = source;
        if (opportunityIndex <= earlyWindow)
        {
            earlyThisSample.Add(eventId);
        }
    }

    private static Sts2EventVisibilityRankedEvent ToRankedEvent(
        string eventId,
        AppearanceStats stats,
        int sampleCount)
    {
        var mostCommonSource = stats.FirstSourceCounts.Count == 0
            ? Sts2EventVisibilitySource.Unknown
            : stats.FirstSourceCounts
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .First()
                .Key;

        return new Sts2EventVisibilityRankedEvent
        {
            EventId = eventId,
            EarlyProbability = (double)stats.EarlyCount / sampleCount,
            SeenProbability = (double)stats.SeenCount / sampleCount,
            AverageFirstOpportunity = stats.SeenCount == 0 ? double.PositiveInfinity : stats.FirstOpportunityTotal / stats.SeenCount,
            MostCommonSource = mostCommonSource,
            RouteCount = 1,
            EarlyRouteSupportCount = stats.EarlyCount > 0 ? 1 : 0,
            SeenRouteSupportCount = stats.SeenCount > 0 ? 1 : 0,
            MinEarlyProbability = (double)stats.EarlyCount / sampleCount,
            MaxEarlyProbability = (double)stats.EarlyCount / sampleCount,
            MinSeenProbability = (double)stats.SeenCount / sampleCount,
            MaxSeenProbability = (double)stats.SeenCount / sampleCount
        };
    }

    private static Sts2EventVisibilityRankedEvent AggregateEvent(
        IReadOnlyList<RouteProfileRun> routeRuns,
        IReadOnlyDictionary<string, Dictionary<string, Sts2EventVisibilityRankedEvent>> itemLookupByProfile,
        double totalWeight,
        string eventId)
    {
        var weightedEarly = 0.0;
        var weightedSeen = 0.0;
        var minEarly = double.PositiveInfinity;
        var maxEarly = 0.0;
        var minSeen = double.PositiveInfinity;
        var maxSeen = 0.0;
        var earlySupport = 0;
        var seenSupport = 0;
        var weightedFirstTotal = 0.0;
        var weightedFirstWeight = 0.0;
        var sourceWeights = new Dictionary<Sts2EventVisibilitySource, double>();

        foreach (var run in routeRuns)
        {
            var weight = run.Profile.Weight;
            var profileItems = itemLookupByProfile[run.Profile.Id];
            profileItems.TryGetValue(eventId, out var item);

            var early = item?.EarlyProbability ?? 0.0;
            var seen = item?.SeenProbability ?? 0.0;

            weightedEarly += early * weight;
            weightedSeen += seen * weight;
            minEarly = Math.Min(minEarly, early);
            maxEarly = Math.Max(maxEarly, early);
            minSeen = Math.Min(minSeen, seen);
            maxSeen = Math.Max(maxSeen, seen);

            if (early >= CompositeEarlySupportThreshold)
            {
                earlySupport++;
            }

            if (seen >= CompositeSeenSupportThreshold)
            {
                seenSupport++;
            }

            if (item is { AverageFirstOpportunity: < double.PositiveInfinity })
            {
                var firstWeight = weight * Math.Max(seen, 0.05);
                weightedFirstTotal += item.AverageFirstOpportunity * firstWeight;
                weightedFirstWeight += firstWeight;

                var sourceWeight = weight * Math.Max(Math.Max(seen, early), 0.05);
                sourceWeights[item.MostCommonSource] = sourceWeights.GetValueOrDefault(item.MostCommonSource) + sourceWeight;
            }
        }

        var mostCommonSource = sourceWeights.Count == 0
            ? Sts2EventVisibilitySource.Unknown
            : sourceWeights
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .First()
                .Key;

        return new Sts2EventVisibilityRankedEvent
        {
            EventId = eventId,
            EarlyProbability = totalWeight <= 0 ? 0.0 : weightedEarly / totalWeight,
            SeenProbability = totalWeight <= 0 ? 0.0 : weightedSeen / totalWeight,
            AverageFirstOpportunity = weightedFirstWeight <= 0 ? double.PositiveInfinity : weightedFirstTotal / weightedFirstWeight,
            MostCommonSource = mostCommonSource,
            RouteCount = routeRuns.Count,
            EarlyRouteSupportCount = earlySupport,
            SeenRouteSupportCount = seenSupport,
            MinEarlyProbability = double.IsPositiveInfinity(minEarly) ? 0.0 : minEarly,
            MaxEarlyProbability = maxEarly,
            MinSeenProbability = double.IsPositiveInfinity(minSeen) ? 0.0 : minSeen,
            MaxSeenProbability = maxSeen
        };
    }

    private static Sts2EventVisibilityActSummary BuildCompositeActSummary(
        IReadOnlyList<RouteProfileRun> routeRuns,
        int actNumber,
        double totalWeight)
    {
        var actGroups = routeRuns
            .Select(run => new
            {
                Weight = run.Profile.Weight,
                Act = run.Result.Acts[Math.Clamp(actNumber - 1, 0, run.Result.Acts.Count - 1)]
            })
            .ToList();

        return new Sts2EventVisibilityActSummary
        {
            ActNumber = actNumber,
            UnknownCounts = BlendWeightedCounts(
                actGroups.Select(group => (group.Act.UnknownCounts, group.Weight))),
            EventCounts = BlendWeightedCounts(
                actGroups.Select(group => (group.Act.EventCounts, group.Weight))),
            AncientVisitChance = totalWeight <= 0
                ? 0.0
                : actGroups.Sum(group => group.Act.AncientVisitChance * group.Weight) / totalWeight
        };
    }

    private static IReadOnlyList<Sts2WeightedIntChance> BlendWeightedCounts(
        IEnumerable<(IReadOnlyList<Sts2WeightedIntChance> Entries, double Weight)> sources)
    {
        var weightByValue = new Dictionary<int, double>();

        foreach (var (entries, sourceWeight) in sources)
        {
            foreach (var entry in entries)
            {
                weightByValue[entry.Value] = weightByValue.GetValueOrDefault(entry.Value) + entry.Weight * sourceWeight;
            }
        }

        var total = weightByValue.Values.Sum();
        if (total <= 0)
        {
            return Array.Empty<Sts2WeightedIntChance>();
        }

        return weightByValue
            .OrderBy(pair => pair.Key)
            .Select(pair => new Sts2WeightedIntChance
            {
                Value = pair.Key,
                Weight = pair.Value / total
            })
            .ToList();
    }

    private sealed class AppearanceStats
    {
        public int SeenCount { get; set; }

        public int EarlyCount { get; set; }

        public double FirstOpportunityTotal { get; set; }

        public Dictionary<Sts2EventVisibilitySource, int> FirstSourceCounts { get; } = new();
    }

    private enum MajorStopKind
    {
        Ancient,
        Shop,
        Elite,
        Treasure
    }

    private sealed class RouteProfile
    {
        private RouteProfile(
            string id,
            string title,
            string description,
            IReadOnlyList<ActRouteProfile> acts,
            double weight)
        {
            Id = id;
            Title = title;
            Description = description;
            Acts = acts;
            Weight = weight;
        }

        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<ActRouteProfile> Acts { get; }

        public double Weight { get; }

        public static IReadOnlyList<RouteProfile> All { get; } =
        [
            new RouteProfile(
                "balanced",
                "Balanced",
                "Typical climb: moderate question marks, a couple of elites, and enough shops to smooth out the run without hard-forcing them.",
                [
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.15), (3, 0.34), (4, 0.29), (5, 0.18), (6, 0.04)),
                        eventCounts: WeightedIntDistribution.Of((0, 0.16), (1, 0.35), (2, 0.33), (3, 0.16)),
                        treasureCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.25), (1, 0.60), (2, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.34), (1, 0.50), (2, 0.16)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop],
                            [MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop],
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((1, 0.28), (2, 0.47), (3, 0.25)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.23), (2, 0.52), (3, 0.25)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.55), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                        ancientVisitChance: 0.88,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.22), (1, 0.53), (2, 0.25)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite],
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.22), (3, 0.51), (4, 0.27)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.17), (2, 0.48), (3, 0.35)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.50), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.45), (3, 0.20)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.32), (1, 0.53), (2, 0.15)),
                        ancientVisitChance: 0.84,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.18), (1, 0.50), (2, 0.32)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ])
                ],
                weight: 1.00),
            new RouteProfile(
                "aggressive",
                "Aggressive",
                "Elite-leaning climb: cuts a few question marks, front-loads combats, and usually reaches events with tighter HP and richer relic state.",
                [
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((1, 0.21), (2, 0.39), (3, 0.29), (4, 0.11)),
                        eventCounts: WeightedIntDistribution.Of((0, 0.31), (1, 0.47), (2, 0.22)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.00,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.12), (1, 0.42), (2, 0.46)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop],
                            [MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop],
                            [MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((1, 0.41), (2, 0.42), (3, 0.17)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.40), (2, 0.42), (3, 0.18)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.45), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.82,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.38), (2, 0.52)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop],
                            [MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((1, 0.13), (2, 0.44), (3, 0.31), (4, 0.12)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.28), (2, 0.46), (3, 0.26)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.35), (1, 0.20)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.78,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.34), (2, 0.56)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Elite, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure],
                            [MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure]
                        ])
                ],
                weight: 0.92),
            new RouteProfile(
                "shopper",
                "Shopper",
                "Shop-leaning climb: still checks question marks, but gives up some event volume to stabilize with removals, potions, and store relics.",
                [
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.14), (3, 0.38), (4, 0.30), (5, 0.14), (6, 0.04)),
                        eventCounts: WeightedIntDistribution.Of((0, 0.24), (1, 0.42), (2, 0.25), (3, 0.09)),
                        treasureCounts: WeightedIntDistribution.Of((2, 0.40), (3, 0.45), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.38), (1, 0.50), (2, 0.12)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop],
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop],
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((1, 0.33), (2, 0.46), (3, 0.21)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.28), (2, 0.50), (3, 0.22)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.90,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.18), (1, 0.55), (2, 0.27)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.34), (3, 0.44), (4, 0.22)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.23), (2, 0.49), (3, 0.28)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.86,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.18), (1, 0.55), (2, 0.27)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Ancient, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ])
                ],
                weight: 1.04),
            new RouteProfile(
                "explorer",
                "Explorer",
                "Question-mark leaning climb: squeezes extra unknowns out of the map, spends fewer stops on elites, and keeps more route-sensitive events alive.",
                [
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((3, 0.18), (4, 0.33), (5, 0.29), (6, 0.16), (7, 0.04)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.12), (2, 0.32), (3, 0.34), (4, 0.18), (5, 0.04)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((0, 0.18), (1, 0.46), (2, 0.28), (3, 0.08)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.38), (1, 0.50), (2, 0.12)),
                        ancientVisitChance: 0.00,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.48), (1, 0.42), (2, 0.10)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.24), (3, 0.45), (4, 0.24), (5, 0.07)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.12), (2, 0.38), (3, 0.34), (4, 0.16)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.50), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((0, 0.16), (1, 0.44), (2, 0.30), (3, 0.10)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.36), (1, 0.50), (2, 0.14)),
                        ancientVisitChance: 0.92,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.36), (1, 0.48), (2, 0.16)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        unknownCounts: WeightedIntDistribution.Of((2, 0.22), (3, 0.44), (4, 0.26), (5, 0.08)),
                        eventCounts: WeightedIntDistribution.Of((1, 0.10), (2, 0.34), (3, 0.36), (4, 0.20)),
                        treasureCounts: WeightedIntDistribution.Of((1, 0.36), (2, 0.44), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((0, 0.12), (1, 0.42), (2, 0.34), (3, 0.12)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.38), (1, 0.48), (2, 0.14)),
                        ancientVisitChance: 0.88,
                        betweenUnknownCombats: WeightedIntDistribution.Of((0, 0.30), (1, 0.50), (2, 0.20)),
                        majorStopVariants:
                        [
                            [MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Shop, MajorStopKind.Treasure, MajorStopKind.Elite, MajorStopKind.Treasure],
                            [MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Ancient, MajorStopKind.Elite, MajorStopKind.Treasure, MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure]
                        ])
                ],
                weight: 0.86)
        ];
    }

    private sealed class ActRouteProfile
    {
        public ActRouteProfile(
            WeightedIntDistribution unknownCounts,
            WeightedIntDistribution eventCounts,
            WeightedIntDistribution treasureCounts,
            WeightedIntDistribution eliteCounts,
            WeightedIntDistribution shopCounts,
            double ancientVisitChance,
            WeightedIntDistribution betweenUnknownCombats,
            IReadOnlyList<MajorStopKind[]> majorStopVariants)
        {
            UnknownCounts = unknownCounts;
            EventCounts = eventCounts;
            TreasureCounts = treasureCounts;
            EliteCounts = eliteCounts;
            ShopCounts = shopCounts;
            AncientVisitChance = ancientVisitChance;
            BetweenUnknownCombats = betweenUnknownCombats;
            MajorStopVariants = majorStopVariants;
        }

        public WeightedIntDistribution UnknownCounts { get; }

        public WeightedIntDistribution EventCounts { get; }

        public WeightedIntDistribution TreasureCounts { get; }

        public WeightedIntDistribution EliteCounts { get; }

        public WeightedIntDistribution ShopCounts { get; }

        public double AncientVisitChance { get; }

        public WeightedIntDistribution BetweenUnknownCombats { get; }

        public IReadOnlyList<MajorStopKind[]> MajorStopVariants { get; }

        public IReadOnlyList<MajorStopKind> BuildMajorStops(GameRng rng)
        {
            var remaining = new Dictionary<MajorStopKind, int>
            {
                [MajorStopKind.Treasure] = TreasureCounts.Sample(rng),
                [MajorStopKind.Elite] = EliteCounts.Sample(rng),
                [MajorStopKind.Shop] = ShopCounts.Sample(rng)
            };

            var result = new List<MajorStopKind>();
            var variant = MajorStopVariants[rng.NextInt(MajorStopVariants.Count)];
            foreach (var kind in variant)
            {
                if (!remaining.TryGetValue(kind, out var count) || count <= 0)
                {
                    continue;
                }

                remaining[kind] = count - 1;
                result.Add(kind);
            }

            foreach (var kind in new[] { MajorStopKind.Shop, MajorStopKind.Elite, MajorStopKind.Treasure })
            {
                while (remaining.GetValueOrDefault(kind) > 0)
                {
                    remaining[kind]--;
                    result.Add(kind);
                }
            }

            return result;
        }

        public Sts2EventVisibilityActSummary ToModel(int actNumber)
        {
            return new Sts2EventVisibilityActSummary
            {
                ActNumber = actNumber,
                UnknownCounts = UnknownCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                EventCounts = EventCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                AncientVisitChance = AncientVisitChance
            };
        }
    }

    private sealed record RouteProfileRun(RouteProfile Profile, Sts2EventVisibilityProfileResult Result);

    private sealed class WeightedIntDistribution
    {
        private WeightedIntDistribution(IReadOnlyList<WeightedIntOption> options)
        {
            Options = options;
            TotalWeight = options.Sum(option => option.Weight);
        }

        public IReadOnlyList<WeightedIntOption> Options { get; }

        public double TotalWeight { get; }

        public static WeightedIntDistribution Of(params (int Value, double Weight)[] options)
        {
            return new WeightedIntDistribution(options
                .Select(option => new WeightedIntOption(option.Value, option.Weight))
                .ToList());
        }

        public int Sample(GameRng rng)
        {
            var roll = rng.NextDouble() * TotalWeight;
            foreach (var option in Options)
            {
                roll -= option.Weight;
                if (roll <= 0)
                {
                    return option.Value;
                }
            }

            return Options[^1].Value;
        }
    }

    private sealed record WeightedIntOption(int Value, double Weight);
}
