using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedModel.Sts2;

var root = FindWorkspaceRoot();
var savePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(root, "存档", "1777474596.run");
if (!File.Exists(savePath))
{
    throw new FileNotFoundException("Save file not found.", savePath);
}

var save = JsonSerializer.Deserialize<SaveSnapshot>(File.ReadAllText(savePath), new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? throw new InvalidOperationException("Failed to parse save file.");

if (!SeedFormatter.TryNormalize(save.Seed ?? string.Empty, out var normalizedSeed, out var error))
{
    throw new InvalidOperationException($"Invalid seed in save: {error}");
}

var character = ResolveSaveCharacter(save);
var unlockedCharacters = new[]
{
    CharacterId.Ironclad,
    CharacterId.Silent,
    CharacterId.Defect,
    CharacterId.Necrobinder,
    CharacterId.Regent
};

var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(root, "data", "0.103.2", "neow", "options.json"));
var act1Options = new NeowGenerator(dataset).Generate(NeowGenerationContext.Create(
    seed: seedValue,
    playerCount: 1,
    scrollBoxesEligible: true,
    hasRunModifiers: false,
    character: character,
    ascensionLevel: save.Ascension));
var actualAct1RelicId = ResolveActualAct1OpeningRelicId(save);
var act1OpeningOption = act1Options.FirstOrDefault(option => string.Equals(option.RelicId, actualAct1RelicId, StringComparison.OrdinalIgnoreCase))
    ?? act1Options.FirstOrDefault();
var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

var actPools = previewer.AnalyzePools(new Sts2SeedAnalysisRequest
{
    SeedText = normalizedSeed,
    SeedValue = seedValue,
    Character = character,
    AscensionLevel = save.Ascension,
    IncludeDarvSharedAncient = true
});
var actPoolMap = actPools.Acts.ToDictionary(
    act => act.ActNumber,
    act => (IReadOnlyList<string>)act.EventPool,
    EqualityComparer<int>.Default);

var runPreview = previewer.Preview(new Sts2RunRequest
{
    SeedText = normalizedSeed,
    SeedValue = seedValue,
    Character = character,
    UnlockedCharacters = unlockedCharacters,
    AscensionLevel = save.Ascension,
    PlayerCount = 1,
    IncludeDarvSharedAncient = true,
    IncludeAct2 = true,
    IncludeAct3 = true
});
foreach (var act in runPreview.Acts.OrderBy(act => act.ActNumber))
{
    Console.WriteLine(
        $"previewAncient.act{act.ActNumber}={act.AncientId}:{string.Join("/", act.AncientOptions.Select(option => option.RelicId ?? option.OptionId ?? string.Empty))}");
}
var firstShopPreview = previewer.PreviewFirstShop(
    dataset,
    new SeedRunEvaluationContext
    {
        SeedText = normalizedSeed,
        RunSeed = seedValue,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = save.Ascension,
        PlayerCount = 1,
        IncludeDarvSharedAncient = true
    },
    act1OpeningOption == null ? Array.Empty<NeowOptionResult>() : [act1OpeningOption]);
var ancientInfoType = typeof(Sts2ExactRouteAnalysis).Assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+AncientInfo")
    ?? throw new InvalidOperationException("Missing AncientInfo type.");
var ancientByAct = CreateAncientByAct(runPreview, ancientInfoType);

var previewerType = typeof(Sts2RunPreviewer);
var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing _world.");
var world = worldField.GetValue(previewer)
    ?? throw new InvalidOperationException("Missing world value.");
var worldType = world.GetType();
var resolveActOneMethod = worldType.GetMethod("ResolveActOne", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ResolveActOne.");
var actsProperty = worldType.GetProperty("Acts", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing Acts property.");
var acts = ((IEnumerable)actsProperty.GetValue(world)!).Cast<object>().ToList();

var assembly = previewerType.Assembly;
var mapStateType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardActMapState")
    ?? throw new InvalidOperationException("Missing StandardActMapState.");
var createMapMethod = mapStateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing map Create.");
var getAllGeneratedRoutesMethod = mapStateType.GetMethod("GetAllGeneratedRoutes", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing GetAllGeneratedRoutes.");

var matchedRoutesByAct = new Dictionary<int, List<RouteCandidate>>();
for (var actNumber = 1; actNumber <= 2; actNumber++)
{
    var actualHistory = save.MapPointHistory != null && save.MapPointHistory.Count >= actNumber
        ? save.MapPointHistory[actNumber - 1] ?? []
        : [];
    var actualMapPath = actualHistory
        .Select(point => NormalizeMapPointType(point.MapPointType))
        .Where(type => !string.IsNullOrWhiteSpace(type) &&
                       !string.Equals(type, "Ancient", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(type, "Boss", StringComparison.OrdinalIgnoreCase))
        .ToList();

    var act = actNumber == 1
        ? resolveActOneMethod.Invoke(world, [seedValue, null, false]) ?? GetAct(1)
        : GetAct(actNumber);
    var map = createMapMethod.Invoke(null, [act, false, save.Ascension, new GameRng(seedValue, $"act_{actNumber}_map")])
        ?? throw new InvalidOperationException($"Failed to create act {actNumber} map.");
    var rawRoutes = ((IEnumerable)(getAllGeneratedRoutesMethod.Invoke(map, [actNumber])
        ?? throw new InvalidOperationException($"Failed to read act {actNumber} routes.")))
        .Cast<object>()
        .ToList();

    matchedRoutesByAct[actNumber] = rawRoutes
        .Select(route => new RouteCandidate(route, ConvertRouteTypes(route)))
        .Where(candidate => StartsWith(candidate.Types, actualMapPath))
        .ToList();
}

var request = new Sts2ExactRouteAnalysisRequest
{
    SeedText = normalizedSeed,
    SeedValue = seedValue,
    Character = character,
    AscensionLevel = save.Ascension,
    PlayerCount = 1,
    Act1OpeningOption = act1OpeningOption
};

var analyzerType = typeof(Sts2ExactRouteAnalysis).Assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RouteSimulator")
    ?? throw new InvalidOperationException("Missing RouteSimulator type.");
var analyzer = Activator.CreateInstance(
    analyzerType,
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
    binder: null,
    args:
    [
        world,
        root,
        dataset,
        request,
        actPoolMap,
        ancientByAct,
        unlockedCharacters
    ],
    culture: null)
    ?? throw new InvalidOperationException("Failed to create RouteSimulator.");
var trySimulateMethod = analyzerType.GetMethod("TrySimulate", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing TrySimulate.");

Console.WriteLine($"save={savePath}");
Console.WriteLine($"seed={normalizedSeed}");
Console.WriteLine($"character={character}");
Console.WriteLine($"act1Option={act1OpeningOption?.RelicId ?? "(none)"}");
Console.WriteLine($"firstShopPreview={string.Join(" / ", firstShopPreview.Relics.Select(relic => relic.Id))}");
Console.WriteLine($"firstShopCards={string.Join(" / ", firstShopPreview.ColoredCards.Concat(firstShopPreview.ColorlessCards).Select(card => card.Id))}");
Console.WriteLine($"firstShopRoute={string.Join(" | ", firstShopPreview.RouteRooms)}");
var actualFirstShopRoute = new[] { "Monster", "Treasure", "Monster", "Event", "Shop" };
Console.WriteLine($"reflected4MonsterShop={string.Join(" / ", SimulateStandardShopForRoute(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption, new[] { "Monster", "Monster", "Monster", "Monster", "Shop" }))}");
Console.WriteLine($"reflectedActualPreShop={string.Join(" / ", SimulateStandardShopForRoute(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption, actualFirstShopRoute))}");
var reflectedActualPreShopPreview = SimulateStandardShopPreviewForRoute(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption, actualFirstShopRoute);
Console.WriteLine($"reflectedActualPreShopCards={string.Join(" / ", reflectedActualPreShopPreview.ColoredCards.Concat(reflectedActualPreShopPreview.ColorlessCards).Select(card => card.Id))}");
Console.WriteLine($"reflectedActualPreShopState={DescribeStandardShopStateForRoute(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption, actualFirstShopRoute)}");
Console.WriteLine($"relicBagCompare={DescribeAct1TreasureBagImpact(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"firstShopBruteforce={FindMatchingStandardShopOffsets(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption, actualFirstShopRoute)}");
Console.WriteLine($"exactAct1FirstShopBruteforce={FindMatchingExactAct1FirstShopOffsets(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"exactAct2FirstShopBruteforce={FindMatchingExactAct2FirstShopOffsets(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"act1PostShopEliteProbe={FindMatchingAct1PostShopEliteOffsets(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"act1PostShopEliteDetail={DescribeAct1PostShopEliteOffsets(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"act1TreasureRewardMatrix={DescribeAct1TreasureRewardMatrix(root, dataset, normalizedSeed, seedValue, save.Ascension, character, act1OpeningOption)}");
Console.WriteLine($"act1Floor7GoldCheck={VerifyAct1Floor7GoldHypothesis(root, dataset, normalizedSeed, seedValue, save.Ascension, character, actPoolMap, act1OpeningOption)}");
if (actPoolMap.TryGetValue(2, out var act2Pool))
{
    Console.WriteLine($"act2Pool={string.Join(" | ", act2Pool.Take(20).Select((eventId, index) => $"{index}:{eventId}"))}");
Console.WriteLine($"act2PoolIndex.ZEN_WEAVER={IndexOfPoolItem(act2Pool, "ZEN_WEAVER")}");
    Console.WriteLine($"act2PoolIndex.WELCOME_TO_WONGOS={IndexOfPoolItem(act2Pool, "WELCOME_TO_WONGOS")}");
}
Console.WriteLine($"act1Matches={matchedRoutesByAct[1].Count} act2Matches={matchedRoutesByAct[2].Count}");

var routeListType = typeof(List<>).MakeGenericType(typeof(Sts2ExactRouteAnalysis).Assembly.GetType("SeedModel.Sts2.Sts2GeneratedActRoute")
    ?? throw new InvalidOperationException("Missing generated route type."));
var actualActs = BuildActualActs(save);
Console.WriteLine($"act2AncientRewardProbe={FindMatchingAct2AncientRewardOffsets(analyzer, save, dataset, matchedRoutesByAct, actualActs, actPoolMap)}");
Console.WriteLine($"bossRewardProbe={FindMatchingBossRewardReplayOffsets(analyzer, save, dataset, matchedRoutesByAct, actualActs, actPoolMap)}");
Console.WriteLine($"combinedReplayProbe={FindBestCombinedReplayProbe(analyzer, save, dataset, matchedRoutesByAct, actualActs, actPoolMap)}");
Console.WriteLine($"actualBossReplayProbe={FindActualBossReplayProbe(analyzer, save, dataset, matchedRoutesByAct, actualActs, actPoolMap)}");
var act1Candidates = matchedRoutesByAct[1];
var act2Candidates = actualActs.Count >= 2 ? matchedRoutesByAct[2] : [new RouteCandidate(new object(), [])];
if (act1Candidates.Count > 0)
{
    DumpAct1BranchSpace(analyzer, act1Candidates[0].RawRoute);
}
var comparisons = new List<RouteComparison>();
var shopAwareComparisons = new List<RouteComparison>();
var shopAwareSelfHelpOffsetComparisons = new List<(int ExtraRewardDraws, RouteComparison Comparison)>();
var shopAwareCombinedReplayComparisons = new List<(bool ReplayBossPotion, bool ReplayBossCards, int Act2AncientExtraRewardDraws, RouteComparison Comparison)>();
for (var act1Index = 0; act1Index < act1Candidates.Count; act1Index++)
{
    for (var act2Index = 0; act2Index < act2Candidates.Count; act2Index++)
    {
        var routeList = (IList)Activator.CreateInstance(routeListType)!;
        routeList.Add(act1Candidates[act1Index].RawRoute);
        if (actualActs.Count >= 2)
        {
            routeList.Add(act2Candidates[act2Index].RawRoute);
        }

        var match = trySimulateMethod.Invoke(analyzer, [routeList]);
        if (match == null)
        {
            continue;
        }

        var simulatedActs = ExtractSimulatedActs(match);
        comparisons.Add(CompareAgainstSave(actualActs, simulatedActs, act1Index + 1, actualActs.Count >= 2 ? act2Index + 1 : null, ignoreShopRelicMismatches: false));

        var rawRoutes = new List<object> { act1Candidates[act1Index].RawRoute };
        if (actualActs.Count >= 2)
        {
            rawRoutes.Add(act2Candidates[act2Index].RawRoute);
        }

        var shopAwareActs = SimulateWithActualShopState(analyzer, save, dataset, rawRoutes, actualActs, simulatedActs, actPoolMap);
        shopAwareComparisons.Add(CompareAgainstSave(actualActs, shopAwareActs, act1Index + 1, actualActs.Count >= 2 ? act2Index + 1 : null, ignoreShopRelicMismatches: true));

        for (var extraRewardDraws = 0; extraRewardDraws <= 12; extraRewardDraws++)
        {
            var offsetActs = SimulateWithActualShopState(analyzer, save, dataset, rawRoutes, actualActs, simulatedActs, actPoolMap, extraRewardDraws);
            shopAwareSelfHelpOffsetComparisons.Add((
                extraRewardDraws,
                CompareAgainstSave(actualActs, offsetActs, act1Index + 1, actualActs.Count >= 2 ? act2Index + 1 : null, ignoreShopRelicMismatches: true)));
        }

        foreach (var replayBossPotion in new[] { false, true })
        {
            foreach (var replayBossCards in new[] { false, true })
            {
                for (var act2AncientExtraRewardDraws = 0; act2AncientExtraRewardDraws <= 24; act2AncientExtraRewardDraws++)
                {
                    var combinedActs = SimulateWithActualShopState(
                        analyzer,
                        save,
                        dataset,
                        rawRoutes,
                        actualActs,
                        simulatedActs,
                        actPoolMap,
                        selfHelpExtraRewardDraws: 0,
                        act2AncientExtraRewardDraws: act2AncientExtraRewardDraws,
                        replayBossRewardCards: replayBossCards,
                        replayBossPotionReward: replayBossPotion);
                    shopAwareCombinedReplayComparisons.Add((
                        replayBossPotion,
                        replayBossCards,
                        act2AncientExtraRewardDraws,
                        CompareAgainstSave(actualActs, combinedActs, act1Index + 1, actualActs.Count >= 2 ? act2Index + 1 : null, ignoreShopRelicMismatches: true)));
                }
            }
        }
    }
}

var bestComparison = comparisons
    .OrderBy(comparison => comparison.Score)
    .ThenBy(comparison => comparison.Mismatches.Count)
    .FirstOrDefault();
if (bestComparison == null)
{
    Console.WriteLine("diff=no-simulated-match");
}
else
{
    Console.WriteLine($"diff.bestScore={bestComparison.Score} act1Route#{bestComparison.Act1RouteIndex} act2Route#{bestComparison.Act2RouteIndex?.ToString() ?? "-"}");
    foreach (var line in bestComparison.SampleLines)
    {
        Console.WriteLine(line);
    }

    foreach (var mismatch in bestComparison.Mismatches)
    {
        Console.WriteLine($"DIFF {mismatch}");
    }
}

var bestShopAwareComparison = shopAwareComparisons
    .OrderBy(comparison => comparison.Score)
    .ThenBy(comparison => comparison.Mismatches.Count)
    .FirstOrDefault();
var bestShopAwareSelfHelpOffset = shopAwareSelfHelpOffsetComparisons
    .OrderBy(item => item.Comparison.Score)
    .ThenBy(item => item.Comparison.Mismatches.Count)
    .FirstOrDefault();
if (bestShopAwareComparison == null)
{
    Console.WriteLine("shopAwareDiff=no-simulated-match");
}
else
{
    Console.WriteLine($"shopAwareDiff.bestScore={bestShopAwareComparison.Score} act1Route#{bestShopAwareComparison.Act1RouteIndex} act2Route#{bestShopAwareComparison.Act2RouteIndex?.ToString() ?? "-"}");
    foreach (var line in bestShopAwareComparison.SampleLines)
    {
        Console.WriteLine($"SHOPAWARE {line}");
    }

    foreach (var mismatch in bestShopAwareComparison.Mismatches)
    {
        Console.WriteLine($"SHOPAWARE DIFF {mismatch}");
    }
}

if (bestShopAwareSelfHelpOffset.Comparison != null)
{
    Console.WriteLine($"shopAwareSelfHelpOffset.bestScore={bestShopAwareSelfHelpOffset.Comparison.Score} extraRewardDraws={bestShopAwareSelfHelpOffset.ExtraRewardDraws} act1Route#{bestShopAwareSelfHelpOffset.Comparison.Act1RouteIndex} act2Route#{bestShopAwareSelfHelpOffset.Comparison.Act2RouteIndex?.ToString() ?? "-"}");
    foreach (var mismatch in bestShopAwareSelfHelpOffset.Comparison.Mismatches.Take(8))
    {
        Console.WriteLine($"SHOPAWARE+SELFHELP DIFF {mismatch}");
    }
}

var bestShopAwareCombinedReplay = shopAwareCombinedReplayComparisons
    .OrderBy(item => item.Comparison.Score)
    .ThenBy(item => item.Comparison.Mismatches.Count)
    .FirstOrDefault();
if (bestShopAwareCombinedReplay.Comparison != null)
{
    Console.WriteLine($"shopAwareCombinedReplay.bestScore={bestShopAwareCombinedReplay.Comparison.Score} bossPotion={(bestShopAwareCombinedReplay.ReplayBossPotion ? 1 : 0)} bossCards={(bestShopAwareCombinedReplay.ReplayBossCards ? 1 : 0)} act2AncientReward+{bestShopAwareCombinedReplay.Act2AncientExtraRewardDraws} act1Route#{bestShopAwareCombinedReplay.Comparison.Act1RouteIndex} act2Route#{bestShopAwareCombinedReplay.Comparison.Act2RouteIndex?.ToString() ?? "-"}");
    foreach (var mismatch in bestShopAwareCombinedReplay.Comparison.Mismatches.Take(8))
    {
        Console.WriteLine($"SHOPAWARE+COMBINED DIFF {mismatch}");
    }
}

if (act1Candidates.Count > 0 && act2Candidates.Count > 0)
{
    if (bestComparison != null)
    {
        var bestRouteList = (IList)Activator.CreateInstance(routeListType)!;
        bestRouteList.Add(act1Candidates[Math.Max(0, bestComparison.Act1RouteIndex - 1)].RawRoute);
        if (actualActs.Count >= 2 && bestComparison.Act2RouteIndex.HasValue)
        {
            bestRouteList.Add(act2Candidates[Math.Max(0, bestComparison.Act2RouteIndex.Value - 1)].RawRoute);
        }

        var bestMatch = trySimulateMethod.Invoke(analyzer, [bestRouteList]);
        if (bestMatch != null)
        {
            var bestActs = ExtractSimulatedActs(bestMatch);
            Console.WriteLine("bestTrace=" + string.Join(" || ", FlattenSimulatedActs(bestActs)));
        }
    }

    var traceRouteList = (IList)Activator.CreateInstance(routeListType)!;
    traceRouteList.Add(act1Candidates[0].RawRoute);
    if (actualActs.Count >= 2)
    {
        traceRouteList.Add(act2Candidates[0].RawRoute);
    }

    var traceMatch = trySimulateMethod.Invoke(analyzer, [traceRouteList]);
    if (traceMatch != null)
    {
        var traceBaselineActs = ExtractSimulatedActs(traceMatch);
        var traceRawRoutes = new List<object> { act1Candidates[0].RawRoute };
        if (actualActs.Count >= 2)
        {
            traceRawRoutes.Add(act2Candidates[0].RawRoute);
        }

        var shopAwareTrace = new List<string>();
        _ = SimulateWithActualShopState(
            analyzer,
            save,
            dataset,
            traceRawRoutes,
            actualActs,
            traceBaselineActs,
            actPoolMap,
            traceLines: shopAwareTrace);
        Console.WriteLine("shopAwareTrace=" + string.Join(" || ", shopAwareTrace));

        var shopAwareSelfHelpTrace = new List<string>();
        _ = SimulateWithActualShopState(
            analyzer,
            save,
            dataset,
            traceRawRoutes,
            actualActs,
            traceBaselineActs,
            actPoolMap,
            selfHelpExtraRewardDraws: 5,
            traceLines: shopAwareSelfHelpTrace);
        Console.WriteLine("shopAwareTrace+selfHelp5=" + string.Join(" || ", shopAwareSelfHelpTrace));

        var shopAwareAct2Ancient7Trace = new List<string>();
        _ = SimulateWithActualShopState(
            analyzer,
            save,
            dataset,
            traceRawRoutes,
            actualActs,
            traceBaselineActs,
            actPoolMap,
            selfHelpExtraRewardDraws: 0,
            act2AncientExtraRewardDraws: 7,
            traceLines: shopAwareAct2Ancient7Trace);
        Console.WriteLine("shopAwareTrace+act2Ancient7=" + string.Join(" || ", shopAwareAct2Ancient7Trace));

        var actualBossReplayTrace = new List<string>();
        _ = SimulateWithActualShopState(
            analyzer,
            save,
            dataset,
            traceRawRoutes,
            actualActs,
            traceBaselineActs,
            actPoolMap,
            replayActualBossRewards: true,
            traceLines: actualBossReplayTrace);
        Console.WriteLine("shopAwareTrace+actualBossReplay=" + string.Join(" || ", actualBossReplayTrace));
    }
}

object GetAct(int actNumber) =>
    acts.First(act =>
        (int)(act.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(act) ?? 0) == actNumber);

static List<ActualActSnapshot> BuildActualActs(SaveSnapshot save)
{
    var result = new List<ActualActSnapshot>();
    if (save.MapPointHistory == null)
    {
        return result;
    }

    for (var actIndex = 0; actIndex < save.MapPointHistory.Count && actIndex < 2; actIndex++)
    {
        var history = save.MapPointHistory[actIndex] ?? [];
        var rooms = new List<ActualRoomSnapshot>();
        var floor = 0;
        foreach (var point in history)
        {
            var pointType = NormalizeMapPointType(point.MapPointType);
            if (string.Equals(pointType, "Ancient", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pointType, "Boss", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            floor++;
            var room = point.Rooms?.FirstOrDefault();
            var roomType = NormalizeSaveRoomType(room?.RoomType, pointType);
            var eventId = string.Equals(roomType, "Event", StringComparison.OrdinalIgnoreCase)
                ? NormalizeModelId(room?.ModelId)
                : string.Empty;
            rooms.Add(new ActualRoomSnapshot(
                actIndex + 1,
                floor,
                roomType,
                eventId,
                ExtractActualRelics(point, roomType),
                point.PlayerStats?.FirstOrDefault()));
        }

        result.Add(new ActualActSnapshot(actIndex + 1, rooms));
    }

    return result;
}

static string? ResolveActualAct1OpeningRelicId(SaveSnapshot save)
{
    var firstPoint = save.MapPointHistory?
        .FirstOrDefault()?
        .FirstOrDefault();
    var pickedRelic = firstPoint?.PlayerStats?
        .FirstOrDefault()?
        .RelicChoices?
        .FirstOrDefault(choice => choice.WasPicked)?
        .Choice;
    if (string.IsNullOrWhiteSpace(pickedRelic))
    {
        return null;
    }

    return NormalizeRelicId(pickedRelic);
}

static string? ResolveActualAncientRelicId(SaveSnapshot save, int actNumber)
{
    if (actNumber <= 1)
    {
        return ResolveActualAct1OpeningRelicId(save);
    }

    var actPoint = save.MapPointHistory != null && save.MapPointHistory.Count >= actNumber
        ? save.MapPointHistory[actNumber - 1]?.FirstOrDefault()
        : null;
    var pickedRelic = actPoint?.PlayerStats?
        .FirstOrDefault()?
        .RelicChoices?
        .FirstOrDefault(choice => choice.WasPicked)?
        .Choice;
    return string.IsNullOrWhiteSpace(pickedRelic)
        ? null
        : NormalizeRelicId(pickedRelic);
}

static List<SimulatedActSnapshot> ExtractSimulatedActs(object match)
{
    var actsResult = (IEnumerable)(match.GetType().GetProperty("Acts", BindingFlags.Instance | BindingFlags.Public)?.GetValue(match)
        ?? throw new InvalidOperationException("Missing Acts result."));
    return actsResult.Cast<object>()
        .OrderBy(actResult => (int)(actResult.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(actResult) ?? 0))
        .Select(actResult =>
        {
            var actNumber = (int)(actResult.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(actResult) ?? 0);
            var actRooms = (IEnumerable)(actResult.GetType().GetProperty("Rooms", BindingFlags.Instance | BindingFlags.Public)?.GetValue(actResult)
                ?? throw new InvalidOperationException("Missing act rooms."));
            var floor = 0;
            var rooms = actRooms.Cast<object>()
                .Select(room =>
                {
                    floor++;
                    var roomType = room.GetType().GetProperty("RoomType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)?.ToString() ?? string.Empty;
                    var eventId = room.GetType().GetProperty("EventId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)?.ToString() ?? string.Empty;
                    var relicIds = ((IEnumerable)(room.GetType().GetProperty("RelicIds", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)
                        ?? Array.Empty<string>()))
                        .Cast<object>()
                        .Select(item => NormalizeRelicId(item.ToString()))
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                    var displayRelicIds = ((IEnumerable)(room.GetType().GetProperty("DisplayRelicIds", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)
                        ?? Array.Empty<string>()))
                        .Cast<object>()
                        .Select(item => NormalizeRelicId(item.ToString()))
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                    return new SimulatedRoomSnapshot(actNumber, floor, roomType, NormalizeModelId(eventId), relicIds, displayRelicIds);
                })
                .ToList();
            return new SimulatedActSnapshot(actNumber, rooms);
        })
        .ToList();
}

static IReadOnlyList<string> FlattenSimulatedActs(IReadOnlyList<SimulatedActSnapshot> acts)
{
    var lines = new List<string>();
    foreach (var act in acts)
    {
        foreach (var room in act.Rooms)
        {
            lines.Add($"act{act.ActNumber}-floor{room.Floor}:{room.RoomType}:{room.EventId}:{string.Join("/", room.RelicIds)}");
        }
    }

    return lines;
}

void DumpAct1BranchSpace(object analyzer, object act1Route)
{
    var routeSimulatorType = analyzer.GetType();
    var beginActBranchesMethod = routeSimulatorType.GetMethod("BeginActBranches", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing BeginActBranches.");
    var advanceBranchForRoomMethod = routeSimulatorType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .FirstOrDefault(method =>
        {
            if (method.Name != "AdvanceBranchForRoom")
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 3 &&
                   string.Equals(parameters[2].ParameterType.Name, "Sts2GeneratedRouteNode", StringComparison.Ordinal);
        })
        ?? throw new InvalidOperationException("Missing AdvanceBranchForRoom.");
    var resolveRoomTypeMethod = routeSimulatorType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .FirstOrDefault(method =>
        {
            if (method.Name != "ResolveRoomType")
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 3 &&
                   string.Equals(parameters[0].ParameterType.Name, "Sts2GeneratedRouteNode", StringComparison.Ordinal);
        })
        ?? throw new InvalidOperationException("Missing ResolveRoomType.");
    var simulationBranchType = routeSimulatorType.GetNestedType("SimulationBranch", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing SimulationBranch type.");
    var unknownOddsType = routeSimulatorType.DeclaringType?.GetNestedType("UnknownOdds", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing UnknownOdds type.");
    var simulationBranchCreateMethod = simulationBranchType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing SimulationBranch.Create.");

    var simulationModelField = routeSimulatorType.GetField("_simulationModel", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _simulationModel.");
    var requestField = routeSimulatorType.GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _request.");
    var relicFactoryField = routeSimulatorType.GetField("_relicFactory", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _relicFactory.");
    var targetEventsField = routeSimulatorType.GetField("_targetEvents", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _targetEvents.");
    var targetRelicsField = routeSimulatorType.GetField("_targetRelics", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _targetRelics.");

    var request = requestField.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing analyzer request.");
    var simulationModel = simulationModelField.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing analyzer simulation model.");
    var relicFactory = relicFactoryField.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing analyzer relic factory.");
    var targetEvents = (ICollection)(targetEventsField.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing target events."));
    var targetRelics = (ICollection)(targetRelicsField.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing target relics."));

    var requestType = request.GetType();
    var seedValueForUnknown = (uint)(requestType.GetProperty("SeedValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing request seed."));
    var characterForSeed = requestType.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing request character.");
    var playerCount = (int)(requestType.GetProperty("PlayerCount", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing player count."));
    var act1OpeningOption = requestType.GetProperty("Act1OpeningOption", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request);

    var eventStateType = routeSimulatorType.Assembly.GetType("SeedModel.Sts2.Sts2EventProgressState")
        ?? throw new InvalidOperationException("Missing Sts2EventProgressState.");
    var eventStateCreateMethod = eventStateType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing event state Create.");
    var baseEventState = eventStateCreateMethod.Invoke(null, [simulationModel, characterForSeed, playerCount])
        ?? throw new InvalidOperationException("Failed to create event state.");

    var relicFactoryCreateMethod = relicFactory.GetType().GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing relic factory Create.");
    var baseRelicState = relicFactoryCreateMethod.Invoke(relicFactory, null)
        ?? throw new InvalidOperationException("Failed to create relic state.");

    var unknownOdds = Activator.CreateInstance(unknownOddsType, new object[] { new GameRng(seedValueForUnknown, "unknown_map_point") })
        ?? throw new InvalidOperationException("Failed to create UnknownOdds.");
    unknownOddsType.GetMethod("ResetToBase", BindingFlags.Instance | BindingFlags.Public)?.Invoke(unknownOdds, null);

    var eventRng = new GameRng(seedValueForUnknown, "explicit_route_probe");
    var seedBranch = simulationBranchCreateMethod.Invoke(
        null,
        [baseEventState, act1OpeningOption, eventRng, unknownOdds, baseRelicState, targetEvents.Count, targetRelics.Count])
        ?? throw new InvalidOperationException("Failed to create seed branch.");

    var actNumber = (int)(act1Route.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(act1Route)
        ?? throw new InvalidOperationException("Missing act number."));
    var nodes = ((IEnumerable)(act1Route.GetType().GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(act1Route)
        ?? throw new InvalidOperationException("Missing route nodes."))).Cast<object>().ToList();
    var roomTypes = new List<object>(nodes.Count);
    object? previousRoomType = null;
    foreach (var node in nodes)
    {
        var roomType = resolveRoomTypeMethod.Invoke(null, [node, previousRoomType, unknownOdds])
            ?? throw new InvalidOperationException("Failed to resolve room type.");
        roomTypes.Add(roomType);
        previousRoomType = roomType;
    }

    var activeBranches = ((IEnumerable)(beginActBranchesMethod.Invoke(analyzer, [seedBranch, actNumber, null, Array.Empty<string>()])
        ?? throw new InvalidOperationException("Failed to begin act."))).Cast<object>().ToList();
    for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
    {
        var roomBranches = new List<object>();
        foreach (var branch in activeBranches)
        {
            var expanded = (IEnumerable)(advanceBranchForRoomMethod.Invoke(analyzer, [branch, actNumber, nodes[nodeIndex]])
                ?? throw new InvalidOperationException($"Failed to advance branch at node {nodeIndex + 1}."));
            roomBranches.AddRange(expanded.Cast<object>());
        }

        activeBranches = roomBranches;
    }

    Console.WriteLine($"act1BranchDump.count={activeBranches.Count}");
    var summary = activeBranches
        .Select(branch => DescribeAct1Branch(branch))
        .GroupBy(text => text, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Take(20)
        .ToList();
    foreach (var item in summary)
    {
        Console.WriteLine($"act1BranchDump[{item.Count()}]={item.Key}");
    }

    var floor14Counts = activeBranches
        .Select(branch => GetAct1FloorRelics(branch, 14))
        .GroupBy(text => text, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .ToList();
    Console.WriteLine("act1Floor14Counts=" + string.Join(" || ", floor14Counts.Select(group => $"{group.Key}:{group.Count()}")));

    var comboCounts = activeBranches
        .Select(branch => $"f11={GetAct1FloorRelics(branch, 11)};f14={GetAct1FloorRelics(branch, 14)}")
        .GroupBy(text => text, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .ToList();
    Console.WriteLine("act1F11F14Counts=" + string.Join(" || ", comboCounts.Select(group => $"{group.Key}:{group.Count()}")));

    var hasPlanisphere = activeBranches.Any(branch => string.Equals(GetAct1FloorRelics(branch, 14), "PLANISPHERE", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"act1HasFloor14Planisphere={hasPlanisphere}");
}

string DescribeAct1Branch(object branch)
{
    var currentRooms = ((IEnumerable)(branch.GetType().GetProperty("CurrentRooms", BindingFlags.Instance | BindingFlags.Public)?.GetValue(branch)
        ?? throw new InvalidOperationException("Missing CurrentRooms."))).Cast<object>().ToList();
    var eventState = branch.GetType().GetProperty("EventState", BindingFlags.Instance | BindingFlags.Public)?.GetValue(branch)
        ?? throw new InvalidOperationException("Missing EventState.");
    var relicState = branch.GetType().GetProperty("RelicState", BindingFlags.Instance | BindingFlags.Public)?.GetValue(branch)
        ?? throw new InvalidOperationException("Missing RelicState.");

    string RoomDisplayRelicsAt(int floor)
    {
        if (floor <= 0 || floor > currentRooms.Count)
        {
            return "-";
        }

        var room = currentRooms[floor - 1];
        var relics = ((IEnumerable)(room.GetType().GetProperty("DisplayRelicIds", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)
            ?? Array.Empty<string>()))
            .Cast<object>()
            .Select(item => NormalizeRelicId(item.ToString()))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        return relics.Count == 0 ? "-" : string.Join("/", relics);
    }

    var currentGold = eventState.GetType().GetProperty("CurrentGold", BindingFlags.Instance | BindingFlags.Public)?.GetValue(eventState)?.ToString() ?? "?";
    var currentHp = eventState.GetType().GetProperty("CurrentHp", BindingFlags.Instance | BindingFlags.Public)?.GetValue(eventState)?.ToString() ?? "?";
    var rewardsRng = relicState.GetType().GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(relicState);
    var shopsRng = relicState.GetType().GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(relicState);
    var rewardsCounter = rewardsRng?.GetType().GetProperty("Counter", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardsRng)?.ToString() ?? "?";
    var shopsCounter = shopsRng?.GetType().GetProperty("Counter", BindingFlags.Instance | BindingFlags.Public)?.GetValue(shopsRng)?.ToString() ?? "?";
    var rareOffset = relicState.GetType().GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)?.GetValue(relicState)?.ToString() ?? "?";
    var potionChance = relicState.GetType().GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)?.GetValue(relicState)?.ToString() ?? "?";

    return $"shopShown={RoomDisplayRelicsAt(5)};shopBought={GetAct1FloorRelics(branch, 5)};f11={GetAct1FloorRelics(branch, 11)};f14={GetAct1FloorRelics(branch, 14)};gold={currentGold};hp={currentHp};rewards={rewardsCounter};shops={shopsCounter};rare={rareOffset};potion={potionChance}";
}

string GetAct1FloorRelics(object branch, int floor)
{
    var currentRooms = ((IEnumerable)(branch.GetType().GetProperty("CurrentRooms", BindingFlags.Instance | BindingFlags.Public)?.GetValue(branch)
        ?? throw new InvalidOperationException("Missing CurrentRooms."))).Cast<object>().ToList();
    if (floor <= 0 || floor > currentRooms.Count)
    {
        return "-";
    }

    var room = currentRooms[floor - 1];
    var relics = ((IEnumerable)(room.GetType().GetProperty("RelicIds", BindingFlags.Instance | BindingFlags.Public)?.GetValue(room)
        ?? Array.Empty<string>()))
        .Cast<object>()
        .Select(item => NormalizeRelicId(item.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
    return relics.Count == 0 ? "-" : string.Join("/", relics);
}

static List<SimulatedActSnapshot> SimulateWithActualShopState(
    object analyzer,
    SaveSnapshot save,
    NeowOptionDataset dataset,
    IReadOnlyList<object> routes,
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyList<SimulatedActSnapshot> baselineActs,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap,
    int selfHelpExtraRewardDraws = 0,
    int act2AncientExtraRewardDraws = 0,
    bool replayBossRewardCards = false,
    bool replayBossPotionReward = false,
    bool replayActualBossRewards = false,
    List<string>? traceLines = null)
{
    if (!HasAnyApplicableShop(actualActs, baselineActs))
    {
        return baselineActs.ToList();
    }

    var analyzerType = analyzer.GetType();
    var assembly = analyzerType.Assembly;
    var request = analyzerType.GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing _request.");
    var simulationModel = analyzerType.GetField("_simulationModel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing _simulationModel.");
    var relicFactory = analyzerType.GetField("_relicFactory", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing _relicFactory.");
    var ancientByAct = analyzerType.GetField("_ancientByAct", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(analyzer)
        ?? throw new InvalidOperationException("Missing _ancientByAct.");

    var requestType = request.GetType();
    var character = (CharacterId)(requestType.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing request character."));
    var playerCount = (int)(requestType.GetProperty("PlayerCount", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing request playerCount."));
    var seedValue = (uint)(requestType.GetProperty("SeedValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request)
        ?? throw new InvalidOperationException("Missing request seedValue."));
    var act1OpeningOption = requestType.GetProperty("Act1OpeningOption", BindingFlags.Instance | BindingFlags.Public)?.GetValue(request);

    var eventStateType = assembly.GetType("SeedModel.Sts2.Sts2EventProgressState")
        ?? throw new InvalidOperationException("Missing Sts2EventProgressState.");
    var createEventState = eventStateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing event Create.");
    var startAct = eventStateType.GetMethod("StartAct", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing StartAct.");
    var applyAct1OpeningOption = eventStateType.GetMethod("ApplyAct1OpeningOption", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyAct1OpeningOption.");
    var applyNeowStartHeal = eventStateType.GetMethod("ApplyNeowStartHeal", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyNeowStartHeal.");
    var consumeRegularCombat = eventStateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public, null, [typeof(GameRng)], null)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeEliteStop = eventStateType.GetMethod("ConsumeEliteStop", BindingFlags.Instance | BindingFlags.Public, null, [typeof(GameRng)], null)
        ?? throw new InvalidOperationException("Missing ConsumeEliteStop.");
    var consumeBossStop = eventStateType.GetMethod("ConsumeBossStop", BindingFlags.Instance | BindingFlags.Public, null, [typeof(GameRng), typeof(GameRng)], null)
        ?? eventStateType.GetMethod("ConsumeBossStop", BindingFlags.Instance | BindingFlags.Public, null, [typeof(GameRng)], null)
        ?? throw new InvalidOperationException("Missing ConsumeBossStop.");
    var consumeTreasureStopSingle = eventStateType.GetMethod("ConsumeTreasureStop", [typeof(GameRng)])
        ?? throw new InvalidOperationException("Missing ConsumeTreasureStop(GameRng).");
    var consumeTreasureStop = eventStateType.GetMethod("ConsumeTreasureStop", [typeof(GameRng), typeof(GameRng)])
        ?? throw new InvalidOperationException("Missing ConsumeTreasureStop.");
    var applyAncientActStartHeal = eventStateType.GetMethod("ApplyAncientActStartHeal", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyAncientActStartHeal.");
    var applyShownEvent = eventStateType.GetMethod("ApplyShownEvent", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyShownEvent.");
    var applyObtainedRelics = eventStateType.GetMethod("ApplyObtainedRelics", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyObtainedRelics.");
    var getPotionIds = eventStateType.GetMethod("GetPotionIds", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing GetPotionIds.");
    var isTradableRelic = eventStateType.GetMethod("IsTradableRelic", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing IsTradableRelic.");
    var applyGoldSpend = eventStateType.GetMethod("ApplyGoldSpend", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyGoldSpend.");
    var applyLosePotion = eventStateType.GetMethod("ApplyLosePotion", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyLosePotion.");
    var applyLoseTradableRelic = eventStateType.GetMethod("ApplyLoseTradableRelic", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyLoseTradableRelic.");
    var applyHatchRestSite = eventStateType.GetMethod("ApplyHatchRestSite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyHatchRestSite.");
    var gainRelic = eventStateType.GetMethod("GainRelic", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing GainRelic.");
    var gainCard = eventStateType.GetMethod("GainCard", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing GainCard.");
    var gainPotion = eventStateType.GetMethod("GainPotion", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing GainPotion.");
    var currentGoldProperty = eventStateType.GetProperty("CurrentGold", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentGold.");
    var currentHpProperty = eventStateType.GetProperty("CurrentHp", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentHp.");
    var totalFloorProperty = eventStateType.GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TotalFloor.");
    var hasByrdonisEggProperty = eventStateType.GetProperty("HasByrdonisEgg", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing HasByrdonisEgg.");
    var deckField = eventStateType.GetField("_deck", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _deck.");

    var rewardFactoryCreate = relicFactory.GetType().GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing relic factory Create.");
    var rewardRelicState = rewardFactoryCreate.Invoke(relicFactory, null)
        ?? throw new InvalidOperationException("Failed to create rewardRelicState.");
    var rewardRelicStateType = rewardRelicState.GetType();
    var applyRelicAct1OpeningOption = rewardRelicStateType.GetMethod("ApplyAct1OpeningOption", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing relic ApplyAct1OpeningOption.");
    var consumeRewardRng = rewardRelicStateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var obtainMany = rewardRelicStateType.GetMethod("Obtain", [typeof(IEnumerable<string>)])
        ?? throw new InvalidOperationException("Missing Obtain(IEnumerable<string>).");
    var obtainOne = rewardRelicStateType.GetMethod("Obtain", [typeof(string)])
        ?? throw new InvalidOperationException("Missing Obtain(string).");
    var consumeRelicRegularCombat = rewardRelicStateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing relic ConsumeRegularCombat.");
    var showElite = rewardRelicStateType.GetMethod("ShowElite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowElite.");
    var showTreasure = rewardRelicStateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = rewardRelicStateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var cloneRelicState = rewardRelicStateType.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing Clone.");
    var consumeBossRewards = rewardRelicStateType.GetMethod("ConsumeBossRewards", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeBossRewards.");
    var pullEventFrontAndObtain = rewardRelicStateType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .FirstOrDefault(method => string.Equals(method.Name, "PullEventFrontAndObtain", StringComparison.Ordinal) &&
                                  method.GetParameters().Length == 3)
        ?? throw new InvalidOperationException("Missing PullEventFrontAndObtain.");
    var removeOwnedRelic = rewardRelicStateType.GetMethod("RemoveOwnedRelic", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RemoveOwnedRelic.");
    var applyRelicHatchRestSite = rewardRelicStateType.GetMethod("ApplyHatchRestSite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing relic ApplyHatchRestSite.");
    var replayImmediatePickupEffects = rewardRelicStateType.GetMethod("ReplayImmediatePickupEffects", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ReplayImmediatePickupEffects.");
    var ownedRelicsProperty = rewardRelicStateType.GetProperty("OwnedRelics", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing OwnedRelics.");
    var playerBagProperty = rewardRelicStateType.GetProperty("PlayerBag", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PlayerBag.");
    var sharedBagProperty = rewardRelicStateType.GetProperty("SharedBag", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing SharedBag.");
    var rewardsRngProperty = rewardRelicStateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng.");
    var shopsRngProperty = rewardRelicStateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShopsRng.");
    var treasureRngProperty = rewardRelicStateType.GetProperty("TreasureRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TreasureRng.");
    var cardRareOffsetProperty = rewardRelicStateType.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRareOffset.");
    var potionChanceProperty = rewardRelicStateType.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionChance.");
    var rewardModelProperty = rewardRelicStateType.GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public);
    var replayCardReward = replayBossRewardCards
        ? rewardRelicStateType.GetMethod("ReplayCardReward", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing ReplayCardReward.")
        : null;
    var rollPotionRewardChanceMethod = replayBossPotionReward
        ? rewardRelicStateType.GetMethod("RollPotionRewardChance", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing RollPotionRewardChance.")
        : null;
    var rollPotionRewardMethod = replayBossPotionReward
        ? rewardRelicStateType.GetMethod("RollPotionReward", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing RollPotionReward.")
        : null;
    var bossEncounterOdds = CardRarityOddsType.BossEncounter;

    var routeRoomTypeType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RouteRoomType")
        ?? throw new InvalidOperationException("Missing RouteRoomType.");
    var resolveRoomType = analyzerType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .FirstOrDefault(method =>
        {
            if (method.Name != "ResolveRoomType")
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 3 &&
                   string.Equals(parameters[0].ParameterType.Name, "Sts2GeneratedRouteNode", StringComparison.Ordinal);
        })
        ?? throw new InvalidOperationException("Missing ResolveRoomType.");
    var applyAncientImmediateEffects = analyzerType.GetMethod("ApplyAncientImmediateEffects", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ApplyAncientImmediateEffects.");
    var resolveEventRelicEffects = analyzerType.GetMethod("ResolveEventRelicEffects", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ResolveEventRelicEffects.");
    var unknownOddsType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+UnknownOdds")
        ?? throw new InvalidOperationException("Missing UnknownOdds.");
    var unknownOdds = Activator.CreateInstance(
            unknownOddsType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [new GameRng(seedValue, "unknown_map_point")],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create UnknownOdds.");
    var resetUnknownOdds = unknownOddsType.GetMethod("ResetToBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing UnknownOdds.ResetToBase.");
    var unknownCurrentOddsField = unknownOddsType.GetField("_currentOdds", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing UnknownOdds._currentOdds.");
    var unknownRollMethod = unknownOddsType.GetMethod("Roll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing UnknownOdds.Roll.");

    var pullEngineType = assembly.GetType("SeedModel.Sts2.Sts2EventPullEngine")
        ?? throw new InvalidOperationException("Missing Sts2EventPullEngine.");
    var ensureNextEventIsValid = pullEngineType.GetMethod("EnsureNextEventIsValid", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing EnsureNextEventIsValid.");
    var peekNextAllowedEvent = pullEngineType.GetMethod("PeekNextAllowedEvent", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PeekNextAllowedEvent.");
    var consumeShownEvent = pullEngineType.GetMethod("ConsumeShownEvent", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeShownEvent.");

    var eventState = createEventState.Invoke(null, [simulationModel, character, playerCount])
        ?? throw new InvalidOperationException("Failed to create eventState.");
    applyNeowStartHeal.Invoke(eventState, null);
    applyAct1OpeningOption.Invoke(eventState, [act1OpeningOption]);
    applyRelicAct1OpeningOption.Invoke(rewardRelicState, [act1OpeningOption]);
    var trackedActualPotionIds = new List<string>();
    var eventContainsCardMethod = eventState.GetType().GetMethod("ContainsCard", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing eventState.ContainsCard.");
    var rewardOwnedRelicsProperty = rewardRelicState.GetType().GetProperty("OwnedRelics", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing rewardRelicState.OwnedRelics.");
    var eventRng = new GameRng(seedValue, "explicit_route_probe");
    var matchedRelicTargets = Array.Empty<bool>();
    var simulatedActs = new List<SimulatedActSnapshot>();

    foreach (var route in routes.OrderBy(candidate => (int)(candidate.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(candidate) ?? 0)))
    {
        resetUnknownOdds.Invoke(unknownOdds, null);
        object? previousRoomType = null;
        var actNumber = (int)(route.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)?.GetValue(route) ?? 0);
        var actualAct = actualActs.FirstOrDefault(act => act.ActNumber == actNumber);
        var baselineAct = baselineActs.FirstOrDefault(act => act.ActNumber == actNumber);
        var rooms = new List<SimulatedRoomSnapshot>();

        var (ancientId, ancientRelicOptions) = GetAncientInfoForAct(ancientByAct, actNumber);
        var actualAncientRelic = ResolveActualAncientRelicId(save, actNumber);
        var chosenAncientRelic = ancientRelicOptions.FirstOrDefault(relicId => string.Equals(relicId, actualAncientRelic, StringComparison.OrdinalIgnoreCase))
            ?? actualAncientRelic
            ?? ancientRelicOptions.FirstOrDefault(static relicId => !string.IsNullOrWhiteSpace(relicId))
            ?? string.Empty;
        var chosenAncientRelics = string.IsNullOrWhiteSpace(chosenAncientRelic) ? Array.Empty<string>() : [chosenAncientRelic];
        startAct.Invoke(eventState, [actNumber, 1]);
        totalFloorProperty.SetValue(eventState, (int)totalFloorProperty.GetValue(eventState)! + 1);
        if (actNumber > 1)
        {
            applyAncientActStartHeal.Invoke(eventState, null);
        }
        if (chosenAncientRelics.Length > 0)
        {
            obtainMany.Invoke(rewardRelicState, [chosenAncientRelics]);
            applyAncientImmediateEffects.Invoke(null, [actNumber, chosenAncientRelic, rewardRelicState, eventState]);
            if (actNumber == 2 && act2AncientExtraRewardDraws > 0)
            {
                consumeRewardRng.Invoke(rewardRelicState, [act2AncientExtraRewardDraws]);
            }
        }

        if (traceLines != null)
        {
            var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(rewardRelicState)
                ?? throw new InvalidOperationException("Missing rewards rng."));
            var shopsRng = (GameRng)(shopsRngProperty.GetValue(rewardRelicState)
                ?? throw new InvalidOperationException("Missing shops rng."));
            var treasureRng = (GameRng)(treasureRngProperty.GetValue(rewardRelicState)
                ?? throw new InvalidOperationException("Missing treasure rng."));
            var playerBag = playerBagProperty.GetValue(rewardRelicState)
                ?? throw new InvalidOperationException("Missing player bag.");
            traceLines.Add(
                $"act{actNumber}-start" +
                $"|ancient={chosenAncientRelic}" +
                $"|gold={currentGoldProperty.GetValue(eventState)}" +
                $"|hp={currentHpProperty.GetValue(eventState)}" +
                $"|potions={string.Join("/", trackedActualPotionIds)}" +
                $"|rewards={rewardsRng.Counter}" +
                $"|shops={shopsRng.Counter}" +
                $"|treasure={treasureRng.Counter}" +
                $"|rareOffset={cardRareOffsetProperty.GetValue(rewardRelicState)}" +
                $"|potionChance={potionChanceProperty.GetValue(rewardRelicState)}" +
                $"|u={DescribeBucketHead(playerBag, "Uncommon")}" +
                $"|c={DescribeBucketHead(playerBag, "Common")}");
        }

        var nodes = ((IEnumerable)(route.GetType().GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(route)
            ?? throw new InvalidOperationException("Missing route nodes."))).Cast<object>().ToList();
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
            var node = nodes[nodeIndex];
            object roomTypeObject;
            var pointType = node.GetType().GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node)?.ToString();
            if (!string.Equals(pointType, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                roomTypeObject = resolveRoomType.Invoke(null, [node, previousRoomType, unknownOdds])
                    ?? throw new InvalidOperationException("Failed to resolve room type.");
            }
            else
            {
                var blacklistType = typeof(HashSet<>).MakeGenericType(routeRoomTypeType);
                var blacklist = Activator.CreateInstance(blacklistType)
                    ?? throw new InvalidOperationException("Failed to create room-type blacklist.");
                var addToBlacklist = blacklistType.GetMethod("Add")
                    ?? throw new InvalidOperationException("Missing blacklist Add.");

                if (previousRoomType?.ToString() == "Shop")
                {
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Shop")]);
                }

                var childPointTypes = ((IEnumerable)(node.GetType().GetProperty("ChildPointTypes", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node)
                    ?? Array.Empty<string>()))
                    .Cast<object>()
                    .Select(item => item?.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();
                if (childPointTypes.Count > 0 &&
                    childPointTypes.All(type => string.Equals(type, "Shop", StringComparison.OrdinalIgnoreCase)))
                {
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Shop")]);
                }

                var ownedRelics = (IEnumerable)(rewardOwnedRelicsProperty.GetValue(rewardRelicState)
                    ?? Array.Empty<string>());
                var ownedRelicIds = ownedRelics.Cast<object>().Select(item => item?.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (ownedRelicIds.Contains("JUZU_BRACELET"))
                {
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Monster")]);
                }

                var hasLanternKey = (bool)(eventContainsCardMethod.Invoke(eventState, ["LANTERN_KEY"]) ?? false);
                if ((actNumber == 3 && hasLanternKey) ||
                    string.Equals(chosenAncientRelic, "GOLDEN_COMPASS", StringComparison.OrdinalIgnoreCase))
                {
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Monster")]);
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Elite")]);
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Treasure")]);
                    addToBlacklist.Invoke(blacklist, [Enum.Parse(routeRoomTypeType, "Shop")]);
                }

                roomTypeObject = unknownRollMethod.Invoke(unknownOdds, [blacklist])
                    ?? throw new InvalidOperationException("Failed to roll unknown room type.");
            }

            previousRoomType = roomTypeObject;
            var roomType = roomTypeObject.ToString() ?? string.Empty;
            totalFloorProperty.SetValue(eventState, (int)totalFloorProperty.GetValue(eventState)! + 1);

            string eventId = string.Empty;
            var relicIds = new List<string>();
            var displayRelicIds = new List<string>();
            switch (roomType)
            {
                case "Event":
                    if (actPoolMap.TryGetValue(actNumber, out var pool) && pool.Count > 0)
                    {
                        ensureNextEventIsValid.Invoke(null, [pool, eventState]);
                        eventId = NormalizeModelId(peekNextAllowedEvent.Invoke(null, [pool, eventState])?.ToString());
                        if (!string.IsNullOrWhiteSpace(eventId))
                        {
                            consumeShownEvent.Invoke(null, [pool, eventId, eventState]);
                            var eventOutcome = resolveEventRelicEffects.Invoke(analyzer, [actNumber, eventId, matchedRelicTargets, rewardRelicState, eventState])
                                ?? throw new InvalidOperationException("Failed to resolve event relic effects.");
                            rewardRelicState = GetNamedValue(eventOutcome, "State")
                                ?? GetNamedValue(eventOutcome, "Item1")
                                ?? throw new InvalidOperationException("Missing event outcome state.");
                            var eventRelics = ((IEnumerable)(GetNamedValue(eventOutcome, "Relics")
                                ?? GetNamedValue(eventOutcome, "Item2")
                                ?? Array.Empty<string>()))
                                .Cast<object>()
                                .Select(item => NormalizeRelicId(item.ToString()))
                                .Where(item => !string.IsNullOrWhiteSpace(item))
                                .ToList();
                            relicIds.AddRange(eventRelics);
                            if (eventRelics.Count > 0)
                            {
                                applyObtainedRelics.Invoke(eventState, [eventRelics]);
                            }
                            var replayedEventRelics = ApplyActualEventState(
                                actualAct != null && nodeIndex < actualAct.Rooms.Count ? actualAct.Rooms[nodeIndex].SourceStats : null,
                                actNumber,
                                eventId,
                                eventState,
                                rewardRelicState,
                                eventRng,
                                applyShownEvent,
                                getPotionIds,
                                isTradableRelic,
                                applyGoldSpend,
                                applyLosePotion,
                                applyLoseTradableRelic,
                                deckField,
                                gainPotion,
                                consumeRewardRng,
                                ownedRelicsProperty,
                                pullEventFrontAndObtain,
                                removeOwnedRelic);
                            relicIds.AddRange(replayedEventRelics);
                            if (replayedEventRelics.Count > 0)
                            {
                                applyObtainedRelics.Invoke(eventState, [replayedEventRelics]);
                            }

                            if (selfHelpExtraRewardDraws > 0 &&
                                string.Equals(NormalizeModelId(eventId), "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase))
                            {
                                consumeRewardRng.Invoke(rewardRelicState, [selfHelpExtraRewardDraws]);
                            }
                        }
                    }
                    break;
                case "Monster":
                    consumeRegularCombat.Invoke(eventState, [eventRng]);
                    consumeRelicRegularCombat.Invoke(rewardRelicState, null);
                    break;
                case "Elite":
                    consumeEliteStop.Invoke(eventState, [eventRng]);
                    relicIds.AddRange(ExtractRelicList(showElite.Invoke(rewardRelicState, [actNumber])));
                    if (relicIds.Count > 0)
                    {
                        applyObtainedRelics.Invoke(eventState, [relicIds]);
                    }
                    displayRelicIds.AddRange(relicIds);
                    break;
                case "Treasure":
                    consumeTreasureStopSingle.Invoke(eventState, [eventRng]);
                    relicIds.AddRange(ExtractRelicList(showTreasure.Invoke(rewardRelicState, [actNumber])));
                    if (relicIds.Count > 0)
                    {
                        applyObtainedRelics.Invoke(eventState, [relicIds]);
                    }
                    displayRelicIds.AddRange(relicIds);
                    if (actualAct != null && nodeIndex < actualAct.Rooms.Count)
                    {
                        var treasureStats = actualAct.Rooms[nodeIndex].SourceStats;
                        if (treasureStats?.CurrentGold is int treasureGold)
                        {
                            SetPropertyValue(currentGoldProperty, eventState, treasureGold);
                        }

                        if (treasureStats?.CurrentHp is int treasureHp)
                        {
                            SetPropertyValue(currentHpProperty, eventState, treasureHp);
                        }
                    }
                    break;
                case "Shop":
                    relicIds.AddRange(ExtractRelicList(showShop.Invoke(rewardRelicState, [actNumber])));
                    displayRelicIds.AddRange(relicIds);
                    if (actualAct != null &&
                        baselineAct != null &&
                        nodeIndex < actualAct.Rooms.Count &&
                        nodeIndex < baselineAct.Rooms.Count &&
                        CanApplyActualShopState(actualAct.Rooms[nodeIndex], baselineAct.Rooms[nodeIndex]))
                    {
                        var shopCardSnapshot = CaptureShopCardSnapshot(rewardRelicState, dataset);
                        ApplyActualShopState(
                            actualAct.Rooms[nodeIndex].SourceStats,
                            dataset,
                            eventState,
                            rewardRelicState,
                            currentGoldProperty,
                            currentHpProperty,
                            deckField,
                            gainCard,
                            gainRelic,
                            obtainOne,
                            ownedRelicsProperty,
                            playerBagProperty,
                            sharedBagProperty,
                            replayImmediatePickupEffects,
                            shopCardSnapshot);
                    }
                    break;
                case "RestSite":
                    var hasEgg = (bool)(hasByrdonisEggProperty.GetValue(eventState) ?? false);
                    var ownedRelics = (IEnumerable<string>)ownedRelicsProperty.GetValue(rewardRelicState)!;
                    if (hasEgg && !ownedRelics.Contains("BYRDPIP", StringComparer.OrdinalIgnoreCase))
                    {
                        applyHatchRestSite.Invoke(eventState, null);
                        applyRelicHatchRestSite.Invoke(rewardRelicState, null);
                        relicIds.Add("BYRDPIP");
                        displayRelicIds.Add("BYRDPIP");
                    }
                    break;
            }

            if (displayRelicIds.Count == 0 && relicIds.Count > 0)
            {
                displayRelicIds.AddRange(relicIds);
            }

            if (actualAct != null && nodeIndex < actualAct.Rooms.Count)
            {
                ApplyActualPotionState(
                    actualAct.Rooms[nodeIndex].SourceStats,
                    trackedActualPotionIds,
                    eventState,
                    getPotionIds,
                    applyLosePotion,
                    gainPotion);
            }

            if (traceLines != null)
            {
                var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(rewardRelicState)
                    ?? throw new InvalidOperationException("Missing rewards rng."));
                var shopsRng = (GameRng)(shopsRngProperty.GetValue(rewardRelicState)
                    ?? throw new InvalidOperationException("Missing shops rng."));
                var treasureRng = (GameRng)(treasureRngProperty.GetValue(rewardRelicState)
                    ?? throw new InvalidOperationException("Missing treasure rng."));
                var potionIds = ((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!)
                    .Select(NormalizeModelId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();
                var playerBag = playerBagProperty.GetValue(rewardRelicState)
                    ?? throw new InvalidOperationException("Missing player bag.");
                var actualRoom = actualAct != null && nodeIndex < actualAct.Rooms.Count
                    ? actualAct.Rooms[nodeIndex]
                    : null;
                traceLines.Add(
                    $"act{actNumber}-floor{nodeIndex + 1}:{roomType}" +
                    $"{(string.IsNullOrWhiteSpace(eventId) ? string.Empty : $":{NormalizeModelId(eventId)}")}" +
                    $"|actual={actualRoom?.RoomType ?? "-"}:{actualRoom?.EventId ?? "-"}" +
                    $"|relics={string.Join("/", displayRelicIds)}" +
                    $"|gold={currentGoldProperty.GetValue(eventState)}" +
                    $"|hp={currentHpProperty.GetValue(eventState)}" +
                    $"|potions={string.Join("/", potionIds)}" +
                    $"|rewards={rewardsRng.Counter}" +
                    $"|shops={shopsRng.Counter}" +
                    $"|treasure={treasureRng.Counter}" +
                    $"|rareOffset={cardRareOffsetProperty.GetValue(rewardRelicState)}" +
                    $"|potionChance={potionChanceProperty.GetValue(rewardRelicState)}" +
                    $"|unknown={DescribeUnknownOdds(unknownCurrentOddsField.GetValue(unknownOdds))}" +
                    $"|u={DescribeBucketHead(playerBag, "Uncommon")}" +
                    $"|c={DescribeBucketHead(playerBag, "Common")}");
            }

            rooms.Add(new SimulatedRoomSnapshot(actNumber, nodeIndex + 1, roomType, NormalizeModelId(eventId), relicIds, displayRelicIds));
        }

        simulatedActs.Add(new SimulatedActSnapshot(actNumber, rooms, ancientId, chosenAncientRelics));
        if (actNumber < routes.Count)
        {
            var bossStats = replayActualBossRewards
                ? GetBossPointStats(save, actNumber)
                : null;
            var bossCardIdsBefore = GetDeckCardIds(deckField, eventState);
            var bossPotionIdsBefore = ((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!).ToList();
            var rewardsRngForBoss = (GameRng)(rewardsRngProperty.GetValue(rewardRelicState)
                ?? throw new InvalidOperationException("Missing rewards rng."));
            var bossRewardsRng = new GameRng(rewardsRngForBoss.Seed, rewardsRngForBoss.Counter);
            var consumeBossParameters = consumeBossStop.GetParameters().Length == 2
                ? new object?[] { eventRng, bossRewardsRng }
                : [eventRng];
            consumeBossStop.Invoke(eventState, consumeBossParameters);
            if (replayActualBossRewards)
            {
                consumeBossRewards.Invoke(rewardRelicState, null);
                ReplayActualBossRewards(
                    bossStats,
                    eventState,
                    deckField,
                    getPotionIds,
                    applyLosePotion,
                    gainCard,
                    gainPotion,
                    currentGoldProperty,
                    currentHpProperty,
                    bossCardIdsBefore,
                    bossPotionIdsBefore);
            }
            else
            {
                if (replayBossPotionReward &&
                    (bool)(rollPotionRewardChanceMethod!.Invoke(rewardRelicState, [false]) ?? false))
                {
                    rollPotionRewardMethod!.Invoke(rewardRelicState, null);
                }

                if (replayBossRewardCards)
                {
                    var rewardModel = rewardModelProperty?.GetValue(rewardRelicState)
                        ?? throw new InvalidOperationException("Missing reward model.");
                    var characterCardPool = rewardModel.GetType().GetMethod("GetCharacterCardPool", BindingFlags.Instance | BindingFlags.Public)
                        ?.Invoke(rewardModel, [false])
                        ?? throw new InvalidOperationException("Missing GetCharacterCardPool.");
                    replayCardReward!.Invoke(
                        rewardRelicState,
                        [characterCardPool, bossEncounterOdds, 3, null, true]);
                }
            }
        }
    }

    return simulatedActs;
}

static string FindMatchingAct2AncientRewardOffsets(
    object analyzer,
    SaveSnapshot save,
    NeowOptionDataset dataset,
    IReadOnlyDictionary<int, List<RouteCandidate>> matchedRoutesByAct,
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap)
{
    if (actualActs.Count < 2 ||
        !matchedRoutesByAct.TryGetValue(1, out var act1Candidates) ||
        act1Candidates.Count == 0 ||
        !matchedRoutesByAct.TryGetValue(2, out var act2Candidates) ||
        act2Candidates.Count == 0)
    {
        return "no-act2-probe";
    }

    var analyzerType = analyzer.GetType();
    var trySimulateMethod = analyzerType.GetMethod("TrySimulate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing TrySimulate.");
    var routeListType = typeof(List<>).MakeGenericType(act1Candidates[0].RawRoute.GetType());
    var rawRouteListForTrySimulate = (IList)(Activator.CreateInstance(routeListType)
        ?? throw new InvalidOperationException("Failed to create route list."));
    rawRouteListForTrySimulate.Add(act1Candidates[0].RawRoute);
    rawRouteListForTrySimulate.Add(act2Candidates[0].RawRoute);

    var baselineMatch = trySimulateMethod.Invoke(analyzer, [rawRouteListForTrySimulate]);
    if (baselineMatch == null)
    {
        return "no-baseline-match";
    }

    var baselineActs = ExtractSimulatedActs(baselineMatch);
    var rawRoutes = new List<object> { act1Candidates[0].RawRoute, act2Candidates[0].RawRoute };
    var actualAct2Shop = actualActs[1].Rooms.FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase));
    if (actualAct2Shop == null)
    {
        return "no-act2-shop";
    }

    var actualSet = new HashSet<string>(actualAct2Shop.RelicIds, StringComparer.OrdinalIgnoreCase);
    var results = new List<string>();
    for (var extraRewards = 0; extraRewards <= 24; extraRewards++)
    {
        var simulatedActs = SimulateWithActualShopState(
            analyzer,
            save,
            dataset,
            rawRoutes,
            actualActs,
            baselineActs,
            actPoolMap,
            selfHelpExtraRewardDraws: 0,
            act2AncientExtraRewardDraws: extraRewards);
        var simulatedAct2Shop = simulatedActs
            .FirstOrDefault(act => act.ActNumber == 2)?
            .Rooms.FirstOrDefault(room => room.Floor == actualAct2Shop.Floor);
        if (simulatedAct2Shop == null)
        {
            continue;
        }

        var overlap = simulatedAct2Shop.DisplayRelicIds.Count(relicId => actualSet.Contains(relicId));
        if (overlap >= 2)
        {
            var comparison = CompareAgainstSave(actualActs, simulatedActs, 1, 1, ignoreShopRelicMismatches: false);
            results.Add($"act2AncientReward+{extraRewards}:overlap={overlap},score={comparison.Score},shop={string.Join("/", simulatedAct2Shop.DisplayRelicIds)}");
        }
    }

    return results.Count > 0
        ? string.Join(" || ", results)
        : "no-close-match-0..24";
}

static string FindMatchingBossRewardReplayOffsets(
    object analyzer,
    SaveSnapshot save,
    NeowOptionDataset dataset,
    IReadOnlyDictionary<int, List<RouteCandidate>> matchedRoutesByAct,
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap)
{
    if (actualActs.Count < 2 ||
        !matchedRoutesByAct.TryGetValue(1, out var act1Candidates) ||
        act1Candidates.Count == 0 ||
        !matchedRoutesByAct.TryGetValue(2, out var act2Candidates) ||
        act2Candidates.Count == 0)
    {
        return "no-boss-probe";
    }

    var analyzerType = analyzer.GetType();
    var trySimulateMethod = analyzerType.GetMethod("TrySimulate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing TrySimulate.");
    var routeListType = typeof(List<>).MakeGenericType(act1Candidates[0].RawRoute.GetType());
    var rawRouteListForTrySimulate = (IList)(Activator.CreateInstance(routeListType)
        ?? throw new InvalidOperationException("Failed to create route list."));
    rawRouteListForTrySimulate.Add(act1Candidates[0].RawRoute);
    rawRouteListForTrySimulate.Add(act2Candidates[0].RawRoute);

    var baselineMatch = trySimulateMethod.Invoke(analyzer, [rawRouteListForTrySimulate]);
    if (baselineMatch == null)
    {
        return "no-baseline-match";
    }

    var baselineActs = ExtractSimulatedActs(baselineMatch);
    var rawRoutes = new List<object> { act1Candidates[0].RawRoute, act2Candidates[0].RawRoute };
    var actualAct2Shop = actualActs[1].Rooms.FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase));
    var actualShopSet = actualAct2Shop == null
        ? null
        : new HashSet<string>(actualAct2Shop.RelicIds, StringComparer.OrdinalIgnoreCase);

    var results = new List<string>();
    foreach (var replayBossPotionReward in new[] { false, true })
    {
        foreach (var replayBossRewardCards in new[] { false, true })
        {
            var simulatedActs = SimulateWithActualShopState(
                analyzer,
                save,
                dataset,
                rawRoutes,
                actualActs,
                baselineActs,
                actPoolMap,
                selfHelpExtraRewardDraws: 0,
                act2AncientExtraRewardDraws: 0,
                replayBossRewardCards: replayBossRewardCards,
                replayBossPotionReward: replayBossPotionReward);
            var comparison = CompareAgainstSave(actualActs, simulatedActs, 1, 1, ignoreShopRelicMismatches: false);
            var simulatedAct2Shop = simulatedActs
                .FirstOrDefault(act => act.ActNumber == 2)?
                .Rooms.FirstOrDefault(room => actualAct2Shop != null && room.Floor == actualAct2Shop.Floor);
            var overlap = actualShopSet == null || simulatedAct2Shop == null
                ? -1
                : simulatedAct2Shop.DisplayRelicIds.Count(relicId => actualShopSet.Contains(relicId));
            results.Add(
                $"bossPotion={(replayBossPotionReward ? 1 : 0)},bossCards={(replayBossRewardCards ? 1 : 0)}:" +
                $"score={comparison.Score},overlap={overlap},shop={string.Join("/", simulatedAct2Shop?.DisplayRelicIds ?? [])}");
        }
    }

    return string.Join(" || ", results);
}

static string FindBestCombinedReplayProbe(
    object analyzer,
    SaveSnapshot save,
    NeowOptionDataset dataset,
    IReadOnlyDictionary<int, List<RouteCandidate>> matchedRoutesByAct,
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap)
{
    if (actualActs.Count < 2 ||
        !matchedRoutesByAct.TryGetValue(1, out var act1Candidates) ||
        act1Candidates.Count == 0 ||
        !matchedRoutesByAct.TryGetValue(2, out var act2Candidates) ||
        act2Candidates.Count == 0)
    {
        return "no-combined-probe";
    }

    var analyzerType = analyzer.GetType();
    var trySimulateMethod = analyzerType.GetMethod("TrySimulate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing TrySimulate.");
    var routeListType = typeof(List<>).MakeGenericType(act1Candidates[0].RawRoute.GetType());
    var rawRouteListForTrySimulate = (IList)(Activator.CreateInstance(routeListType)
        ?? throw new InvalidOperationException("Failed to create route list."));
    rawRouteListForTrySimulate.Add(act1Candidates[0].RawRoute);
    rawRouteListForTrySimulate.Add(act2Candidates[0].RawRoute);

    var baselineMatch = trySimulateMethod.Invoke(analyzer, [rawRouteListForTrySimulate]);
    if (baselineMatch == null)
    {
        return "no-baseline-match";
    }

    var baselineActs = ExtractSimulatedActs(baselineMatch);
    var rawRoutes = new List<object> { act1Candidates[0].RawRoute, act2Candidates[0].RawRoute };
    var best = default((int Score, bool BossPotion, bool BossCards, int AncientExtra, List<string> Mismatches, SimulatedRoomSnapshot? Act2Shop));
    best.Score = int.MaxValue;

    foreach (var replayBossPotionReward in new[] { false, true })
    {
        foreach (var replayBossRewardCards in new[] { false, true })
        {
            for (var extraRewards = 0; extraRewards <= 24; extraRewards++)
            {
                var simulatedActs = SimulateWithActualShopState(
                    analyzer,
                    save,
                    dataset,
                    rawRoutes,
                    actualActs,
                    baselineActs,
                    actPoolMap,
                    selfHelpExtraRewardDraws: 0,
                    act2AncientExtraRewardDraws: extraRewards,
                    replayBossRewardCards: replayBossRewardCards,
                    replayBossPotionReward: replayBossPotionReward);
                var comparison = CompareAgainstSave(actualActs, simulatedActs, 1, 1, ignoreShopRelicMismatches: false);
                if (comparison.Score >= best.Score)
                {
                    continue;
                }

                best = (
                    comparison.Score,
                    replayBossPotionReward,
                    replayBossRewardCards,
                    extraRewards,
                    comparison.Mismatches.Take(8).ToList(),
                    simulatedActs
                        .FirstOrDefault(act => act.ActNumber == 2)?
                        .Rooms.FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase) && room.Floor == 2));
            }
        }
    }

    if (best.Score == int.MaxValue)
    {
        return "no-combined-match";
    }

    return
        $"score={best.Score},bossPotion={(best.BossPotion ? 1 : 0)},bossCards={(best.BossCards ? 1 : 0)},act2AncientReward+{best.AncientExtra}," +
        $"shop={string.Join("/", best.Act2Shop?.DisplayRelicIds ?? [])}," +
        $"mismatches={string.Join(" || ", best.Mismatches)}";
}

static string FindActualBossReplayProbe(
    object analyzer,
    SaveSnapshot save,
    NeowOptionDataset dataset,
    IReadOnlyDictionary<int, List<RouteCandidate>> matchedRoutesByAct,
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap)
{
    if (actualActs.Count < 2 ||
        !matchedRoutesByAct.TryGetValue(1, out var act1Candidates) ||
        act1Candidates.Count == 0 ||
        !matchedRoutesByAct.TryGetValue(2, out var act2Candidates) ||
        act2Candidates.Count == 0)
    {
        return "no-actual-boss-probe";
    }

    var analyzerType = analyzer.GetType();
    var trySimulateMethod = analyzerType.GetMethod("TrySimulate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing TrySimulate.");
    var routeListType = typeof(List<>).MakeGenericType(act1Candidates[0].RawRoute.GetType());
    var rawRouteListForTrySimulate = (IList)(Activator.CreateInstance(routeListType)
        ?? throw new InvalidOperationException("Failed to create route list."));
    rawRouteListForTrySimulate.Add(act1Candidates[0].RawRoute);
    rawRouteListForTrySimulate.Add(act2Candidates[0].RawRoute);

    var baselineMatch = trySimulateMethod.Invoke(analyzer, [rawRouteListForTrySimulate]);
    if (baselineMatch == null)
    {
        return "no-baseline-match";
    }

    var baselineActs = ExtractSimulatedActs(baselineMatch);
    var rawRoutes = new List<object> { act1Candidates[0].RawRoute, act2Candidates[0].RawRoute };
    var simulatedActs = SimulateWithActualShopState(
        analyzer,
        save,
        dataset,
        rawRoutes,
        actualActs,
        baselineActs,
        actPoolMap,
        replayActualBossRewards: true);
    var comparison = CompareAgainstSave(actualActs, simulatedActs, 1, 1, ignoreShopRelicMismatches: false);
    var act2Shop = simulatedActs
        .FirstOrDefault(act => act.ActNumber == 2)?
        .Rooms.FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase));
    return
        $"score={comparison.Score},shop={string.Join("/", act2Shop?.DisplayRelicIds ?? [])},mismatches={string.Join(" || ", comparison.Mismatches.Take(6))}";
}

static SavePlayerStats? GetBossPointStats(SaveSnapshot save, int actNumber)
{
    if (save.MapPointHistory == null ||
        actNumber < 1 ||
        actNumber > save.MapPointHistory.Count)
    {
        return null;
    }

    var history = save.MapPointHistory[actNumber - 1] ?? [];
    return history
        .FirstOrDefault(point => string.Equals(NormalizeMapPointType(point.MapPointType), "Boss", StringComparison.OrdinalIgnoreCase))
        ?.PlayerStats?
        .FirstOrDefault();
}

static void ReplayActualBossRewards(
    SavePlayerStats? bossStats,
    object eventState,
    FieldInfo deckField,
    MethodInfo getPotionIds,
    MethodInfo applyLosePotion,
    MethodInfo gainCard,
    MethodInfo gainPotion,
    PropertyInfo currentGoldProperty,
    PropertyInfo currentHpProperty,
    IReadOnlyList<string> deckCardIdsBefore,
    IReadOnlyList<string> potionIdsBefore)
{
    if (bossStats == null)
    {
        return;
    }

    var deckCardIdsAfter = GetDeckCardIds(deckField, eventState);
    var actualBossCard = (bossStats.CardChoices ?? [])
        .FirstOrDefault(choice => choice.WasPicked)?
        .Card?
        .Id;
    var addedBossCard = deckCardIdsAfter
        .Skip(deckCardIdsBefore.Count)
        .LastOrDefault();
    if (!string.IsNullOrWhiteSpace(addedBossCard) &&
        !string.Equals(NormalizeModelId(addedBossCard), NormalizeModelId(actualBossCard), StringComparison.OrdinalIgnoreCase))
    {
        RemoveDeckCardById(deckField, eventState, addedBossCard);
    }

    if (!string.IsNullOrWhiteSpace(actualBossCard) &&
        !GetDeckCardIds(deckField, eventState).Contains(NormalizeModelId(actualBossCard), StringComparer.OrdinalIgnoreCase))
    {
        gainCard.Invoke(eventState, [actualBossCard]);
    }

    var potionIdsAfter = ((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!).ToList();
    var actualBossPotion = (bossStats.PotionChoices ?? [])
        .FirstOrDefault(choice => choice.WasPicked)?
        .Choice;
    var addedBossPotion = potionIdsAfter
        .Skip(potionIdsBefore.Count)
        .LastOrDefault();
    if (!string.IsNullOrWhiteSpace(addedBossPotion) &&
        !string.Equals(NormalizeModelId(addedBossPotion), NormalizeModelId(actualBossPotion), StringComparison.OrdinalIgnoreCase))
    {
        applyLosePotion.Invoke(eventState, [addedBossPotion]);
    }

    if (!string.IsNullOrWhiteSpace(actualBossPotion) &&
        !((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!).Contains(NormalizeModelId(actualBossPotion), StringComparer.OrdinalIgnoreCase))
    {
        gainPotion.Invoke(eventState, [actualBossPotion]);
    }

    if (bossStats.CurrentGold.HasValue)
    {
        SetPropertyValue(currentGoldProperty, eventState, bossStats.CurrentGold.Value);
    }

    if (bossStats.CurrentHp.HasValue)
    {
        SetPropertyValue(currentHpProperty, eventState, bossStats.CurrentHp.Value);
    }
}

static void ApplyActualPotionState(
    SavePlayerStats? stats,
    List<string> trackedPotionIds,
    object eventState,
    MethodInfo getPotionIds,
    MethodInfo applyLosePotion,
    MethodInfo gainPotion)
{
    ArgumentNullException.ThrowIfNull(trackedPotionIds);
    ArgumentNullException.ThrowIfNull(eventState);
    ArgumentNullException.ThrowIfNull(getPotionIds);
    ArgumentNullException.ThrowIfNull(applyLosePotion);
    ArgumentNullException.ThrowIfNull(gainPotion);

    if (stats != null)
    {
        foreach (var potionId in stats.PotionChoices?
                     .Where(choice => choice.WasPicked)
                     .Select(choice => NormalizeModelId(choice.Choice))
                     .Where(id => !string.IsNullOrWhiteSpace(id)) ?? [])
        {
            trackedPotionIds.Add(potionId);
        }

        foreach (var potionId in stats.BoughtPotions?
                     .Select(NormalizeModelId)
                     .Where(id => !string.IsNullOrWhiteSpace(id)) ?? [])
        {
            trackedPotionIds.Add(potionId);
        }

        foreach (var potionId in stats.PotionUsed?
                     .Select(NormalizeModelId)
                     .Where(id => !string.IsNullOrWhiteSpace(id)) ?? [])
        {
            RemoveFirstTrackedPotion(trackedPotionIds, potionId);
        }

        foreach (var potionId in stats.PotionDiscarded?
                     .Select(NormalizeModelId)
                     .Where(id => !string.IsNullOrWhiteSpace(id)) ?? [])
        {
            RemoveFirstTrackedPotion(trackedPotionIds, potionId);
        }
    }

    var currentPotionIds = ((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!)
        .Select(NormalizeModelId)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToList();

    foreach (var potionId in currentPotionIds)
    {
        if (!trackedPotionIds.Contains(potionId, StringComparer.OrdinalIgnoreCase))
        {
            applyLosePotion.Invoke(eventState, [potionId]);
        }
    }

    var finalPotionIds = ((IEnumerable<string>)getPotionIds.Invoke(eventState, null)!)
        .Select(NormalizeModelId)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToList();

    foreach (var potionId in trackedPotionIds)
    {
        if (!finalPotionIds.Contains(potionId, StringComparer.OrdinalIgnoreCase))
        {
            gainPotion.Invoke(eventState, [potionId]);
        }
    }
}

static void RemoveFirstTrackedPotion(List<string> trackedPotionIds, string potionId)
{
    var index = trackedPotionIds.FindIndex(existing => string.Equals(existing, potionId, StringComparison.OrdinalIgnoreCase));
    if (index >= 0)
    {
        trackedPotionIds.RemoveAt(index);
    }
}

static RouteComparison CompareAgainstSave(
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyList<SimulatedActSnapshot> simulatedActs,
    int act1RouteIndex,
    int? act2RouteIndex,
    bool ignoreShopRelicMismatches)
{
    var mismatches = new List<string>();
    var sampleLines = new List<string>();
    var score = 0;

    foreach (var actualAct in actualActs)
    {
        var simulatedAct = simulatedActs.FirstOrDefault(act => act.ActNumber == actualAct.ActNumber);
        if (simulatedAct == null)
        {
            score += 1000;
            mismatches.Add($"Act {actualAct.ActNumber}: missing simulated act");
            continue;
        }

        var roomCount = Math.Max(actualAct.Rooms.Count, simulatedAct.Rooms.Count);
        for (var i = 0; i < roomCount; i++)
        {
            if (i >= actualAct.Rooms.Count)
            {
                break;
            }

            if (i >= simulatedAct.Rooms.Count)
            {
                score += 100;
                mismatches.Add($"Act {actualAct.ActNumber} floor {i + 1}: missing simulated room, actual {DescribeActual(actualAct.Rooms[i])}");
                continue;
            }

            var actual = actualAct.Rooms[i];
            var simulated = simulatedAct.Rooms[i];
            if (sampleLines.Count < 10)
            {
                sampleLines.Add($"CHECK Act {actual.ActNumber} floor {actual.Floor}: actual={DescribeActual(actual)} | simulated={DescribeSimulated(simulated)}");
            }

            if (!string.Equals(actual.RoomType, simulated.RoomType, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
                mismatches.Add($"Act {actual.ActNumber} floor {actual.Floor}: roomType actual={actual.RoomType} simulated={simulated.RoomType}");
            }

            if (!string.Equals(actual.EventId, simulated.EventId, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(actual.EventId) || !string.IsNullOrWhiteSpace(simulated.EventId))
                {
                    score += 10;
                    mismatches.Add($"Act {actual.ActNumber} floor {actual.Floor}: event actual={actual.EventId} simulated={simulated.EventId}");
                }
            }

            var simulatedRelics = string.Equals(actual.RoomType, "Shop", StringComparison.OrdinalIgnoreCase)
                ? simulated.DisplayRelicIds
                : simulated.RelicIds;
            if (!RelicsEqualForRoom(actual.RoomType, actual.RelicIds, simulatedRelics))
            {
                if (ignoreShopRelicMismatches && string.Equals(actual.RoomType, "Shop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                score += 10;
                mismatches.Add($"Act {actual.ActNumber} floor {actual.Floor}: relics actual={string.Join(" / ", actual.RelicIds)} simulated={string.Join(" / ", simulatedRelics)}");
            }
        }
    }

    return new RouteComparison(act1RouteIndex, act2RouteIndex, score, sampleLines, mismatches);
}

static bool SequenceEqualIgnoreCase(IReadOnlyList<string> left, IReadOnlyList<string> right)
{
    if (left.Count != right.Count)
    {
        return false;
    }

    for (var i = 0; i < left.Count; i++)
    {
        if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    return true;
}

static bool RelicsEqualForRoom(string roomType, IReadOnlyList<string> actual, IReadOnlyList<string> simulated)
{
    if (string.Equals(roomType, "Shop", StringComparison.OrdinalIgnoreCase))
    {
        return actual.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(simulated.OrderBy(item => item, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    return SequenceEqualIgnoreCase(actual, simulated);
}

static string DescribeActual(ActualRoomSnapshot room) =>
    $"{room.RoomType}:{room.EventId}:{string.Join("/", room.RelicIds)}";

static string DescribeSimulated(SimulatedRoomSnapshot room) =>
    $"{room.RoomType}:{room.EventId}:{string.Join("/", room.RelicIds)}:{string.Join("/", room.DisplayRelicIds)}";

static string NormalizeSaveRoomType(string? roomType, string? fallbackPointType)
{
    var normalized = roomType?.ToLowerInvariant() ?? fallbackPointType?.ToLowerInvariant() ?? string.Empty;
    return normalized switch
    {
        "event" => "Event",
        "monster" => "Monster",
        "elite" => "Elite",
        "treasure" => "Treasure",
        "shop" => "Shop",
        "rest_site" => "RestSite",
        "restsite" => "RestSite",
        _ => NormalizeMapPointType(fallbackPointType)
    };
}

static string NormalizeModelId(string? modelId)
{
    if (string.IsNullOrWhiteSpace(modelId))
    {
        return string.Empty;
    }

    var normalized = modelId.Trim();
    foreach (var prefix in new[] { "EVENT.", "RELIC.", "ENCOUNTER.", "CARD." })
    {
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
            break;
        }
    }

    return normalized.Replace('-', '_').ToUpperInvariant();
}

static string NormalizeRelicId(string? relicId)
{
    return NormalizeModelId(relicId);
}

static IReadOnlyList<string> ExtractActualRelics(SaveMapPoint point, string roomType)
{
    var stats = point.PlayerStats?.FirstOrDefault();
    if (stats == null)
    {
        return Array.Empty<string>();
    }

    if (string.Equals(roomType, "Shop", StringComparison.OrdinalIgnoreCase))
    {
        return stats.RelicChoices == null
            ? Array.Empty<string>()
            : stats.RelicChoices
                .Select(choice => NormalizeRelicId(choice.Choice))
                .Where(choice => !string.IsNullOrWhiteSpace(choice))
                .ToList();
    }

    return stats.RelicChoices == null
        ? Array.Empty<string>()
        : stats.RelicChoices
            .Where(choice => choice.WasPicked)
            .Select(choice => NormalizeRelicId(choice.Choice))
            .Where(choice => !string.IsNullOrWhiteSpace(choice))
            .ToList();
}

static void ApplyActualShopState(
    SavePlayerStats? stats,
    NeowOptionDataset dataset,
    object eventState,
    object rewardRelicState,
    PropertyInfo currentGoldProperty,
    PropertyInfo currentHpProperty,
    FieldInfo deckField,
    MethodInfo gainCard,
    MethodInfo gainRelic,
    MethodInfo obtainOne,
    PropertyInfo ownedRelicsProperty,
    PropertyInfo playerBagProperty,
    PropertyInfo sharedBagProperty,
    MethodInfo replayImmediatePickupEffects,
    ShopCardSnapshot? shopCardSnapshot)
{
    if (stats == null)
    {
        return;
    }

    if (stats.CurrentGold.HasValue)
    {
        SetPropertyValue(currentGoldProperty, eventState, stats.CurrentGold.Value);
    }

    if (stats.CurrentHp.HasValue)
    {
        SetPropertyValue(currentHpProperty, eventState, stats.CurrentHp.Value);
    }

    foreach (var removedCard in stats.CardsRemoved ?? [])
    {
        RemoveDeckCardById(deckField, eventState, removedCard.Id);
    }

    foreach (var gainedCard in stats.CardsGained ?? [])
    {
        if (!string.IsNullOrWhiteSpace(gainedCard.Id))
        {
            gainCard.Invoke(eventState, [gainedCard.Id]);
        }
    }

    ReplayMerchantCardRestocks(stats, dataset, rewardRelicState, shopCardSnapshot);

    var boughtRelics = (stats.BoughtRelics ?? [])
        .Concat((stats.RelicChoices ?? []).Where(choice => choice.WasPicked).Select(choice => choice.Choice))
        .Select(NormalizeRelicId)
        .Where(relicId => !string.IsNullOrWhiteSpace(relicId))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var ownedRelics = ownedRelicsProperty.GetValue(rewardRelicState);
    var playerBag = playerBagProperty.GetValue(rewardRelicState);
    var sharedBag = sharedBagProperty.GetValue(rewardRelicState);
    var removeRelicMethod = playerBag?.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RelicBag.Remove.");
    var removeSharedRelicMethod = sharedBag?.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing shared RelicBag.Remove.");
    foreach (var relicId in boughtRelics)
    {
        gainRelic.Invoke(eventState, [relicId]);
        obtainOne.Invoke(rewardRelicState, [relicId]);
        removeRelicMethod.Invoke(playerBag, [relicId]);
        removeSharedRelicMethod.Invoke(sharedBag, [relicId]);
        replayImmediatePickupEffects.Invoke(rewardRelicState, [relicId]);
    }
}

static void ReplayMerchantCardRestocks(
    SavePlayerStats? stats,
    NeowOptionDataset dataset,
    object rewardRelicState,
    ShopCardSnapshot? shopCardSnapshot)
{
    var purchasedCards = (stats?.CardsGained ?? [])
        .Select(card => NormalizeModelId(card.Id))
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .ToList();
    if (purchasedCards.Count == 0)
    {
        return;
    }

    if (shopCardSnapshot == null)
    {
        return;
    }

    if (TryReplayMerchantCardRestocksFromSlots(
        stats,
        dataset,
        rewardRelicState,
        shopCardSnapshot,
        purchasedCards))
    {
        return;
    }

    Console.WriteLine("slotReplay=fallback-apply-saved-order");
    _ = TrySimulateMerchantRestocks(
        stats,
        rewardRelicState,
        purchasedCards,
        rewardRelicState.GetType().GetMethod("ResolvePurchasedShopCard", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ResolvePurchasedShopCard."),
        rewardRelicState.GetType().GetProperty("ShownShopCards", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ShownShopCards."),
        recordGeneratedCards: true,
        traceLabel: "fallback",
        out _,
        out _);
}

static bool TryReplayMerchantCardRestocksFromSlots(
    SavePlayerStats? stats,
    NeowOptionDataset dataset,
    object rewardRelicState,
    ShopCardSnapshot shopCardSnapshot,
    IReadOnlyList<string> purchasedCards)
{
    var stateType = rewardRelicState.GetType();
    var cloneMethod = stateType.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing Clone.");
    var resolvePurchasedShopCard = stateType.GetMethod("ResolvePurchasedShopCard", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ResolvePurchasedShopCard.");
    var shownShopCardsProperty = stateType.GetProperty("ShownShopCards", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShownShopCards.");

    _ = dataset;
    _ = shopCardSnapshot;

    var purchaseOrders = BuildPurchaseOrders(purchasedCards);
    IReadOnlyList<string>? matchedOrder = null;
    if (!purchaseOrders.Any(order =>
            TrySimulateMerchantRestocks(
                stats,
                cloneMethod.Invoke(rewardRelicState, null)
                    ?? throw new InvalidOperationException("Failed to clone rewardRelicState for search."),
                order,
                resolvePurchasedShopCard,
                shownShopCardsProperty,
                recordGeneratedCards: false,
                traceLabel: null,
                out _,
                out matchedOrder)))
    {
        var debugCandidate = cloneMethod.Invoke(rewardRelicState, null)
            ?? throw new InvalidOperationException("Failed to clone rewardRelicState for debug.");
        _ = TrySimulateMerchantRestocks(
            stats,
            debugCandidate,
            purchasedCards,
            resolvePurchasedShopCard,
            shownShopCardsProperty,
            recordGeneratedCards: true,
            traceLabel: "saved-order",
            out var debugSlots,
            out _);
        if (purchasedCards.Count > 1)
        {
            var reverseCandidate = cloneMethod.Invoke(rewardRelicState, null)
                ?? throw new InvalidOperationException("Failed to clone rewardRelicState for reverse debug.");
            _ = TrySimulateMerchantRestocks(
                stats,
                reverseCandidate,
                purchasedCards.Reverse().ToList(),
                resolvePurchasedShopCard,
                shownShopCardsProperty,
                recordGeneratedCards: true,
                traceLabel: "reverse-order",
                out _,
                out _);
        }
        Console.WriteLine(
            "slotReplay=no-match snapshot=" + string.Join("/", shopCardSnapshot.Slots.Select(slot => slot.CardId)) +
            " final=" + string.Join("/", debugSlots.Select(slot => slot.CardId)));
        return false;
    }

    var applied = TrySimulateMerchantRestocks(
        stats,
        rewardRelicState,
        matchedOrder ?? purchasedCards,
        resolvePurchasedShopCard,
        shownShopCardsProperty,
        recordGeneratedCards: true,
        traceLabel: "applied",
        out var liveSlots,
        out _);
    Console.WriteLine(
        "slotReplay=applied order=" + string.Join("->", matchedOrder ?? purchasedCards) +
        " final=" + string.Join("/", liveSlots.Select(slot => slot.CardId)));
    return applied;
}

static bool TrySimulateMerchantRestocks(
    SavePlayerStats? stats,
    object rewardRelicState,
    IReadOnlyList<string> purchasedCards,
    MethodInfo resolvePurchasedShopCard,
    PropertyInfo shownShopCardsProperty,
    bool recordGeneratedCards,
    string? traceLabel,
    out List<ShopCardSlot> finalSlots,
    out IReadOnlyList<string>? usedOrder)
{
    usedOrder = purchasedCards.ToList();

    foreach (var purchasedCardId in purchasedCards)
    {
        var beforeSlots = CaptureShopCardSnapshot(rewardRelicState, dataset: null);
        resolvePurchasedShopCard.Invoke(rewardRelicState, [purchasedCardId]);
        var afterSlots = CaptureShopCardSnapshot(rewardRelicState, dataset: null);
        if (afterSlots.Slots.Count == 0)
        {
            finalSlots = afterSlots.Slots.ToList();
            return false;
        }

        if (recordGeneratedCards)
        {
            var slotIndex = FindPurchaseSlotIndex(beforeSlots.Slots.ToList(), purchasedCardId);
            var restockedCardId = slotIndex >= 0 && slotIndex < afterSlots.Slots.Count
                ? afterSlots.Slots[slotIndex].CardId
                : string.Empty;
            var slot = slotIndex >= 0 && slotIndex < beforeSlots.Slots.Count
                ? beforeSlots.Slots[slotIndex]
                : null;
            Console.WriteLine(
                $"slotReplayStep[{traceLabel ?? "trace"}] kind={slot?.Kind.ToString() ?? "?"} buy={purchasedCardId} slot={slotIndex} restock={restockedCardId}");
        }
    }

    finalSlots = CaptureShopCardSnapshot(rewardRelicState, dataset: null).Slots.ToList();
    if (!recordGeneratedCards)
    {
        return MatchesSavedShopCards(stats, finalSlots);
    }

    return true;
}

static IReadOnlyList<IReadOnlyList<string>> BuildPurchaseOrders(IReadOnlyList<string> purchasedCards)
{
    if (purchasedCards.Count <= 1)
    {
        return [purchasedCards.ToList()];
    }

    var orders = new List<IReadOnlyList<string>>
    {
        purchasedCards.ToList(),
        purchasedCards.Reverse().ToList()
    };
    return orders;
}

static int FindPurchaseSlotIndex(List<ShopCardSlot> slots, string purchasedCardId)
{
    for (var i = 0; i < slots.Count; i++)
    {
        if (string.Equals(slots[i].CardId, purchasedCardId, StringComparison.OrdinalIgnoreCase))
        {
            return i;
        }
    }

    return -1;
}

static bool MatchesSavedShopCards(SavePlayerStats? stats, IReadOnlyList<ShopCardSlot> slots)
{
    var savedCards = (stats?.CardChoices ?? [])
        .Select(choice => NormalizeModelId(choice.Card?.Id))
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (savedCards.Count == 0)
    {
        return true;
    }

    var currentCards = slots
        .Select(slot => slot.CardId)
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    return savedCards.All(currentCards.Contains);
}

static ShopCardSnapshot CaptureShopCardSnapshot(object rewardRelicState, NeowOptionDataset? dataset)
{
    _ = dataset;
    var shownCards = (IEnumerable)(rewardRelicState.GetType().GetProperty("ShownShopCards", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardRelicState)
        ?? Array.Empty<object>());
    var slots = shownCards
        .Cast<object>()
        .Select(card =>
        {
            var type = card.GetType();
            var isColorless = (bool)(type.GetProperty("IsColorless", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card) ?? false);
            var cardType = type.GetProperty("CardType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString();
            var fixedRarity = type.GetProperty("FixedRarity", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString();
            return new ShopCardSlot(
                NormalizeModelId(type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString()),
                isColorless ? ShopCardSlotKind.Colorless : ShopCardSlotKind.Colored,
                cardType,
                fixedRarity);
        })
        .Where(slot => !string.IsNullOrWhiteSpace(slot.CardId))
        .ToList();
    return new ShopCardSnapshot(slots);
}

static GameRng GetRequiredGameRng(object instance, string propertyName)
{
    return (GameRng)(instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance)
        ?? throw new InvalidOperationException($"Missing {propertyName}."));
}

static object ParseCardType(string? cardType)
{
    return cardType switch
    {
        "Attack" => Enum.Parse(typeof(CardType), "Attack"),
        "Skill" => Enum.Parse(typeof(CardType), "Skill"),
        "Power" => Enum.Parse(typeof(CardType), "Power"),
        _ => throw new InvalidOperationException($"Unsupported card type: {cardType}")
    };
}

static bool CanApplyActualShopState(ActualRoomSnapshot actualRoom, SimulatedRoomSnapshot baselineRoom)
{
    if (!string.Equals(actualRoom.RoomType, "Shop", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(baselineRoom.RoomType, "Shop", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return true;
}

static bool HasAnyApplicableShop(
    IReadOnlyList<ActualActSnapshot> actualActs,
    IReadOnlyList<SimulatedActSnapshot> baselineActs)
{
    foreach (var actualAct in actualActs)
    {
        var baselineAct = baselineActs.FirstOrDefault(act => act.ActNumber == actualAct.ActNumber);
        if (baselineAct == null)
        {
            continue;
        }

        var roomCount = Math.Min(actualAct.Rooms.Count, baselineAct.Rooms.Count);
        for (var i = 0; i < roomCount; i++)
        {
            if (CanApplyActualShopState(actualAct.Rooms[i], baselineAct.Rooms[i]))
            {
                return true;
            }
        }
    }

    return false;
}

static void SetPropertyValue(PropertyInfo property, object instance, object value)
{
    var setter = property.GetSetMethod(nonPublic: true)
        ?? throw new InvalidOperationException($"Missing setter for {property.Name}.");
    setter.Invoke(instance, [value]);
}

static IReadOnlyList<string> GetDeckCardIds(FieldInfo deckField, object eventState)
{
    var deck = (IEnumerable?)deckField.GetValue(eventState);
    if (deck == null)
    {
        return [];
    }

    return deck
        .Cast<object>()
        .Select(card => NormalizeModelId(card.GetType().GetProperty("CardId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString()))
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .ToList();
}

static void RemoveDeckCardById(FieldInfo deckField, object eventState, string? cardId)
{
    var normalizedId = NormalizeModelId(cardId);
    if (string.IsNullOrWhiteSpace(normalizedId))
    {
        return;
    }

    var deck = (IList?)deckField.GetValue(eventState);
    if (deck == null)
    {
        return;
    }

    for (var i = 0; i < deck.Count; i++)
    {
        var card = deck[i];
        var deckCardId = NormalizeModelId(card?.GetType().GetProperty("CardId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString());
        if (string.Equals(deckCardId, normalizedId, StringComparison.OrdinalIgnoreCase))
        {
            deck.RemoveAt(i);
            return;
        }
    }
}

static IReadOnlyList<string> ApplyActualEventState(
    SavePlayerStats? stats,
    int actNumber,
    string eventId,
    object eventState,
    object rewardRelicState,
    GameRng eventRng,
    MethodInfo applyShownEvent,
    MethodInfo getPotionIds,
    MethodInfo isTradableRelic,
    MethodInfo applyGoldSpend,
    MethodInfo applyLosePotion,
    MethodInfo applyLoseTradableRelic,
    FieldInfo deckField,
    MethodInfo gainPotion,
    MethodInfo consumeRewardRng,
    PropertyInfo ownedRelicsProperty,
    MethodInfo pullEventFrontAndObtain,
    MethodInfo removeOwnedRelic)
{
    var normalizedEventId = NormalizeModelId(eventId);
    if (string.Equals(normalizedEventId, "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase) &&
        ApplyActualSelfHelpBookState(stats, eventState, rewardRelicState, deckField, consumeRewardRng))
    {
        return Array.Empty<string>();
    }

    if (string.Equals(normalizedEventId, "DROWNING_BEACON", StringComparison.OrdinalIgnoreCase) &&
        ApplyActualDrowningBeaconState(stats, eventState, gainPotion))
    {
        return Array.Empty<string>();
    }

    if (string.Equals(normalizedEventId, "RANWID_THE_ELDER", StringComparison.OrdinalIgnoreCase))
    {
        return ApplyActualRanwidState(
            stats,
            actNumber,
            eventState,
            rewardRelicState,
            eventRng,
            getPotionIds,
            isTradableRelic,
            applyGoldSpend,
            applyLosePotion,
            applyLoseTradableRelic,
            ownedRelicsProperty,
            pullEventFrontAndObtain,
            removeOwnedRelic);
    }

    applyShownEvent.Invoke(eventState, [eventId, eventRng]);
    return Array.Empty<string>();
}

static IReadOnlyList<string> ApplyActualRanwidState(
    SavePlayerStats? stats,
    int actNumber,
    object eventState,
    object rewardRelicState,
    GameRng eventRng,
    MethodInfo getPotionIds,
    MethodInfo isTradableRelic,
    MethodInfo applyGoldSpend,
    MethodInfo applyLosePotion,
    MethodInfo applyLoseTradableRelic,
    PropertyInfo ownedRelicsProperty,
    MethodInfo pullEventFrontAndObtain,
    MethodInfo removeOwnedRelic)
{
    var choiceKey = stats?.EventChoices?
        .FirstOrDefault()?
        .Title?
        .Key ?? string.Empty;

    var potionIds = ((IEnumerable)(getPotionIds.Invoke(eventState, null) ?? Array.Empty<string>()))
        .Cast<object>()
        .Select(item => NormalizeModelId(item?.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
    var previewPotionId = potionIds.Length > 0
        ? NormalizeModelId(eventRng.NextItem(potionIds))
        : string.Empty;

    var ownedRelics = ((IEnumerable)(ownedRelicsProperty.GetValue(rewardRelicState) ?? Array.Empty<string>()))
        .Cast<object>()
        .Select(item => NormalizeRelicId(item?.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
    var tradableOwnedRelics = ownedRelics
        .Where(relicId => (bool)(isTradableRelic.Invoke(eventState, [relicId]) ?? false))
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var previewTradeRelicId = tradableOwnedRelics.Length > 0
        ? NormalizeRelicId(eventRng.NextItem(tradableOwnedRelics))
        : string.Empty;

    if (choiceKey.IndexOf("POTION", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        applyLosePotion.Invoke(eventState, [previewPotionId]);
        return PullRanwidRelics(actNumber, rewardRelicState, pullEventFrontAndObtain, 1);
    }

    if (choiceKey.IndexOf("GOLD", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        applyGoldSpend.Invoke(eventState, [100]);
        return PullRanwidRelics(actNumber, rewardRelicState, pullEventFrontAndObtain, 1);
    }

    if (choiceKey.IndexOf("RELIC", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        applyLoseTradableRelic.Invoke(eventState, null);
        if (!string.IsNullOrWhiteSpace(previewTradeRelicId))
        {
            removeOwnedRelic.Invoke(rewardRelicState, [previewTradeRelicId]);
        }

        return PullRanwidRelics(actNumber, rewardRelicState, pullEventFrontAndObtain, 2);
    }

    return Array.Empty<string>();
}

static IReadOnlyList<string> PullRanwidRelics(
    int actNumber,
    object rewardRelicState,
    MethodInfo pullEventFrontAndObtain,
    int count)
{
    var relicIds = new List<string>(count);
    for (var i = 0; i < count; i++)
    {
        var pulled = NormalizeRelicId(pullEventFrontAndObtain.Invoke(rewardRelicState, [actNumber, null, null])?.ToString());
        if (!string.IsNullOrWhiteSpace(pulled))
        {
            relicIds.Add(pulled);
        }
    }

    return relicIds;
}

static bool ApplyActualSelfHelpBookState(
    SavePlayerStats? stats,
    object eventState,
    object rewardRelicState,
    FieldInfo deckField,
    MethodInfo consumeRewardRng)
{
    var choiceKey = stats?.EventChoices?
        .FirstOrDefault()?
        .Title?
        .Key;
    if (string.IsNullOrWhiteSpace(choiceKey))
    {
        return false;
    }

    var typeRestriction = choiceKey.IndexOf("READ_THE_BACK", StringComparison.OrdinalIgnoreCase) >= 0
        ? "Attack"
        : choiceKey.IndexOf("READ_PASSAGE", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Skill"
            : choiceKey.IndexOf("READ_ENTIRE_BOOK", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Power"
                : null;
    if (typeRestriction == null)
    {
        return false;
    }

    var deck = (IList?)deckField.GetValue(eventState);
    if (deck == null)
    {
        return false;
    }

    for (var i = 0; i < deck.Count; i++)
    {
        var card = deck[i];
        if (card == null)
        {
            continue;
        }

        var cardType = card.GetType().GetProperty("CardId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString();
        var normalizedCardId = NormalizeModelId(cardType);
        var hasEnchantmentProperty = card.GetType().GetProperty("HasEnchantment", BindingFlags.Instance | BindingFlags.Public);
        if (string.IsNullOrWhiteSpace(normalizedCardId) || hasEnchantmentProperty == null)
        {
            continue;
        }

        var matchesType = typeRestriction switch
        {
            "Attack" => IsAttackCard(normalizedCardId),
            "Skill" => IsSkillCard(normalizedCardId),
            "Power" => IsPowerCard(normalizedCardId),
            _ => false
        };
        if (!matchesType)
        {
            continue;
        }

        hasEnchantmentProperty.SetValue(card, true);
        return true;
    }

    return true;
}

static bool ApplyActualDrowningBeaconState(SavePlayerStats? stats, object eventState, MethodInfo gainPotion)
{
    var choiceKey = stats?.EventChoices?
        .FirstOrDefault()?
        .Title?
        .Key;
    if (string.IsNullOrWhiteSpace(choiceKey))
    {
        return false;
    }

    if (choiceKey.IndexOf("BOTTLE", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        var pickedPotion = stats?.PotionChoices?
            .FirstOrDefault(choice => choice.WasPicked)?
            .Choice;
        if (!string.IsNullOrWhiteSpace(pickedPotion))
        {
            gainPotion.Invoke(eventState, [pickedPotion]);
        }

        return true;
    }

    return false;
}

static bool IsAttackCard(string cardId) => cardId switch
{
    "BASH" or "STRIKE_IRONCLAD" or "STRIKE_SILENT" or "STRIKE_DEFECT" or "STRIKE_NECROBINDER" or "STRIKE_REGENT" => true,
    _ => cardId.Contains("SLASH", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("ATTACK", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("BITE", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("SPRAY", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("KNIVES", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("SKEWER", StringComparison.OrdinalIgnoreCase) ||
         cardId.Contains("CUT", StringComparison.OrdinalIgnoreCase)
};

static bool IsSkillCard(string cardId) => cardId.StartsWith("DEFEND_", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("FOOTWORK", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("PREPARED", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("BACKFLIP", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("ACROBATICS", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("DEFLECT", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("CLOAK_AND_DAGGER", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("PIERCING_WAIL", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("ANTICIPATE", StringComparison.OrdinalIgnoreCase);

static bool IsPowerCard(string cardId) => cardId.Contains("POWER", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("FOOTWORK", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("SERPENT_FORM", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("WELL_LAID_PLANS", StringComparison.OrdinalIgnoreCase) ||
                                          cardId.Contains("NOSTALGIA", StringComparison.OrdinalIgnoreCase);

static IReadOnlyList<string> ExtractRelicList(object? value)
{
    if (value is not IEnumerable enumerable)
    {
        return Array.Empty<string>();
    }

    return enumerable.Cast<object>()
        .Select(item => NormalizeRelicId(item?.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
}

static object? GetNamedValue(object instance, string memberName)
{
    var type = instance.GetType();
    return type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
        ?? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
}

static (string AncientId, IReadOnlyList<string> AncientRelics) GetAncientInfoForAct(object ancientByAct, int actNumber)
{
    var tryGetValue = ancientByAct.GetType().GetMethod("TryGetValue")
        ?? throw new InvalidOperationException("Missing ancientByAct.TryGetValue.");
    var args = new object?[] { actNumber, null };
    var found = (bool)(tryGetValue.Invoke(ancientByAct, args) ?? false);
    if (!found || args[1] == null)
    {
        return (string.Empty, Array.Empty<string>());
    }

    var info = args[1]!;
    var ancientId = info.GetType().GetProperty("AncientId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(info)?.ToString() ?? string.Empty;
    var relicIds = ((IEnumerable)(info.GetType().GetProperty("RelicIds", BindingFlags.Instance | BindingFlags.Public)?.GetValue(info)
        ?? Array.Empty<string>()))
        .Cast<object>()
        .Select(item => NormalizeRelicId(item?.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
    return (NormalizeModelId(ancientId), relicIds);
}

static object CreateAncientByAct(Sts2RunPreview preview, Type ancientInfoType)
{
    var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), ancientInfoType);
    var addMethod = dictionaryType.GetMethod("Add", [typeof(int), ancientInfoType])
        ?? throw new InvalidOperationException("Missing ancient dictionary Add.");
    var result = Activator.CreateInstance(dictionaryType)
        ?? throw new InvalidOperationException("Failed to create ancient dictionary.");
    foreach (var act in preview.Acts)
    {
        var relicIds = act.AncientOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
            .Select(option => option.RelicId!)
            .ToList();
        var info = Activator.CreateInstance(ancientInfoType, act.AncientId ?? string.Empty, relicIds)
            ?? throw new InvalidOperationException("Failed to create AncientInfo.");
        addMethod.Invoke(result, [act.ActNumber, info]);
    }

    return result;
}

static int IndexOfPoolItem(IReadOnlyList<string> pool, string eventId)
{
    for (var i = 0; i < pool.Count; i++)
    {
        if (string.Equals(NormalizePoolItem(pool[i]), eventId, StringComparison.OrdinalIgnoreCase))
        {
            return i;
        }
    }

    return -1;
}

static string NormalizePoolItem(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var normalized = value.Trim();
    const string eventPrefix = "EVENT.";
    if (normalized.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
    {
        normalized = normalized[eventPrefix.Length..];
    }

    return normalized.Replace('-', '_').ToUpperInvariant();
}

static IReadOnlyList<string> ConvertRouteTypes(object rawRoute)
{
    var nodesProperty = rawRoute.GetType().GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing route nodes.");
    return ((IEnumerable)nodesProperty.GetValue(rawRoute)!)
        .Cast<object>()
        .Select(rawNode => rawNode.GetType().GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rawNode)?.ToString() ?? string.Empty)
        .ToList();
}

static bool StartsWith(IReadOnlyList<string> actual, IReadOnlyList<string> target)
{
    if (actual.Count < target.Count)
    {
        return false;
    }

    for (var i = 0; i < target.Count; i++)
    {
        if (!string.Equals(actual[i], target[i], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    return true;
}

static string NormalizeMapPointType(string? mapPointType)
{
    return mapPointType?.ToLowerInvariant() switch
    {
        "unknown" => "Unknown",
        "monster" => "Monster",
        "elite" => "Elite",
        "treasure" => "Treasure",
        "shop" => "Shop",
        "rest_site" => "RestSite",
        "restsite" => "RestSite",
        "ancient" => "Ancient",
        "boss" => "Boss",
        _ => mapPointType ?? string.Empty
    };
}

static CharacterId ResolveSaveCharacter(SaveSnapshot save)
{
    var raw = save.Players?.FirstOrDefault()?.Character;
    if (string.IsNullOrWhiteSpace(raw))
    {
        return CharacterId.Ironclad;
    }

    var normalized = raw.Trim();
    const string prefix = "CHARACTER.";
    if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        normalized = normalized[prefix.Length..];
    }

    return normalized.ToUpperInvariant() switch
    {
        "IRONCLAD" => CharacterId.Ironclad,
        "SILENT" => CharacterId.Silent,
        "DEFECT" => CharacterId.Defect,
        "NECROBINDER" => CharacterId.Necrobinder,
        "REGENT" => CharacterId.Regent,
        _ => CharacterId.Ironclad
    };
}

static string FindWorkspaceRoot()
{
    var current = AppContext.BaseDirectory;
    for (var i = 0; i < 8; i++)
    {
        var candidate = current;
        for (var step = 0; step < i; step++)
        {
            candidate = Path.Combine(candidate, "..");
        }

        candidate = Path.GetFullPath(candidate);
        if (Directory.Exists(Path.Combine(candidate, "src")) &&
            Directory.Exists(Path.Combine(candidate, "data")))
        {
            return candidate;
        }
    }

    return Directory.GetCurrentDirectory();
}

static IReadOnlyList<string> SimulateStandardShopForRoute(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    IReadOnlyList<string> routeRoomTypes,
    int extraRewardsDraws = 0,
    int extraShopsDraws = 0)
{
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(
        Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
        Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

    var previewerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer")
        ?? throw new InvalidOperationException("Missing Sts2StandardShopPreviewer type.");
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing previewer world.");
    var datasetField = typeof(Sts2RunPreviewer).GetField("_dataset", BindingFlags.Instance | BindingFlags.NonPublic);
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");
    var standardPreviewer = Activator.CreateInstance(
        previewerType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args: [dataset, world],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create standard previewer.");

    var stateType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardShopState")
        ?? throw new InvalidOperationException("Missing StandardShopState.");
    var roomTypeEnum = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardRoomType")
        ?? throw new InvalidOperationException("Missing StandardRoomType.");
    var buildShopPreviewMethod = previewerType.GetMethod("BuildShopPreview", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing BuildShopPreview.");
    var simulateBeforeShopRoomMethod = previewerType.GetMethod("SimulateBeforeShopRoom", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing SimulateBeforeShopRoom.");
    var applyNeowRuleMethod = previewerType.GetMethod("ApplyStandardNeowRule", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ApplyStandardNeowRule.");
    var requestType = typeof(ShopPreview).Assembly.GetType("SeedModel.Sts2.ShopPreviewRequest")
        ?? throw new InvalidOperationException("Missing ShopPreviewRequest.");
    var fullRequest = requestType.GetProperty("Full", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException("Missing full shop request.");

    var runRngSetType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Rng.RunRngSet")
        ?? throw new InvalidOperationException("Missing RunRngSet.");
    var runRng = Activator.CreateInstance(runRngSetType, seedValue)
        ?? throw new InvalidOperationException("Failed to create RunRngSet.");
    var getMethod = runRngSetType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RunRngSet.Get.");
    var ancientAvailability = Sts2AncientAvailability.Default;
    var deterministicHashMethod = typeof(GameRng).GetMethod("GetDeterministicHashCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing deterministic hash.");
    var playerSeed = unchecked((uint)(int)deterministicHashMethod.Invoke(null, [normalizedSeed])! + 1u);

    var state = Activator.CreateInstance(
        stateType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            dataset,
            world,
            character,
            1,
            ascension,
            ancientAvailability,
            runRng,
            new GameRng(playerSeed, "rewards"),
            new GameRng(playerSeed, "shops")
        ],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create StandardShopState.");

    applyNeowRuleMethod.Invoke(null, [state, act1OpeningOption == null ? Array.Empty<NeowOptionResult>() : new[] { act1OpeningOption }]);

    foreach (var roomTypeName in routeRoomTypes)
    {
        if (string.Equals(roomTypeName, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var roomType = Enum.Parse(roomTypeEnum, roomTypeName);
        simulateBeforeShopRoomMethod.Invoke(null, [state, roomType]);
    }

    var rewardsRngProperty = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng property.");
    var shopsRngProperty = stateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShopsRng property.");
    var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing rewards rng."));
    var shopsRng = (GameRng)(shopsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing shops rng."));
    for (var i = 0; i < extraRewardsDraws; i++)
    {
        _ = rewardsRng.NextFloat();
    }

    for (var i = 0; i < extraShopsDraws; i++)
    {
        _ = shopsRng.NextFloat();
    }

    var preview = (ShopPreview)(buildShopPreviewMethod.Invoke(standardPreviewer, [state, fullRequest])
        ?? throw new InvalidOperationException("Failed to build shop preview."));
    return preview.Relics.Select(entry => entry.Id).ToList();
}

static ShopPreview SimulateStandardShopPreviewForRoute(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    IReadOnlyList<string> routeRoomTypes,
    int extraRewardsDraws = 0,
    int extraShopsDraws = 0)
{
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(
        Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
        Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

    var previewerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer")
        ?? throw new InvalidOperationException("Missing Sts2StandardShopPreviewer type.");
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing previewer world.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");
    var standardPreviewer = Activator.CreateInstance(
        previewerType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args: [dataset, world],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create standard previewer.");

    var stateType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardShopState")
        ?? throw new InvalidOperationException("Missing StandardShopState.");
    var roomTypeEnum = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardRoomType")
        ?? throw new InvalidOperationException("Missing StandardRoomType.");
    var buildShopPreviewMethod = previewerType.GetMethod("BuildShopPreview", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing BuildShopPreview.");
    var simulateBeforeShopRoomMethod = previewerType.GetMethod("SimulateBeforeShopRoom", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing SimulateBeforeShopRoom.");
    var applyNeowRuleMethod = previewerType.GetMethod("ApplyStandardNeowRule", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ApplyStandardNeowRule.");
    var requestType = typeof(ShopPreview).Assembly.GetType("SeedModel.Sts2.ShopPreviewRequest")
        ?? throw new InvalidOperationException("Missing ShopPreviewRequest.");
    var fullRequest = requestType.GetProperty("Full", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException("Missing full shop request.");

    var runRngSetType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Rng.RunRngSet")
        ?? throw new InvalidOperationException("Missing RunRngSet.");
    var runRng = Activator.CreateInstance(runRngSetType, seedValue)
        ?? throw new InvalidOperationException("Failed to create RunRngSet.");
    var ancientAvailability = Sts2AncientAvailability.Default;
    var deterministicHashMethod = typeof(GameRng).GetMethod("GetDeterministicHashCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing deterministic hash.");
    var playerSeed = unchecked((uint)(int)deterministicHashMethod.Invoke(null, [normalizedSeed])! + 1u);

    var state = Activator.CreateInstance(
        stateType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            dataset,
            world,
            character,
            1,
            ascension,
            ancientAvailability,
            runRng,
            new GameRng(playerSeed, "rewards"),
            new GameRng(playerSeed, "shops")
        ],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create StandardShopState.");

    applyNeowRuleMethod.Invoke(null, [state, act1OpeningOption == null ? Array.Empty<NeowOptionResult>() : new[] { act1OpeningOption }]);

    foreach (var roomTypeName in routeRoomTypes)
    {
        if (string.Equals(roomTypeName, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var roomType = Enum.Parse(roomTypeEnum, roomTypeName);
        simulateBeforeShopRoomMethod.Invoke(null, [state, roomType]);
    }

    var rewardsRngProperty = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng property.");
    var shopsRngProperty = stateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShopsRng property.");
    var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing rewards rng."));
    var shopsRng = (GameRng)(shopsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing shops rng."));
    for (var i = 0; i < extraRewardsDraws; i++)
    {
        _ = rewardsRng.NextFloat();
    }

    for (var i = 0; i < extraShopsDraws; i++)
    {
        _ = shopsRng.NextFloat();
    }

    return (ShopPreview)(buildShopPreviewMethod.Invoke(standardPreviewer, [state, fullRequest])
        ?? throw new InvalidOperationException("Failed to build shop preview."));
}

static string DescribeStandardShopStateForRoute(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    IReadOnlyList<string> routeRoomTypes)
{
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(
        Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
        Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

    var previewerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer")
        ?? throw new InvalidOperationException("Missing Sts2StandardShopPreviewer type.");
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing previewer world.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");
    var standardPreviewer = Activator.CreateInstance(
        previewerType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args: [dataset, world],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create standard previewer.");
    _ = standardPreviewer;

    var stateType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardShopState")
        ?? throw new InvalidOperationException("Missing StandardShopState.");
    var roomTypeEnum = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardRoomType")
        ?? throw new InvalidOperationException("Missing StandardRoomType.");
    var simulateBeforeShopRoomMethod = previewerType.GetMethod("SimulateBeforeShopRoom", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing SimulateBeforeShopRoom.");
    var applyNeowRuleMethod = previewerType.GetMethod("ApplyStandardNeowRule", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ApplyStandardNeowRule.");

    var runRngSetType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Rng.RunRngSet")
        ?? throw new InvalidOperationException("Missing RunRngSet.");
    var runRng = Activator.CreateInstance(runRngSetType, seedValue)
        ?? throw new InvalidOperationException("Failed to create RunRngSet.");
    var ancientAvailability = Sts2AncientAvailability.Default;
    var deterministicHashMethod = typeof(GameRng).GetMethod("GetDeterministicHashCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing deterministic hash.");
    var playerSeed = unchecked((uint)(int)deterministicHashMethod.Invoke(null, [normalizedSeed])! + 1u);

    var state = Activator.CreateInstance(
        stateType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            dataset,
            world,
            character,
            1,
            ascension,
            ancientAvailability,
            runRng,
            new GameRng(playerSeed, "rewards"),
            new GameRng(playerSeed, "shops")
        ],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create StandardShopState.");

    applyNeowRuleMethod.Invoke(null, [state, act1OpeningOption == null ? Array.Empty<NeowOptionResult>() : new[] { act1OpeningOption }]);

    foreach (var roomTypeName in routeRoomTypes)
    {
        if (string.Equals(roomTypeName, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var roomType = Enum.Parse(roomTypeEnum, roomTypeName);
        simulateBeforeShopRoomMethod.Invoke(null, [state, roomType]);
    }

    var rewardsRngProperty = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng property.");
    var shopsRngProperty = stateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShopsRng property.");
    var cardRarityOddsProperty = stateType.GetProperty("CardRarityOdds", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRarityOdds property.");
    var potionOddsProperty = stateType.GetProperty("PotionOdds", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionOdds property.");
    var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing rewards rng."));
    var shopsRng = (GameRng)(shopsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing shops rng."));
    var cardRarityOdds = cardRarityOddsProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing card rarity odds.");
    var potionOdds = potionOddsProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing potion odds.");
    var rareOffset = cardRarityOdds.GetType().GetProperty("CurrentValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(cardRarityOdds);
    var potionChance = potionOdds.GetType().GetProperty("CurrentValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(potionOdds);
    return $"rewards={rewardsRng.Counter};shops={shopsRng.Counter};rareOffset={rareOffset};potionChance={potionChance}";
}

static object BuildStandardShopState(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(
        Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
        Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));
    var previewerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer")
        ?? throw new InvalidOperationException("Missing Sts2StandardShopPreviewer type.");
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing previewer world.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");
    var runRngSetType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Rng.RunRngSet")
        ?? throw new InvalidOperationException("Missing RunRngSet.");
    var runRng = Activator.CreateInstance(runRngSetType, seedValue)
        ?? throw new InvalidOperationException("Failed to create RunRngSet.");
    var deterministicHashMethod = typeof(GameRng).GetMethod("GetDeterministicHashCode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Missing deterministic hash.");
    var playerSeed = unchecked((uint)(int)deterministicHashMethod.Invoke(null, [normalizedSeed])! + 1u);
    var stateType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardShopState")
        ?? throw new InvalidOperationException("Missing StandardShopState.");
    var state = Activator.CreateInstance(
        stateType,
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            dataset,
            world,
            character,
            1,
            ascension,
            Sts2AncientAvailability.Default,
            runRng,
            new GameRng(playerSeed, "rewards"),
            new GameRng(playerSeed, "shops")
        ],
        culture: null)
        ?? throw new InvalidOperationException("Failed to create StandardShopState.");
    var applyNeowRuleMethod = previewerType.GetMethod("ApplyStandardNeowRule", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing ApplyStandardNeowRule.");
    applyNeowRuleMethod.Invoke(null, [state, act1OpeningOption == null ? Array.Empty<NeowOptionResult>() : new[] { act1OpeningOption }]);
    var roomTypeEnum = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardRoomType")
        ?? throw new InvalidOperationException("Missing StandardRoomType.");
    var simulateBeforeShopRoomMethod = previewerType.GetMethod("SimulateBeforeShopRoom", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing SimulateBeforeShopRoom.");
    foreach (var roomTypeName in new[] { "Monster", "Treasure", "Monster", "Event" })
    {
        var roomType = Enum.Parse(roomTypeEnum, roomTypeName);
        simulateBeforeShopRoomMethod.Invoke(null, [state, roomType]);
    }

    return state;
}

static string DescribeStandardBucketTail(object bag, string rarity)
{
    var bucketsField = bag.GetType().GetField("_deques", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing standard bag deques.");
    var buckets = (IDictionary)(bucketsField.GetValue(bag)
        ?? throw new InvalidOperationException("Missing standard bag buckets."));
    foreach (DictionaryEntry entry in buckets)
    {
        if (!string.Equals(entry.Key?.ToString(), rarity, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var items = ((IEnumerable)entry.Value).Cast<object>().Select(item => item.ToString() ?? string.Empty).Reverse().Take(3).Reverse();
        return string.Join(",", items);
    }

    return string.Empty;
}

static string FindMatchingStandardShopOffsets(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    IReadOnlyList<string> routeRoomTypes)
{
    var target = new HashSet<string>(["MINIATURE_TENT", "STRIKE_DUMMY", "SHOVEL"], StringComparer.OrdinalIgnoreCase);
    for (var extraRewards = 0; extraRewards <= 96; extraRewards++)
    {
        for (var extraShops = 0; extraShops <= 96; extraShops++)
        {
            var relics = SimulateStandardShopForRoute(
                root,
                dataset,
                normalizedSeed,
                seedValue,
                ascension,
                character,
                act1OpeningOption,
                routeRoomTypes,
                extraRewards,
                extraShops);
            if (relics.Count == 3 && target.SetEquals(relics))
            {
                return $"reward+{extraRewards}, shop+{extraShops} => {string.Join(" / ", relics)}";
            }
        }
    }

    return "no-hit-0..96";
}

static string FindMatchingExactAct2FirstShopOffsets(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var target = new HashSet<string>(["SHOVEL", "VAJRA", "ORRERY"], StringComparer.OrdinalIgnoreCase);
    for (var boughtTent = 0; boughtTent <= 1; boughtTent++)
    {
        for (var extraRewards = 0; extraRewards <= 12; extraRewards++)
        {
            var relics = SimulateExactAct2FirstShop(
                root,
                dataset,
                normalizedSeed,
                seedValue,
                ascension,
                character,
                act1OpeningOption,
                buyMiniatureTent: boughtTent == 1,
                extraPaelsGrowthRewardDraws: extraRewards);
            if (relics.Count == 3 && target.SetEquals(relics))
            {
                return $"buyTent={boughtTent == 1}, paelsReward+{extraRewards} => {string.Join(" / ", relics)}";
            }
        }
    }

    return "no-hit";
}

static string FindMatchingExactAct1FirstShopOffsets(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var target = new HashSet<string>(["SHOVEL", "STRIKE_DUMMY", "MINIATURE_TENT"], StringComparer.OrdinalIgnoreCase);
    for (var extraRewards = 0; extraRewards <= 32; extraRewards++)
    {
        var shop = SimulateExactAct1FirstShop(
            root,
            dataset,
            normalizedSeed,
            seedValue,
            ascension,
            character,
            act1OpeningOption,
            extraRewards);
        if (shop.Count == 3 && target.SetEquals(shop))
        {
            return $"reward+{extraRewards} => {string.Join(" / ", shop)}";
        }
    }

    return "no-hit";
}

static string FindMatchingAct1PostShopEliteOffsets(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var results = new List<string>();
    for (var extraRewards = 0; extraRewards <= 12; extraRewards++)
    {
        var relicId = SimulateAct1PostShopEliteRelic(
            root,
            dataset,
            normalizedSeed,
            seedValue,
            ascension,
            character,
            act1OpeningOption,
            extraRewards);
        if (string.Equals(relicId, "AKABEKO", StringComparison.OrdinalIgnoreCase))
        {
            results.Add($"reward+{extraRewards}");
        }
    }

    return results.Count > 0
        ? string.Join(", ", results)
        : "no-hit";
}

static string DescribeAct1PostShopEliteOffsets(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var results = new List<string>();
    for (var extraRewards = 0; extraRewards <= 12; extraRewards++)
    {
        results.Add(SimulateAct1PostShopEliteTrace(
            root,
            dataset,
            normalizedSeed,
            seedValue,
            ascension,
            character,
            act1OpeningOption,
            extraRewards));
    }

    return string.Join(" || ", results);
}

static string DescribeAct1TreasureRewardMatrix(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var results = new List<string>();
    foreach (var firstTreasureUsesRewards in new[] { false, true })
    {
        foreach (var secondTreasureUsesRewards in new[] { false, true })
        {
            results.Add(SimulateAct1TreasureRewardMatrixEntry(
                root,
                dataset,
                normalizedSeed,
                seedValue,
                ascension,
                character,
                act1OpeningOption,
                firstTreasureUsesRewards,
                secondTreasureUsesRewards));
        }
    }

    return string.Join(" || ", results);
}

static string DescribeAct1TreasureBagImpact(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var factoryCreate = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing factory Create.");
    var state = factoryCreate.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create reward state.");
    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var playerBagProperty = stateType.GetProperty("PlayerBag", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PlayerBag.");

    consumeRegularCombat.Invoke(state, null);
    var beforeTreasureBag = playerBagProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing player bag before treasure.");
    var beforeTreasureTail = DescribeBucketTail(beforeTreasureBag, "Common");
    _ = showTreasure.Invoke(state, [1]);
    var afterTreasureBag = playerBagProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing player bag after treasure.");
    var afterTreasureTail = DescribeBucketTail(afterTreasureBag, "Common");
    consumeRegularCombat.Invoke(state, null);
    var exactShop = ((IEnumerable)(showShop.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing exact shop.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .ToList();

    var standardState = BuildStandardShopState(root, dataset, normalizedSeed, seedValue, ascension, character, act1OpeningOption);
    var standardType = standardState.GetType();
    var standardBagProperty = standardType.GetProperty("PlayerRelicBag", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing standard PlayerRelicBag.");
    var standardBag = standardBagProperty.GetValue(standardState)
        ?? throw new InvalidOperationException("Missing standard bag.");
    var standardBeforeTail = DescribeStandardBucketTail(standardBag, "Common");
    var standardRemove = standardBag.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing standard Remove.");
    standardRemove.Invoke(standardBag, ["BOOK_OF_FIVE_RINGS"]);
    var standardAfterTail = DescribeStandardBucketTail(standardBag, "Common");
    var buildShopPreview = standardType.DeclaringType?.GetMethod("BuildShopPreview", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing BuildShopPreview.");
    var shopPreviewRequestType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.ShopPreviewRequest")
        ?? throw new InvalidOperationException("Missing ShopPreviewRequest.");
    var fullRequest = shopPreviewRequestType.GetProperty("Full", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException("Missing ShopPreviewRequest.Full.");
    var standardPreview = buildShopPreview.Invoke(
        Activator.CreateInstance(
            standardType.DeclaringType!,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [dataset, world],
            culture: null),
        [standardState, fullRequest]);
    var standardRelics = ((IEnumerable)(standardPreview?.GetType().GetProperty("Relics", BindingFlags.Instance | BindingFlags.Public)?.GetValue(standardPreview)
        ?? throw new InvalidOperationException("Missing standard preview relics.")))
        .Cast<object>()
        .Select(item => item.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item)?.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();

    return $"exact.cTail={beforeTreasureTail}->{afterTreasureTail};exact.shop={string.Join("/", exactShop)};standard.cTail={standardBeforeTail}->{standardAfterTail};standard.shop={string.Join("/", standardRelics)}";
}

static string SimulateAct1PostShopEliteRelic(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    int selfHelpExtraRewards)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var factoryCreateMethod = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing factory Create.");
    var state = factoryCreateMethod.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeRewardRng = stateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var showElite = stateType.GetMethod("ShowElite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowElite.");

    // 1777165965.run act1 known path before first elite:
    // Monster -> Treasure -> Monster -> SunkenTreasury -> Shop -> SelfHelpBook -> DrowningBeacon -> Rest -> Treasure -> Rest -> Elite
    consumeRegularCombat.Invoke(state, null);
    _ = showTreasure.Invoke(state, [1]);
    consumeRegularCombat.Invoke(state, null);
    _ = showShop.Invoke(state, [1]);
    if (selfHelpExtraRewards > 0)
    {
        consumeRewardRng.Invoke(state, [selfHelpExtraRewards]);
    }
    _ = showTreasure.Invoke(state, [1]);
    var eliteRelics = ((IEnumerable)(showElite.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing act1 elite.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();

    return eliteRelics.FirstOrDefault() ?? string.Empty;
}

static string SimulateAct1TreasureRewardMatrixEntry(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    bool firstTreasureUsesRewards,
    bool secondTreasureUsesRewards)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var state = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)?.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShopEntries = stateType.GetMethod("ShowShopEntries", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShopEntries.");
    var consumeRewardRng = stateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var showElite = stateType.GetMethod("ShowElite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowElite.");
    var rewardsRngProperty = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng.");
    var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing rewards rng."));

    // Monster -> Treasure -> Monster -> Sunken Treasury -> Shop -> Self Help (read entire book) -> Drowning Beacon -> Rest -> Treasure -> Rest -> Elite
    consumeRegularCombat.Invoke(state, null);
    if (firstTreasureUsesRewards)
    {
        _ = rewardsRng.NextInt(42, 53);
    }

    _ = showTreasure.Invoke(state, [1]);
    consumeRegularCombat.Invoke(state, null);
    var firstShopEntries = ((IEnumerable)(showShopEntries.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing shop entries.")))
        .Cast<object>()
        .Select(item => item.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item)?.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
    consumeRewardRng.Invoke(state, [5]);
    if (secondTreasureUsesRewards)
    {
        _ = rewardsRng.NextInt(42, 53);
    }

    _ = showTreasure.Invoke(state, [1]);
    var eliteRelic = ((IEnumerable)(showElite.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing act1 elite.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? string.Empty;

    return $"t1={(firstTreasureUsesRewards ? "reward" : "skip")},t2={(secondTreasureUsesRewards ? "reward" : "skip")}=>shop={string.Join("/", firstShopEntries)};elite={eliteRelic}";
}

static string SimulateAct1PostShopEliteTrace(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    int selfHelpExtraRewards)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var factoryCreateMethod = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing factory Create.");
    var state = factoryCreateMethod.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeRewardRng = stateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var showElite = stateType.GetMethod("ShowElite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowElite.");
    var rewardsRngProperty = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing RewardsRng.");
    var shopsRngProperty = stateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShopsRng.");
    var cardRareOffsetProperty = stateType.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRareOffset.");
    var potionChanceProperty = stateType.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionChance.");
    var playerBagProperty = stateType.GetProperty("PlayerBag", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PlayerBag.");

    consumeRegularCombat.Invoke(state, null);
    _ = showTreasure.Invoke(state, [1]);
    consumeRegularCombat.Invoke(state, null);
    _ = showShop.Invoke(state, [1]);
    if (selfHelpExtraRewards > 0)
    {
        consumeRewardRng.Invoke(state, [selfHelpExtraRewards]);
    }
    _ = showTreasure.Invoke(state, [1]);

    var playerBag = playerBagProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing player bag.");
    var eliteRelics = ((IEnumerable)(showElite.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing act1 elite.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();

    var rewardsRng = (GameRng)(rewardsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing rewards rng."));
    var shopsRng = (GameRng)(shopsRngProperty.GetValue(state)
        ?? throw new InvalidOperationException("Missing shops rng."));
    var uncommonHead = DescribeBucketHead(playerBag, "Uncommon");
    var commonHead = DescribeBucketHead(playerBag, "Common");
    return $"reward+{selfHelpExtraRewards}:elite={eliteRelics.FirstOrDefault() ?? "-"};rewards={rewardsRng.Counter};shops={shopsRng.Counter};rareOffset={cardRareOffsetProperty.GetValue(state)};potionChance={potionChanceProperty.GetValue(state)};u={uncommonHead};c={commonHead}";
}

static string DescribeBucketHead(object bag, string rarity)
{
    var bucketsField = bag.GetType().GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing bag buckets.");
    var buckets = (IDictionary)(bucketsField.GetValue(bag)
        ?? throw new InvalidOperationException("Missing buckets value."));
    foreach (DictionaryEntry entry in buckets)
    {
        if (!string.Equals(entry.Key?.ToString(), rarity, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var items = ((IEnumerable)entry.Value).Cast<object>().Select(item => item.ToString() ?? string.Empty).Take(3);
        return string.Join(",", items);
    }

    return string.Empty;
}

static string DescribeBucketTail(object bag, string rarity)
{
    var bucketsField = bag.GetType().GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing bag buckets.");
    var buckets = (IDictionary)(bucketsField.GetValue(bag)
        ?? throw new InvalidOperationException("Missing buckets value."));
    foreach (DictionaryEntry entry in buckets)
    {
        if (!string.Equals(entry.Key?.ToString(), rarity, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var items = ((IEnumerable)entry.Value).Cast<object>().Select(item => item.ToString() ?? string.Empty).Reverse().Take(3).Reverse();
        return string.Join(",", items);
    }

    return string.Empty;
}

static string DescribeUnknownOdds(object? odds)
{
    if (odds is not IEnumerable entries)
    {
        return "()";
    }

    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in entries)
    {
        if (entry == null)
        {
            continue;
        }

        var type = entry.GetType();
        var key = type.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty;
        var value = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(key))
        {
            values[key] = value;
        }
    }

    return $"({values.GetValueOrDefault("Monster", "?")},{values.GetValueOrDefault("Elite", "?")},{values.GetValueOrDefault("Treasure", "?")},{values.GetValueOrDefault("Shop", "?")})";
}

static IReadOnlyList<string> SimulateExactAct1FirstShop(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    int extraRewards)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var factoryCreateMethod = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing factory Create.");
    var state = factoryCreateMethod.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeRewardRng = stateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var applyHatchRestSite = stateType.GetMethod("ApplyHatchRestSite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyHatchRestSite.");

    consumeRegularCombat.Invoke(state, null);
    consumeRewardRng.Invoke(state, [4]);
    consumeRegularCombat.Invoke(state, null);
    consumeRegularCombat.Invoke(state, null);
    applyHatchRestSite.Invoke(state, null);
    consumeRegularCombat.Invoke(state, null);
    _ = showTreasure.Invoke(state, [1]);
    if (extraRewards > 0)
    {
        consumeRewardRng.Invoke(state, [extraRewards]);
    }

    return ((IEnumerable)(showShop.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing act1 first shop.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .ToList();
}

static IReadOnlyList<string> SimulateExactAct2FirstShop(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    NeowOptionResult? act1OpeningOption,
    bool buyMiniatureTent,
    int extraPaelsGrowthRewardDraws)
{
    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var optionPath = Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json");
    var actPath = Path.Combine(root, "data", "0.103.2", "sts2", "acts.json");
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        Act1OpeningOption = act1OpeningOption
    };

    var factoryType = assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
    var factory = Activator.CreateInstance(
            factoryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [world, dataset, request],
            culture: null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var factoryCreateMethod = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing factory Create.");
    var state = factoryCreateMethod.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var stateType = state.GetType();
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeRewardRng = stateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
    var showTreasure = stateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowTreasure.");
    var showShop = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop.");
    var applyHatchRestSite = stateType.GetMethod("ApplyHatchRestSite", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyHatchRestSite.");
    var obtainMany = stateType.GetMethod("Obtain", [typeof(IEnumerable<string>)])
        ?? throw new InvalidOperationException("Missing Obtain(IEnumerable<string>).");
    var obtainOne = stateType.GetMethod("Obtain", [typeof(string)])
        ?? throw new InvalidOperationException("Missing Obtain(string).");

    consumeRegularCombat.Invoke(state, null);
    consumeRewardRng.Invoke(state, [4]);
    consumeRegularCombat.Invoke(state, null);
    consumeRegularCombat.Invoke(state, null);
    applyHatchRestSite.Invoke(state, null);
    consumeRegularCombat.Invoke(state, null);
    _ = showTreasure.Invoke(state, [1]);
    var act1FirstShop = ((IEnumerable)(showShop.Invoke(state, [1])
        ?? throw new InvalidOperationException("Missing act1 first shop.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .ToList();
    if (buyMiniatureTent && act1FirstShop.Contains("MINIATURE_TENT", StringComparer.OrdinalIgnoreCase))
    {
        obtainOne.Invoke(state, ["MINIATURE_TENT"]);
    }

    consumeRegularCombat.Invoke(state, null);
    _ = showShop.Invoke(state, [1]);

    obtainMany.Invoke(state, [new[] { "PAELS_GROWTH" }]);
    if (extraPaelsGrowthRewardDraws > 0)
    {
        consumeRewardRng.Invoke(state, [extraPaelsGrowthRewardDraws]);
    }

    consumeRegularCombat.Invoke(state, null);
    consumeRegularCombat.Invoke(state, null);

    return ((IEnumerable)(showShop.Invoke(state, [2])
        ?? throw new InvalidOperationException("Missing act2 first shop.")))
        .Cast<object>()
        .Select(item => item.ToString() ?? string.Empty)
        .ToList();
}

static string VerifyAct1Floor7GoldHypothesis(
    string root,
    NeowOptionDataset dataset,
    string normalizedSeed,
    uint seedValue,
    int ascension,
    CharacterId character,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap,
    NeowOptionResult? act1OpeningOption)
{
    if (!actPoolMap.TryGetValue(1, out var act1Pool))
    {
        return "no-act1-pool";
    }

    var assembly = typeof(Sts2ExactRouteAnalysis).Assembly;
    var modelType = assembly.GetType("SeedModel.Sts2.Sts2EventVisibilitySimulationModel")
        ?? throw new InvalidOperationException("Missing simulation model type.");
    var modelCreate = modelType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing simulation model Create.");
    var model = modelCreate.Invoke(null, [dataset, character, new[]
    {
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    }, 1, ascension, root])
        ?? throw new InvalidOperationException("Failed to create simulation model.");

    var stateType = assembly.GetType("SeedModel.Sts2.Sts2EventProgressState")
        ?? throw new InvalidOperationException("Missing event state type.");
    var createState = stateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing event state Create.");
    var startAct = stateType.GetMethod("StartAct", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing StartAct.");
    var applyAct1 = stateType.GetMethod("ApplyAct1OpeningOption", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyAct1OpeningOption.");
    var consumeRegularCombat = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public, null, [typeof(GameRng)], null)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
    var consumeTreasureStop = stateType.GetMethod("ConsumeTreasureStop", [typeof(GameRng)])
        ?? throw new InvalidOperationException("Missing ConsumeTreasureStop.");
    var applyShownEvent = stateType.GetMethod("ApplyShownEvent", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ApplyShownEvent.");
    var totalFloorProperty = stateType.GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TotalFloor.");
    var currentGoldProperty = stateType.GetProperty("CurrentGold", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentGold.");
    var currentHpProperty = stateType.GetProperty("CurrentHp", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentHp.");

    var pullEngineType = assembly.GetType("SeedModel.Sts2.Sts2EventPullEngine")
        ?? throw new InvalidOperationException("Missing event pull engine type.");
    var consumeShownEvent = pullEngineType.GetMethod("ConsumeShownEvent", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeShownEvent.");
    var peekNextAllowedEvent = pullEngineType.GetMethod("PeekNextAllowedEvent", BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PeekNextAllowedEvent.");

    object BuildState(int? overrideGold = null)
    {
        var state = createState.Invoke(null, [model, character, 1])
            ?? throw new InvalidOperationException("Failed to create event state.");
        applyAct1.Invoke(state, [act1OpeningOption]);
        startAct.Invoke(state, [1, 1]);
        totalFloorProperty.SetValue(state, 1);

        var rng = new GameRng(seedValue, "explicit_route_probe");
        totalFloorProperty.SetValue(state, 2);
        consumeRegularCombat.Invoke(state, [rng]);
        totalFloorProperty.SetValue(state, 3);
        consumeTreasureStop.Invoke(state, [rng]);
        totalFloorProperty.SetValue(state, 4);
        consumeRegularCombat.Invoke(state, [rng]);
        totalFloorProperty.SetValue(state, 5);
        consumeShownEvent.Invoke(null, [act1Pool, "SUNKEN_TREASURY", state]);
        applyShownEvent.Invoke(state, ["SUNKEN_TREASURY", rng]);
        totalFloorProperty.SetValue(state, 6);
        totalFloorProperty.SetValue(state, 7);
        consumeShownEvent.Invoke(null, [act1Pool, "SELF_HELP_BOOK", state]);
        applyShownEvent.Invoke(state, ["SELF_HELP_BOOK", rng]);

        if (overrideGold.HasValue)
        {
            currentGoldProperty.SetValue(state, overrideGold.Value);
        }

        return state;
    }

    var baselineState = BuildState();
    var actualGoldState = BuildState(37);
    var baselineNext = peekNextAllowedEvent.Invoke(null, [act1Pool, baselineState])?.ToString() ?? string.Empty;
    var actualGoldNext = peekNextAllowedEvent.Invoke(null, [act1Pool, actualGoldState])?.ToString() ?? string.Empty;
    var baselineGold = currentGoldProperty.GetValue(baselineState)?.ToString() ?? "?";
    var actualGold = currentGoldProperty.GetValue(actualGoldState)?.ToString() ?? "?";
    var hp = currentHpProperty.GetValue(actualGoldState)?.ToString() ?? "?";
    return $"baselineGold={baselineGold} next={baselineNext} | actualGold={actualGold} hp={hp} next={actualGoldNext}";
}

sealed record RouteCandidate(object RawRoute, IReadOnlyList<string> Types);

sealed class SaveSnapshot
{
    [JsonPropertyName("seed")]
    public string? Seed { get; set; }

    [JsonPropertyName("ascension")]
    public int Ascension { get; set; }

    [JsonPropertyName("map_point_history")]
    public List<List<SaveMapPoint>?>? MapPointHistory { get; set; }

    [JsonPropertyName("players")]
    public List<SavePlayer>? Players { get; set; }
}

sealed class SavePlayer
{
    [JsonPropertyName("character")]
    public string? Character { get; set; }
}

sealed class SaveMapPoint
{
    [JsonPropertyName("map_point_type")]
    public string? MapPointType { get; set; }

    [JsonPropertyName("rooms")]
    public List<SaveRoom>? Rooms { get; set; }

    [JsonPropertyName("player_stats")]
    public List<SavePlayerStats>? PlayerStats { get; set; }
}

sealed class SaveRoom
{
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("room_type")]
    public string? RoomType { get; set; }
}

sealed class SavePlayerStats
{
    [JsonPropertyName("current_gold")]
    public int? CurrentGold { get; set; }

    [JsonPropertyName("current_hp")]
    public int? CurrentHp { get; set; }

    [JsonPropertyName("relic_choices")]
    public List<SaveRelicChoice>? RelicChoices { get; set; }

    [JsonPropertyName("cards_removed")]
    public List<SaveCardRef>? CardsRemoved { get; set; }

    [JsonPropertyName("cards_gained")]
    public List<SaveCardRef>? CardsGained { get; set; }

    [JsonPropertyName("card_choices")]
    public List<SaveCardChoice>? CardChoices { get; set; }

    [JsonPropertyName("bought_relics")]
    public List<string>? BoughtRelics { get; set; }

    [JsonPropertyName("bought_colorless")]
    public List<string>? BoughtColorless { get; set; }

    [JsonPropertyName("event_choices")]
    public List<SaveEventChoice>? EventChoices { get; set; }

    [JsonPropertyName("potion_choices")]
    public List<SavePotionChoice>? PotionChoices { get; set; }

    [JsonPropertyName("potion_used")]
    public List<string>? PotionUsed { get; set; }

    [JsonPropertyName("potion_discarded")]
    public List<string>? PotionDiscarded { get; set; }

    [JsonPropertyName("bought_potions")]
    public List<string>? BoughtPotions { get; set; }
}

sealed class SaveCardRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

sealed class SaveCardChoice
{
    [JsonPropertyName("card")]
    public SaveCardRef? Card { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SaveRelicChoice
{
    [JsonPropertyName("choice")]
    public string? Choice { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SavePotionChoice
{
    [JsonPropertyName("choice")]
    public string? Choice { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SaveEventChoice
{
    [JsonPropertyName("title")]
    public SaveLocalizedRef? Title { get; set; }
}

sealed class SaveLocalizedRef
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}

sealed record ActualActSnapshot(int ActNumber, IReadOnlyList<ActualRoomSnapshot> Rooms);

sealed record ActualRoomSnapshot(int ActNumber, int Floor, string RoomType, string EventId, IReadOnlyList<string> RelicIds, SavePlayerStats? SourceStats = null);

sealed record SimulatedActSnapshot(int ActNumber, IReadOnlyList<SimulatedRoomSnapshot> Rooms, string AncientId = "", IReadOnlyList<string>? AncientRelics = null);

sealed record SimulatedRoomSnapshot(int ActNumber, int Floor, string RoomType, string EventId, IReadOnlyList<string> RelicIds, IReadOnlyList<string> DisplayRelicIds);

enum ShopCardSlotKind
{
    Colored,
    Colorless
}

sealed record ShopCardSlot(string CardId, ShopCardSlotKind Kind, string? CardType, string? ColorlessRarity);

sealed record ShopCardSnapshot(IReadOnlyList<ShopCardSlot> Slots);

sealed record RouteComparison(
    int Act1RouteIndex,
    int? Act2RouteIndex,
    int Score,
    IReadOnlyList<string> SampleLines,
    IReadOnlyList<string> Mismatches);
