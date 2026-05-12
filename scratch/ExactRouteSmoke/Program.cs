using SeedModel.Neow;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedModel.Sts2;
using System.Reflection;

var root = Directory.GetCurrentDirectory();
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(root, "data", "0.103.2", "neow", "options.json"));
var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

var seedText = GetArg("--seed") ?? "AAAAAAAAAA";
if (!SeedFormatter.TryNormalize(seedText, out var normalizedSeed, out var error))
{
    throw new InvalidOperationException(error);
}

var character = ParseCharacter(GetArg("--character") ?? "Silent");
var ascension = int.TryParse(GetArg("--ascension"), out var parsedAscension) ? parsedAscension : 0;
var maxResults = int.TryParse(GetArg("--max-results"), out var parsedMaxResults) ? parsedMaxResults : 5;
var maxChecks = long.TryParse(GetArg("--max-checks"), out var parsedMaxChecks) ? parsedMaxChecks : 500_000;
var printMatches = HasFlag("--print-matches");
var shopStrategy = ParseShopStrategy(GetArg("--shop-strategy"));
var relicTargets = GetArgs("--relic")
    .Select(ParseRelicTarget)
    .ToList();
var summaryRelics = GetArgs("--summary-relic")
    .Select(raw => raw.Trim().ToUpperInvariant())
    .Where(raw => !string.IsNullOrWhiteSpace(raw))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
var summaryShownRelics = GetArgs("--summary-shown-relic")
    .Select(raw => raw.Trim().ToUpperInvariant())
    .Where(raw => !string.IsNullOrWhiteSpace(raw))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

var result = previewer.AnalyzeExactRoutes(
    dataset,
    new Sts2ExactRouteAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = SeedFormatter.ToUIntSeed(normalizedSeed),
        Character = character,
        AscensionLevel = ascension,
        PlayerCount = 1,
        ShopStrategy = shopStrategy,
        RelicTargets = relicTargets,
        MaxResults = maxResults,
        MaxRouteChecks = maxChecks
    });

var context = new SeedRunEvaluationContext
{
    SeedText = normalizedSeed,
    RunSeed = SeedFormatter.ToUIntSeed(normalizedSeed),
    Character = character,
    UnlockedCharacters =
    [
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    ],
    PlayerCount = 1,
    AscensionLevel = ascension
};
var firstShop = previewer.PreviewFirstShop(dataset, context, Array.Empty<NeowOptionResult>());

Console.WriteLine($"seed={normalizedSeed} character={character} asc={ascension}");
Console.WriteLine($"shopStrategy={shopStrategy}");
Console.WriteLine($"relicTargets={string.Join(", ", relicTargets.Select(target => $"{(target.ActNumber.HasValue ? $"A{target.ActNumber.Value}" : "Any")}:{target.RelicId}"))}");
Console.WriteLine($"checked={result.CheckedRoutes} found={result.FoundRouteCount} truncated={result.WasTruncated}");
Console.WriteLine($"firstShopRoute={string.Join(" -> ", firstShop.RouteRooms)}");
Console.WriteLine($"firstShopRelics={string.Join("/", firstShop.Relics.Select(entry => entry.Id))}");
Console.WriteLine($"exactStateFirstShopRelics={string.Join("/", DebugExactStateFirstShop(previewer, dataset, normalizedSeed, character, ascension))}");
if (summaryRelics.Count > 0)
{
    PrintRouteSummary(result, summaryRelics);
}
if (summaryShownRelics.Count > 0)
{
    PrintShownRelicSummary(result, summaryShownRelics);
}
if (printMatches || (summaryRelics.Count == 0 && summaryShownRelics.Count == 0))
{
    foreach (var match in result.Matches)
    {
        Console.WriteLine("=== Match ===");
        foreach (var act in match.Acts)
        {
            Console.WriteLine($"Act {act.ActNumber}");
            foreach (var room in act.Rooms.Where(room =>
                         !string.IsNullOrWhiteSpace(room.EventId) ||
                         room.RelicIds.Count > 0 ||
                         room.DisplayRelicIds.Count > 0))
            {
                var displayRelics = room.DisplayRelicIds.Count > 0
                    ? $" shown={string.Join("/", room.DisplayRelicIds)}"
                    : string.Empty;
                Console.WriteLine($"  row={room.Row} col={room.Col} type={room.RoomType} event={room.EventId} relics={string.Join("/", room.RelicIds)}{displayRelics}");
            }
        }
    }
}

static string? GetArg(string name)
{
    for (var i = 0; i < Environment.GetCommandLineArgs().Length - 1; i++)
    {
        if (string.Equals(Environment.GetCommandLineArgs()[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetCommandLineArgs()[i + 1];
        }
    }

    return null;
}

static IEnumerable<string> GetArgs(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            yield return args[i + 1];
        }
    }
}

static bool HasFlag(string name)
{
    return Environment.GetCommandLineArgs()
        .Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
}

static CharacterId ParseCharacter(string raw)
{
    return Enum.TryParse<CharacterId>(raw, ignoreCase: true, out var character)
        ? character
        : throw new InvalidOperationException($"Unknown character: {raw}");
}

static Sts2ExactRouteShopStrategy ParseShopStrategy(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return Sts2ExactRouteShopStrategy.TargetRelicsOnly;
    }

    return Enum.TryParse<Sts2ExactRouteShopStrategy>(raw, ignoreCase: true, out var strategy)
        ? strategy
        : throw new InvalidOperationException($"Unknown shop strategy: {raw}");
}

static Sts2ExactRouteRelicTargetRequest ParseRelicTarget(string raw)
{
    var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
    if (parts.Length == 2 &&
        parts[0].StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(parts[0][1..], out var actNumber))
    {
        return new Sts2ExactRouteRelicTargetRequest(actNumber, parts[1].Trim().ToUpperInvariant());
    }

    return new Sts2ExactRouteRelicTargetRequest(null, raw.Trim().ToUpperInvariant());
}

static IReadOnlyList<string> DebugExactStateFirstShop(
    Sts2RunPreviewer previewer,
    NeowOptionDataset dataset,
    string seedText,
    CharacterId character,
    int ascensionLevel)
{
    var previewerType = typeof(Sts2RunPreviewer);
    var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing _world field.");
    var world = worldField.GetValue(previewer)
        ?? throw new InvalidOperationException("Missing world value.");

    var request = new Sts2ExactRouteAnalysisRequest
    {
        SeedText = seedText,
        SeedValue = SeedFormatter.ToUIntSeed(seedText),
        Character = character,
        AscensionLevel = ascensionLevel,
        PlayerCount = 1
    };

    var analyzerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer")
        ?? throw new InvalidOperationException("Missing Sts2ExactRouteAnalyzer type.");
    var factoryType = analyzerType.GetNestedType("RewardRelicStateFactory", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing RewardRelicStateFactory type.");
    var stateType = analyzerType.GetNestedType("RewardRelicState", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing RewardRelicState type.");

    var factory = Activator.CreateInstance(
        factoryType,
        world,
        dataset,
        request)
        ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
    var createMethod = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing Create method.");
    var state = createMethod.Invoke(factory, null)
        ?? throw new InvalidOperationException("Failed to create RewardRelicState.");

    var consumeRegularCombatMethod = stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ConsumeRegularCombat method.");
    consumeRegularCombatMethod.Invoke(state, null);
    consumeRegularCombatMethod.Invoke(state, null);

    var showShopMethod = stateType.GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing ShowShop method.");
    var relics = (IReadOnlyList<string>?)showShopMethod.Invoke(state, [1]);
    return relics ?? Array.Empty<string>();
}

static void PrintRouteSummary(
    Sts2ExactRouteAnalysis result,
    IReadOnlyList<string> summaryRelics)
{
    Console.WriteLine("summary:");
    Console.WriteLine($"  routeMatches={result.Matches.Count}");

    foreach (var targetRelic in summaryRelics)
    {
        var routeHits = 0;
        var roomHits = 0;
        var byAct = new Dictionary<int, int>();
        var byRoomType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var examples = new List<string>();

        foreach (var match in result.Matches)
        {
            var hitRooms = match.Acts
                .SelectMany(act => act.Rooms.Select(room => (act.ActNumber, Room: room)))
                .Where(entry => entry.Room.RelicIds.Any(relicId => RouteIdEquals(relicId, targetRelic)))
                .ToList();

            if (hitRooms.Count == 0)
            {
                continue;
            }

            routeHits++;
            roomHits += hitRooms.Count;

            foreach (var (actNumber, room) in hitRooms)
            {
                byAct[actNumber] = byAct.TryGetValue(actNumber, out var actCount) ? actCount + 1 : 1;
                var roomType = room.RoomType ?? "Unknown";
                byRoomType[roomType] = byRoomType.TryGetValue(roomType, out var roomTypeCount) ? roomTypeCount + 1 : 1;
            }

            if (examples.Count < 5)
            {
                var room = hitRooms[0].Room;
                examples.Add(
                    $"A{hitRooms[0].ActNumber}:{room.Row}:{room.Col}:{room.RoomType}:{string.Join("/", room.RelicIds)}");
            }
        }

        Console.WriteLine($"  {targetRelic}: routes={routeHits}, rooms={roomHits}");
        if (routeHits == 0)
        {
            continue;
        }

        Console.WriteLine($"    byAct={string.Join(", ", byAct.OrderBy(pair => pair.Key).Select(pair => $"A{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"    byRoomType={string.Join(", ", byRoomType.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"    examples={string.Join(" | ", examples)}");
    }
}

static void PrintShownRelicSummary(
    Sts2ExactRouteAnalysis result,
    IReadOnlyList<string> summaryRelics)
{
    Console.WriteLine("shown-summary:");
    Console.WriteLine($"  routeMatches={result.Matches.Count}");

    foreach (var targetRelic in summaryRelics)
    {
        var routeHits = 0;
        var roomHits = 0;
        var byAct = new Dictionary<int, int>();
        var byRoomType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var examples = new List<string>();

        foreach (var match in result.Matches)
        {
            var hitRooms = match.Acts
                .SelectMany(act => act.Rooms.Select(room => (act.ActNumber, Room: room)))
                .Where(entry => entry.Room.DisplayRelicIds.Any(relicId => RouteIdEquals(relicId, targetRelic)))
                .ToList();

            if (hitRooms.Count == 0)
            {
                continue;
            }

            routeHits++;

            foreach (var hit in hitRooms)
            {
                roomHits++;
                byAct[hit.ActNumber] = byAct.TryGetValue(hit.ActNumber, out var actCount) ? actCount + 1 : 1;
                var roomType = string.IsNullOrWhiteSpace(hit.Room.RoomType) ? "(none)" : hit.Room.RoomType;
                byRoomType[roomType] = byRoomType.TryGetValue(roomType, out var roomTypeCount) ? roomTypeCount + 1 : 1;

                if (examples.Count < 5)
                {
                    examples.Add($"A{hit.ActNumber}:row{hit.Room.Row}:{hit.Room.RoomType}:{string.Join("/", hit.Room.DisplayRelicIds)}");
                }
            }
        }

        Console.WriteLine($"  {targetRelic}: routes={routeHits}, rooms={roomHits}");
        if (byAct.Count > 0)
        {
            Console.WriteLine($"    byAct={string.Join(", ", byAct.OrderBy(entry => entry.Key).Select(entry => $"A{entry.Key}={entry.Value}"))}");
        }

        if (byRoomType.Count > 0)
        {
            Console.WriteLine($"    byRoomType={string.Join(", ", byRoomType.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"))}");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine($"    examples={string.Join(" | ", examples)}");
        }
    }
}

static bool RouteIdEquals(string? left, string? right)
{
    return string.Equals(NormalizeRouteId(left), NormalizeRouteId(right), StringComparison.Ordinal);
}

static string NormalizeRouteId(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
