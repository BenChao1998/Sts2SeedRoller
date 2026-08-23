using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SeedModel.Collections;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Sts2.Generation;

namespace SeedModel.Sts2;

internal sealed class Sts2RelicVisibilityAnalyzer
{
    private static readonly HashSet<string> ShopBlockedRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "AMETHYST_AUBERGINE",
        "BOWLER_HAT",
        "LUCKY_FYSH",
        "OLD_COIN",
        "THE_COURIER"
    };

    private static readonly HashSet<string> BeforeAct3TreasureChestRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "AMETHYST_AUBERGINE",
        "BOOK_OF_FIVE_RINGS",
        "BOWLER_HAT",
        "DRAGON_FRUIT",
        "FROZEN_EGG",
        "GIRYA",
        "JUZU_BRACELET",
        "LASTING_CANDY",
        "LUCKY_FYSH",
        "MEAL_TICKET",
        "MOLTEN_EGG",
        "OLD_COIN",
        "PLANISPHERE",
        "SHOVEL",
        "TOXIC_EGG",
        "WHITE_BEAST_STATUE",
        "WHITE_STAR"
    };

    private static readonly HashSet<string> SinglePlayerOnlyRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "SILVER_CRUCIBLE",
        "WINGED_BOOTS"
    };

    private static readonly HashSet<string> MultiplayerOnlyRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "MASSIVE_SCROLL"
    };

    private readonly Sts2WorldData _world;

    internal Sts2RelicVisibilityAnalyzer(Sts2WorldData world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    internal Sts2RelicVisibilityAnalysis Analyze(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs,
        IReadOnlyDictionary<string, string> rarityMap,
        IReadOnlyList<Sts2ActPoolPreview>? actPools = null,
        Sts2RunPreview? ancientPreview = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ancientActs);
        ArgumentNullException.ThrowIfNull(rarityMap);

        if (request.Samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Samples must be positive.");
        }

        if (request.EarlyWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Early window must be positive.");
        }

        var playerCount = Math.Max(1, request.PlayerCount);
        var ancientAvailability = request.ResolveAncientAvailability();
        var baseline = BaselineState.Create(
            _world.RelicPools,
            rarityMap,
            request.SeedText,
            request.SeedValue,
            request.Character,
            playerCount,
            request.PlayerNetId,
            request.TeamCharacters,
            request.AscensionLevel,
            ancientAvailability);
        var rewardModel = RewardSimulationModel.Create(dataset, request.Character, playerCount);
        var ancientMap = BuildAncientActRelicMap(ancientActs);
        var relicIndexMap = BuildRelicIndexMap(rarityMap.Keys, ancientMap);
        if (request.UseExactRouteCoverage && actPools != null && ancientPreview != null)
        {
            var exactProfile = RunExactRouteCoverageProfile(
                dataset,
                request,
                actPools,
                ancientPreview,
                rarityMap.Keys,
                relicIndexMap);

            return new Sts2RelicVisibilityAnalysis
            {
                SeedText = request.SeedText,
                SeedValue = request.SeedValue,
                Character = request.Character,
                PlayerCount = playerCount,
                Samples = request.Samples,
                EarlyWindow = request.EarlyWindow,
                SharedBagSize = baseline.SharedBag.TotalCount,
                PlayerBagSize = baseline.PlayerBag.TotalCount,
                Act3OnlyGateTrackedRelics = BeforeAct3TreasureChestRelics.Count,
                UsesExactRouteCoverage = true,
                AncientActs = ancientActs.OrderBy(act => act.ActNumber).ToList(),
                Profiles = [exactProfile]
            };
        }

        var profiles = RouteProfile.All;
        var profileResults = new Sts2RelicVisibilityProfileResult[profiles.Count];
        Parallel.For(0, profiles.Count, index =>
        {
            profileResults[index] = RunProfile(request, profiles[index], baseline, rewardModel, ancientMap, relicIndexMap);
        });

        return new Sts2RelicVisibilityAnalysis
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            Character = request.Character,
            PlayerCount = playerCount,
            Samples = request.Samples,
            EarlyWindow = request.EarlyWindow,
            SharedBagSize = baseline.SharedBag.TotalCount,
            PlayerBagSize = baseline.PlayerBag.TotalCount,
            Act3OnlyGateTrackedRelics = BeforeAct3TreasureChestRelics.Count,
            UsesExactRouteCoverage = false,
            AncientActs = ancientActs.OrderBy(act => act.ActNumber).ToList(),
            Profiles = profileResults
        };
    }

    internal bool MatchesHighProbabilityRelics(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs,
        IReadOnlyDictionary<string, string> rarityMap,
        IReadOnlyList<Sts2ActPoolPreview>? actPools,
        Sts2RunPreview? ancientPreview,
        Sts2PoolFilter filter)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ancientActs);
        ArgumentNullException.ThrowIfNull(rarityMap);
        ArgumentNullException.ThrowIfNull(filter);

        var requiredRelicIds = filter.HighProbabilityRelicIds;
        var targetRelics = requiredRelicIds
            .Where(relicId => !string.IsNullOrWhiteSpace(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetRelics.Count == 0)
        {
            return true;
        }

        if (request.UseExactRouteCoverage &&
            actPools != null &&
            ancientPreview != null &&
            filter.HighProbabilitySeenThreshold > 0 &&
            !AllTargetRelicsReachableOnExactRoutes(dataset, request, actPools, ancientPreview, targetRelics))
        {
            return false;
        }

        if (request.Samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Samples must be positive.");
        }

        var playerCount = Math.Max(1, request.PlayerCount);
        var ancientAvailability = request.ResolveAncientAvailability();
        var baseline = BaselineState.Create(
            _world.RelicPools,
            rarityMap,
            request.SeedText,
            request.SeedValue,
            request.Character,
            playerCount,
            request.PlayerNetId,
            request.TeamCharacters,
            request.AscensionLevel,
            ancientAvailability);
        var rewardModel = RewardSimulationModel.Create(dataset, request.Character, playerCount);
        var ancientMap = BuildAncientActRelicMap(ancientActs);
        var profiles = RouteProfile.All;
        var matchedRelics = new bool[targetRelics.Count];
        foreach (var profile in profiles)
        {
            var profileMatches = RunTargetedProfileMatches(
                request,
                profile,
                baseline,
                rewardModel,
                ancientMap,
                targetRelics,
                filter);

            for (var relicIndex = 0; relicIndex < targetRelics.Count; relicIndex++)
            {
                if (profileMatches[relicIndex])
                {
                    matchedRelics[relicIndex] = true;
                }
            }

            if (matchedRelics.All(static matched => matched))
            {
                return true;
            }
        }

        return matchedRelics.All(static matched => matched);
    }

    private bool AllTargetRelicsReachableOnExactRoutes(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2ActPoolPreview> actPools,
        Sts2RunPreview ancientPreview,
        IReadOnlyList<string> targetRelics)
    {
        var unlockedCharacters = request.UnlockedCharacters?.Count > 0
            ? request.UnlockedCharacters
            : [request.Character];
        var allRoutes = Sts2StandardShopPreviewer.GetAllRoutes(_world, new SeedRunEvaluationContext
        {
            SeedText = request.SeedText,
            RunSeed = request.SeedValue,
            Character = request.Character,
            UnlockedCharacters = unlockedCharacters,
            TeamCharacters = request.TeamCharacters,
            PlayerCount = request.PlayerCount,
            PlayerNetId = request.PlayerNetId,
            AscensionLevel = request.AscensionLevel,
            AncientAvailability = request.AncientAvailability,
            IncludeDarvSharedAncient = request.IncludeDarvSharedAncient
        });
        var totalRouteCount = allRoutes.Values.Aggregate(1L, (product, routes) => product * Math.Max(1, routes.Count));
        var maxResults = totalRouteCount > int.MaxValue ? int.MaxValue : (int)totalRouteCount;
        var analyzer = new Sts2ExactRouteAnalyzer(_world, workspaceRoot: null);
        var exactAnalysis = analyzer.Analyze(
            dataset,
            new Sts2ExactRouteAnalysisRequest
            {
                SeedText = request.SeedText,
                SeedValue = request.SeedValue,
                Character = request.Character,
                UnlockedCharacters = unlockedCharacters,
                TeamCharacters = request.TeamCharacters,
                AscensionLevel = request.AscensionLevel,
                PlayerCount = request.PlayerCount,
                PlayerNetId = request.PlayerNetId,
                AncientAvailability = request.AncientAvailability,
                IncludeDarvSharedAncient = request.IncludeDarvSharedAncient,
                MaxResults = maxResults,
                MaxRouteChecks = totalRouteCount
            },
            actPools,
            ancientPreview,
            unlockedCharacters);

        foreach (var targetRelic in targetRelics)
        {
            var matched = exactAnalysis.Matches.Any(match =>
                match.Acts.Any(act =>
                    act.AncientRelics.Any(relicId => string.Equals(relicId, targetRelic, StringComparison.OrdinalIgnoreCase)) ||
                    act.Rooms.Any(room => room.RelicIds.Any(relicId => string.Equals(relicId, targetRelic, StringComparison.OrdinalIgnoreCase)))));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private Sts2RelicVisibilityProfileResult RunProfile(
        Sts2RelicVisibilityRequest request,
        RouteProfile profile,
        BaselineState baseline,
        RewardSimulationModel rewardModel,
        IReadOnlyDictionary<int, ShownRelic[]> ancientActs,
        IReadOnlyDictionary<string, int> relicIndexMap)
    {
        var routeRng = new GameRng(request.SeedValue, $"relic_visibility_{profile.Id}");
        var relicIds = relicIndexMap
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToArray();
        var stats = new AppearanceStats[relicIds.Length];
        for (var i = 0; i < stats.Length; i++)
        {
            stats[i] = new AppearanceStats();
        }

        var earlySamples = new List<IReadOnlyList<string>>(capacity: 3);
        var sampleSeen = new bool[relicIds.Length];
        var sampleFirstSeen = new int[relicIds.Length];
        var sampleFirstAct = new int[relicIds.Length];
        var sampleFirstSource = new Sts2RelicVisibilitySource[relicIds.Length];
        var sampleSourcePresence = new byte[relicIds.Length];
        var seenIndices = new List<int>(64);
        var earliestThisSample = new List<string>();
        var opportunities = new List<Opportunity>(32);

        for (var sample = 0; sample < request.Samples; sample++)
        {
            var state = baseline.Clone();
            seenIndices.Clear();
            earliestThisSample.Clear();

            BuildOpportunities(routeRng, profile, opportunities);
            var currentAct = 0;
            foreach (var opportunity in opportunities)
            {
                if (currentAct != opportunity.ActNumber)
                {
                    currentAct = opportunity.ActNumber;
                }
                else
                {
                    var actProfile = profile.Acts[opportunity.ActNumber - 1];
                    var regularCombats = actProfile.BetweenOpportunityCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        ConsumeRegularCombat(state, rewardModel);
                    }
                }

                var shownRelics = opportunity.Kind switch
                {
                    OpportunityKind.Treasure => ShowTreasure(state, opportunity.ActNumber),
                    OpportunityKind.Elite => ShowElite(state, opportunity.ActNumber, rewardModel),
                    OpportunityKind.Shop => ShowShop(state, opportunity.ActNumber),
                    OpportunityKind.Ancient => ShowAncient(ancientActs, opportunity.ActNumber),
                    _ => Array.Empty<ShownRelic>()
                };

                foreach (var shown in shownRelics)
                {
                    if (!relicIndexMap.TryGetValue(shown.RelicId, out var relicIndex))
                    {
                        continue;
                    }

                    if (shown.Source == Sts2RelicVisibilitySource.Shop)
                    {
                        sampleSourcePresence[relicIndex] |= 0b10;
                    }
                    else
                    {
                        sampleSourcePresence[relicIndex] |= 0b01;
                    }

                    if (sampleSeen[relicIndex])
                    {
                        continue;
                    }

                    sampleSeen[relicIndex] = true;
                    seenIndices.Add(relicIndex);
                    sampleFirstSeen[relicIndex] = opportunity.GlobalIndex;
                    sampleFirstAct[relicIndex] = opportunity.ActNumber;
                    sampleFirstSource[relicIndex] = shown.Source;
                    if (opportunity.GlobalIndex <= request.EarlyWindow)
                    {
                        earliestThisSample.Add(shown.RelicId);
                    }
                }
            }

            if (earlySamples.Count < 3)
            {
                earlySamples.Add(earliestThisSample);
            }

            foreach (var relicIndex in seenIndices)
            {
                var relicStats = stats[relicIndex];
                var firstIndex = sampleFirstSeen[relicIndex];
                relicStats.SeenCount++;
                relicStats.FirstOpportunityTotal += firstIndex;
                if (firstIndex <= request.EarlyWindow)
                {
                    relicStats.EarlyCount++;
                }

                var firstAct = sampleFirstAct[relicIndex];
                relicStats.FirstSeenActCounts[firstAct] =
                    relicStats.FirstSeenActCounts.GetValueOrDefault(firstAct) + 1;

                var presence = sampleSourcePresence[relicIndex];
                if ((presence & 0b01) != 0)
                {
                    relicStats.NonShopSeenCount++;
                }

                if ((presence & 0b10) != 0)
                {
                    relicStats.ShopSeenCount++;
                }

                var firstSource = sampleFirstSource[relicIndex];
                relicStats.FirstSourceCounts[firstSource] =
                    relicStats.FirstSourceCounts.GetValueOrDefault(firstSource) + 1;

                sampleSeen[relicIndex] = false;
                sampleSourcePresence[relicIndex] = 0;
            }
        }

        var ranked = new List<Sts2RelicVisibilityRankedRelic>(stats.Length);
        for (var i = 0; i < stats.Length; i++)
        {
            if (stats[i].SeenCount <= 0)
            {
                continue;
            }

            ranked.Add(ToRankedRelic(relicIds[i], stats[i], request.Samples));
        }

        ranked.Sort(CompareEarlyRelics);

        var seenRanked = new List<Sts2RelicVisibilityRankedRelic>(ranked);
        seenRanked.Sort(CompareSeenRelics);

        return new Sts2RelicVisibilityProfileResult
        {
            Id = profile.Id,
            Title = profile.Title,
            Description = profile.Description,
            Acts = profile.Acts
                .Select((act, index) => act.ToModel(index + 1))
                .ToList(),
            EarlyRelics = ranked,
            SeenRelics = seenRanked,
            EarlySamples = earlySamples
        };
    }

    private bool[] RunTargetedProfileMatches(
        Sts2RelicVisibilityRequest request,
        RouteProfile profile,
        BaselineState baseline,
        RewardSimulationModel rewardModel,
        IReadOnlyDictionary<int, ShownRelic[]> ancientActs,
        IReadOnlyList<string> targetRelics,
        Sts2PoolFilter filter)
    {
        var routeRng = new GameRng(request.SeedValue, $"relic_visibility_{profile.Id}");
        var targetIds = targetRelics
            .Where(static relicId => !string.IsNullOrWhiteSpace(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetIndexMap = new Dictionary<string, int>(targetIds.Length, StringComparer.OrdinalIgnoreCase);
        var stats = new TargetedRelicStats[targetIds.Length];
        for (var i = 0; i < targetIds.Length; i++)
        {
            targetIndexMap[targetIds[i]] = i;
        }

        var sampleSeen = new bool[targetIds.Length];
        var sampleFirstSeen = new int[targetIds.Length];
        var sampleFirstSource = new Sts2RelicVisibilitySource[targetIds.Length];
        var sampleShopSeen = new bool[targetIds.Length];
        var sampleNonShopSeen = new bool[targetIds.Length];
        var seenIndices = new List<int>(targetIds.Length);
        var opportunities = new List<Opportunity>(32);
        var enablePruning = ShouldEnableTargetedRelicPruning(filter);
        var possible = enablePruning ? new bool[targetIds.Length] : null;
        if (possible != null)
        {
            Array.Fill(possible, true);
        }

        var remainingPossible = targetIds.Length;
        for (var sample = 0; sample < request.Samples; sample++)
        {
            if (enablePruning && remainingPossible == 0)
            {
                break;
            }

            var state = baseline.Clone();
            seenIndices.Clear();
            BuildOpportunities(routeRng, profile, opportunities);
            var currentAct = 0;

            foreach (var opportunity in opportunities)
            {
                if (currentAct != opportunity.ActNumber)
                {
                    currentAct = opportunity.ActNumber;
                }
                else
                {
                    var actProfile = profile.Acts[opportunity.ActNumber - 1];
                    var regularCombats = actProfile.BetweenOpportunityCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        ConsumeRegularCombat(state, rewardModel);
                    }
                }

                var shownRelics = opportunity.Kind switch
                {
                    OpportunityKind.Treasure => ShowTreasure(state, opportunity.ActNumber),
                    OpportunityKind.Elite => ShowElite(state, opportunity.ActNumber, rewardModel),
                    OpportunityKind.Shop => ShowShop(state, opportunity.ActNumber),
                    OpportunityKind.Ancient => ShowAncient(ancientActs, opportunity.ActNumber),
                    _ => Array.Empty<ShownRelic>()
                };

                foreach (var shown in shownRelics)
                {
                    if (!targetIndexMap.TryGetValue(shown.RelicId, out var targetIndex) ||
                        (possible != null && !possible[targetIndex]))
                    {
                        continue;
                    }

                    if (shown.Source == Sts2RelicVisibilitySource.Shop)
                    {
                        sampleShopSeen[targetIndex] = true;
                    }
                    else
                    {
                        sampleNonShopSeen[targetIndex] = true;
                    }

                    if (sampleSeen[targetIndex])
                    {
                        continue;
                    }

                    sampleSeen[targetIndex] = true;
                    sampleFirstSeen[targetIndex] = opportunity.GlobalIndex;
                    sampleFirstSource[targetIndex] = shown.Source;
                    seenIndices.Add(targetIndex);

                    if (seenIndices.Count == targetIds.Length)
                    {
                        break;
                    }
                }

                if (seenIndices.Count == targetIds.Length)
                {
                    break;
                }
            }

            for (var index = 0; index < targetIds.Length; index++)
            {
                var relicStats = stats[index];

                if (sampleSeen[index])
                {
                    relicStats.SeenCount++;
                    relicStats.FirstOpportunityTotal += sampleFirstSeen[index];
                    if (sampleFirstSeen[index] <= request.EarlyWindow)
                    {
                        relicStats.EarlyCount++;
                    }

                    relicStats.AddFirstSource(sampleFirstSource[index]);
                }

                if (sampleNonShopSeen[index])
                {
                    relicStats.NonShopSeenCount++;
                }

                if (sampleShopSeen[index])
                {
                    relicStats.ShopSeenCount++;
                }

                stats[index] = relicStats;

                if (possible != null &&
                    possible[index] &&
                    !CanStillMatchTargetedRelicStats(
                        filter,
                        relicStats,
                        samplesProcessed: sample + 1,
                        totalSamples: request.Samples))
                {
                    possible[index] = false;
                    remainingPossible--;
                }

                sampleSeen[index] = false;
                sampleShopSeen[index] = false;
                sampleNonShopSeen[index] = false;
            }
        }

        var result = new bool[targetIds.Length];
        for (var i = 0; i < targetIds.Length; i++)
        {
            result[i] = MatchesTargetedRelicStats(filter, stats[i], request.Samples);
        }

        return result;
    }

    private Sts2RelicVisibilityProfileResult RunExactRouteCoverageProfile(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2ActPoolPreview> actPools,
        Sts2RunPreview ancientPreview,
        IEnumerable<string> trackedRelicIds,
        IReadOnlyDictionary<string, int> relicIndexMap)
    {
        var unlockedCharacters = request.UnlockedCharacters?.Count > 0
            ? request.UnlockedCharacters
            : [request.Character];
        var context = new SeedRunEvaluationContext
        {
            SeedText = request.SeedText,
            RunSeed = request.SeedValue,
            Character = request.Character,
            UnlockedCharacters = unlockedCharacters,
            TeamCharacters = request.TeamCharacters,
            PlayerCount = request.PlayerCount,
            PlayerNetId = request.PlayerNetId,
            AscensionLevel = request.AscensionLevel,
            AncientAvailability = request.AncientAvailability,
            IncludeDarvSharedAncient = request.IncludeDarvSharedAncient
        };
        var allRoutes = Sts2StandardShopPreviewer.GetAllRoutes(_world, context);
        var totalRouteCount = allRoutes.Values.Aggregate(1L, (product, routes) => product * Math.Max(1, routes.Count));
        var maxResults = totalRouteCount > int.MaxValue ? int.MaxValue : (int)totalRouteCount;
        var exactAnalyzer = new Sts2ExactRouteAnalyzer(_world, workspaceRoot: null);
        var exactAnalysis = exactAnalyzer.Analyze(
            dataset,
            new Sts2ExactRouteAnalysisRequest
            {
                SeedText = request.SeedText,
                SeedValue = request.SeedValue,
                Character = request.Character,
                UnlockedCharacters = unlockedCharacters,
                TeamCharacters = request.TeamCharacters,
                AscensionLevel = request.AscensionLevel,
                PlayerCount = request.PlayerCount,
                PlayerNetId = request.PlayerNetId,
                AncientAvailability = request.AncientAvailability,
                IncludeDarvSharedAncient = request.IncludeDarvSharedAncient,
                MaxResults = maxResults,
                MaxRouteChecks = totalRouteCount
            },
            actPools,
            ancientPreview,
            unlockedCharacters);

        var relicIds = relicIndexMap
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToArray();
        var stats = new AppearanceStats[relicIds.Length];
        for (var i = 0; i < stats.Length; i++)
        {
            stats[i] = new AppearanceStats();
        }

        var trackedSet = trackedRelicIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var earlySamples = new List<IReadOnlyList<string>>(capacity: 3);
        var actTreasureCounts = new Dictionary<int, Dictionary<int, int>>();
        var actEliteCounts = new Dictionary<int, Dictionary<int, int>>();
        var actShopCounts = new Dictionary<int, Dictionary<int, int>>();
        var actAncientCounts = new Dictionary<int, Dictionary<int, int>>();

        foreach (var match in exactAnalysis.Matches)
        {
            var sampleSeen = new bool[relicIds.Length];
            var sampleFirstSeen = new int[relicIds.Length];
            var sampleFirstAct = new int[relicIds.Length];
            var sampleFirstSource = new Sts2RelicVisibilitySource[relicIds.Length];
            var sampleSourcePresence = new byte[relicIds.Length];
            var seenIndices = new List<int>(64);
            var earliestThisSample = new List<string>();
            var opportunityIndex = 0;

            foreach (var act in match.Acts.OrderBy(item => item.ActNumber))
            {
                var ancientCount = act.ActNumber > 1 && act.AncientRelics.Count > 0 ? 1 : 0;
                IncrementCount(actAncientCounts, act.ActNumber, ancientCount);
                if (ancientCount > 0)
                {
                    opportunityIndex++;
                    foreach (var relicId in act.AncientRelics)
                    {
                        RegisterExactSeenRelic(
                            relicId,
                            act.ActNumber,
                            act.ActNumber == 2 ? Sts2RelicVisibilitySource.AncientAct2 : Sts2RelicVisibilitySource.AncientAct3,
                            opportunityIndex);
                    }
                }

                var treasureCount = act.Rooms.Count(room => string.Equals(room.RoomType, "Treasure", StringComparison.OrdinalIgnoreCase));
                var eliteCount = act.Rooms.Count(room => string.Equals(room.RoomType, "Elite", StringComparison.OrdinalIgnoreCase));
                var shopCount = act.Rooms.Count(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase));
                IncrementCount(actTreasureCounts, act.ActNumber, treasureCount);
                IncrementCount(actEliteCounts, act.ActNumber, eliteCount);
                IncrementCount(actShopCounts, act.ActNumber, shopCount);

                foreach (var room in act.Rooms)
                {
                    if (!TryMapRoomTypeToSource(room.RoomType, out var source))
                    {
                        continue;
                    }

                    opportunityIndex++;
                    foreach (var relicId in room.RelicIds)
                    {
                        RegisterExactSeenRelic(relicId, act.ActNumber, source, opportunityIndex);
                    }
                }
            }

            if (earlySamples.Count < 3)
            {
                earlySamples.Add(earliestThisSample);
            }

            foreach (var relicIndex in seenIndices)
            {
                var relicStats = stats[relicIndex];
                var firstIndex = sampleFirstSeen[relicIndex];
                relicStats.SeenCount++;
                relicStats.FirstOpportunityTotal += firstIndex;
                if (firstIndex <= request.EarlyWindow)
                {
                    relicStats.EarlyCount++;
                }

                var firstAct = sampleFirstAct[relicIndex];
                relicStats.FirstSeenActCounts[firstAct] =
                    relicStats.FirstSeenActCounts.GetValueOrDefault(firstAct) + 1;

                var presence = sampleSourcePresence[relicIndex];
                if ((presence & 0b01) != 0)
                {
                    relicStats.NonShopSeenCount++;
                }

                if ((presence & 0b10) != 0)
                {
                    relicStats.ShopSeenCount++;
                }

                var firstSource = sampleFirstSource[relicIndex];
                relicStats.FirstSourceCounts[firstSource] =
                    relicStats.FirstSourceCounts.GetValueOrDefault(firstSource) + 1;
            }

            void RegisterExactSeenRelic(
                string relicId,
                int actNumber,
                Sts2RelicVisibilitySource source,
                int firstOpportunity)
            {
                if (string.IsNullOrWhiteSpace(relicId) ||
                    !trackedSet.Contains(relicId) ||
                    !relicIndexMap.TryGetValue(relicId, out var relicIndex))
                {
                    return;
                }

                if (source == Sts2RelicVisibilitySource.Shop)
                {
                    sampleSourcePresence[relicIndex] |= 0b10;
                }
                else
                {
                    sampleSourcePresence[relicIndex] |= 0b01;
                }

                if (sampleSeen[relicIndex])
                {
                    return;
                }

                sampleSeen[relicIndex] = true;
                sampleFirstSeen[relicIndex] = firstOpportunity;
                sampleFirstAct[relicIndex] = actNumber;
                sampleFirstSource[relicIndex] = source;
                seenIndices.Add(relicIndex);
                if (firstOpportunity <= request.EarlyWindow)
                {
                    earliestThisSample.Add(relicId);
                }
            }
        }

        var ranked = new List<Sts2RelicVisibilityRankedRelic>(stats.Length);
        for (var i = 0; i < stats.Length; i++)
        {
            if (stats[i].SeenCount <= 0)
            {
                continue;
            }

            ranked.Add(ToRankedRelic(relicIds[i], stats[i], exactAnalysis.Matches.Count));
        }

        ranked.Sort(CompareEarlyRelics);
        var seenRanked = new List<Sts2RelicVisibilityRankedRelic>(ranked);
        seenRanked.Sort(CompareSeenRelics);

        return new Sts2RelicVisibilityProfileResult
        {
            Id = "exact-map",
            Title = "真实地图",
            Description = "按该种子的真实地图路线全量统计，与精确路线分析保持同一套底层逻辑。",
            Acts =
            [
                BuildExactActSummary(1, actTreasureCounts, actEliteCounts, actShopCounts, actAncientCounts, exactAnalysis.Matches.Count),
                BuildExactActSummary(2, actTreasureCounts, actEliteCounts, actShopCounts, actAncientCounts, exactAnalysis.Matches.Count),
                BuildExactActSummary(3, actTreasureCounts, actEliteCounts, actShopCounts, actAncientCounts, exactAnalysis.Matches.Count)
            ],
            EarlyRelics = ranked,
            SeenRelics = seenRanked,
            EarlySamples = earlySamples
        };
    }

    private static bool MatchesTargetedRelicStats(
        Sts2PoolFilter filter,
        TargetedRelicStats stats,
        int totalSamples)
    {
        var seenProbability = (double)stats.SeenCount / totalSamples;
        if (seenProbability < filter.HighProbabilitySeenThreshold)
        {
            return false;
        }

        if (filter.HighProbabilityNonShopThreshold.HasValue &&
            (double)stats.NonShopSeenCount / totalSamples < filter.HighProbabilityNonShopThreshold.Value)
        {
            return false;
        }

        if (filter.HighProbabilityShopThreshold.HasValue &&
            (double)stats.ShopSeenCount / totalSamples < filter.HighProbabilityShopThreshold.Value)
        {
            return false;
        }

        if (filter.HighProbabilityEarlyThreshold.HasValue &&
            (double)stats.EarlyCount / totalSamples < filter.HighProbabilityEarlyThreshold.Value)
        {
            return false;
        }

        var averageFirstOpportunity = stats.SeenCount == 0
            ? double.PositiveInfinity
            : stats.FirstOpportunityTotal / stats.SeenCount;
        if (filter.HighProbabilityAverageFirstOpportunityMax.HasValue &&
            averageFirstOpportunity > filter.HighProbabilityAverageFirstOpportunityMax.Value)
        {
            return false;
        }

        if (filter.HighProbabilityMostCommonSource.HasValue &&
            stats.GetMostCommonSource() != filter.HighProbabilityMostCommonSource.Value)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldEnableTargetedRelicPruning(Sts2PoolFilter filter)
    {
        return filter.HighProbabilitySeenThreshold >= 0.5 ||
               filter.HighProbabilityNonShopThreshold >= 0.5 ||
               filter.HighProbabilityShopThreshold >= 0.5 ||
               filter.HighProbabilityEarlyThreshold >= 0.5;
    }

    private static bool CanStillMatchTargetedRelicStats(
        Sts2PoolFilter filter,
        TargetedRelicStats stats,
        int samplesProcessed,
        int totalSamples)
    {
        var remainingSamples = totalSamples - samplesProcessed;
        if (!CanStillReachThreshold(stats.SeenCount, remainingSamples, totalSamples, filter.HighProbabilitySeenThreshold))
        {
            return false;
        }

        if (filter.HighProbabilityNonShopThreshold.HasValue &&
            !CanStillReachThreshold(stats.NonShopSeenCount, remainingSamples, totalSamples, filter.HighProbabilityNonShopThreshold.Value))
        {
            return false;
        }

        if (filter.HighProbabilityShopThreshold.HasValue &&
            !CanStillReachThreshold(stats.ShopSeenCount, remainingSamples, totalSamples, filter.HighProbabilityShopThreshold.Value))
        {
            return false;
        }

        if (filter.HighProbabilityEarlyThreshold.HasValue &&
            !CanStillReachThreshold(stats.EarlyCount, remainingSamples, totalSamples, filter.HighProbabilityEarlyThreshold.Value))
        {
            return false;
        }

        return true;
    }

    private static bool CanStillReachThreshold(int currentCount, int remainingSamples, int totalSamples, double threshold)
    {
        var requiredCount = (int)Math.Ceiling(threshold * totalSamples);
        return currentCount + remainingSamples >= requiredCount;
    }

    private void BuildOpportunities(GameRng rng, RouteProfile profile, List<Opportunity> result)
    {
        result.Clear();
        var globalIndex = 0;
        for (var actNumber = 1; actNumber <= profile.Acts.Count; actNumber++)
        {
            var actProfile = profile.Acts[actNumber - 1];
            var remaining = new Dictionary<OpportunityKind, int>
            {
                [OpportunityKind.Treasure] = actProfile.TreasureCounts.Sample(rng),
                [OpportunityKind.Elite] = actProfile.EliteCounts.Sample(rng),
                [OpportunityKind.Shop] = actProfile.ShopCounts.Sample(rng),
                [OpportunityKind.Ancient] = actNumber == 1
                    ? 0
                    : (rng.NextDouble() < actProfile.AncientVisitChance ? 1 : 0)
            };

            var slotVariant = actProfile.SlotVariants[rng.NextInt(actProfile.SlotVariants.Count)];
            foreach (var kind in slotVariant)
            {
                if (!remaining.TryGetValue(kind, out var count) || count <= 0)
                {
                    continue;
                }

                remaining[kind] = count - 1;
                globalIndex++;
                result.Add(new Opportunity(actNumber, kind, globalIndex));
            }

            foreach (var kind in new[] { OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure })
            {
                while (remaining.GetValueOrDefault(kind) > 0)
                {
                    remaining[kind]--;
                    globalIndex++;
                    result.Add(new Opportunity(actNumber, kind, globalIndex));
                }
            }
        }
    }

    private static bool TryMapRoomTypeToSource(string? roomType, out Sts2RelicVisibilitySource source)
    {
        switch (roomType)
        {
            case "Treasure":
                source = Sts2RelicVisibilitySource.Treasure;
                return true;
            case "Elite":
                source = Sts2RelicVisibilitySource.Elite;
                return true;
            case "Shop":
                source = Sts2RelicVisibilitySource.Shop;
                return true;
            default:
                source = default;
                return false;
        }
    }

    private static void IncrementCount(
        IDictionary<int, Dictionary<int, int>> countsByAct,
        int actNumber,
        int count)
    {
        if (!countsByAct.TryGetValue(actNumber, out var counts))
        {
            counts = new Dictionary<int, int>();
            countsByAct[actNumber] = counts;
        }

        counts[count] = counts.GetValueOrDefault(count) + 1;
    }

    private static Sts2RelicVisibilityActSummary BuildExactActSummary(
        int actNumber,
        IReadOnlyDictionary<int, Dictionary<int, int>> treasureCounts,
        IReadOnlyDictionary<int, Dictionary<int, int>> eliteCounts,
        IReadOnlyDictionary<int, Dictionary<int, int>> shopCounts,
        IReadOnlyDictionary<int, Dictionary<int, int>> ancientCounts,
        int totalRoutes)
    {
        return new Sts2RelicVisibilityActSummary
        {
            ActNumber = actNumber,
            TreasureCounts = ToWeightedChances(treasureCounts.GetValueOrDefault(actNumber), totalRoutes),
            EliteCounts = ToWeightedChances(eliteCounts.GetValueOrDefault(actNumber), totalRoutes),
            ShopCounts = ToWeightedChances(shopCounts.GetValueOrDefault(actNumber), totalRoutes),
            AncientVisitChance = totalRoutes <= 0
                ? 0d
                : ancientCounts.GetValueOrDefault(actNumber)?.GetValueOrDefault(1) / (double)totalRoutes ?? 0d
        };
    }

    private static IReadOnlyList<Sts2WeightedIntChance> ToWeightedChances(
        IReadOnlyDictionary<int, int>? counts,
        int totalRoutes)
    {
        if (counts == null || totalRoutes <= 0)
        {
            return [];
        }

        return counts
            .OrderBy(pair => pair.Key)
            .Select(pair => new Sts2WeightedIntChance
            {
                Value = pair.Key,
                Weight = pair.Value / (double)totalRoutes
            })
            .ToList();
    }

    private IReadOnlyList<ShownRelic> ShowTreasure(BaselineState state, int actNumber)
    {
        EnsureActRestrictions(state, actNumber);
        var rarity = RollRelicRarity(state.TreasureRng);
        var relic = state.SharedBag.PullFromFront(rarity);
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.PlayerBag.Remove(relic);
        return [new ShownRelic(relic, Sts2RelicVisibilitySource.Treasure)];
    }

    private IReadOnlyList<ShownRelic> ShowElite(BaselineState state, int actNumber, RewardSimulationModel rewardModel)
    {
        EnsureActRestrictions(state, actNumber);
        ConsumeEliteCombatPreRelic(state, rewardModel);
        var rarity = RollRelicRarity(state.RewardsRng);
        var relic = state.PlayerBag.PullFromFront(rarity);
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.SharedBag.Remove(relic);
        return [new ShownRelic(relic, Sts2RelicVisibilitySource.Elite)];
    }

    private IReadOnlyList<ShownRelic> ShowShop(BaselineState state, int actNumber)
    {
        EnsureActRestrictions(state, actNumber);
        ConsumeShopPreRelicRewards(state);
        var shown = new List<ShownRelic>(3);
        string? selected1 = null;
        string? selected2 = null;

        PullShopRelic(RollRelicRarity(state.RewardsRng));
        PullShopRelic(RollRelicRarity(state.RewardsRng));
        PullShopRelic(RelicRarity.Shop);

        return shown;

        void PullShopRelic(RelicRarity rarity)
        {
            var relic = state.PlayerBag.PullFromBack(
                rarity,
                selected1,
                selected2,
                ShopBlockedRelics);
            if (relic == null)
            {
                return;
            }

            if (selected1 == null)
            {
                selected1 = relic;
            }
            else if (selected2 == null)
            {
                selected2 = relic;
            }

            state.SharedBag.Remove(relic);
            shown.Add(new ShownRelic(relic, Sts2RelicVisibilitySource.Shop));
        }
    }

    private static IReadOnlyList<ShownRelic> ShowAncient(
        IReadOnlyDictionary<int, ShownRelic[]> ancientActs,
        int actNumber)
    {
        if (!ancientActs.TryGetValue(actNumber, out var act))
        {
            return Array.Empty<ShownRelic>();
        }

        return act;
    }

    private static void ConsumeRegularCombat(BaselineState state, RewardSimulationModel rewardModel)
    {
        var hasPotionReward = RollPotionRewardChance(state, isElite: false);
        _ = state.RewardsRng.NextInt(10, 21);
        if (hasPotionReward)
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.RegularEncounter);
        }
    }

    private static void ConsumeEliteCombatPreRelic(BaselineState state, RewardSimulationModel rewardModel)
    {
        var hasPotionReward = RollPotionRewardChance(state, isElite: true);
        _ = state.RewardsRng.NextInt(25, 36);
        if (hasPotionReward)
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.EliteEncounter);
        }
    }

    private static void ConsumeShopPreRelicRewards(BaselineState state)
    {
        for (var i = 0; i < 5; i++)
        {
            _ = state.RewardsRng.NextFloat();
            _ = state.RewardsRng.NextFloat();
        }

        for (var i = 0; i < 2; i++)
        {
            _ = state.RewardsRng.NextFloat();
        }
    }

    private static bool RollPotionRewardChance(BaselineState state, bool isElite)
    {
        var current = state.PotionChance;
        var roll = state.RewardsRng.NextFloat();
        if (roll < current)
        {
            state.PotionChance -= 0.1f;
        }
        else
        {
            state.PotionChance += 0.1f;
        }

        var eliteBonus = isElite ? 0.125f : 0f;
        return roll < current + eliteBonus;
    }

    private static void RollPotionReward(BaselineState state, RewardSimulationModel rewardModel)
    {
        var rarity = RollPotionRarity(state.RewardsRng);
        if (!rewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) || pool.Length == 0)
        {
            pool = rewardModel.PotionPool.ToArray();
        }

        if (pool.Length > 0)
        {
            _ = state.RewardsRng.NextItem(pool);
        }
    }

    private static void RollCombatRewardCard(BaselineState state, RewardSimulationModel rewardModel, CardRarityOddsType oddsType)
    {
        if (rewardModel.CardPool.Count == 0)
        {
            return;
        }

        var rarity = RollCardRarity(state, oddsType);
        string cardId;
        while (!TryPickAvailableCard(rewardModel, rarity, state.CurrentRewardCards, state.RewardsRng, out cardId))
        {
            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return;
            }
        }

        state.CurrentRewardCards.Add(cardId);
        _ = state.RewardsRng.NextFloat();
        if (state.CurrentRewardCards.Count >= 3)
        {
            state.CurrentRewardCards.Clear();
        }
    }

    private static CardRarity RollCardRarity(BaselineState state, CardRarityOddsType oddsType)
    {
        var roll = state.RewardsRng.NextFloat();
        var rareOdds = GetBaseCardOdds(oddsType, CardRarity.Rare, state.AscensionLevel) + state.CardRareOffset;
        CardRarity rarity;
        if (roll < rareOdds)
        {
            rarity = CardRarity.Rare;
        }
        else if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon, state.AscensionLevel) + rareOdds)
        {
            rarity = CardRarity.Uncommon;
        }
        else
        {
            rarity = CardRarity.Common;
        }

        if (rarity == CardRarity.Rare)
        {
            state.CardRareOffset = -0.05f;
        }
        else
        {
            state.CardRareOffset = Math.Min(
                state.CardRareOffset + (state.AscensionLevel >= 7 ? 0.005f : 0.01f),
                0.4f);
        }

        return rarity;
    }

    private static float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity, int ascensionLevel)
    {
        var scarcityActive = ascensionLevel >= 7;
        return oddsType switch
        {
            CardRarityOddsType.EliteEncounter => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
                CardRarity.Uncommon => 0.4f,
                CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
                _ => 0f
            },
            CardRarityOddsType.BossEncounter => rarity switch
            {
                CardRarity.Common => 0f,
                CardRarity.Uncommon => 0f,
                CardRarity.Rare => 1f,
                _ => 0f
            },
            CardRarityOddsType.Shop => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.585f : 0.54f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.045f : 0.09f,
                _ => 0f
            },
            _ => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
                _ => 0f
            }
        };
    }

    private static bool TryPickAvailableCard(
        RewardSimulationModel rewardModel,
        CardRarity rarity,
        RewardCardBuffer excluded,
        GameRng rng,
        out string cardId)
    {
        if (!rewardModel.CardPoolByRarity.TryGetValue(rarity, out var pool))
        {
            cardId = string.Empty;
            return false;
        }

        var availableCount = 0;
        for (var i = 0; i < pool.Length; i++)
        {
            if (!excluded.Contains(pool[i]))
            {
                availableCount++;
            }
        }

        if (availableCount == 0)
        {
            cardId = string.Empty;
            return false;
        }

        var targetIndex = rng.NextInt(availableCount);
        for (var i = 0; i < pool.Length; i++)
        {
            var candidate = pool[i];
            if (excluded.Contains(candidate))
            {
                continue;
            }

            if (targetIndex == 0)
            {
                cardId = candidate;
                return true;
            }

            targetIndex--;
        }

        cardId = string.Empty;
        return false;
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.None
        };

    private static PotionRarity RollPotionRarity(GameRng rng)
    {
        var roll = rng.NextFloat();
        if (roll <= 0.1f)
        {
            return PotionRarity.Rare;
        }

        if (roll <= 0.35f)
        {
            return PotionRarity.Uncommon;
        }

        return PotionRarity.Common;
    }

    private static RelicRarity RollRelicRarity(GameRng rng)
    {
        var value = rng.NextFloat();
        if (value < 0.5f)
        {
            return RelicRarity.Common;
        }

        if (value < 0.83f)
        {
            return RelicRarity.Uncommon;
        }

        return RelicRarity.Rare;
    }

    private static void EnsureActRestrictions(BaselineState state, int actNumber)
    {
        if (actNumber < 3 || state.Act3RestrictionsApplied)
        {
            return;
        }

        state.SharedBag.RemoveAll(BeforeAct3TreasureChestRelics);
        state.PlayerBag.RemoveAll(BeforeAct3TreasureChestRelics);
        state.Act3RestrictionsApplied = true;
    }

    private static Sts2RelicVisibilityRankedRelic ToRankedRelic(string relicId, AppearanceStats stats, int totalSamples)
    {
        var mostCommonSource = GetMostCommonSource(stats.FirstSourceCounts);

        return new Sts2RelicVisibilityRankedRelic
        {
            RelicId = relicId,
            EarlyProbability = (double)stats.EarlyCount / totalSamples,
            SeenProbability = (double)stats.SeenCount / totalSamples,
            NonShopSeenProbability = (double)stats.NonShopSeenCount / totalSamples,
            ShopSeenProbability = (double)stats.ShopSeenCount / totalSamples,
            AverageFirstOpportunity = stats.SeenCount == 0 ? double.PositiveInfinity : stats.FirstOpportunityTotal / stats.SeenCount,
            FirstSeenActChances = BuildActChances(stats.FirstSeenActCounts, totalSamples),
            MostCommonSource = mostCommonSource
        };
    }

    private static List<Sts2RelicVisibilityActChance> BuildActChances(
        IReadOnlyDictionary<int, int> firstSeenActCounts,
        int totalSamples)
    {
        if (firstSeenActCounts.Count == 0)
        {
            return [];
        }

        var result = new List<Sts2RelicVisibilityActChance>(firstSeenActCounts.Count);
        if (firstSeenActCounts.TryGetValue(1, out var act1Count) && act1Count > 0)
        {
            result.Add(new Sts2RelicVisibilityActChance
            {
                ActNumber = 1,
                Probability = (double)act1Count / totalSamples
            });
        }

        if (firstSeenActCounts.TryGetValue(2, out var act2Count) && act2Count > 0)
        {
            result.Add(new Sts2RelicVisibilityActChance
            {
                ActNumber = 2,
                Probability = (double)act2Count / totalSamples
            });
        }

        if (firstSeenActCounts.TryGetValue(3, out var act3Count) && act3Count > 0)
        {
            result.Add(new Sts2RelicVisibilityActChance
            {
                ActNumber = 3,
                Probability = (double)act3Count / totalSamples
            });
        }

        if (result.Count == firstSeenActCounts.Count)
        {
            return result;
        }

        foreach (var pair in firstSeenActCounts)
        {
            if (pair.Key is 1 or 2 or 3 || pair.Value <= 0)
            {
                continue;
            }

            result.Add(new Sts2RelicVisibilityActChance
            {
                ActNumber = pair.Key,
                Probability = (double)pair.Value / totalSamples
            });
        }

        result.Sort(static (left, right) => left.ActNumber.CompareTo(right.ActNumber));
        return result;
    }

    private static Sts2RelicVisibilitySource GetMostCommonSource(
        IReadOnlyDictionary<Sts2RelicVisibilitySource, int> firstSourceCounts)
    {
        if (firstSourceCounts.Count == 0)
        {
            return Sts2RelicVisibilitySource.Treasure;
        }

        var bestSource = Sts2RelicVisibilitySource.Treasure;
        var bestCount = int.MinValue;
        foreach (var pair in firstSourceCounts)
        {
            if (pair.Value > bestCount ||
                (pair.Value == bestCount && pair.Key < bestSource))
            {
                bestSource = pair.Key;
                bestCount = pair.Value;
            }
        }

        return bestSource;
    }

    private static int CompareEarlyRelics(Sts2RelicVisibilityRankedRelic left, Sts2RelicVisibilityRankedRelic right)
    {
        var compare = right.EarlyProbability.CompareTo(left.EarlyProbability);
        if (compare != 0)
        {
            return compare;
        }

        compare = right.SeenProbability.CompareTo(left.SeenProbability);
        if (compare != 0)
        {
            return compare;
        }

        compare = left.AverageFirstOpportunity.CompareTo(right.AverageFirstOpportunity);
        if (compare != 0)
        {
            return compare;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.RelicId, right.RelicId);
    }

    private static int CompareSeenRelics(Sts2RelicVisibilityRankedRelic left, Sts2RelicVisibilityRankedRelic right)
    {
        var compare = right.SeenProbability.CompareTo(left.SeenProbability);
        if (compare != 0)
        {
            return compare;
        }

        compare = right.EarlyProbability.CompareTo(left.EarlyProbability);
        if (compare != 0)
        {
            return compare;
        }

        compare = left.AverageFirstOpportunity.CompareTo(right.AverageFirstOpportunity);
        if (compare != 0)
        {
            return compare;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.RelicId, right.RelicId);
    }

    private sealed class AppearanceStats
    {
        public int SeenCount { get; set; }

        public int EarlyCount { get; set; }

        public int NonShopSeenCount { get; set; }

        public int ShopSeenCount { get; set; }

        public double FirstOpportunityTotal { get; set; }

        public Dictionary<int, int> FirstSeenActCounts { get; } = new();

        public Dictionary<Sts2RelicVisibilitySource, int> FirstSourceCounts { get; } = new();
    }

    private struct TargetedRelicStats
    {
        private int _treasureFirstCount;
        private int _eliteFirstCount;
        private int _shopFirstCount;
        private int _ancientAct2FirstCount;
        private int _ancientAct3FirstCount;

        public int SeenCount;

        public int EarlyCount;

        public int NonShopSeenCount;

        public int ShopSeenCount;

        public double FirstOpportunityTotal;

        public void AddFirstSource(Sts2RelicVisibilitySource source)
        {
            switch (source)
            {
                case Sts2RelicVisibilitySource.Treasure:
                    _treasureFirstCount++;
                    break;
                case Sts2RelicVisibilitySource.Elite:
                    _eliteFirstCount++;
                    break;
                case Sts2RelicVisibilitySource.Shop:
                    _shopFirstCount++;
                    break;
                case Sts2RelicVisibilitySource.AncientAct2:
                    _ancientAct2FirstCount++;
                    break;
                case Sts2RelicVisibilitySource.AncientAct3:
                    _ancientAct3FirstCount++;
                    break;
            }
        }

        public Sts2RelicVisibilitySource GetMostCommonSource()
        {
            var bestSource = Sts2RelicVisibilitySource.Treasure;
            var bestCount = _treasureFirstCount;

            Consider(Sts2RelicVisibilitySource.Elite, _eliteFirstCount);
            Consider(Sts2RelicVisibilitySource.Shop, _shopFirstCount);
            Consider(Sts2RelicVisibilitySource.AncientAct2, _ancientAct2FirstCount);
            Consider(Sts2RelicVisibilitySource.AncientAct3, _ancientAct3FirstCount);

            return bestSource;

            void Consider(Sts2RelicVisibilitySource source, int count)
            {
                if (count > bestCount)
                {
                    bestCount = count;
                    bestSource = source;
                }
            }
        }
    }

    private readonly record struct Opportunity(int ActNumber, OpportunityKind Kind, int GlobalIndex);

    private readonly record struct ShownRelic(string RelicId, Sts2RelicVisibilitySource Source);

    private static IReadOnlyDictionary<int, ShownRelic[]> BuildAncientActRelicMap(
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs)
    {
        var map = new Dictionary<int, ShownRelic[]>(ancientActs.Count);
        foreach (var act in ancientActs)
        {
            var source = act.ActNumber == 2
                ? Sts2RelicVisibilitySource.AncientAct2
                : Sts2RelicVisibilitySource.AncientAct3;
            map[act.ActNumber] = act.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
                .Select(option => new ShownRelic(option.RelicId, source))
                .ToArray();
        }

        return map;
    }

    private static IReadOnlyDictionary<string, int> BuildRelicIndexMap(
        IEnumerable<string> relicIds,
        IReadOnlyDictionary<int, ShownRelic[]> ancientActs)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var relicId in relicIds)
        {
            AddRelicId(map, relicId);
        }

        foreach (var act in ancientActs.Values)
        {
            foreach (var shown in act)
            {
                AddRelicId(map, shown.RelicId);
            }
        }

        return map;

        static void AddRelicId(IDictionary<string, int> target, string? relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId) || target.ContainsKey(relicId))
            {
                return;
            }

            target[relicId] = target.Count;
        }
    }

    private sealed class SourcePresence
    {
        public bool NonShopSeen { get; set; }

        public bool ShopSeen { get; set; }
    }

    private enum OpportunityKind
    {
        Treasure,
        Elite,
        Shop,
        Ancient
    }

    private sealed class RewardSimulationModel
    {
        private RewardSimulationModel(
            IReadOnlyList<string> cardPool,
            IReadOnlyDictionary<CardRarity, string[]> cardPoolByRarity,
            IReadOnlyList<string> potionPool,
            IReadOnlyDictionary<PotionRarity, string[]> potionPoolByRarity)
        {
            CardPool = cardPool;
            CardPoolByRarity = cardPoolByRarity;
            PotionPool = potionPool;
            PotionPoolByRarity = potionPoolByRarity;
        }

        public IReadOnlyList<string> CardPool { get; }

        public IReadOnlyDictionary<CardRarity, string[]> CardPoolByRarity { get; }

        public IReadOnlyList<string> PotionPool { get; }

        public IReadOnlyDictionary<PotionRarity, string[]> PotionPoolByRarity { get; }

        public static RewardSimulationModel Create(NeowOptionDataset dataset, CharacterId character, int playerCount)
        {
            var cardPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
                ? characterCards
                    .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                     IsCardAllowed(metadata, playerCount))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            var cardPoolByRarity = cardPool
                .Where(cardId => dataset.CardMetadataMap.ContainsKey(cardId))
                .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var potionPool = new List<string>(dataset.SharedPotionPoolList);
            if (dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions))
            {
                potionPool.AddRange(characterPotions);
            }

            potionPool = potionPool
                .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var potionPoolByRarity = potionPool
                .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            return new RewardSimulationModel(
                cardPool,
                cardPoolByRarity,
                potionPool,
                potionPoolByRarity);
        }

        private static bool IsCardAllowed(NeowCardMetadata metadata, int playerCount)
        {
            if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
            {
                return false;
            }

            if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
            {
                return false;
            }

            return metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
        }
    }

    private sealed class RewardCardBuffer
    {
        private string? _first;
        private string? _second;
        private string? _third;

        public int Count { get; private set; }

        public bool Contains(string cardId)
        {
            return (!string.IsNullOrWhiteSpace(_first) && string.Equals(_first, cardId, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(_second) && string.Equals(_second, cardId, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(_third) && string.Equals(_third, cardId, StringComparison.OrdinalIgnoreCase));
        }

        public void Add(string cardId)
        {
            if (Contains(cardId))
            {
                return;
            }

            switch (Count)
            {
                case 0:
                    _first = cardId;
                    break;
                case 1:
                    _second = cardId;
                    break;
                case 2:
                    _third = cardId;
                    break;
                default:
                    return;
            }

            Count++;
        }

        public void Clear()
        {
            _first = null;
            _second = null;
            _third = null;
            Count = 0;
        }

        public RewardCardBuffer Clone()
        {
            return new RewardCardBuffer
            {
                _first = _first,
                _second = _second,
                _third = _third,
                Count = Count
            };
        }
    }

    private sealed class BaselineState
    {
        private BaselineState(
            string seedText,
            CharacterId character,
            int playerCount,
            int ascensionLevel,
            uint runSeed,
            uint playerSeed,
            RelicBag sharedBag,
            RelicBag playerBag,
            GameRng treasureRng,
            GameRng rewardsRng,
            float cardRareOffset,
            float potionChance,
            RewardCardBuffer currentRewardCards,
            bool act3RestrictionsApplied)
        {
            SeedText = seedText;
            Character = character;
            PlayerCount = playerCount;
            AscensionLevel = ascensionLevel;
            RunSeed = runSeed;
            PlayerSeed = playerSeed;
            SharedBag = sharedBag;
            PlayerBag = playerBag;
            TreasureRng = treasureRng;
            RewardsRng = rewardsRng;
            CardRareOffset = cardRareOffset;
            PotionChance = potionChance;
            CurrentRewardCards = currentRewardCards;
            Act3RestrictionsApplied = act3RestrictionsApplied;
        }

        public string SeedText { get; }

        public CharacterId Character { get; }

        public int PlayerCount { get; }

        public int AscensionLevel { get; }

        public uint RunSeed { get; }

        public uint PlayerSeed { get; }

        public RelicBag SharedBag { get; }

        public RelicBag PlayerBag { get; }

        public GameRng TreasureRng { get; }

        public GameRng RewardsRng { get; }

        public float CardRareOffset { get; set; }

        public float PotionChance { get; set; }

        public RewardCardBuffer CurrentRewardCards { get; }

        public bool Act3RestrictionsApplied { get; set; }

        public BaselineState Clone()
        {
            return new BaselineState(
                SeedText,
                Character,
                PlayerCount,
                AscensionLevel,
                RunSeed,
                PlayerSeed,
                SharedBag.Clone(),
                PlayerBag.Clone(),
                new GameRng(TreasureRng.Seed, TreasureRng.Counter),
                new GameRng(RewardsRng.Seed, RewardsRng.Counter),
                CardRareOffset,
                PotionChance,
                CurrentRewardCards.Clone(),
                Act3RestrictionsApplied);
        }

        public static BaselineState Create(
            Sts2WorldData.RelicPoolInfo pools,
            IReadOnlyDictionary<string, string> rarityMap,
            string seedText,
            uint runSeed,
            CharacterId character,
            int playerCount,
            ulong playerNetId,
            IReadOnlyList<CharacterId>? teamCharacters,
            int ascensionLevel,
            Sts2AncientAvailability availability)
        {
            var upFrontRng = new GameRng(runSeed, "up_front");
            var sharedSequence = pools.GetSharedSequence(availability);

            // Match the run creation order from the game: shuffle every unlocked
            // shared relic rarity first, then immediately shuffle only the
            // tracked gameplay rarities for each player's combined grab bag.
            var sharedBag = RelicBag.CreateFromSequence(sharedSequence, rarityMap, upFrontRng, trackedOnly: false);
            RelicBag? playerBag = null;
            foreach (var teamCharacter in ResolveTeamCharacters(character, playerCount, teamCharacters))
            {
                var playerSequence = pools.GetCombinedSequence(teamCharacter, availability);
                playerBag = RelicBag.CreateFromSequence(playerSequence, rarityMap, upFrontRng, trackedOnly: true);
            }

            playerBag ??= RelicBag.CreateFromSequence(
                pools.GetCombinedSequence(character, availability),
                rarityMap,
                upFrontRng,
                trackedOnly: true);
            ApplyPlayerCountRestrictions(sharedBag, playerBag, playerCount);

            var playerSeed = unchecked((uint)((ulong)runSeed +
                GameRng.PlayerStreamAddend(playerNetId)));
            return new BaselineState(
                seedText,
                character,
                playerCount,
                ascensionLevel,
                runSeed,
                playerSeed,
                sharedBag,
                playerBag,
                new GameRng(runSeed, "treasure_room_relics"),
                new GameRng(playerSeed, "rewards"),
                -0.05f,
                0.4f,
                new RewardCardBuffer(),
                act3RestrictionsApplied: false);
        }

        private static IReadOnlyList<CharacterId> ResolveTeamCharacters(
            CharacterId selectedCharacter,
            int playerCount,
            IReadOnlyList<CharacterId>? teamCharacters)
        {
            if (teamCharacters is { Count: > 0 })
            {
                return teamCharacters;
            }

            return Enumerable
                .Repeat(selectedCharacter, Math.Max(1, playerCount))
                .ToArray();
        }

        private static void ApplyPlayerCountRestrictions(RelicBag sharedBag, RelicBag playerBag, int playerCount)
        {
            if (playerCount <= 1)
            {
                sharedBag.RemoveAll(MultiplayerOnlyRelics);
                playerBag.RemoveAll(MultiplayerOnlyRelics);
            }
            else
            {
                sharedBag.RemoveAll(SinglePlayerOnlyRelics);
                playerBag.RemoveAll(SinglePlayerOnlyRelics);
            }
        }
    }

    private sealed class RelicBag
    {
        private readonly Dictionary<RelicRarity, List<string>> _deques;

        private RelicBag(Dictionary<RelicRarity, List<string>> deques)
        {
            _deques = deques;
        }

        public int TotalCount => _deques.Values.Sum(list => list.Count);

        public static RelicBag CreateFromSequence(
            IReadOnlyList<string> sequence,
            IReadOnlyDictionary<string, string> rarityMap,
            GameRng rng,
            bool trackedOnly)
        {
            var buckets = new Dictionary<RelicRarity, List<string>>();
            foreach (var relicId in sequence)
            {
                if (string.IsNullOrWhiteSpace(relicId) ||
                    !rarityMap.TryGetValue(relicId, out var rarityText) ||
                    !Enum.TryParse<RelicRarity>(rarityText, ignoreCase: true, out var rarity))
                {
                    continue;
                }

                if (trackedOnly &&
                    rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
                {
                    continue;
                }

                if (!buckets.TryGetValue(rarity, out var list))
                {
                    list = new List<string>();
                    buckets[rarity] = list;
                }

                list.Add(relicId);
            }

            foreach (var list in buckets.Values)
            {
                if (list.Count > 1)
                {
                    list.UnstableShuffle(rng);
                }
            }

            return new RelicBag(buckets);
        }

        public RelicBag Clone()
        {
            return new RelicBag(_deques.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()));
        }

        public string? PullFromFront(RelicRarity rarity, Func<string, bool>? isAllowed = null)
        {
            if (isAllowed != null)
            {
                RemoveDisallowed(isAllowed);
            }

            foreach (var list in EnumerateFallbackBuckets(rarity))
            {
                if (list is { Count: > 0 })
                {
                    var relic = list[0];
                    list.RemoveAt(0);
                    return relic;
                }
            }

            return null;
        }

        public string? PullFromBack(
            RelicRarity rarity,
            IReadOnlySet<string>? selected = null,
            IReadOnlySet<string>? extraBlacklist = null,
            Func<string, bool>? isAllowed = null)
        {
            if (isAllowed != null)
            {
                RemoveDisallowed(isAllowed);
            }

            foreach (var list in EnumerateFallbackBuckets(rarity))
            {
                if (list is not { Count: > 0 })
                {
                    continue;
                }

                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var relic = list[i];
                    if (selected != null && selected.Contains(relic))
                    {
                        continue;
                    }

                    if (extraBlacklist != null && extraBlacklist.Contains(relic))
                    {
                        continue;
                    }

                    list.RemoveAt(i);
                    return relic;
                }
            }

            return null;
        }

        public string? PullFromBack(
            RelicRarity rarity,
            string? selected1,
            string? selected2,
            IReadOnlySet<string>? extraBlacklist = null,
            Func<string, bool>? isAllowed = null)
        {
            if (isAllowed != null)
            {
                RemoveDisallowed(isAllowed);
            }

            foreach (var list in EnumerateFallbackBuckets(rarity))
            {
                if (list is not { Count: > 0 })
                {
                    continue;
                }

                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var relic = list[i];
                    if ((selected1 != null && string.Equals(selected1, relic, StringComparison.OrdinalIgnoreCase)) ||
                        (selected2 != null && string.Equals(selected2, relic, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (extraBlacklist != null && extraBlacklist.Contains(relic))
                    {
                        continue;
                    }

                    list.RemoveAt(i);
                    return relic;
                }
            }

            return null;
        }

        public void Remove(string relicId)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void RemoveAll(IReadOnlySet<string> relicIds)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(item => relicIds.Contains(item));
            }
        }

        private void RemoveDisallowed(Func<string, bool> isAllowed)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(item => !isAllowed(item));
            }
        }

        private IEnumerable<List<string>?> EnumerateFallbackBuckets(RelicRarity rarity)
        {
            RelicRarity? current = rarity;
            while (current is RelicRarity currentRarity)
            {
                yield return GetBucket(currentRarity);
                current = currentRarity switch
                {
                    RelicRarity.Shop => RelicRarity.Common,
                    RelicRarity.Common => RelicRarity.Uncommon,
                    RelicRarity.Uncommon => RelicRarity.Rare,
                    _ => null
                };
            }
        }

        private List<string>? GetBucket(RelicRarity rarity)
        {
            return _deques.TryGetValue(rarity, out var list) ? list : null;
        }
    }

    private sealed class RouteProfile
    {
        private RouteProfile(
            string id,
            string title,
            string description,
            IReadOnlyList<ActRouteProfile> acts,
            double regularPotionChance,
            double elitePotionChance)
        {
            Id = id;
            Title = title;
            Description = description;
            Acts = acts;
            RegularPotionChance = regularPotionChance;
            ElitePotionChance = elitePotionChance;
        }

        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<ActRouteProfile> Acts { get; }

        public double RegularPotionChance { get; }

        public double ElitePotionChance { get; }

        public static IReadOnlyList<RouteProfile> All { get; } =
        [
            new RouteProfile(
                "balanced",
                "Balanced",
                "Balanced route: a moderate number of elites, a few shops, and a medium chance to visit ancients.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.25), (1, 0.60), (2, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.25), (1, 0.55), (2, 0.20)),
                        slotVariants:
                        [
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.55), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                        ancientVisitChance: 0.68,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.50), (2, 0.35)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.35), (1, 0.50), (2, 0.15)),
                        ancientVisitChance: 0.62,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.38,
                elitePotionChance: 0.58),
            new RouteProfile(
                "aggressive",
                "Aggressive",
                "Aggressive route: earlier elites, fewer shops, and a slightly lower ancient chance.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.45), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.60,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.40), (2, 0.50)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.35), (1, 0.20)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.56,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.35), (2, 0.55)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.35,
                elitePotionChance: 0.60),
            new RouteProfile(
                "shopper",
                "Shopper",
                "Shop-heavy route: more shop visibility, fewer elites, and a slightly higher ancient chance.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((2, 0.40), (3, 0.45), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                        slotVariants:
                        [
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.74,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.70,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.40,
                elitePotionChance: 0.55)
        ];
    }

    private sealed class ActRouteProfile
    {
        public ActRouteProfile(
            WeightedIntDistribution treasureCounts,
            WeightedIntDistribution eliteCounts,
            WeightedIntDistribution shopCounts,
            double ancientVisitChance,
            WeightedIntDistribution betweenOpportunityCombats,
            IReadOnlyList<OpportunityKind[]> slotVariants)
        {
            TreasureCounts = treasureCounts;
            EliteCounts = eliteCounts;
            ShopCounts = shopCounts;
            AncientVisitChance = ancientVisitChance;
            BetweenOpportunityCombats = betweenOpportunityCombats;
            SlotVariants = slotVariants;
        }

        public WeightedIntDistribution TreasureCounts { get; }

        public WeightedIntDistribution EliteCounts { get; }

        public WeightedIntDistribution ShopCounts { get; }

        public double AncientVisitChance { get; }

        public WeightedIntDistribution BetweenOpportunityCombats { get; }

        public IReadOnlyList<OpportunityKind[]> SlotVariants { get; }

        public Sts2RelicVisibilityActSummary ToModel(int actNumber)
        {
            return new Sts2RelicVisibilityActSummary
            {
                ActNumber = actNumber,
                TreasureCounts = TreasureCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                EliteCounts = EliteCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                ShopCounts = ShopCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                AncientVisitChance = AncientVisitChance
            };
        }
    }

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
