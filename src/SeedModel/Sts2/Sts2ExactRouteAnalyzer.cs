using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Collections;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Sts2.Generation;

namespace SeedModel.Sts2;

internal sealed class Sts2ExactRouteAnalyzer
{
    private static readonly CardType[] MerchantColoredCardTypes =
    [
        CardType.Attack,
        CardType.Attack,
        CardType.Skill,
        CardType.Skill,
        CardType.Power
    ];

    private static readonly CardRarity[] MerchantColorlessCardRarities =
    [
        CardRarity.Uncommon,
        CardRarity.Rare
    ];

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
    private readonly string? _workspaceRoot;

    public Sts2ExactRouteAnalyzer(Sts2WorldData world, string? workspaceRoot)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _workspaceRoot = workspaceRoot;
    }

    public Sts2ExactRouteAnalysis Analyze(
        NeowOptionDataset dataset,
        Sts2ExactRouteAnalysisRequest request,
        IReadOnlyList<Sts2ActPoolPreview> actPools,
        Sts2RunPreview ancientPreview,
        IReadOnlyList<CharacterId> unlockedCharacters)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actPools);
        ArgumentNullException.ThrowIfNull(ancientPreview);
        ArgumentNullException.ThrowIfNull(unlockedCharacters);

        var actPoolMap = actPools.ToDictionary(act => act.ActNumber, act => act.FullEventPool, EqualityComparer<int>.Default);
        var ancientByAct = ancientPreview.Acts.ToDictionary(
            act => act.ActNumber,
            act => new AncientInfo(
                act.AncientId ?? string.Empty,
                act.AncientOptions
                    .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
                    .Select(option => option.RelicId!)
                    .ToList()),
            EqualityComparer<int>.Default);

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
        var mapActs = BuildMapActs(allRoutes);
        var simulator = new RouteSimulator(_world, _workspaceRoot, dataset, request, actPoolMap, ancientByAct, unlockedCharacters);

        var matches = new List<Sts2ExactRouteMatch>();
        var coverageMatches = new List<Sts2ExactRouteMatch>();
        var checkedRoutes = 0;
        long nextProgressReport = 1;
        var progressReportInterval = Math.Max(1, Math.Min(250, Math.Max(1, request.MaxRouteChecks) / 20));
        var stop = false;
        for (var i1 = 0; i1 < allRoutes[1].Count && !stop; i1++)
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                stop = true;
                break;
            }

            for (var i2 = 0; i2 < allRoutes[2].Count && !stop; i2++)
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    stop = true;
                    break;
                }

                for (var i3 = 0; i3 < allRoutes[3].Count && !stop; i3++)
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        stop = true;
                        break;
                    }

                    checkedRoutes++;
                    request.Progress?.Report(new Sts2ExactRouteProgress(checkedRoutes, Math.Max(0, checkedRoutes - 1), matches.Count));
                    var route = new[]
                    {
                        allRoutes[1][i1],
                        allRoutes[2][i2],
                        allRoutes[3][i3]
                    };

                    var match = simulator.TrySimulate(route);
                    if (match != null)
                    {
                        coverageMatches.Add(match);
                        matches.Add(match);
                    }

                    if (checkedRoutes >= nextProgressReport)
                    {
                        request.Progress?.Report(new Sts2ExactRouteProgress(checkedRoutes, checkedRoutes, matches.Count));
                        nextProgressReport = checkedRoutes + progressReportInterval;
                    }

                    if (checkedRoutes >= Math.Max(1, request.MaxRouteChecks))
                    {
                        stop = true;
                        break;
                    }
                }
            }
        }

        var limitedMatches = SelectDiverseMatches(matches, Math.Max(1, request.MaxResults), request.Preference);

        return new Sts2ExactRouteAnalysis
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            CheckedRoutes = checkedRoutes,
            FoundRouteCount = matches.Count,
            WasTruncated = checkedRoutes >= Math.Max(1, request.MaxRouteChecks),
            ShopOutputBranchesDropped = simulator.ShopOutputBranchesDropped,
            MapActs = mapActs,
            Matches = limitedMatches,
            Coverage = BuildCoverage(coverageMatches)
        };
    }

    private static Sts2ExactRouteCoverage BuildCoverage(IReadOnlyList<Sts2ExactRouteMatch> matches)
    {
        return new Sts2ExactRouteCoverage
        {
            TotalRoutes = matches.Count,
            EventCoverage = BuildEventCoverage(matches),
            RelicCoverage = BuildRelicCoverage(matches)
        };
    }

    private static IReadOnlyList<Sts2ExactRouteCoverageItem> BuildEventCoverage(IReadOnlyList<Sts2ExactRouteMatch> matches)
    {
        var totalRoutes = matches.Count;
        if (totalRoutes == 0)
        {
            return Array.Empty<Sts2ExactRouteCoverageItem>();
        }

        return matches
            .SelectMany((match, routeIndex) => match.Acts.SelectMany(act => act.Rooms
                .Where(room => !string.IsNullOrWhiteSpace(room.EventId))
                .Select(room => new CoverageOccurrence(
                    routeIndex,
                    act.ActNumber,
                    room.EventId!,
                    room.Row,
                    room.RoomType))))
            .GroupBy(item => (item.ActNumber, item.Id), StringComparerTuple.Instance)
            .Select(group => CreateCoverageItem(group.Key.ActNumber, group.Key.Id, totalRoutes, group))
            .OrderByDescending(item => item.SeenRouteCount)
            .ThenBy(item => item.ActNumber)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<Sts2ExactRouteCoverageItem> BuildRelicCoverage(IReadOnlyList<Sts2ExactRouteMatch> matches)
    {
        var totalRoutes = matches.Count;
        if (totalRoutes == 0)
        {
            return Array.Empty<Sts2ExactRouteCoverageItem>();
        }

        return matches
            .SelectMany((match, routeIndex) => match.Acts.SelectMany(act => act.Rooms
                .SelectMany(room => BuildRelicCoverageOccurrences(routeIndex, act.ActNumber, room))))
            .GroupBy(item => (item.ActNumber, item.Id), StringComparerTuple.Instance)
            .Select(group => CreateCoverageItem(group.Key.ActNumber, group.Key.Id, totalRoutes, group))
            .OrderByDescending(item => item.SeenRouteCount)
            .ThenBy(item => item.ActNumber)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<CoverageOccurrence> BuildRelicCoverageOccurrences(
        int routeIndex,
        int actNumber,
        Sts2ExactRouteRoom room)
    {
        foreach (var relicId in room.RelicIds)
        {
            yield return new CoverageOccurrence(routeIndex, actNumber, relicId, room.Row, room.RoomType);
        }

        foreach (var relicId in room.DisplayRelicIds)
        {
            yield return new CoverageOccurrence(routeIndex, actNumber, relicId, room.Row, "ShopDisplay");
        }
    }

    private static Sts2ExactRouteCoverageItem CreateCoverageItem(
        int actNumber,
        string id,
        int totalRoutes,
        IEnumerable<CoverageOccurrence> occurrences)
    {
        var occurrenceList = occurrences.ToList();
        var firstRows = occurrenceList
            .GroupBy(item => item.RouteIndex)
            .Select(group => group.Min(item => item.Row))
            .ToList();

        return new Sts2ExactRouteCoverageItem
        {
            Id = id,
            ActNumber = actNumber,
            SeenRouteCount = occurrenceList.Select(item => item.RouteIndex).Distinct().Count(),
            TotalRouteCount = totalRoutes,
            FirstRowMin = firstRows.Count > 0 ? firstRows.Min() : null,
            FirstRowMax = firstRows.Count > 0 ? firstRows.Max() : null,
            Sources = occurrenceList
                .Select(item => item.Source)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private sealed record CoverageOccurrence(
        int RouteIndex,
        int ActNumber,
        string Id,
        int Row,
        string Source);

    private sealed class StringComparerTuple : IEqualityComparer<(int ActNumber, string Id)>
    {
        public static StringComparerTuple Instance { get; } = new();

        public bool Equals((int ActNumber, string Id) x, (int ActNumber, string Id) y) =>
            x.ActNumber == y.ActNumber && string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int ActNumber, string Id) obj) =>
            HashCode.Combine(obj.ActNumber, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id));
    }

    public Sts2SeaGlassPreview AnalyzeSeaGlassPreview(
        NeowOptionDataset dataset,
        Sts2RunRequest request,
        IReadOnlyList<Generation.Sts2RunSimulator.ActPoolResult> actPools,
        CharacterId targetCharacter,
        IReadOnlyList<CharacterId> unlockedCharacters)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actPools);
        ArgumentNullException.ThrowIfNull(unlockedCharacters);

        var samples = Math.Max(0, request.SeaGlassPreviewSamples ?? 0);
        if (samples <= 0)
        {
            return new Sts2SeaGlassPreview
            {
                TargetCharacter = targetCharacter,
                Samples = 0,
                RankedCards = Array.Empty<Sts2SeaGlassPreviewCard>()
            };
        }

        var exactRequest = new Sts2ExactRouteAnalysisRequest
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            Character = request.Character,
            UnlockedCharacters = request.UnlockedCharacters,
            TeamCharacters = request.TeamCharacters,
            AscensionLevel = request.AscensionLevel,
            PlayerCount = request.PlayerCount,
            PlayerNetId = request.PlayerNetId,
            AncientAvailability = request.AncientAvailability,
            IncludeDarvSharedAncient = request.IncludeDarvSharedAncient,
            ShopStrategy = Sts2ExactRouteShopStrategy.NoPurchase,
            Act1OpeningOption = null
        };

        var actPoolMap = actPools.ToDictionary(
            act => act.ActNumber,
            act => (IReadOnlyList<string>)act.Events.ToList(),
            EqualityComparer<int>.Default);
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
        if (!allRoutes.TryGetValue(1, out var actOneRoutes) || actOneRoutes.Count == 0)
        {
            return new Sts2SeaGlassPreview
            {
                TargetCharacter = targetCharacter,
                Samples = samples,
                RankedCards = Array.Empty<Sts2SeaGlassPreviewCard>()
            };
        }

        var simulator = new RouteSimulator(
            _world,
            _workspaceRoot,
            dataset,
            exactRequest,
            actPoolMap,
            new Dictionary<int, AncientInfo>(),
            unlockedCharacters);

        return simulator.AnalyzeSeaGlass(actOneRoutes, targetCharacter, samples);
    }

    private static IReadOnlyList<Sts2ExactRouteMatch> SelectDiverseMatches(
        IReadOnlyList<Sts2ExactRouteMatch> matches,
        int maxResults,
        Sts2ExactRoutePreference preference)
    {
        if (matches.Count <= maxResults)
        {
            return OrderMatchesByPreference(matches, preference).ToList();
        }

        var selected = new List<Sts2ExactRouteMatch>(maxResults);
        var usedActSignatures = new Dictionary<int, HashSet<string>>();
        var remaining = OrderMatchesByPreference(matches, preference).ToList();

        while (selected.Count < maxResults && remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestScore = int.MinValue;

            for (var i = 0; i < remaining.Count; i++)
            {
                var score = ScoreMatchDiversity(remaining[i], usedActSignatures) +
                            GetPreferenceScore(remaining[i], preference);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            var chosen = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            selected.Add(chosen);
            RegisterMatchSignatures(chosen, usedActSignatures);
        }

        return selected;
    }

    private static IEnumerable<Sts2ExactRouteMatch> OrderMatchesByPreference(
        IReadOnlyList<Sts2ExactRouteMatch> matches,
        Sts2ExactRoutePreference preference)
    {
        return matches
            .Select((match, index) => new { match, index, score = GetPreferenceScore(match, preference) })
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.index)
            .Select(item => item.match);
    }

    private static int GetPreferenceScore(Sts2ExactRouteMatch match, Sts2ExactRoutePreference preference)
    {
        var (eliteCount, restSiteCount, questionMarkCount) = CountRoutePreferences(match);
        return preference switch
        {
            Sts2ExactRoutePreference.MostElites => eliteCount * 10_000,
            Sts2ExactRoutePreference.FewestElites => -eliteCount * 10_000,
            Sts2ExactRoutePreference.MostRestSites => restSiteCount * 10_000,
            Sts2ExactRoutePreference.MostQuestionMarks => questionMarkCount * 10_000,
            _ => 0
        };
    }

    private static (int EliteCount, int RestSiteCount, int QuestionMarkCount) CountRoutePreferences(Sts2ExactRouteMatch match)
    {
        var eliteCount = 0;
        var restSiteCount = 0;
        var questionMarkCount = 0;

        foreach (var room in match.Acts.SelectMany(act => act.Rooms))
        {
            if (string.Equals(room.RoomType, "Elite", StringComparison.OrdinalIgnoreCase))
            {
                eliteCount++;
            }

            if (string.Equals(room.RoomType, "RestSite", StringComparison.OrdinalIgnoreCase))
            {
                restSiteCount++;
            }

            if (string.Equals(room.PointType, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                questionMarkCount++;
            }
        }

        return (eliteCount, restSiteCount, questionMarkCount);
    }

    private static int ScoreMatchDiversity(
        Sts2ExactRouteMatch match,
        IReadOnlyDictionary<int, HashSet<string>> usedActSignatures)
    {
        var score = 0;
        foreach (var act in match.Acts)
        {
            var signature = BuildActSignature(act);
            if (!usedActSignatures.TryGetValue(act.ActNumber, out var seen) || !seen.Contains(signature))
            {
                score += 100;
            }

            score += act.Rooms
                .Count(room => !string.IsNullOrWhiteSpace(room.EventId) || room.RelicIds.Count > 0);
        }

        return score;
    }

    private static void RegisterMatchSignatures(
        Sts2ExactRouteMatch match,
        IDictionary<int, HashSet<string>> usedActSignatures)
    {
        foreach (var act in match.Acts)
        {
            if (!usedActSignatures.TryGetValue(act.ActNumber, out var signatures))
            {
                signatures = new HashSet<string>(StringComparer.Ordinal);
                usedActSignatures[act.ActNumber] = signatures;
            }

            signatures.Add(BuildActSignature(act));
        }
    }

    private static string BuildActSignature(Sts2ExactRouteAct act)
    {
        return string.Join(
            "->",
            act.Rooms.Select(room => $"{room.Row}:{room.Col}:{room.RoomType}"));
    }

    private static IReadOnlyList<Sts2ExactMapAct> BuildMapActs(IReadOnlyDictionary<int, IReadOnlyList<Sts2GeneratedActRoute>> allRoutes)
    {
        return allRoutes
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var nodes = pair.Value
                    .SelectMany(route => route.Nodes)
                    .GroupBy(node => (node.Row, node.Col))
                    .Select(group =>
                    {
                        var first = group.First();
                        return new Sts2ExactMapNode
                        {
                            Row = first.Row,
                            Col = first.Col,
                            PointType = first.PointType
                        };
                    })
                    .OrderBy(node => node.Row)
                    .ThenBy(node => node.Col)
                    .ToList();

                var links = pair.Value
                    .SelectMany(route => route.Nodes.Zip(route.Nodes.Skip(1), (from, to) => new Sts2ExactMapLink
                    {
                        FromRow = from.Row,
                        FromCol = from.Col,
                        ToRow = to.Row,
                        ToCol = to.Col
                    }))
                    .GroupBy(link => (link.FromRow, link.FromCol, link.ToRow, link.ToCol))
                    .Select(group => group.First())
                    .OrderBy(link => link.FromRow)
                    .ThenBy(link => link.FromCol)
                    .ThenBy(link => link.ToRow)
                    .ThenBy(link => link.ToCol)
                    .ToList();

                return new Sts2ExactMapAct
                {
                    ActNumber = pair.Key,
                    Nodes = nodes,
                    Links = links
                };
            })
            .ToList();
    }

    private sealed record AncientInfo(string AncientId, IReadOnlyList<string> RelicIds);

    private sealed class RouteSimulator
    {
        private readonly NeowOptionDataset _dataset;
        private readonly Sts2EventVisibilitySimulationModel _simulationModel;
        private readonly RewardRelicStateFactory _relicFactory;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _actPoolMap;
        private readonly IReadOnlyDictionary<int, AncientInfo> _ancientByAct;
        private readonly IReadOnlyList<Sts2ExactRouteEventTargetRequest> _targetEvents;
        private readonly IReadOnlyList<Sts2ExactRouteRelicTargetRequest> _targetRelics;
        private readonly Sts2ExactRouteAnalysisRequest _request;
        private const int DefaultShopRemovalCost = 100;
        private const int MaxShopCardCandidates = 7;
        private const int MaxShopPotionCandidates = 2;

        public RouteSimulator(
            Sts2WorldData world,
            string? workspaceRoot,
            NeowOptionDataset dataset,
            Sts2ExactRouteAnalysisRequest request,
            IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap,
            IReadOnlyDictionary<int, AncientInfo> ancientByAct,
            IReadOnlyList<CharacterId> unlockedCharacters)
        {
            _dataset = dataset;
            _request = request;
            _actPoolMap = actPoolMap;
            _ancientByAct = ancientByAct;
            _targetEvents = request.GetResolvedEventTargets();
            _targetRelics = request.GetResolvedRelicTargets();
            _simulationModel = Sts2EventVisibilitySimulationModel.Create(
                dataset,
                request.Character,
                unlockedCharacters,
                request.PlayerCount,
                request.AscensionLevel,
                workspaceRoot);
            _relicFactory = new RewardRelicStateFactory(world, dataset, request);
        }

        public long ShopOutputBranchesDropped { get; private set; }

        public Sts2ExactRouteMatch? TrySimulate(IReadOnlyList<Sts2GeneratedActRoute> routes)
        {
            if (_request.CancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var orderedActs = routes
                .OrderBy(route => route.ActNumber)
                .ToList();

            var branches = new List<SimulationBranch>
            {
                SimulationBranch.Create(
                    Sts2EventProgressState.Create(_simulationModel, _request.Character, _request.PlayerCount),
                    _request.Act1OpeningOption,
                    new GameRng(_request.SeedValue, "explicit_route_probe"),
                    new UnknownOdds(new GameRng(_request.SeedValue, "unknown_map_point")),
                    _relicFactory.Create(),
                    _targetEvents.Count,
                    _targetRelics.Count)
            };

            for (var actPlanIndex = 0; actPlanIndex < orderedActs.Count; actPlanIndex++)
            {
                if (_request.CancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var act = orderedActs[actPlanIndex];
                var steps = act.Nodes
                    .Zip(act.Nodes.Skip(1), (from, to) => new Sts2ExactRouteStep
                    {
                        FromRow = from.Row,
                        FromCol = from.Col,
                        ToRow = to.Row,
                        ToCol = to.Col
                    })
                    .ToList();
                _ = _ancientByAct.TryGetValue(act.ActNumber, out var ancient);
                var ancientId = act.ActNumber > 1 ? ancient?.AncientId : null;
                var ancientRelicOptions = act.ActNumber > 1 ? ancient?.RelicIds ?? Array.Empty<string>() : Array.Empty<string>();

                var nextActBranches = new List<SimulationBranch>();
                foreach (var seedBranch in branches)
                {
                    if (_request.CancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    var activeBranches = BeginActBranches(seedBranch, act.ActNumber, ancientId, ancientRelicOptions);

                    for (var nodeIndex = 0; nodeIndex < act.Nodes.Count; nodeIndex++)
                    {
                        if (_request.CancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }

                        var node = act.Nodes[nodeIndex];
                        var roomBranches = new List<SimulationBranch>();
                        foreach (var branch in activeBranches)
                        {
                            if (_request.CancellationToken.IsCancellationRequested)
                            {
                                return null;
                            }

                            roomBranches.AddRange(AdvanceBranchForRoom(branch, act.ActNumber, node));
                        }

                        if (roomBranches.Count == 0)
                        {
                            activeBranches.Clear();
                            break;
                        }

                        activeBranches = roomBranches;
                    }

                    foreach (var branch in activeBranches)
                    {
                        if (actPlanIndex < orderedActs.Count - 1)
                        {
                            var bossRewardsRng = new GameRng(
                                branch.RelicState.RewardsRng.Seed,
                                branch.RelicState.RewardsRng.Counter);
                            branch.EventState.ConsumeBossStop(branch.EventRng, bossRewardsRng);
                            branch.RelicState.ConsumeBossRewards();
                        }

                        var chosenAncientRelic = branch.SelectedAncientRelicByAct.GetValueOrDefault(act.ActNumber);
                        var chosenAncientRelics = string.IsNullOrWhiteSpace(chosenAncientRelic)
                            ? Array.Empty<string>()
                            : [chosenAncientRelic];
                        branch.ActResults.Add(new Sts2ExactRouteAct
                        {
                            ActNumber = act.ActNumber,
                            AncientId = ancientId,
                            AncientRelics = chosenAncientRelics,
                            Steps = steps,
                            Rooms = branch.CurrentRooms.ToList()
                        });
                        branch.CurrentRooms.Clear();
                        nextActBranches.Add(branch);
                    }
                }

                branches = nextActBranches;
                if (branches.Count == 0)
                {
                    return null;
                }
            }

            var match = branches.FirstOrDefault(branch =>
                branch.MatchedEventTargets.All(static matched => matched) &&
                branch.MatchedRelicTargets.All(static matched => matched));
            return match == null
                ? null
                : new Sts2ExactRouteMatch { Acts = match.ActResults };
        }

        public Sts2SeaGlassPreview AnalyzeSeaGlass(
            IReadOnlyList<Sts2GeneratedActRoute> actOneRoutes,
            CharacterId targetCharacter,
            int samples)
        {
            var sampleRng = new GameRng(_request.SeedValue, "sea_glass_preview");
            var targetRewardModel = RewardSimulationModel.Create(_dataset, targetCharacter, _request.PlayerCount);
            var seenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var sampleIndex = 0; sampleIndex < samples; sampleIndex++)
            {
                var route = sampleRng.NextItem(actOneRoutes);
                if (route == null)
                {
                    continue;
                }

                var branches = new List<SimulationBranch>
                {
                    SimulationBranch.Create(
                        Sts2EventProgressState.Create(_simulationModel, _request.Character, _request.PlayerCount),
                        _request.Act1OpeningOption,
                        new GameRng(_request.SeedValue, "explicit_route_probe"),
                        new UnknownOdds(new GameRng(_request.SeedValue, "unknown_map_point")),
                        _relicFactory.Create(),
                        eventTargetCount: 0,
                        relicTargetCount: 0)
                };

                foreach (var node in route.Nodes)
                {
                    var roomBranches = new List<SimulationBranch>();
                    foreach (var branch in branches)
                    {
                        roomBranches.AddRange(AdvanceBranchForRoom(branch, route.ActNumber, node));
                    }

                    if (roomBranches.Count == 0)
                    {
                        branches.Clear();
                        break;
                    }

                    branches = roomBranches;
                }

                if (branches.Count == 0)
                {
                    continue;
                }

                foreach (var branch in branches)
                {
                    var bossRewardsRng = new GameRng(
                        branch.RelicState.RewardsRng.Seed,
                        branch.RelicState.RewardsRng.Counter);
                    branch.EventState.ConsumeBossStop(branch.EventRng, bossRewardsRng);
                    branch.RelicState.ConsumeBossRewards();
                }

                var chosenBranch = branches[sampleRng.NextInt(branches.Count)];
                foreach (var cardId in chosenBranch.RelicState.PreviewSeaGlass(targetRewardModel))
                {
                    if (string.IsNullOrWhiteSpace(cardId))
                    {
                        continue;
                    }

                    seenCounts[cardId] = seenCounts.TryGetValue(cardId, out var currentCount)
                        ? currentCount + 1
                        : 1;
                }
            }

            var rankedCards = seenCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new Sts2SeaGlassPreviewCard
                {
                    CardId = pair.Key,
                    SeenCount = pair.Value,
                    SeenProbability = samples <= 0 ? 0d : (double)pair.Value / samples
                })
                .ToList();

            return new Sts2SeaGlassPreview
            {
                TargetCharacter = targetCharacter,
                Samples = samples,
                RankedCards = rankedCards
            };
        }

        private List<SimulationBranch> BeginActBranches(
            SimulationBranch seedBranch,
            int actNumber,
            string? ancientId,
            IReadOnlyList<string> ancientRelicOptions)
        {
            var options = actNumber > 1 && ancientRelicOptions.Count > 0
                ? ancientRelicOptions
                    .Where(static relicId => !string.IsNullOrWhiteSpace(relicId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
            if (options.Count == 0)
            {
                options.Add(string.Empty);
            }

            var branches = new List<SimulationBranch>(options.Count);
            foreach (var option in options)
            {
                var branch = seedBranch.Clone();
                branch.EventState.StartAct(actNumber, initialEventsVisited: 1);
                branch.EventState.TotalFloor++;
                branch.UnknownOdds.ResetToBase();
                branch.PreviousRoomTypeInAct = null;
                if (actNumber > 1)
                {
                    branch.EventState.ApplyAncientActStartHeal();
                }
                branch.SelectedAncientRelicByAct[actNumber] = option;

                if (!string.IsNullOrWhiteSpace(option))
                {
                    branch.RelicState.ObtainWithImmediateEffects(option);
                    ApplyAncientImmediateEffects(actNumber, option, branch.RelicState, branch.EventState);
                    MarkMatchedRelics(branch.MatchedRelicTargets, actNumber, [option]);
                }

                MarkMatchedEvents(branch.MatchedEventTargets, actNumber, ancientId);
                branches.Add(branch);
            }

            return branches;
        }

        private IEnumerable<SimulationBranch> AdvanceBranchForRoom(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node)
        {
            branch.EventState.TotalFloor++;
            var roomType = ResolveRoomType(branch, actNumber, node);

            string? eventId = null;
            var relicIds = new List<string>();
            var displayRelicIds = new List<string>();
            switch (roomType)
            {
                case RouteRoomType.Event:
                    return ExpandEventBranches(branch, actNumber, node, roomType);
                case RouteRoomType.Monster:
                    var regularRewardsRng = new GameRng(
                        branch.RelicState.RewardsRng.Seed,
                        branch.RelicState.RewardsRng.Counter);
                    branch.EventState.ConsumeRegularCombat(branch.EventRng, regularRewardsRng);
                    branch.RelicState.ConsumeRegularCombat();
                    break;
                case RouteRoomType.Elite:
                    var eliteRewardsRng = new GameRng(
                        branch.RelicState.RewardsRng.Seed,
                        branch.RelicState.RewardsRng.Counter);
                    relicIds.AddRange(branch.RelicState.ShowElite(actNumber));
                    branch.EventState.ConsumeEliteStop(branch.EventRng, eliteRewardsRng);
                    branch.EventState.ApplyObtainedRelics(relicIds);
                    break;
                case RouteRoomType.Treasure:
                    var treasureRewardsRng = new GameRng(
                        branch.RelicState.RewardsRng.Seed,
                        branch.RelicState.RewardsRng.Counter);
                    branch.EventState.ConsumeTreasureStop(branch.EventRng, treasureRewardsRng);
                    relicIds.AddRange(branch.RelicState.ShowTreasure(actNumber));
                    branch.EventState.ApplyObtainedRelics(relicIds);
                    displayRelicIds.AddRange(relicIds);
                    break;
                case RouteRoomType.Shop:
                    return ExpandShopBranches(branch, actNumber, node, roomType);
                case RouteRoomType.RestSite:
                    if (branch.EventState.HasByrdonisEgg && !branch.RelicState.OwnedRelics.Contains("BYRDPIP"))
                    {
                        branch.EventState.ApplyHatchRestSite();
                        branch.RelicState.ApplyHatchRestSite();
                        relicIds.Add("BYRDPIP");
                    }

                    if (HasUnmatchedRelicTargets(branch.MatchedRelicTargets))
                    {
                        relicIds.AddRange(branch.RelicState.TryDig(actNumber));
                    }

                    displayRelicIds.AddRange(relicIds);
                    break;
                default:
                    displayRelicIds.AddRange(relicIds);
                    break;
            }

            AppendRoom(branch, actNumber, node, roomType, eventId, relicIds, displayRelicIds);
            return [branch];
        }

        private IEnumerable<SimulationBranch> ExpandEventBranches(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node,
            RouteRoomType roomType)
        {
            var expanded = new List<SimulationBranch>();
            foreach (var candidate in CreatePotionUsageBranches(branch))
            {
                string? eventId = null;
                var relicIds = new List<string>();
                if (_actPoolMap.TryGetValue(actNumber, out var pool) && pool.Count > 0)
                {
                    Sts2EventPullEngine.EnsureNextEventIsValid(pool, candidate.EventState);
                    eventId = Sts2EventPullEngine.PeekNextAllowedEvent(pool, candidate.EventState);
                    if (!string.IsNullOrWhiteSpace(eventId))
                    {
                        Sts2EventPullEngine.ConsumeShownEvent(pool, eventId, candidate.EventState);
                        if (string.Equals(eventId, "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase))
                        {
                            expanded.AddRange(ExpandSelfHelpBranches(candidate, actNumber, node, roomType, eventId));
                            continue;
                        }

                        if (string.Equals(eventId, "RANWID_THE_ELDER", StringComparison.OrdinalIgnoreCase))
                        {
                            expanded.AddRange(ExpandRanwidBranches(candidate, actNumber, node, roomType, eventId));
                            continue;
                        }

                        var eventOutcome = ResolveEventRelicEffects(actNumber, eventId, candidate.MatchedRelicTargets, candidate.RelicState, candidate.EventState);
                        candidate.RelicState = eventOutcome.State;
                        relicIds.AddRange(eventOutcome.Relics);
                        candidate.EventState.ApplyShownEvent(eventId, candidate.EventRng);
                        candidate.EventState.ApplyObtainedRelics(relicIds);
                        MarkMatchedEvents(candidate.MatchedEventTargets, actNumber, eventId);
                    }
                }

                AppendRoom(candidate, actNumber, node, roomType, eventId, relicIds, Array.Empty<string>());
                expanded.Add(candidate);
            }

            return expanded.Count > 0 ? expanded : [branch];
        }

        private static IEnumerable<SimulationBranch> CreatePotionUsageBranches(SimulationBranch branch)
        {
            yield return branch;

            if (branch.EventState.PotionCount <= 1)
            {
                yield break;
            }

            var reducedPotionBranch = branch.Clone();
            SpendPotionsForEventPreview(reducedPotionBranch.EventState, targetPotionCount: 1);
            if (reducedPotionBranch.EventState.PotionCount != branch.EventState.PotionCount)
            {
                yield return reducedPotionBranch;
            }
        }

        private static void SpendPotionsForEventPreview(Sts2EventProgressState eventState, int targetPotionCount)
        {
            while (eventState.PotionCount > targetPotionCount)
            {
                var potionToSpend = eventState.GetPotionIds()
                    .FirstOrDefault(static potionId => !string.Equals(potionId, "FOUL_POTION", StringComparison.OrdinalIgnoreCase))
                    ?? eventState.GetPotionIds().FirstOrDefault();
                if (string.IsNullOrWhiteSpace(potionToSpend))
                {
                    break;
                }

                eventState.ApplyLosePotion(potionToSpend);
            }
        }

        private IEnumerable<SimulationBranch> ExpandShopBranches(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node,
            RouteRoomType roomType)
        {
            var shopPreview = branch.RelicState.ShowShopPreview(actNumber);
            var displayRelicIds = shopPreview.Relics.Select(offer => offer.Id).ToList();
            var branches = new List<SimulationBranch>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddCandidate(
                SimulationBranch candidate,
                IReadOnlyList<string> purchasedRelics,
                string? extraSignatureKey = null)
            {
                var signature = BuildShopBranchSignature(candidate, purchasedRelics, extraSignatureKey);
                if (!seen.Add(signature))
                {
                    return;
                }

                AppendRoom(candidate, actNumber, node, roomType, eventId: null, purchasedRelics, displayRelicIds);
                branches.Add(candidate);
            }

            if (_request.ShopStrategy == Sts2ExactRouteShopStrategy.TargetRelicsOnly)
            {
                var targetRelicBranch = branch.Clone();
                var purchasedRelics = ResolveShopPurchases(
                    actNumber,
                    shopPreview.Relics,
                    targetRelicBranch.MatchedRelicTargets,
                    targetRelicBranch.RelicState,
                    targetRelicBranch.EventState);
                if (purchasedRelics.Count > 0)
                {
                    AddCandidate(targetRelicBranch, purchasedRelics, "target-relics");
                }
            }

            foreach (var candidate in CreatePragmaticShopBranches(branch, actNumber, shopPreview))
            {
                AddCandidate(candidate.Branch, Array.Empty<string>(), BuildShopPlanSignature(candidate.Plan));
            }

            var defaultBranch = branch;
            if (_request.ShopStrategy == Sts2ExactRouteShopStrategy.CardRemovalOnly &&
                shopPreview.CardRemovalPrice > 0 &&
                defaultBranch.EventState.HasRemovableBasicCard &&
                defaultBranch.EventState.CurrentGold >= shopPreview.CardRemovalPrice)
            {
                defaultBranch.EventState.ApplyShopCardRemoval(shopPreview.CardRemovalPrice);
            }

            AddCandidate(defaultBranch, Array.Empty<string>(), "default");
            ApplyFastShopOutputLimit(branches);
            return branches;
        }

        private void ApplyFastShopOutputLimit(List<SimulationBranch> branches)
        {
            if (_request.SimulationMode != Sts2ExactRouteSimulationMode.FastShopLimited ||
                _request.ShopOutputLimit <= 0 ||
                branches.Count <= _request.ShopOutputLimit)
            {
                return;
            }

            ShopOutputBranchesDropped += branches.Count - _request.ShopOutputLimit;
            branches.RemoveRange(_request.ShopOutputLimit, branches.Count - _request.ShopOutputLimit);
        }

        private IEnumerable<SimulationBranch> ExpandRanwidBranches(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node,
            RouteRoomType roomType,
            string eventId)
        {
            var unmatchedTargets = GetUnmatchedRelicTargets(branch.MatchedRelicTargets, actNumber);
            string? previewPotionId = null;
            if (branch.EventState.PotionCount > 0)
            {
                previewPotionId = branch.EventRng.NextItem(branch.EventState.GetPotionIds());
            }

            var tradableOwnedRelics = branch.RelicState.OwnedRelics
                .Where(branch.EventState.IsTradableRelic)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string? previewTradeRelicId = null;
            if (tradableOwnedRelics.Length > 0)
            {
                previewTradeRelicId = branch.EventRng.NextItem(tradableOwnedRelics);
            }

            var expanded = new List<SimulationBranch>();

            void FinalizeBranch(SimulationBranch optionBranch, IReadOnlyList<string> relicIds)
            {
                MarkMatchedEvents(optionBranch.MatchedEventTargets, actNumber, eventId);
                MarkMatchedRelics(optionBranch.MatchedRelicTargets, actNumber, relicIds);
                AppendRoom(optionBranch, actNumber, node, roomType, eventId, relicIds, Array.Empty<string>());
                expanded.Add(optionBranch);
            }

            if (!string.IsNullOrWhiteSpace(previewPotionId))
            {
                var potionBranch = branch.Clone();
                potionBranch.EventState.ApplyLosePotion(previewPotionId);
                potionBranch.RelicState.PullEventFrontAndObtain(actNumber);
                FinalizeBranch(potionBranch, potionBranch.RelicState.LastEventRelics.ToArray());
            }

            if (branch.EventState.CurrentGold >= 100)
            {
                var goldBranch = branch.Clone();
                goldBranch.EventState.ApplyGoldSpend(100);
                goldBranch.RelicState.PullEventFrontAndObtain(actNumber);
                FinalizeBranch(goldBranch, goldBranch.RelicState.LastEventRelics.ToArray());
            }

            if (!string.IsNullOrWhiteSpace(previewTradeRelicId))
            {
                var relicBranch = branch.Clone();
                relicBranch.EventState.ApplyLoseTradableRelic();
                relicBranch.RelicState.RemoveOwnedRelic(previewTradeRelicId);
                relicBranch.RelicState.PullEventFrontAndObtain(actNumber);
                relicBranch.RelicState.PullEventFrontAndObtain(actNumber);
                FinalizeBranch(relicBranch, relicBranch.RelicState.LastEventRelics.ToArray());
            }

            if (expanded.Count == 0)
            {
                MarkMatchedEvents(branch.MatchedEventTargets, actNumber, eventId);
                AppendRoom(branch, actNumber, node, roomType, eventId, Array.Empty<string>(), Array.Empty<string>());
                return [branch];
            }

            return expanded
                .OrderByDescending(candidate => candidate.RelicState.LastEventRelics.Count(relicId => unmatchedTargets.Contains(relicId)))
                .ThenByDescending(candidate => candidate.RelicState.LastEventRelics.Count)
                .ThenBy(candidate => candidate.EventState.CurrentGold)
                .ToList();
        }

        private IEnumerable<SimulationBranch> ExpandSelfHelpBranches(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node,
            RouteRoomType roomType,
            string eventId)
        {
            var branches = branch.EventState.GetAvailableSelfHelpBookBranches();
            if (branches.Count == 0)
            {
                branch.EventState.ApplyShownEvent(eventId, branch.EventRng);
                MarkMatchedEvents(branch.MatchedEventTargets, actNumber, eventId);
                AppendRoom(branch, actNumber, node, roomType, eventId, Array.Empty<string>(), Array.Empty<string>());
                return [branch];
            }

            var expanded = new List<SimulationBranch>(branches.Count);
            foreach (var selfHelpBranch in branches)
            {
                var optionBranch = branch.Clone();
                var eventOutcome = ResolveSelfHelpBook(optionBranch.RelicState, selfHelpBranch);
                optionBranch.RelicState = eventOutcome.State;
                optionBranch.EventState.ApplySelfHelpBook(selfHelpBranch);
                MarkMatchedEvents(optionBranch.MatchedEventTargets, actNumber, eventId);
                AppendRoom(optionBranch, actNumber, node, roomType, eventId, eventOutcome.Relics, Array.Empty<string>());
                expanded.Add(optionBranch);
            }

            return expanded;
        }

        private void AppendRoom(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node,
            RouteRoomType roomType,
            string? eventId,
            IReadOnlyList<string> relicIds,
            IReadOnlyList<string> displayRelicIds)
        {
            MarkMatchedRelics(branch.MatchedRelicTargets, actNumber, relicIds);
            branch.CurrentRooms.Add(new Sts2ExactRouteRoom
            {
                Row = node.Row,
                Col = node.Col,
                PointType = node.PointType,
                RoomType = roomType.ToString(),
                EventId = eventId,
                RelicIds = relicIds.ToList(),
                DisplayRelicIds = displayRelicIds.Count > 0 ? displayRelicIds.ToList() : relicIds.ToList()
            });
            branch.PreviousRoomTypeInAct = roomType;
        }

        private void MarkMatchedEvents(bool[] matchedTargets, int actNumber, string? eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            for (var i = 0; i < _targetEvents.Count; i++)
            {
                if (matchedTargets[i])
                {
                    continue;
                }

                var target = _targetEvents[i];
                if ((!target.ActNumber.HasValue || target.ActNumber.Value == actNumber) &&
                    RouteIdComparer.Equals(target.EventId, eventId))
                {
                    matchedTargets[i] = true;
                }
            }
        }

        private void MarkMatchedRelics(bool[] matchedTargets, int actNumber, IReadOnlyList<string> relicIds)
        {
            if (relicIds.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _targetRelics.Count; i++)
            {
                if (matchedTargets[i])
                {
                    continue;
                }

                var target = _targetRelics[i];
                if ((!target.ActNumber.HasValue || target.ActNumber.Value == actNumber) &&
                    relicIds.Any(relicId => RouteIdComparer.Equals(relicId, target.RelicId)))
                {
                    matchedTargets[i] = true;
                }
            }
        }

        private static bool HasUnmatchedRelicTargets(bool[] matchedTargets)
        {
            for (var i = 0; i < matchedTargets.Length; i++)
            {
                if (!matchedTargets[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyAncientImmediateEffects(
            int actNumber,
            string ancientRelicId,
            RewardRelicState relicState,
            Sts2EventProgressState eventState)
        {
            if (actNumber <= 1 || string.IsNullOrWhiteSpace(ancientRelicId))
            {
                return;
            }

            switch (ancientRelicId.ToUpperInvariant())
            {
                case NeowOptionIds.LostCoffer:
                    relicState.ReplayImmediatePickupEffectsOnly(ancientRelicId);
                    break;
                default:
                    break;
            }
        }

        private (RewardRelicState State, IReadOnlyList<string> Relics) ResolveEventRelicEffects(
            int actNumber,
            string eventId,
            bool[] matchedRelicTargets,
            RewardRelicState relicState,
            Sts2EventProgressState eventState)
        {
            var unmatchedTargets = GetUnmatchedRelicTargets(matchedRelicTargets, actNumber);
            return eventId switch
            {
                "WELCOME_TO_WONGOS" => ResolveWelcomeToWongos(actNumber, unmatchedTargets, relicState, eventState),
                "SELF_HELP_BOOK" => ResolveSelfHelpBook(relicState, Sts2SelfHelpBookBranch.ReadTheBack),
                _ => (relicState, Array.Empty<string>())
            };
        }

        private static (RewardRelicState State, IReadOnlyList<string> Relics) ResolveSelfHelpBook(
            RewardRelicState relicState,
            Sts2SelfHelpBookBranch branch)
        {
            return (relicState, Array.Empty<string>());
        }

        private IReadOnlySet<string> GetUnmatchedRelicTargets(bool[] matchedRelicTargets, int actNumber)
        {
            var unmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _targetRelics.Count; i++)
            {
                if (matchedRelicTargets[i])
                {
                    continue;
                }

                var target = _targetRelics[i];
                if (!target.ActNumber.HasValue || target.ActNumber.Value == actNumber)
                {
                    unmatched.Add(target.RelicId);
                }
            }

            return unmatched;
        }

        private IReadOnlyList<string> ResolveShopPurchases(
            int actNumber,
            IReadOnlyList<ShopRelicEntry> shopOffers,
            bool[] matchedRelicTargets,
            RewardRelicState relicState,
            Sts2EventProgressState eventState)
        {
            if (shopOffers.Count == 0)
            {
                return Array.Empty<string>();
            }

            switch (_request.ShopStrategy)
            {
                case Sts2ExactRouteShopStrategy.NoPurchase:
                    return Array.Empty<string>();
                case Sts2ExactRouteShopStrategy.CardRemovalOnly:
                    if (eventState.HasRemovableBasicCard &&
                        eventState.CurrentGold >= DefaultShopRemovalCost)
                    {
                        eventState.ApplyShopCardRemoval(DefaultShopRemovalCost);
                    }

                    return Array.Empty<string>();
                case Sts2ExactRouteShopStrategy.TargetRelicsOnly:
                default:
                    var unmatchedTargets = GetUnmatchedRelicTargets(matchedRelicTargets, actNumber);
                    var purchased = new List<string>();

                    foreach (var offer in shopOffers)
                    {
                        if (!unmatchedTargets.Contains(offer.Id) || eventState.CurrentGold < offer.Price)
                        {
                            continue;
                        }

                        eventState.ApplyShopRelicPurchase(offer.Id, offer.Price);
                        relicState.ObtainShownRelic(offer.Id);
                        relicState.ResolvePurchasedShopRelic(offer.Id, actNumber);
                        purchased.Add(offer.Id);
                    }

                    if (purchased.Count > 0)
                    {
                        return purchased;
                    }

                    return Array.Empty<string>();
            }
        }

        private IEnumerable<ShopBranchCandidate> CreatePragmaticShopBranches(
            SimulationBranch branch,
            int actNumber,
            ShopPreview shopPreview)
        {
            if (_request.ShopStrategy == Sts2ExactRouteShopStrategy.NoPurchase)
            {
                yield break;
            }

            var shouldExploreSpendBranches =
                _targetEvents.Count == 0 ||
                _targetRelics.Count == 0 ||
                ShouldApplyLowGoldShopSpendHeuristic(actNumber) ||
                HasUnmatchedRelicTargets(branch.MatchedRelicTargets);

            if (!shouldExploreSpendBranches)
            {
                yield break;
            }

            var cardCandidates = shopPreview.ColoredCards
                .Concat(shopPreview.ColorlessCards)
                .OrderBy(card => card.Price)
                .ThenBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
                .Take(MaxShopCardCandidates)
                .ToArray();
            var potionCandidates = shopPreview.Potions
                .OrderBy(potion => potion.Price)
                .ThenBy(potion => potion.Id, StringComparer.OrdinalIgnoreCase)
                .Take(MaxShopPotionCandidates)
                .ToArray();
            var removalPrice = shopPreview.CardRemovalPrice;
            var result = new List<ShopBranchCandidate>();

            foreach (var plan in BuildShopPurchasePlans(cardCandidates, potionCandidates, removalPrice))
            {
                var candidate = branch.Clone();
                if (!TryApplyShopPlan(candidate, plan, removalPrice))
                {
                    continue;
                }

                result.Add(new ShopBranchCandidate(candidate, plan));
            }

            foreach (var candidate in result
                         .OrderBy(candidate => GetShopBranchPriority(candidate.Branch, actNumber))
                         .ThenBy(candidate => candidate.Branch.EventState.CurrentGold))
            {
                yield return candidate;
            }
        }

        private IEnumerable<ShopPurchasePlan> BuildShopPurchasePlans(
            IReadOnlyList<ShopCardEntry> cardCandidates,
            IReadOnlyList<ShopPotionEntry> potionCandidates,
            int removalPrice)
        {
            if (removalPrice > 0)
            {
                yield return new ShopPurchasePlan(RemoveCard: true, CardIds: Array.Empty<string>(), PotionIds: Array.Empty<string>());
            }

            for (var i = 0; i < cardCandidates.Count; i++)
            {
                yield return new ShopPurchasePlan(
                    RemoveCard: false,
                    CardIds: [cardCandidates[i].Id],
                    PotionIds: Array.Empty<string>());

                if (removalPrice > 0)
                {
                    yield return new ShopPurchasePlan(
                        RemoveCard: true,
                        CardIds: [cardCandidates[i].Id],
                        PotionIds: Array.Empty<string>());
                }

                for (var j = i + 1; j < cardCandidates.Count; j++)
                {
                    yield return new ShopPurchasePlan(
                        RemoveCard: false,
                        CardIds: [cardCandidates[i].Id, cardCandidates[j].Id],
                        PotionIds: Array.Empty<string>());

                    if (removalPrice > 0)
                    {
                        yield return new ShopPurchasePlan(
                            RemoveCard: true,
                            CardIds: [cardCandidates[i].Id, cardCandidates[j].Id],
                            PotionIds: Array.Empty<string>());
                    }
                }
            }

            for (var i = 0; i < potionCandidates.Count; i++)
            {
                yield return new ShopPurchasePlan(
                    RemoveCard: false,
                    CardIds: Array.Empty<string>(),
                    PotionIds: [potionCandidates[i].Id]);
            }
        }

        private static string BuildShopBranchSignature(
            SimulationBranch branch,
            IReadOnlyList<string> purchasedRelics,
            string? extraSignatureKey)
        {
            var relicKey = purchasedRelics.Count == 0 ? string.Empty : string.Join(",", purchasedRelics.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            return string.Join(
                "|",
                extraSignatureKey ?? string.Empty,
                branch.EventState.CurrentGold,
                branch.EventState.CurrentHp,
                branch.EventState.DeckCount,
                branch.EventState.PotionCount,
                branch.EventState.BasicStrikeCount,
                branch.EventState.BasicDefendCount,
                branch.RelicState.RewardsRng.Counter,
                branch.RelicState.ShopsRng.Counter,
                branch.RelicState.TreasureRng.Counter,
                branch.RelicState.CardRareOffset,
                branch.RelicState.PotionChance,
                relicKey);
        }

        private static string BuildShopPlanSignature(ShopPurchasePlan plan)
        {
            var cards = plan.CardIds.Count == 0 ? string.Empty : string.Join(",", plan.CardIds);
            var potions = plan.PotionIds.Count == 0 ? string.Empty : string.Join(",", plan.PotionIds);
            return $"remove={plan.RemoveCard};cards={cards};potions={potions}";
        }

        private static bool TryApplyShopPlan(SimulationBranch branch, ShopPurchasePlan plan, int removalPrice)
        {
            if (plan.RemoveCard)
            {
                if (removalPrice <= 0 ||
                    !branch.EventState.HasRemovableBasicCard ||
                    branch.EventState.CurrentGold < removalPrice)
                {
                    return false;
                }

                branch.EventState.ApplyShopCardRemoval(removalPrice);
            }

            foreach (var cardId in plan.CardIds)
            {
                var offer = branch.RelicState.FindShownShopCard(cardId);
                if (offer == null || branch.EventState.CurrentGold < offer.Price)
                {
                    return false;
                }

                branch.EventState.ApplyShopCardPurchase(offer.Id, offer.Price);
                branch.RelicState.ResolvePurchasedShopCard(offer.Id);
            }

            foreach (var potionId in plan.PotionIds)
            {
                var offer = branch.RelicState.FindShownShopPotion(potionId);
                if (offer == null || branch.EventState.CurrentGold < offer.Price)
                {
                    return false;
                }

                branch.EventState.ApplyShopPotionPurchase(offer.Id, offer.Price);
                branch.RelicState.ResolvePurchasedShopPotion(offer.Id);
            }

            return plan.RemoveCard || plan.CardIds.Count > 0 || plan.PotionIds.Count > 0;
        }

        private int GetShopBranchPriority(SimulationBranch branch, int actNumber)
        {
            if (ShouldApplyLowGoldShopSpendHeuristic(actNumber))
            {
                return Math.Abs(branch.EventState.CurrentGold - 43);
            }

            return branch.EventState.CurrentGold;
        }

        private bool ShouldApplyShopRemovalHeuristic(int actNumber, Sts2EventProgressState eventState)
        {
            if (!eventState.HasRemovableBasicCard)
            {
                return false;
            }

            return _targetEvents.Any(target =>
                !string.IsNullOrWhiteSpace(target.EventId) &&
                (!target.ActNumber.HasValue || target.ActNumber.Value == actNumber) &&
                EventPrefersLowerGold(target.EventId));
        }

        private static bool EventPrefersLowerGold(string eventId)
        {
            return Sts2EventIdNormalizer.FromPoolItem(eventId) switch
            {
                "DROWNING_BEACON" => true,
                _ => false
            };
        }

        private bool ShouldApplyLowGoldShopSpendHeuristic(int actNumber)
        {
            return _targetEvents.Any(target =>
                !string.IsNullOrWhiteSpace(target.EventId) &&
                (!target.ActNumber.HasValue || target.ActNumber.Value == actNumber) &&
                EventPrefersLowerGold(target.EventId));
        }

        private static void ApplyLowGoldShopSpendHeuristic(Sts2EventProgressState eventState)
        {
            const int targetGold = 43;
            if (eventState.CurrentGold <= targetGold)
            {
                return;
            }

            eventState.ApplyAnonymousShopSpend(eventState.CurrentGold - targetGold);
        }

        private static (RewardRelicState State, IReadOnlyList<string> Relics) ResolveWelcomeToWongos(
            int actNumber,
            IReadOnlySet<string> unmatchedTargets,
            RewardRelicState relicState,
            Sts2EventProgressState eventState)
        {
            var featuredItem = relicState.PullEventFront(actNumber, "Rare", relicId => !ShopBlockedRelics.Contains(relicId));
            if (featuredItem == null)
            {
                return (relicState, Array.Empty<string>());
            }

            if (eventState.CurrentGold >= 200 && unmatchedTargets.Contains(featuredItem))
            {
                relicState.Obtain(featuredItem);
                return (relicState, [featuredItem]);
            }

            if (eventState.CurrentGold >= 100)
            {
                var bargainBin = relicState.PullEventFront(actNumber, "Common", relicId => !ShopBlockedRelics.Contains(relicId));
                if (!string.IsNullOrWhiteSpace(bargainBin) && unmatchedTargets.Contains(bargainBin))
                {
                    relicState.Obtain(bargainBin);
                    return (relicState, [bargainBin]);
                }
            }

            return (relicState, Array.Empty<string>());
        }

        private static RouteRoomType ResolveRoomType(
            SimulationBranch branch,
            int actNumber,
            Sts2GeneratedRouteNode node)
        {
            if (!string.Equals(node.PointType, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return node.PointType switch
                {
                    "Shop" => RouteRoomType.Shop,
                    "Treasure" => RouteRoomType.Treasure,
                    "RestSite" => RouteRoomType.RestSite,
                    "Monster" => RouteRoomType.Monster,
                    "Elite" => RouteRoomType.Elite,
                    "Boss" => RouteRoomType.Boss,
                    "Ancient" => RouteRoomType.Event,
                    _ => RouteRoomType.Unassigned
                };
            }

            var previousRoomType = branch.PreviousRoomTypeInAct;
            var blacklist = new HashSet<RouteRoomType>();
            if (previousRoomType == RouteRoomType.Shop ||
                (node.ChildPointTypes.Count > 0 && node.ChildPointTypes.All(type => string.Equals(type, "Shop", StringComparison.OrdinalIgnoreCase))))
            {
                blacklist.Add(RouteRoomType.Shop);
            }

            ApplyUnknownRoomTypeHooks(branch, actNumber, blacklist);
            return branch.UnknownOdds.Roll(blacklist);
        }

        private static RouteRoomType ResolveRoomType(
            Sts2GeneratedRouteNode node,
            RouteRoomType? previousRoomType,
            UnknownOdds unknownOdds)
        {
            if (!string.Equals(node.PointType, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return node.PointType switch
                {
                    "Shop" => RouteRoomType.Shop,
                    "Treasure" => RouteRoomType.Treasure,
                    "RestSite" => RouteRoomType.RestSite,
                    "Monster" => RouteRoomType.Monster,
                    "Elite" => RouteRoomType.Elite,
                    "Boss" => RouteRoomType.Boss,
                    "Ancient" => RouteRoomType.Event,
                    _ => RouteRoomType.Unassigned
                };
            }

            var blacklist = new HashSet<RouteRoomType>();
            if (previousRoomType == RouteRoomType.Shop ||
                (node.ChildPointTypes.Count > 0 && node.ChildPointTypes.All(type => string.Equals(type, "Shop", StringComparison.OrdinalIgnoreCase))))
            {
                blacklist.Add(RouteRoomType.Shop);
            }

            return unknownOdds.Roll(blacklist);
        }

        private static void ApplyUnknownRoomTypeHooks(
            SimulationBranch branch,
            int actNumber,
            ISet<RouteRoomType> blacklist)
        {
            if (branch.RelicState.OwnedRelics.Contains("JUZU_BRACELET"))
            {
                blacklist.Add(RouteRoomType.Monster);
            }

            if (actNumber == 3 && branch.EventState.ContainsCard("LANTERN_KEY"))
            {
                blacklist.Add(RouteRoomType.Monster);
                blacklist.Add(RouteRoomType.Elite);
                blacklist.Add(RouteRoomType.Treasure);
                blacklist.Add(RouteRoomType.Shop);
            }

            if (branch.SelectedAncientRelicByAct.TryGetValue(actNumber, out var ancientRelicId) &&
                string.Equals(ancientRelicId, "GOLDEN_COMPASS", StringComparison.OrdinalIgnoreCase))
            {
                blacklist.Add(RouteRoomType.Monster);
                blacklist.Add(RouteRoomType.Elite);
                blacklist.Add(RouteRoomType.Treasure);
                blacklist.Add(RouteRoomType.Shop);
            }
        }

        private sealed class SimulationBranch
        {
            private SimulationBranch(
                Sts2EventProgressState eventState,
                GameRng eventRng,
                UnknownOdds unknownOdds,
                RewardRelicState relicState,
                bool[] matchedEventTargets,
                bool[] matchedRelicTargets,
                Dictionary<int, string> selectedAncientRelicByAct,
                List<Sts2ExactRouteAct> actResults,
                List<Sts2ExactRouteRoom> currentRooms,
                RouteRoomType? previousRoomTypeInAct)
            {
                EventState = eventState;
                EventRng = eventRng;
                UnknownOdds = unknownOdds;
                RelicState = relicState;
                MatchedEventTargets = matchedEventTargets;
                MatchedRelicTargets = matchedRelicTargets;
                SelectedAncientRelicByAct = selectedAncientRelicByAct;
                ActResults = actResults;
                CurrentRooms = currentRooms;
                PreviousRoomTypeInAct = previousRoomTypeInAct;
            }

            public Sts2EventProgressState EventState { get; set; }

            public GameRng EventRng { get; }

            public UnknownOdds UnknownOdds { get; }

            public RewardRelicState RelicState { get; set; }

            public bool[] MatchedEventTargets { get; }

            public bool[] MatchedRelicTargets { get; }

            public Dictionary<int, string> SelectedAncientRelicByAct { get; }

            public List<Sts2ExactRouteAct> ActResults { get; }

            public List<Sts2ExactRouteRoom> CurrentRooms { get; }

            public RouteRoomType? PreviousRoomTypeInAct { get; set; }

            public static SimulationBranch Create(
                Sts2EventProgressState eventState,
                NeowOptionResult? act1OpeningOption,
                GameRng eventRng,
                UnknownOdds unknownOdds,
                RewardRelicState relicState,
                int eventTargetCount,
                int relicTargetCount)
            {
                eventState.ApplyNeowStartHeal();
                eventState.ApplyAct1OpeningOption(act1OpeningOption);
                return new SimulationBranch(
                    eventState,
                    eventRng,
                    unknownOdds,
                    relicState,
                    new bool[eventTargetCount],
                    new bool[relicTargetCount],
                    [],
                    [],
                    [],
                    null);
            }

            public SimulationBranch Clone()
            {
                return new SimulationBranch(
                    EventState.Clone(),
                    new GameRng(EventRng.Seed, EventRng.Counter),
                    UnknownOdds.Clone(),
                    RelicState.Clone(),
                    (bool[])MatchedEventTargets.Clone(),
                    (bool[])MatchedRelicTargets.Clone(),
                    SelectedAncientRelicByAct.ToDictionary(pair => pair.Key, pair => pair.Value),
                    ActResults.ToList(),
                    CurrentRooms.ToList(),
                    PreviousRoomTypeInAct);
            }
        }

        private sealed record ShopPurchasePlan(
            bool RemoveCard,
            IReadOnlyList<string> CardIds,
            IReadOnlyList<string> PotionIds);

        private sealed record ShopBranchCandidate(
            SimulationBranch Branch,
            ShopPurchasePlan Plan);

    }

    private enum RouteRoomType
    {
        Unassigned,
        Event,
        Monster,
        Elite,
        Treasure,
        Shop,
        RestSite,
        Boss
    }

    private sealed class UnknownOdds
    {
        private readonly GameRng _rng;
        private readonly Dictionary<RouteRoomType, float> _baseOdds = new()
        {
            [RouteRoomType.Monster] = 0.1f,
            [RouteRoomType.Elite] = -1f,
            [RouteRoomType.Treasure] = 0.02f,
            [RouteRoomType.Shop] = 0.03f
        };

        private readonly Dictionary<RouteRoomType, float> _currentOdds = new()
        {
            [RouteRoomType.Monster] = 0.1f,
            [RouteRoomType.Elite] = -1f,
            [RouteRoomType.Treasure] = 0.02f,
            [RouteRoomType.Shop] = 0.03f
        };

        public UnknownOdds(GameRng rng)
        {
            _rng = rng;
        }

        public RouteRoomType Roll(IReadOnlySet<RouteRoomType> blacklist)
        {
            var allowed = _currentOdds.Keys
                .Append(RouteRoomType.Event)
                .Where(roomType => !blacklist.Contains(roomType))
                .ToList();

            var selected = allowed.Contains(RouteRoomType.Event)
                ? RouteRoomType.Event
                : RouteRoomType.Monster;
            var roll = _rng.NextFloat();
            var cumulative = 0f;
            foreach (var (roomType, odds) in _currentOdds)
            {
                if (!allowed.Contains(roomType) || odds < 0f)
                {
                    continue;
                }

                cumulative += odds;
                if (roll <= cumulative)
                {
                    selected = roomType;
                    break;
                }
            }

            foreach (var (roomType, baseOdds) in _baseOdds)
            {
                if (roomType == selected)
                {
                    _currentOdds[roomType] = baseOdds;
                }
                else if (allowed.Contains(roomType))
                {
                    _currentOdds[roomType] += baseOdds;
                }
            }

            return selected;
        }

        public void ResetToBase()
        {
            foreach (var (roomType, baseOdds) in _baseOdds)
            {
                _currentOdds[roomType] = baseOdds;
            }
        }

        public UnknownOdds Clone()
        {
            var clone = new UnknownOdds(new GameRng(_rng.Seed, _rng.Counter));
            foreach (var (roomType, odds) in _currentOdds)
            {
                clone._currentOdds[roomType] = odds;
            }

            return clone;
        }
    }

    private sealed class RewardRelicStateFactory
    {
        private readonly Sts2WorldData _world;
        private readonly Sts2WorldData.RelicPoolInfo _pools;
        private readonly IReadOnlyDictionary<string, string> _rarityMap;
        private readonly Sts2ExactRouteAnalysisRequest _request;
        private readonly RewardSimulationModel _rewardModel;

        public RewardRelicStateFactory(
            Sts2WorldData world,
            NeowOptionDataset dataset,
            Sts2ExactRouteAnalysisRequest request)
        {
            _world = world;
            _pools = world.RelicPools;
            _rarityMap = _pools.RarityMap;
            _request = request;
            _rewardModel = RewardSimulationModel.Create(dataset, request.Character, request.PlayerCount);
        }

        public RewardRelicState Create()
        {
            var ancientAvailability = _request.ResolveAncientAvailability();
            var upFrontRng = new GameRng(_request.SeedValue, "up_front");

            var sharedSequence = _pools.GetSharedSequence(ancientAvailability);
            var sharedBag = RelicBag.Create(sharedSequence, _rarityMap, upFrontRng, trackedOnly: false);
            RelicBag? playerBag = null;
            foreach (var teamCharacter in ResolveTeamCharacters(_request.Character, _request.PlayerCount, _request.TeamCharacters))
            {
                var playerSequence = _pools.GetCombinedSequence(teamCharacter, ancientAvailability);
                playerBag = RelicBag.Create(playerSequence, _rarityMap, upFrontRng, trackedOnly: true);
            }

            playerBag ??= RelicBag.Create(
                _pools.GetCombinedSequence(_request.Character, ancientAvailability),
                _rarityMap,
                upFrontRng,
                trackedOnly: true);
            ApplyPlayerCountRestrictions(sharedBag, playerBag, _request.PlayerCount);
            var playerSeed = unchecked((uint)((ulong)GameRng.GetDeterministicHashCode(_request.SeedText) + _request.PlayerNetId));

            var state = new RewardRelicState(
                sharedBag,
                playerBag,
                new GameRng(_request.SeedValue, "treasure_room_relics"),
                new GameRng(_request.SeedValue, "niche"),
                new GameRng(playerSeed, "rewards"),
                new GameRng(playerSeed, "shops"),
                _rewardModel,
                _request.AscensionLevel,
                _request.PlayerCount);
            state.ApplyAct1OpeningOption(_request.Act1OpeningOption);
            return state;
        }

        private static void ApplyPlayerCountRestrictions(RelicBag sharedBag, RelicBag playerBag, int playerCount)
        {
            // Keep the raw bag order intact. Official grab bags lazily filter
            // disallowed relics at pull time instead of removing them up front.
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
    }

    private sealed class RewardRelicState
    {
        public RewardRelicState(
            RelicBag sharedBag,
            RelicBag playerBag,
            GameRng treasureRng,
            GameRng nicheRng,
            GameRng rewardsRng,
            GameRng shopsRng,
            RewardSimulationModel rewardModel,
            int ascensionLevel,
            int playerCount)
        {
            SharedBag = sharedBag;
            PlayerBag = playerBag;
            TreasureRng = treasureRng;
            NicheRng = nicheRng;
            RewardsRng = rewardsRng;
            ShopsRng = shopsRng;
            RewardModel = rewardModel;
            AscensionLevel = ascensionLevel;
            PlayerCount = playerCount;
        }

        public RelicBag SharedBag { get; }

        public RelicBag PlayerBag { get; }

        public GameRng TreasureRng { get; }

        public GameRng NicheRng { get; }

        public GameRng RewardsRng { get; }

        public GameRng ShopsRng { get; }

        public RewardSimulationModel RewardModel { get; }

        public int AscensionLevel { get; }

        public int PlayerCount { get; }

        public float CardRareOffset { get; set; } = -0.05f;

        public float PotionChance { get; set; } = 0.4f;

        public RewardCardBuffer CurrentRewardCards { get; } = new();

        public HashSet<string> OwnedRelics { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> LastEventRelics { get; } = [];

        public List<ShopCardEntry> ShownShopCards { get; } = [];

        public List<ShopRelicEntry> ShownShopRelics { get; } = [];

        public List<ShopPotionEntry> ShownShopPotions { get; } = [];

        private List<ShownMerchantCardState> ShownShopCardStates { get; } = [];

        private List<ShownMerchantRelicState> ShownShopRelicStates { get; } = [];

        private List<ShownMerchantPotionState> ShownShopPotionStates { get; } = [];

        private sealed record ShownMerchantCardState(
            ShopCardEntry Entry,
            bool IsColorless,
            CardType? CardType,
            CardRarity? FixedRarity);

        private sealed record ShownMerchantRelicState(ShopRelicEntry Entry);

        private sealed record ShownMerchantPotionState(ShopPotionEntry Entry);

        public void Obtain(IEnumerable<string> relicIds)
        {
            foreach (var relicId in relicIds)
            {
                if (!string.IsNullOrWhiteSpace(relicId))
                {
                    PlayerBag.Remove(relicId);
                    SharedBag.Remove(relicId);
                    OwnedRelics.Add(relicId);
                }
            }
        }

        public void Obtain(string relicId)
        {
            if (!string.IsNullOrWhiteSpace(relicId))
            {
                PlayerBag.Remove(relicId);
                SharedBag.Remove(relicId);
                OwnedRelics.Add(relicId);
            }
        }

        public void RemoveOwnedRelic(string relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                return;
            }

            OwnedRelics.Remove(relicId);
        }

        public void ObtainWithImmediateEffects(string relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                return;
            }

            Obtain(relicId);
            ReplayImmediatePickupEffects(relicId);
        }

        public void ReplayImmediatePickupEffectsOnly(string relicId)
        {
            ReplayImmediatePickupEffects(relicId);
        }

        public void ApplyHatchRestSite()
        {
            if (OwnedRelics.Contains("BYRDPIP"))
            {
                return;
            }

            OwnedRelics.Add("BYRDPIP");
            PlayerBag.Remove("BYRDPIP");
            SharedBag.Remove("BYRDPIP");
        }

        public void ConsumeRewardRng(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _ = RewardsRng.NextFloat();
            }
        }


        public void ApplyAct1OpeningOption(NeowOptionResult? selectedOption)
        {
            if (selectedOption == null)
            {
                return;
            }

            Obtain(selectedOption.RelicId);
            PlayerBag.Remove(selectedOption.RelicId);

            foreach (var detail in selectedOption.Details)
            {
                if (!string.IsNullOrWhiteSpace(detail.ModelId) &&
                    RewardModel.RelicMetadataMap.ContainsKey(detail.ModelId))
                {
                    PlayerBag.Remove(detail.ModelId);
                    Obtain(detail.ModelId);
                }
            }

            ReplayImmediatePickupEffects(selectedOption.RelicId);
        }

        public void ConsumeRegularCombat()
        {
            var hasPotionReward = RollPotionRewardChance(isElite: false);
            _ = RewardsRng.NextInt(10, 21);
            if (hasPotionReward)
            {
                RollPotionReward();
            }

            for (var i = 0; i < 3; i++)
            {
                RollCombatRewardCard(CardRarityOddsType.RegularEncounter);
            }
        }

        public IReadOnlyList<string> ShowTreasure(int actNumber)
        {
            // Treasure rooms consume a rewards RNG gold roll before the chest relic is shown.
            _ = RewardsRng.NextInt(42, 53);
            var relic = SharedBag.PullFromFront(
                RollRelicRarity(TreasureRng),
                relicId => IsRelicAllowed(relicId, actNumber));
            if (string.IsNullOrWhiteSpace(relic))
            {
                return Array.Empty<string>();
            }

            PlayerBag.Remove(relic);
            OwnedRelics.Add(relic);
            return [relic];
        }

        public IReadOnlyList<string> ShowElite(int actNumber)
        {
            var hasPotionReward = RollPotionRewardChance(isElite: true);
            _ = RewardsRng.NextInt(35, 46);
            if (hasPotionReward)
            {
                RollPotionReward();
            }

            for (var i = 0; i < 3; i++)
            {
                RollCombatRewardCard(CardRarityOddsType.EliteEncounter);
            }

            var relic = PlayerBag.PullFromFront(
                RollRelicRarity(RewardsRng),
                relicId => IsRelicAllowed(relicId, actNumber));
            if (string.IsNullOrWhiteSpace(relic))
            {
                return Array.Empty<string>();
            }

            SharedBag.Remove(relic);
            OwnedRelics.Add(relic);
            return [relic];
        }

        public void ConsumeBossRewards()
        {
            var goldAmount = AscensionLevel >= 3 ? 75 : 100;
            _ = RewardsRng.NextInt(goldAmount, goldAmount + 1);
            var hasPotionReward = RollPotionRewardChance(isElite: false);
            if (hasPotionReward)
            {
                RollPotionReward();
            }

            for (var i = 0; i < 3; i++)
            {
                RollCombatRewardCard(CardRarityOddsType.BossEncounter);
            }
        }

        public IReadOnlyList<string> PreviewSeaGlass(RewardSimulationModel targetRewardModel)
        {
            var result = new List<string>(15);
            ReplaySeaGlassRewardBatch(targetRewardModel, CardRarity.Common, result);
            ReplaySeaGlassRewardBatch(targetRewardModel, CardRarity.Uncommon, result);
            ReplaySeaGlassRewardBatch(targetRewardModel, CardRarity.Rare, result);
            return result;
        }

        public IReadOnlyList<string> ShowShop(int actNumber)
        {
            return ShowShopPreview(actNumber).Relics.Select(entry => entry.Id).ToList();
        }

        public IReadOnlyList<ShopRelicEntry> ShowShopEntries(int actNumber)
        {
            return ShowShopPreview(actNumber).Relics;
        }

        public ShopPreview ShowShopPreview(int actNumber)
        {
            ShownShopCards.Clear();
            ShownShopRelics.Clear();
            ShownShopPotions.Clear();
            ShownShopCardStates.Clear();
            ShownShopRelicStates.Clear();
            ShownShopPotionStates.Clear();

            var discountedSlot = ShopsRng.NextInt(5);
            GenerateShownColoredMerchantCards(discountedSlot);
            GenerateShownColorlessMerchantCards();

            var relics = GenerateShownShopRelics(actNumber);
            GenerateShownShopPotions();

            return new ShopPreview
            {
                ColoredCards = ShownShopCards.Take(5).ToArray(),
                ColorlessCards = ShownShopCards.Skip(5).ToArray(),
                Relics = relics,
                Potions = ShownShopPotions.ToArray(),
                DiscountedColoredSlot = discountedSlot,
                CardRemovalPrice = GetShopRemovalPrice()
            };
        }

        public void ObtainShownRelic(string relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                return;
            }

            OwnedRelics.Add(relicId);
            ReplayImmediatePickupEffects(relicId);
        }

        public ShopCardEntry? FindShownShopCard(string cardId)
        {
            return ShownShopCards.FirstOrDefault(entry => string.Equals(entry.Id, cardId, StringComparison.OrdinalIgnoreCase));
        }

        public ShopPotionEntry? FindShownShopPotion(string potionId)
        {
            return ShownShopPotions.FirstOrDefault(entry => string.Equals(entry.Id, potionId, StringComparison.OrdinalIgnoreCase));
        }

        public void ResolvePurchasedShopRelic(string relicId, int actNumber)
        {
            if (ShouldRestockMerchantEntry())
            {
                RestockPurchasedShopRelic(relicId, actNumber);
                return;
            }

            RemoveShownShopRelic(relicId);
        }

        public void ResolvePurchasedShopPotion(string potionId)
        {
            if (ShouldRestockMerchantEntry())
            {
                RestockPurchasedShopPotion(potionId);
                return;
            }

            RemoveShownShopPotion(potionId);
        }

        public void ResolvePurchasedShopCard(string cardId)
        {
            if (ShouldRestockMerchantEntry())
            {
                RestockPurchasedShopCard(cardId);
                return;
            }

            RemoveShownShopCard(cardId);
        }

        public void RestockPurchasedShopRelic(string relicId, int actNumber)
        {
            var index = ShownShopRelicStates.FindIndex(state => string.Equals(state.Entry.Id, relicId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var selected = ShownShopRelicStates
                .Select(item => item.Entry.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected.Remove(relicId);

            var replacement = GenerateSingleShopRelic(selected, actNumber);
            if (replacement == null)
            {
                ShownShopRelicStates.RemoveAt(index);
                if (index < ShownShopRelics.Count)
                {
                    ShownShopRelics.RemoveAt(index);
                }

                return;
            }

            ShownShopRelicStates[index] = replacement;
            if (index < ShownShopRelics.Count)
            {
                ShownShopRelics[index] = replacement.Entry;
            }
        }

        public void RestockPurchasedShopPotion(string potionId)
        {
            var index = ShownShopPotionStates.FindIndex(state => string.Equals(state.Entry.Id, potionId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var selected = ShownShopPotionStates
                .Select(item => item.Entry.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected.Remove(potionId);

            var replacement = GenerateSingleShopPotion(selected);
            if (replacement == null)
            {
                ShownShopPotionStates.RemoveAt(index);
                if (index < ShownShopPotions.Count)
                {
                    ShownShopPotions.RemoveAt(index);
                }

                return;
            }

            ShownShopPotionStates[index] = replacement;
            if (index < ShownShopPotions.Count)
            {
                ShownShopPotions[index] = replacement.Entry;
            }
        }

        public void RestockPurchasedShopCard(string cardId)
        {
            var index = ShownShopCardStates.FindIndex(state => string.Equals(state.Entry.Id, cardId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var state = ShownShopCardStates[index];
            var selected = ShownShopCardStates
                .Select(item => item.Entry.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var replacement = state.IsColorless
                ? GenerateSingleColorlessMerchantCard(selected, state.FixedRarity!.Value)
                : GenerateSingleColoredMerchantCard(selected, state.CardType!.Value, discounted: false);
            if (replacement == null)
            {
                ShownShopCardStates.RemoveAt(index);
                if (index < ShownShopCards.Count)
                {
                    ShownShopCards.RemoveAt(index);
                }

                return;
            }

            ShownShopCardStates[index] = replacement;
            if (index < ShownShopCards.Count)
            {
                ShownShopCards[index] = replacement.Entry;
            }
        }

        private bool ShouldRestockMerchantEntry()
        {
            return OwnedRelics.Contains("THE_COURIER");
        }

        private void RemoveShownShopRelic(string relicId)
        {
            var index = ShownShopRelicStates.FindIndex(state => string.Equals(state.Entry.Id, relicId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            ShownShopRelicStates.RemoveAt(index);
            if (index < ShownShopRelics.Count)
            {
                ShownShopRelics.RemoveAt(index);
            }
        }

        private void RemoveShownShopPotion(string potionId)
        {
            var index = ShownShopPotionStates.FindIndex(state => string.Equals(state.Entry.Id, potionId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            ShownShopPotionStates.RemoveAt(index);
            if (index < ShownShopPotions.Count)
            {
                ShownShopPotions.RemoveAt(index);
            }
        }

        private void RemoveShownShopCard(string cardId)
        {
            var index = ShownShopCardStates.FindIndex(state => string.Equals(state.Entry.Id, cardId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            ShownShopCardStates.RemoveAt(index);
            if (index < ShownShopCards.Count)
            {
                ShownShopCards.RemoveAt(index);
            }
        }

        public IReadOnlyList<string> TryDig(int actNumber)
        {
            if (!OwnedRelics.Contains("SHOVEL"))
            {
                return Array.Empty<string>();
            }

            var relic = PlayerBag.PullFromFront(
                RollRelicRarity(RewardsRng),
                relicId => IsRelicAllowed(relicId, actNumber));
            if (string.IsNullOrWhiteSpace(relic))
            {
                return Array.Empty<string>();
            }

            SharedBag.Remove(relic);
            OwnedRelics.Add(relic);
            return [relic];
        }

        public string? PullEventFront(int actNumber, string rarity, Func<string, bool>? extraFilter = null)
        {
            var relic = PlayerBag.PullFromFront(
                rarity,
                relicId => IsRelicAllowed(relicId, actNumber),
                extraFilter);
            if (string.IsNullOrWhiteSpace(relic))
            {
                return null;
            }

            SharedBag.Remove(relic);
            return relic;
        }

        public string? PullEventFrontAndObtain(int actNumber, string? rarityOverride = null, Func<string, bool>? extraFilter = null)
        {
            var relic = PullEventFront(actNumber, rarityOverride ?? RollRelicRarity(RewardsRng), extraFilter);
            if (string.IsNullOrWhiteSpace(relic))
            {
                return null;
            }

            OwnedRelics.Add(relic);
            LastEventRelics.Add(relic);
            return relic;
        }

        public RewardRelicState Clone()
        {
            var clone = new RewardRelicState(
                SharedBag.Clone(),
                PlayerBag.Clone(),
                new GameRng(TreasureRng.Seed, TreasureRng.Counter),
                new GameRng(NicheRng.Seed, NicheRng.Counter),
                new GameRng(RewardsRng.Seed, RewardsRng.Counter),
                new GameRng(ShopsRng.Seed, ShopsRng.Counter),
                RewardModel,
                AscensionLevel,
                PlayerCount)
            {
                CardRareOffset = CardRareOffset,
                PotionChance = PotionChance
            };

            clone.CurrentRewardCards.CopyFrom(CurrentRewardCards);
            foreach (var relicId in OwnedRelics)
            {
                clone.OwnedRelics.Add(relicId);
            }

            clone.ShownShopCards.AddRange(ShownShopCards.Select(entry => entry with { }));
            clone.ShownShopRelics.AddRange(ShownShopRelics.Select(entry => entry with { }));
            clone.ShownShopPotions.AddRange(ShownShopPotions.Select(entry => entry with { }));
            clone.ShownShopCardStates.AddRange(ShownShopCardStates.Select(state => state with { Entry = state.Entry with { } }));
            clone.ShownShopRelicStates.AddRange(ShownShopRelicStates.Select(state => state with { Entry = state.Entry with { } }));
            clone.ShownShopPotionStates.AddRange(ShownShopPotionStates.Select(state => state with { Entry = state.Entry with { } }));

            return clone;
        }

        private void GenerateShownColoredMerchantCards(int discountedSlot)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < MerchantColoredCardTypes.Length; i++)
            {
                var cardType = MerchantColoredCardTypes[i];
                var rarity = RollShopCardRarityWithoutChangingFutureOdds();
                var cardId = PickMerchantCard(selected, cardType, rarity);
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    continue;
                }

                selected.Add(cardId);
                _ = RewardsRng.NextFloat();
                var price = RollCardPrice(cardId, discounted: false, colorless: false);
                if (i == discountedSlot)
                {
                    price = RollCardPrice(cardId, discounted: true, colorless: false);
                }

                var entry = new ShopCardEntry { Id = cardId, Price = price };
                ShownShopCards.Add(entry);
                ShownShopCardStates.Add(new ShownMerchantCardState(entry, IsColorless: false, CardType: cardType, FixedRarity: null));
            }
        }

        private void GenerateShownColorlessMerchantCards()
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rarity in MerchantColorlessCardRarities)
            {
                var pool = RewardModel.GetColorlessMerchantPool(rarity);
                var candidates = pool.Where(cardId => !selected.Contains(cardId)).ToArray();
                if (candidates.Length == 0)
                {
                    candidates = RewardModel.GetColorlessMerchantPool()
                        .Where(cardId => !selected.Contains(cardId))
                        .ToArray();
                }

                var cardId = ShopsRng.NextItem(candidates);
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    continue;
                }

                selected.Add(cardId);
                _ = RewardsRng.NextFloat();
                var entry = new ShopCardEntry
                {
                    Id = cardId,
                    Price = RollCardPrice(cardId, discounted: false, colorless: true)
                };
                ShownShopCards.Add(entry);
                ShownShopCardStates.Add(new ShownMerchantCardState(entry, IsColorless: true, CardType: null, FixedRarity: rarity));
            }
        }

        private ShownMerchantCardState? GenerateSingleColoredMerchantCard(
            ISet<string> selected,
            CardType cardType,
            bool discounted)
        {
            var rarity = RollShopCardRarityWithoutChangingFutureOdds();
            var cardId = PickMerchantCard(selected, cardType, rarity);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                return null;
            }

            _ = RewardsRng.NextFloat();
            return new ShownMerchantCardState(
                new ShopCardEntry
                {
                    Id = cardId,
                    Price = RollCardPrice(cardId, discounted, colorless: false)
                },
                IsColorless: false,
                CardType: cardType,
                FixedRarity: null);
        }

        private ShownMerchantCardState? GenerateSingleColorlessMerchantCard(
            ISet<string> selected,
            CardRarity rarity)
        {
            var pool = RewardModel.GetColorlessMerchantPool(rarity);
            var candidates = pool.Where(cardId => !selected.Contains(cardId)).ToArray();
            if (candidates.Length == 0)
            {
                candidates = RewardModel.GetColorlessMerchantPool()
                    .Where(cardId => !selected.Contains(cardId))
                    .ToArray();
            }

            var cardId = ShopsRng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                return null;
            }

            _ = RewardsRng.NextFloat();
            return new ShownMerchantCardState(
                new ShopCardEntry
                {
                    Id = cardId,
                    Price = RollCardPrice(cardId, discounted: false, colorless: true)
                },
                IsColorless: true,
                CardType: null,
                FixedRarity: rarity);
        }

        private IReadOnlyList<ShopRelicEntry> GenerateShownShopRelics(int actNumber)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shown = new List<ShopRelicEntry>(3);

            void PullShopRelic(string rarity)
            {
                var relic = PlayerBag.PullFromBack(
                    rarity,
                    selected,
                    ShopBlockedRelics,
                    relicId => IsRelicAllowed(relicId, actNumber));
                if (string.IsNullOrWhiteSpace(relic))
                {
                    return;
                }

                selected.Add(relic);
                SharedBag.Remove(relic);
                shown.Add(new ShopRelicEntry
                {
                    Id = relic,
                    Price = RollRelicPrice(relic)
                });
            }

            PullShopRelic(RollRelicRarity(RewardsRng));
            PullShopRelic(RollRelicRarity(RewardsRng));
            PullShopRelic("Shop");
            ShownShopRelics.AddRange(shown.Select(entry => entry with { }));
            ShownShopRelicStates.AddRange(shown.Select(entry => new ShownMerchantRelicState(entry with { })));
            return shown;
        }

        private void GenerateShownShopPotions()
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 3; i++)
            {
                var rarity = RollPotionRarity(ShopsRng);
                var candidates = RewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) && pool.Length > 0
                    ? pool.Where(candidate => !selected.Contains(candidate)).ToArray()
                    : Array.Empty<string>();

                if (candidates.Length == 0)
                {
                    candidates = RewardModel.PotionPool
                        .Where(candidate => !selected.Contains(candidate))
                        .ToArray();
                }

                var potionId = ShopsRng.NextItem(candidates);
                if (string.IsNullOrWhiteSpace(potionId))
                {
                    continue;
                }

                selected.Add(potionId);
                var entry = new ShopPotionEntry
                {
                    Id = potionId,
                    Price = RollPotionPrice(rarity)
                };
                ShownShopPotions.Add(entry);
                ShownShopPotionStates.Add(new ShownMerchantPotionState(entry));
            }
        }

        private ShownMerchantRelicState? GenerateSingleShopRelic(IReadOnlySet<string> selected, int actNumber)
        {
            var relic = PlayerBag.PullFromBack(
                RollRelicRarity(RewardsRng),
                selected,
                ShopBlockedRelics,
                relicId => IsRelicAllowed(relicId, actNumber));
            if (string.IsNullOrWhiteSpace(relic))
            {
                return null;
            }

            SharedBag.Remove(relic);
            return new ShownMerchantRelicState(new ShopRelicEntry
            {
                Id = relic,
                Price = RollRelicPrice(relic)
            });
        }

        private ShownMerchantPotionState? GenerateSingleShopPotion(IReadOnlySet<string> selected)
        {
            var rarity = RollPotionRarity(ShopsRng);
            var candidates = RewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) && pool.Length > 0
                ? pool.Where(candidate => !selected.Contains(candidate)).ToArray()
                : Array.Empty<string>();

            if (candidates.Length == 0)
            {
                candidates = RewardModel.PotionPool
                    .Where(candidate => !selected.Contains(candidate))
                    .ToArray();
            }

            var potionId = ShopsRng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(potionId))
            {
                return null;
            }

            return new ShownMerchantPotionState(new ShopPotionEntry
            {
                Id = potionId,
                Price = RollPotionPrice(rarity)
            });
        }

        private string? PickMerchantCard(ISet<string> selected, CardType targetType, CardRarity targetRarity)
        {
            var rarity = targetRarity;
            while (true)
            {
                var pool = RewardModel.GetMerchantCardPool(targetType, rarity);
                var cardId = ShopsRng.NextItem(pool.Where(candidate => !selected.Contains(candidate)).ToArray());
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    return cardId;
                }

                rarity = GetNextHighestRarity(rarity);
                if (rarity == CardRarity.None)
                {
                    break;
                }
            }

            return ShopsRng.NextItem(
                RewardModel.GetMerchantCardPool(targetType)
                    .Where(candidate => !selected.Contains(candidate))
                    .ToArray());
        }

        private CardRarity RollShopCardRarityWithoutChangingFutureOdds()
        {
            var roll = RewardsRng.NextFloat();
            var rareOdds = GetBaseCardOdds(CardRarityOddsType.Shop, CardRarity.Rare) + CardRareOffset;
            if (roll < rareOdds)
            {
                return CardRarity.Rare;
            }

            return roll < GetBaseCardOdds(CardRarityOddsType.Shop, CardRarity.Uncommon) + rareOdds
                ? CardRarity.Uncommon
                : CardRarity.Common;
        }

        private int RollRelicPrice(string relicId)
        {
            var basePrice = RewardModel.RelicMetadataMap.TryGetValue(relicId, out var metadata)
                ? metadata.MerchantCost
                : 200;
            return (int)Math.Round(basePrice * (0.85f + ShopsRng.NextFloat() * 0.30f));
        }

        private int RollCardPrice(string cardId, bool discounted, bool colorless)
        {
            var rarity = RewardModel.GetCardRarity(cardId);
            var basePrice = rarity switch
            {
                CardRarity.Uncommon => 75,
                CardRarity.Rare => 150,
                _ => 50
            };

            if (colorless)
            {
                basePrice = (int)Math.Round(basePrice * 1.15f);
            }

            var rolled = (int)Math.Round(basePrice * (0.95f + ShopsRng.NextFloat() * 0.10f));
            if (discounted)
            {
                rolled /= 2;
            }

            return rolled;
        }

        private int RollPotionPrice(PotionRarity rarity)
        {
            var basePrice = rarity switch
            {
                PotionRarity.Uncommon => 75,
                PotionRarity.Rare => 100,
                _ => 50
            };

            return (int)Math.Round(basePrice * (0.95f + ShopsRng.NextFloat() * 0.10f));
        }

        private int GetShopRemovalPrice()
        {
            return 100;
        }

        private bool IsRelicAllowed(string relicId, int actNumber)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                return false;
            }

            if (actNumber >= 3 && BeforeAct3TreasureChestRelics.Contains(relicId))
            {
                return false;
            }

            if (PlayerCount <= 1 && MultiplayerOnlyRelics.Contains(relicId))
            {
                return false;
            }

            if (PlayerCount > 1 && SinglePlayerOnlyRelics.Contains(relicId))
            {
                return false;
            }

            return true;
        }

        private void ReplayImmediatePickupEffects(string? relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                return;
            }

            var prismaticActive = OwnedRelics.Contains("PRISMATIC_GEM");

            switch (relicId.ToUpperInvariant())
            {
                case NeowOptionIds.ArcaneScroll:
                    ReplayCardReward(
                        RewardModel.GetCharacterCardPool(prismaticActive),
                        CardRarityOddsType.Uniform,
                        1,
                        metadata => metadata.ParsedRarity == CardRarity.Rare,
                        simulateUpgradeRoll: false);
                    break;
                case NeowOptionIds.HeftyTablet:
                    ReplayCardReward(
                        RewardModel.GetCharacterCardPool(prismaticActive),
                        CardRarityOddsType.Uniform,
                        3,
                        metadata => metadata.ParsedRarity == CardRarity.Rare,
                        simulateUpgradeRoll: false);
                    break;
                case NeowOptionIds.Kaleidoscope:
                    ReplayKaleidoscope();
                    break;
                case NeowOptionIds.LeadPaperweight:
                    ReplayCardReward(
                        RewardModel.GetColorlessCardPool(),
                        CardRarityOddsType.RegularEncounter,
                        2);
                    break;
                case NeowOptionIds.MassiveScroll:
                    ReplayCardReward(
                        RewardModel.GetMassiveScrollPool(),
                        CardRarityOddsType.RegularEncounter,
                        3,
                        metadata => metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly);
                    break;
                case NeowOptionIds.LostCoffer:
                    ReplayCardReward(
                        RewardModel.GetCharacterCardPool(prismaticActive),
                        CardRarityOddsType.RegularEncounter,
                        3);
                    RollPotionReward();
                    break;
                case NeowOptionIds.ScrollBoxes:
                    ReplayScrollBoxes();
                    break;
                case NeowOptionIds.LeafyPoultice:
                case NeowOptionIds.PhialHolster:
                    break;
            }
        }

        private void ReplayKaleidoscope()
        {
            var otherCharacters = RewardModel.AllCharacterCardPools.Keys
                .Where(character => character != RewardModel.Character)
                .OrderBy(GetKaleidoscopeStableSortKey, StringComparer.Ordinal)
                .ToList();
            if (otherCharacters.Count == 0)
            {
                return;
            }

            for (var bundleIndex = 0; bundleIndex < 2; bundleIndex++)
            {
                var shuffledCharacters = otherCharacters.ToList();
                NicheRng.Shuffle(shuffledCharacters);
                foreach (var character in shuffledCharacters.Take(3))
                {
                    if (!RewardModel.AllCharacterCardPools.TryGetValue(character, out var pool) || pool.Count == 0)
                    {
                        continue;
                    }

                    ReplayCardReward(pool, CardRarityOddsType.RegularEncounter, 1);
                }
            }
        }

        private static string GetKaleidoscopeStableSortKey(CharacterId character) =>
            character switch
            {
                CharacterId.Defect => "DEFECT",
                CharacterId.Ironclad => "IRONCLAD",
                CharacterId.Necrobinder => "NECROBINDER",
                CharacterId.Regent => "REGENT",
                CharacterId.Silent => "SILENT",
                _ => character.ToString().ToUpperInvariant()
            };

        private void ReplayCardReward(
            IEnumerable<string> pool,
            CardRarityOddsType oddsType,
            int count,
            Func<NeowCardMetadata, bool>? predicate = null,
            bool simulateUpgradeRoll = true)
        {
            var candidates = pool
                .Where(cardId => RewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 RewardSimulationModel.IsCardAllowedForReward(metadata, PlayerCount) &&
                                 (predicate == null || predicate(metadata)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0 || count <= 0)
            {
                return;
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var available = candidates.Where(cardId => !selected.Contains(cardId)).ToList();
                if (available.Count == 0)
                {
                    break;
                }

                var cardId = oddsType == CardRarityOddsType.Uniform
                    ? PickUniformRewardCard(available)
                    : PickWeightedRewardCard(available, oddsType);
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    break;
                }

                selected.Add(cardId);
                if (simulateUpgradeRoll)
                {
                    _ = RewardsRng.NextFloat();
                }
            }
        }

        private void ReplaySeaGlassRewardBatch(
            RewardSimulationModel targetRewardModel,
            CardRarity rarity,
            List<string> result)
        {
            var pool = targetRewardModel.GetRewardCardPool(rarity);
            if (pool.Length == 0)
            {
                return;
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < 5; i++)
            {
                var available = pool.Where(cardId => !selected.Contains(cardId)).ToArray();
                if (available.Length == 0)
                {
                    break;
                }

                var cardId = RewardsRng.NextItem(available);
                if (string.IsNullOrWhiteSpace(cardId))
                {
                    break;
                }

                selected.Add(cardId);
                result.Add(cardId);
                _ = RewardsRng.NextFloat();
            }
        }

        private string? PickUniformRewardCard(IReadOnlyList<string> available)
        {
            var filtered = available
                .Where(cardId => RewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 metadata.ParsedRarity is not CardRarity.Basic and not CardRarity.Ancient)
                .ToList();
            if (filtered.Count == 0)
            {
                filtered = available.ToList();
            }

            return RewardsRng.NextItem(filtered);
        }

        private string? PickWeightedRewardCard(IReadOnlyList<string> available, CardRarityOddsType oddsType)
        {
            var rarity = RollCardRarity(oddsType);
            var allowedRarities = available
                .Select(cardId => RewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) ? metadata.ParsedRarity : CardRarity.None)
                .Where(cardRarity => cardRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                .ToHashSet();

            while (!allowedRarities.Contains(rarity) && rarity != CardRarity.None)
            {
                rarity = GetNextHighestRarity(rarity);
            }

            var candidates = rarity == CardRarity.None
                ? available.ToList()
                : available
                    .Where(cardId => RewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                     metadata.ParsedRarity == rarity)
                    .ToList();
            if (candidates.Count == 0)
            {
                candidates = available.ToList();
            }

            return RewardsRng.NextItem(candidates);
        }

        private void ReplayScrollBoxes()
        {
            var pool = RewardModel.GetCharacterCardPool(OwnedRelics.Contains("PRISMATIC_GEM"));
            if (pool.Count == 0)
            {
                return;
            }

            var commons = new List<string>();
            var uncommons = new List<string>();
            foreach (var cardId in pool)
            {
                if (!RewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) ||
                    !RewardSimulationModel.IsCardAllowedForReward(metadata, PlayerCount))
                {
                    continue;
                }

                if (metadata.ParsedRarity == CardRarity.Common)
                {
                    commons.Add(cardId);
                }
                else if (metadata.ParsedRarity == CardRarity.Uncommon)
                {
                    uncommons.Add(cardId);
                }
            }

            if (commons.Count < 4 || uncommons.Count < 2)
            {
                return;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var bundle = 0; bundle < 2; bundle++)
            {
                if (RewardModel.Character == CharacterId.Defect && RewardsRng.NextInt(100) < 1)
                {
                    continue;
                }

                for (var i = 0; i < 2; i++)
                {
                    var availableCommons = commons.Where(cardId => !used.Contains(cardId)).ToList();
                    if (availableCommons.Count == 0)
                    {
                        break;
                    }

                    var cardId = RewardsRng.NextItem(availableCommons);
                    if (!string.IsNullOrWhiteSpace(cardId))
                    {
                        used.Add(cardId);
                    }
                }

                var availableUncommons = uncommons.Where(cardId => !used.Contains(cardId)).ToList();
                var uncommonId = RewardsRng.NextItem(availableUncommons);
                if (!string.IsNullOrWhiteSpace(uncommonId))
                {
                    used.Add(uncommonId);
                }
            }
        }

        private bool RollPotionRewardChance(bool isElite)
        {
            var current = PotionChance;
            var roll = RewardsRng.NextFloat();
            var eliteBonus = isElite ? 0.125f : 0f;
            var threshold = current + eliteBonus;
            if (roll < threshold)
            {
                PotionChance -= 0.1f;
            }
            else
            {
                PotionChance += 0.1f;
            }

            return roll < threshold;
        }

        private void RollPotionReward()
        {
            var rarity = RollPotionRarity(RewardsRng);
            if (!RewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) || pool.Length == 0)
            {
                pool = RewardModel.PotionPool.ToArray();
            }

            if (pool.Length > 0)
            {
                _ = RewardsRng.NextItem(pool);
            }
        }

        private void RollCombatRewardCard(CardRarityOddsType oddsType)
        {
            if (RewardModel.GetCharacterCardPool(OwnedRelics.Contains("PRISMATIC_GEM")).Count == 0)
            {
                return;
            }

            var rarity = RollCardRarity(oddsType);
            string cardId;
            while (!TryPickAvailableCard(rarity, out cardId))
            {
                rarity = GetNextHighestRarity(rarity);
                if (rarity == CardRarity.None)
                {
                    return;
                }
            }

            CurrentRewardCards.Add(cardId);
            _ = RewardsRng.NextFloat();
            if (CurrentRewardCards.Count >= 3)
            {
                CurrentRewardCards.Clear();
            }
        }

        private CardRarity RollCardRarity(CardRarityOddsType oddsType)
        {
            var roll = RewardsRng.NextFloat();
            var rareOdds = GetBaseCardOdds(oddsType, CardRarity.Rare) + CardRareOffset;
            CardRarity rarity;
            if (roll < rareOdds)
            {
                rarity = CardRarity.Rare;
            }
            else if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon) + rareOdds)
            {
                rarity = CardRarity.Uncommon;
            }
            else
            {
                rarity = CardRarity.Common;
            }

            if (rarity == CardRarity.Rare)
            {
                CardRareOffset = -0.05f;
            }
            else
            {
                CardRareOffset = Math.Min(
                    CardRareOffset + (AscensionLevel >= 7 ? 0.005f : 0.01f),
                    0.4f);
            }

            return rarity;
        }

        private bool TryPickAvailableCard(CardRarity rarity, out string cardId)
        {
            cardId = string.Empty;
            var pool = RewardModel.GetRewardCardPool(rarity, OwnedRelics.Contains("PRISMATIC_GEM"));
            if (pool.Length == 0)
            {
                return false;
            }

            var available = pool.Where(candidate => !CurrentRewardCards.Contains(candidate)).ToArray();
            if (available.Length == 0)
            {
                return false;
            }

            cardId = RewardsRng.NextItem(available);
            return !string.IsNullOrWhiteSpace(cardId);
        }

        private static string RollRelicRarity(GameRng rng)
        {
            var value = rng.NextFloat();
            if (value < 0.5f)
            {
                return "Common";
            }

            if (value < 0.83f)
            {
                return "Uncommon";
            }

            return "Rare";
        }

        private float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity)
        {
            var scarcityActive = AscensionLevel >= 7;
            return oddsType switch
            {
                CardRarityOddsType.RegularEncounter => rarity switch
                {
                    CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
                    CardRarity.Uncommon => 0.37f,
                    CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
                    _ => 0f
                },
                CardRarityOddsType.EliteEncounter => rarity switch
                {
                    CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
                    CardRarity.Uncommon => 0.4f,
                    CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
                    _ => 0f
                },
                CardRarityOddsType.BossEncounter => rarity == CardRarity.Rare ? 1f : 0f,
                CardRarityOddsType.Shop => rarity switch
                {
                    CardRarity.Common => scarcityActive ? 0.585f : 0.54f,
                    CardRarity.Uncommon => 0.37f,
                    CardRarity.Rare => scarcityActive ? 0.045f : 0.09f,
                    _ => 0f
                },
                CardRarityOddsType.Uniform => 1f / 3f,
                _ => 0f
            };
        }

        private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
            rarity switch
            {
                CardRarity.Basic => CardRarity.Common,
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
    }

    private sealed class RewardSimulationModel
    {
        private RewardSimulationModel(
            CharacterId character,
            IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> allCharacterCardPools,
            IReadOnlyList<string> cardPool,
            IReadOnlyDictionary<CardRarity, string[]> cardPoolByRarity,
            IReadOnlyList<string> prismaticCardPool,
            IReadOnlyDictionary<CardRarity, string[]> prismaticCardPoolByRarity,
            IReadOnlyList<string> characterCardPool,
            IReadOnlyDictionary<CardType, string[]> merchantCardPoolByType,
            IReadOnlyDictionary<(CardType Type, CardRarity Rarity), string[]> merchantCardPoolByTypeAndRarity,
            IReadOnlyList<string> colorlessCardPool,
            IReadOnlyList<string> colorlessMerchantPool,
            IReadOnlyDictionary<CardRarity, string[]> colorlessMerchantPoolByRarity,
            IReadOnlyDictionary<string, CardRarity> cardRarityMap,
            IReadOnlyDictionary<string, NeowCardMetadata> cardMetadataMap,
            IReadOnlyDictionary<string, NeowRelicMetadata> relicMetadataMap,
            IReadOnlyList<string> potionPool,
            IReadOnlyDictionary<PotionRarity, string[]> potionPoolByRarity)
        {
            Character = character;
            AllCharacterCardPools = allCharacterCardPools;
            CardPool = cardPool;
            CardPoolByRarity = cardPoolByRarity;
            PrismaticCardPool = prismaticCardPool;
            PrismaticCardPoolByRarity = prismaticCardPoolByRarity;
            CharacterCardPool = characterCardPool;
            MerchantCardPoolByType = merchantCardPoolByType;
            MerchantCardPoolByTypeAndRarity = merchantCardPoolByTypeAndRarity;
            ColorlessCardPool = colorlessCardPool;
            ColorlessMerchantPool = colorlessMerchantPool;
            ColorlessMerchantPoolByRarity = colorlessMerchantPoolByRarity;
            CardRarityMap = cardRarityMap;
            CardMetadataMap = cardMetadataMap;
            RelicMetadataMap = relicMetadataMap;
            PotionPool = potionPool;
            PotionPoolByRarity = potionPoolByRarity;
        }

        public CharacterId Character { get; }

        public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> AllCharacterCardPools { get; }

        public IReadOnlyList<string> CardPool { get; }

        public IReadOnlyDictionary<CardRarity, string[]> CardPoolByRarity { get; }

        public IReadOnlyList<string> PrismaticCardPool { get; }

        public IReadOnlyDictionary<CardRarity, string[]> PrismaticCardPoolByRarity { get; }

        public IReadOnlyList<string> CharacterCardPool { get; }

        public IReadOnlyDictionary<CardType, string[]> MerchantCardPoolByType { get; }

        public IReadOnlyDictionary<(CardType Type, CardRarity Rarity), string[]> MerchantCardPoolByTypeAndRarity { get; }

        public IReadOnlyList<string> ColorlessCardPool { get; }

        public IReadOnlyList<string> ColorlessMerchantPool { get; }

        public IReadOnlyDictionary<CardRarity, string[]> ColorlessMerchantPoolByRarity { get; }

        public IReadOnlyDictionary<string, CardRarity> CardRarityMap { get; }

        public IReadOnlyDictionary<string, NeowCardMetadata> CardMetadataMap { get; }

        public IReadOnlyDictionary<string, NeowRelicMetadata> RelicMetadataMap { get; }

        public IReadOnlyList<string> PotionPool { get; }

        public IReadOnlyDictionary<PotionRarity, string[]> PotionPoolByRarity { get; }

        public static RewardSimulationModel Create(NeowOptionDataset dataset, CharacterId character, int playerCount)
        {
            var characterPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
                ? characterCards
                : Array.Empty<string>();
            var allCharacterCardPools = dataset.CharacterCardPoolMap.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray());

            var merchantCards = characterPool
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForPlayer(metadata, playerCount) &&
                                 metadata.ParsedRarity is not CardRarity.Basic)
                .ToList();
            var merchantCardPoolByType = merchantCards
                .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedType)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var merchantCardPoolByTypeAndRarity = merchantCards
                .GroupBy(cardId => (dataset.CardMetadataMap[cardId].ParsedType, dataset.CardMetadataMap[cardId].ParsedRarity))
                .ToDictionary(group => group.Key, group => group.ToArray());

            var cardPool = characterPool
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForReward(metadata, playerCount))
                .ToList();

            var cardPoolByRarity = cardPool
                .Where(cardId => dataset.CardMetadataMap.ContainsKey(cardId))
                .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var prismaticCardPool = dataset.CharacterCardPoolMap.Values
                .SelectMany(static pool => pool)
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForReward(metadata, playerCount))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var prismaticCardPoolByRarity = prismaticCardPool
                .Where(cardId => dataset.CardMetadataMap.ContainsKey(cardId))
                .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var colorlessMerchantPool = dataset.ColorlessCardPoolList
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForPlayer(metadata, playerCount))
                .ToList();
            var colorlessMerchantPoolByRarity = colorlessMerchantPool
                .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var cardRarityMap = dataset.CardMetadataMap
                .Where(pair => IsCardAllowedForReward(pair.Value, playerCount))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ParsedRarity, StringComparer.OrdinalIgnoreCase);

            var potionPool = dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions)
                ? characterPotions.Concat(dataset.SharedPotionPoolList).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : dataset.SharedPotionPoolList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            potionPool = potionPool
                .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
                .ToList();

            var potionPoolByRarity = potionPool
                .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            return new RewardSimulationModel(
                character,
                allCharacterCardPools,
                cardPool,
                cardPoolByRarity,
                prismaticCardPool,
                prismaticCardPoolByRarity,
                characterPool,
                merchantCardPoolByType,
                merchantCardPoolByTypeAndRarity,
                dataset.ColorlessCardPoolList,
                colorlessMerchantPool,
                colorlessMerchantPoolByRarity,
                cardRarityMap,
                dataset.CardMetadataMap,
                dataset.RelicMetadataMap,
                potionPool,
                potionPoolByRarity);
        }

        public IReadOnlyList<string> GetCharacterCardPool(bool prismatic = false)
        {
            return prismatic ? PrismaticCardPool : CharacterCardPool;
        }

        public IReadOnlyList<string> GetColorlessCardPool()
        {
            return ColorlessCardPool;
        }

        public IReadOnlyList<string> GetMassiveScrollPool()
        {
            return ColorlessCardPool
                .Concat(CharacterCardPool)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string[] GetRewardCardPool(CardRarity rarity, bool prismatic = false)
        {
            var source = prismatic ? PrismaticCardPoolByRarity : CardPoolByRarity;
            return source.TryGetValue(rarity, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetMerchantCardPool(CardType type)
        {
            return MerchantCardPoolByType.TryGetValue(type, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetMerchantCardPool(CardType type, CardRarity rarity)
        {
            return MerchantCardPoolByTypeAndRarity.TryGetValue((type, rarity), out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public IReadOnlyList<string> GetColorlessMerchantPool()
        {
            return ColorlessMerchantPool;
        }

        public string[] GetColorlessMerchantPool(CardRarity rarity)
        {
            return ColorlessMerchantPoolByRarity.TryGetValue(rarity, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public CardRarity GetCardRarity(string cardId)
        {
            return CardRarityMap.TryGetValue(cardId, out var rarity)
                ? rarity
                : CardRarity.Common;
        }

        private static bool IsCardAllowedForPlayer(NeowCardMetadata metadata, int playerCount)
        {
            if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
            {
                return false;
            }

            if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
            {
                return false;
            }

            return true;
        }

        public static bool IsCardAllowedForReward(NeowCardMetadata metadata, int playerCount)
        {
            if (!IsCardAllowedForPlayer(metadata, playerCount))
            {
                return false;
            }

            return metadata.CanBeGeneratedInCombat &&
                   metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
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

        public void CopyFrom(RewardCardBuffer other)
        {
            _first = other._first;
            _second = other._second;
            _third = other._third;
            Count = other.Count;
        }
    }

    private sealed class RelicBag
    {
        private readonly Dictionary<string, List<string>> _buckets;

        private RelicBag(Dictionary<string, List<string>> buckets)
        {
            _buckets = buckets;
        }

        public static RelicBag Create(
            IReadOnlyList<string> sequence,
            IReadOnlyDictionary<string, string> rarityMap,
            GameRng rng,
            bool trackedOnly)
        {
            var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var relicId in sequence)
            {
                if (!rarityMap.TryGetValue(relicId, out var rarity))
                {
                    continue;
                }

                if (trackedOnly &&
                    !string.Equals(rarity, "Common", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rarity, "Uncommon", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rarity, "Rare", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rarity, "Shop", StringComparison.OrdinalIgnoreCase))
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

        public void Remove(string relicId)
        {
            foreach (var list in _buckets.Values)
            {
                list.RemoveAll(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void RemoveAll(IEnumerable<string> relicIds)
        {
            foreach (var relicId in relicIds)
            {
                Remove(relicId);
            }
        }

        public RelicBag Clone()
        {
            return new RelicBag(_buckets.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase));
        }

        public string? PullFromFront(
            string rarity,
            Func<string, bool>? globalAllowed = null,
            Func<string, bool>? pickAllowed = null)
        {
            RemoveDisallowed(globalAllowed);
            foreach (var key in EnumerateFallbackRarities(rarity))
            {
                if (_buckets.TryGetValue(key, out var list) && list.Count > 0)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (pickAllowed != null && !pickAllowed(list[i]))
                        {
                            continue;
                        }

                        var relic = list[i];
                        list.RemoveAt(i);
                        return relic;
                    }
                }
            }

            return null;
        }

        public string? PullFromBack(
            string rarity,
            IReadOnlySet<string> selected,
            IReadOnlySet<string>? extraBlacklist = null,
            Func<string, bool>? globalAllowed = null,
            Func<string, bool>? pickAllowed = null)
        {
            RemoveDisallowed(globalAllowed);
            foreach (var key in EnumerateFallbackRarities(rarity))
            {
                if (!_buckets.TryGetValue(key, out var list))
                {
                    continue;
                }

                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (selected.Contains(list[i]) ||
                        (extraBlacklist != null && extraBlacklist.Contains(list[i])) ||
                        (pickAllowed != null && !pickAllowed(list[i])))
                    {
                        continue;
                    }

                    var relic = list[i];
                    list.RemoveAt(i);
                    return relic;
                }
            }

            return null;
        }

        private void RemoveDisallowed(Func<string, bool>? globalAllowed)
        {
            if (globalAllowed == null)
            {
                return;
            }

            foreach (var list in _buckets.Values)
            {
                list.RemoveAll(relicId => !globalAllowed(relicId));
            }
        }

        private static IEnumerable<string> EnumerateFallbackRarities(string rarity)
        {
            var current = rarity;
            while (!string.IsNullOrWhiteSpace(current))
            {
                yield return current;
                current = current switch
                {
                    "Shop" => "Common",
                    "Common" => "Uncommon",
                    "Uncommon" => "Rare",
                    _ => string.Empty
                };
            }
        }
    }

    private static class RouteIdComparer
    {
        public static bool Equals(string? left, string? right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        }
    }
}
