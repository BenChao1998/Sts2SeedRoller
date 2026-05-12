using System.Collections;
using System.Reflection;
using System.Text;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

Console.OutputEncoding = Encoding.UTF8;

var options = ProbeOptions.Parse(args);
var workspaceRoot = options.WorkspaceRoot;
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(workspaceRoot, "data", "0.103.2", "neow", "options.json"));
var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(workspaceRoot, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(workspaceRoot, "data", "0.103.2", "sts2", "acts.json"));

var pools = previewer.AnalyzePools(new Sts2SeedAnalysisRequest
{
    SeedText = options.SeedText,
    SeedValue = options.SeedValue,
    Character = options.Character,
    AscensionLevel = options.AscensionLevel,
    IncludeDarvSharedAncient = true
});
var actPools = pools.Acts.ToDictionary(act => act.ActNumber, act => (IReadOnlyList<string>)act.EventPool, EqualityComparer<int>.Default);

var preview = previewer.Preview(new Sts2RunRequest
{
    SeedText = options.SeedText,
    SeedValue = options.SeedValue,
    Character = options.Character,
    UnlockedCharacters =
    [
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    ],
    AscensionLevel = options.AscensionLevel,
    PlayerCount = 1,
    IncludeDarvSharedAncient = true,
    IncludeAct2 = true,
    IncludeAct3 = true
});
var ancientByAct = preview.Acts.ToDictionary(
    act => act.ActNumber,
    act => new AncientInfo(
        act.AncientId ?? string.Empty,
        act.AncientOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
            .Select(option => option.RelicId!)
            .ToList()),
    EqualityComparer<int>.Default);

var mapFactory = new MapFactory(previewer);
var actRoutes = mapFactory.BuildRoutes(options.SeedValue);
var simulator = new RouteSimulator(previewer, dataset, actPools, ancientByAct, options);

var matches = new List<RouteMatch>();
var checkedCount = 0L;
var stop = false;
for (var i1 = 0; i1 < actRoutes[1].Count && !stop; i1++)
{
    for (var i2 = 0; i2 < actRoutes[2].Count && !stop; i2++)
    {
        for (var i3 = 0; i3 < actRoutes[3].Count && !stop; i3++)
        {
            checkedCount++;
            var route = new[]
            {
                actRoutes[1][i1],
                actRoutes[2][i2],
                actRoutes[3][i3]
            };
            var result = simulator.TrySimulate(route);
            if (result != null)
            {
                matches.Add(result);
                if (matches.Count >= options.MaxResults)
                {
                    stop = true;
                    break;
                }
            }

            if (checkedCount >= options.MaxChecks)
            {
                stop = true;
                break;
            }
        }
    }
}

Console.WriteLine($"Seed={options.SeedText} Character={options.Character} Asc={options.AscensionLevel}");
Console.WriteLine($"TargetEvent={(options.TargetEvent == null ? "(none)" : $"A{options.TargetEvent.ActNumber}:{options.TargetEvent.EventId}")}");
Console.WriteLine($"TargetRelic={(options.TargetRelic == null ? "(none)" : $"{(options.TargetRelic.ActNumber.HasValue ? $"A{options.TargetRelic.ActNumber.Value}:" : string.Empty)}{options.TargetRelic.RelicId}")}");
Console.WriteLine($"CheckedRoutes={checkedCount}");
Console.WriteLine($"FoundMatches={matches.Count}");
if (checkedCount >= options.MaxChecks)
{
    Console.WriteLine($"Truncated=true (max-checks={options.MaxChecks})");
}

Console.WriteLine();
if (matches.Count == 0)
{
    Console.WriteLine("No matching routes found in the checked route combinations.");
    return;
}

for (var index = 0; index < matches.Count; index++)
{
    var match = matches[index];
    Console.WriteLine($"=== Match #{index + 1} ===");
    foreach (var act in match.Acts.OrderBy(item => item.ActNumber))
    {
        Console.WriteLine($"Act {act.ActNumber}");
        if (!string.IsNullOrWhiteSpace(act.AncientId))
        {
            var ancientRelics = act.AncientRelics.Count == 0 ? "(none)" : string.Join(", ", act.AncientRelics);
            Console.WriteLine($"  Ancient: {act.AncientId} | relics: {ancientRelics}");
        }

        foreach (var room in act.Rooms)
        {
            var roomSummary = $"{room.Row}:{room.Col} {room.PointType}->{room.RoomType}";
            if (!string.IsNullOrWhiteSpace(room.EventId))
            {
                roomSummary += $" | event={room.EventId}";
            }

            if (room.RelicIds.Count > 0)
            {
                roomSummary += $" | relics={string.Join("/", room.RelicIds)}";
            }

            Console.WriteLine($"  {roomSummary}");
        }
    }

    Console.WriteLine();
}

sealed record ProbeOptions(
    string WorkspaceRoot,
    string SeedText,
    uint SeedValue,
    CharacterId Character,
    int AscensionLevel,
    EventTarget? TargetEvent,
    RelicTarget? TargetRelic,
    int MaxResults,
    long MaxChecks)
{
    public static ProbeOptions Parse(string[] args)
    {
        var workspaceRoot = Directory.GetCurrentDirectory();
        var seedText = "AAAAAAAAAA";
        var character = CharacterId.Silent;
        var ascensionLevel = 0;
        EventTarget? targetEvent = new(3, "THE_FUTURE_OF_POTIONS");
        RelicTarget? targetRelic = null;
        var maxResults = 10;
        long maxChecks = 200_000;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed" when i + 1 < args.Length:
                    seedText = args[++i];
                    break;
                case "--character" when i + 1 < args.Length:
                    if (Enum.TryParse<CharacterId>(args[++i], true, out var parsedCharacter))
                    {
                        character = parsedCharacter;
                    }
                    break;
                case "--ascension" when i + 1 < args.Length:
                    ascensionLevel = int.Parse(args[++i]);
                    break;
                case "--event" when i + 1 < args.Length:
                    var rawEvent = args[++i];
                    targetEvent = string.Equals(rawEvent, "none", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : ParseEventTarget(rawEvent);
                    break;
                case "--relic" when i + 1 < args.Length:
                    var rawRelic = args[++i];
                    targetRelic = string.Equals(rawRelic, "none", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : ParseRelicTarget(rawRelic);
                    break;
                case "--max-results" when i + 1 < args.Length:
                    maxResults = int.Parse(args[++i]);
                    break;
                case "--max-checks" when i + 1 < args.Length:
                    maxChecks = long.Parse(args[++i]);
                    break;
            }
        }

        return new ProbeOptions(
            workspaceRoot,
            SeedFormatter.TryNormalize(seedText, out var normalized, out _) ? normalized : seedText,
            SeedFormatter.TryNormalize(seedText, out normalized, out _) ? SeedFormatter.ToUIntSeed(normalized) : uint.Parse(seedText),
            character,
            ascensionLevel,
            targetEvent,
            targetRelic,
            maxResults,
            maxChecks);
    }

    private static EventTarget ParseEventTarget(string raw)
    {
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length < 2 || !parts[0].StartsWith("A", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid event target: {raw}");
        }

        return new EventTarget(int.Parse(parts[0][1..]), parts[1].Trim().ToUpperInvariant());
    }

    private static RelicTarget ParseRelicTarget(string raw)
    {
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            parts[0].Length >= 2 &&
            parts[0].StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[0][1..], out var actNumber))
        {
            return new RelicTarget(parts[1].Trim().ToUpperInvariant(), actNumber);
        }

        return new RelicTarget(raw.Trim().ToUpperInvariant(), null);
    }
}

sealed record EventTarget(int ActNumber, string EventId);
sealed record RelicTarget(string RelicId, int? ActNumber);
sealed record AncientInfo(string AncientId, IReadOnlyList<string> RelicIds);
sealed record RouteNode(int Row, int Col, string PointType, IReadOnlyList<string> ChildPointTypes, object Raw);
sealed record ActRoute(int ActNumber, IReadOnlyList<RouteNode> Nodes);
sealed record RouteRoomResult(int Row, int Col, string PointType, string RoomType, string? EventId, IReadOnlyList<string> RelicIds);
sealed record ActRouteResult(int ActNumber, string? AncientId, IReadOnlyList<string> AncientRelics, IReadOnlyList<RouteRoomResult> Rooms);
sealed record RouteMatch(IReadOnlyList<ActRouteResult> Acts);

static class RouteId
{
    public static bool Equals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }
}

sealed class MapFactory
{
    private readonly object _world;
    private readonly MethodInfo _resolveActOneMethod;
    private readonly IReadOnlyList<object> _acts;
    private readonly Type _mapStateType;
    private readonly MethodInfo _createMapMethod;
    private readonly PropertyInfo _startingPointProperty;
    private readonly MethodInfo _findAllPathsMethod;
    private readonly PropertyInfo _coordProperty;
    private readonly PropertyInfo _pointTypeProperty;
    private readonly PropertyInfo _childrenProperty;
    private readonly PropertyInfo _rowProperty;
    private readonly PropertyInfo _colProperty;

    public MapFactory(Sts2RunPreviewer previewer)
    {
        var previewerType = typeof(Sts2RunPreviewer);
        var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing _world.");
        _world = worldField.GetValue(previewer)
            ?? throw new InvalidOperationException("Missing world value.");

        var worldType = _world.GetType();
        _resolveActOneMethod = worldType.GetMethod("ResolveActOne", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ResolveActOne.");
        var actsProperty = worldType.GetProperty("Acts", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Acts property.");
        _acts = ((IEnumerable)actsProperty.GetValue(_world)!)
            .Cast<object>()
            .ToList();

        var assembly = previewerType.Assembly;
        _mapStateType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardActMapState")
            ?? throw new InvalidOperationException("Missing StandardActMapState.");
        _createMapMethod = _mapStateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing map Create.");
        _startingPointProperty = _mapStateType.GetProperty("StartingMapPoint", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing StartingMapPoint.");

        var mapPointType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardMapPoint")
            ?? throw new InvalidOperationException("Missing StandardMapPoint.");
        _findAllPathsMethod = _mapStateType.GetMethod("FindAllPaths", BindingFlags.Static | BindingFlags.NonPublic, [mapPointType])
            ?? throw new InvalidOperationException("Missing FindAllPaths.");
        _coordProperty = mapPointType.GetProperty("Coord", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Coord.");
        _pointTypeProperty = mapPointType.GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing PointType.");
        _childrenProperty = mapPointType.GetProperty("Children", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Children.");

        var coordType = _coordProperty.PropertyType;
        _rowProperty = coordType.GetProperty("Row", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Row.");
        _colProperty = coordType.GetProperty("Col", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Col.");
    }

    public IReadOnlyDictionary<int, List<ActRoute>> BuildRoutes(uint seed)
    {
        var result = new Dictionary<int, List<ActRoute>>();
        for (var actNumber = 1; actNumber <= 3; actNumber++)
        {
            var act = actNumber == 1
                ? _resolveActOneMethod.Invoke(_world, [seed, null, false]) ?? GetAct(actNumber)
                : GetAct(actNumber);
            var map = _createMapMethod.Invoke(null, [act, false, 0, new GameRng(seed, $"act_{actNumber}_map")])
                ?? throw new InvalidOperationException($"Unable to create act {actNumber} map.");
            var start = _startingPointProperty.GetValue(map)
                ?? throw new InvalidOperationException($"Unable to get act {actNumber} start.");
            var rawPaths = (IEnumerable)_findAllPathsMethod.Invoke(null, [start])!;
            var actRoutes = new List<ActRoute>();
            foreach (var rawPath in rawPaths)
            {
                var nodes = new List<RouteNode>();
                foreach (var rawPoint in (IEnumerable)rawPath)
                {
                    var coord = _coordProperty.GetValue(rawPoint)!;
                    var row = (int)(_rowProperty.GetValue(coord) ?? 0);
                    var col = (int)(_colProperty.GetValue(coord) ?? 0);
                    var pointType = _pointTypeProperty.GetValue(rawPoint)?.ToString() ?? "Unassigned";
                    if (row == 0 || string.Equals(pointType, "Boss", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var childPointTypes = ((IEnumerable)_childrenProperty.GetValue(rawPoint)!)
                        .Cast<object>()
                        .Select(child => _pointTypeProperty.GetValue(child)?.ToString() ?? "Unassigned")
                        .ToList();
                    nodes.Add(new RouteNode(row, col, pointType, childPointTypes, rawPoint));
                }

                actRoutes.Add(new ActRoute(actNumber, nodes));
            }

            result[actNumber] = actRoutes;
        }

        return result;
    }

    private object GetAct(int actNumber) =>
        _acts.First(act =>
            (int)(act.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)!.GetValue(act) ?? 0) == actNumber);
}

sealed class RouteSimulator
{
    private readonly EventReflection _events;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _actPools;
    private readonly IReadOnlyDictionary<int, AncientInfo> _ancientByAct;
    private readonly ProbeOptions _options;
    private readonly RelicRouteStateFactory _relicFactory;

    public RouteSimulator(
        Sts2RunPreviewer previewer,
        NeowOptionDataset dataset,
        IReadOnlyDictionary<int, IReadOnlyList<string>> actPools,
        IReadOnlyDictionary<int, AncientInfo> ancientByAct,
        ProbeOptions options)
    {
        _events = new EventReflection(previewer, dataset, options);
        _actPools = actPools;
        _ancientByAct = ancientByAct;
        _options = options;
        _relicFactory = new RelicRouteStateFactory(previewer, options);
    }

    public RouteMatch? TrySimulate(IReadOnlyList<ActRoute> acts)
    {
        var eventState = _events.CreateState();
        var eventRng = new GameRng(_options.SeedValue, "explicit_route_probe");
        var unknownOdds = new UnknownOdds(new GameRng(_options.SeedValue, "unknown_map_point"));
        var relicState = _relicFactory.Create();
        var actResults = new List<ActRouteResult>(acts.Count);
        var eventMatched = _options.TargetEvent == null;
        var relicMatched = _options.TargetRelic == null;
        StandardRoomType? previousRoomType = null;

        foreach (var act in acts.OrderBy(item => item.ActNumber))
        {
            var rooms = new List<RouteRoomResult>();
            var ancientId = act.ActNumber > 1 && _ancientByAct.TryGetValue(act.ActNumber, out var ancient)
                ? ancient.AncientId
                : null;
            var ancientRelics = act.ActNumber > 1 && _ancientByAct.TryGetValue(act.ActNumber, out ancient)
                ? ancient.RelicIds
                : Array.Empty<string>();

            _events.StartAct(eventState, act.ActNumber, initialEventsVisited: 1);
            _events.IncrementTotalFloor(eventState);
            if (act.ActNumber > 1 && !string.IsNullOrWhiteSpace(ancientId))
            {
                if (_options.TargetEvent != null &&
                    _options.TargetEvent.ActNumber == act.ActNumber &&
                    RouteId.Equals(_options.TargetEvent.EventId, ancientId))
                {
                    eventMatched = true;
                }

                if (_options.TargetRelic != null &&
                    (!_options.TargetRelic.ActNumber.HasValue || _options.TargetRelic.ActNumber.Value == act.ActNumber) &&
                    ancientRelics.Any(id => RouteId.Equals(id, _options.TargetRelic.RelicId)))
                {
                    relicMatched = true;
                }
            }

            foreach (var node in act.Nodes)
            {
                var roomType = ResolveRoomType(node, previousRoomType, unknownOdds);
                previousRoomType = roomType;
                _events.IncrementTotalFloor(eventState);

                string? eventId = null;
                List<string> relicIds = [];
                switch (roomType)
                {
                    case StandardRoomType.Event:
                        if (_actPools.TryGetValue(act.ActNumber, out var pool) && pool.Count > 0)
                        {
                            eventId = _events.ShowEvent(pool, eventState, eventRng);
                            if (!string.IsNullOrWhiteSpace(eventId))
                            {
                                if (_options.TargetEvent != null &&
                                    _options.TargetEvent.ActNumber == act.ActNumber &&
                                    RouteId.Equals(_options.TargetEvent.EventId, eventId))
                                {
                                    eventMatched = true;
                                }
                            }
                        }
                        break;
                    case StandardRoomType.Monster:
                        _events.ConsumeRegularCombat(eventState, eventRng);
                        relicState.ConsumeRegularCombat();
                        break;
                    case StandardRoomType.Elite:
                        _events.ConsumeEliteStop(eventState, eventRng);
                        relicIds = relicState.ShowElite(act.ActNumber).ToList();
                        break;
                    case StandardRoomType.Treasure:
                        _events.ConsumeTreasureStop(eventState, eventRng, relicState.RewardsRng);
                        relicIds = relicState.ShowTreasure(act.ActNumber).ToList();
                        break;
                    case StandardRoomType.Shop:
                        _events.ConsumeShopStop(eventState, eventRng);
                        relicIds = relicState.ShowShop(act.ActNumber).ToList();
                        break;
                }

                if (!relicMatched && _options.TargetRelic != null)
                {
                    if ((!_options.TargetRelic.ActNumber.HasValue || _options.TargetRelic.ActNumber.Value == act.ActNumber) &&
                        relicIds.Any(id => RouteId.Equals(id, _options.TargetRelic.RelicId)))
                    {
                        relicMatched = true;
                    }
                }

                rooms.Add(new RouteRoomResult(node.Row, node.Col, node.PointType, roomType.ToString(), eventId, relicIds));
            }

            actResults.Add(new ActRouteResult(act.ActNumber, ancientId, ancientRelics, rooms));
        }

        return eventMatched && relicMatched
            ? new RouteMatch(actResults)
            : null;
    }

    private static StandardRoomType ResolveRoomType(RouteNode node, StandardRoomType? previousRoomType, UnknownOdds unknownOdds)
    {
        if (!string.Equals(node.PointType, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return node.PointType switch
            {
                "Shop" => StandardRoomType.Shop,
                "Treasure" => StandardRoomType.Treasure,
                "RestSite" => StandardRoomType.RestSite,
                "Monster" => StandardRoomType.Monster,
                "Elite" => StandardRoomType.Elite,
                "Boss" => StandardRoomType.Boss,
                "Ancient" => StandardRoomType.Event,
                _ => StandardRoomType.Unassigned
            };
        }

        var blacklist = new HashSet<StandardRoomType>();
        if (previousRoomType == StandardRoomType.Shop ||
            (node.ChildPointTypes.Count > 0 && node.ChildPointTypes.All(type => string.Equals(type, "Shop", StringComparison.OrdinalIgnoreCase))))
        {
            blacklist.Add(StandardRoomType.Shop);
        }

        return unknownOdds.Roll(blacklist);
    }
}

enum StandardRoomType
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

sealed class UnknownOdds
{
    private readonly GameRng _rng;
    private readonly Dictionary<StandardRoomType, float> _baseOdds = new()
    {
        [StandardRoomType.Monster] = 0.1f,
        [StandardRoomType.Elite] = -1f,
        [StandardRoomType.Treasure] = 0.02f,
        [StandardRoomType.Shop] = 0.03f
    };
    private readonly Dictionary<StandardRoomType, float> _currentOdds = new()
    {
        [StandardRoomType.Monster] = 0.1f,
        [StandardRoomType.Elite] = -1f,
        [StandardRoomType.Treasure] = 0.02f,
        [StandardRoomType.Shop] = 0.03f
    };

    public UnknownOdds(GameRng rng)
    {
        _rng = rng;
    }

    public StandardRoomType Roll(IReadOnlySet<StandardRoomType> blacklist)
    {
        var allowed = _currentOdds.Keys
            .Append(StandardRoomType.Event)
            .Where(roomType => !blacklist.Contains(roomType))
            .ToList();

        var selected = allowed.Contains(StandardRoomType.Event)
            ? StandardRoomType.Event
            : StandardRoomType.Monster;
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
}

sealed class EventReflection
{
    private readonly ProbeOptions _options;
    private readonly Type _simulationModelType;
    private readonly Type _stateType;
    private readonly MethodInfo _createSimulationModelMethod;
    private readonly MethodInfo _createStateMethod;
    private readonly MethodInfo _startActMethod;
    private readonly MethodInfo _consumeRegularCombatMethod;
    private readonly MethodInfo _consumeEliteStopMethod;
    private readonly MethodInfo _consumeTreasureStopMethod;
    private readonly MethodInfo _consumeShopStopMethod;
    private readonly MethodInfo _applyShownEventMethod;
    private readonly MethodInfo _ensureNextEventIsValidMethod;
    private readonly MethodInfo _peekNextAllowedEventMethod;
    private readonly MethodInfo _consumeShownEventMethod;
    private readonly PropertyInfo _totalFloorProperty;
    private readonly object _simulationModel;

    public EventReflection(Sts2RunPreviewer previewer, NeowOptionDataset dataset, ProbeOptions options)
    {
        _options = options;
        var assembly = typeof(Sts2RunPreviewer).Assembly;
        _simulationModelType = assembly.GetType("SeedModel.Sts2.Sts2EventVisibilitySimulationModel")
            ?? throw new InvalidOperationException("Missing event simulation model.");
        _stateType = assembly.GetType("SeedModel.Sts2.Sts2EventProgressState")
            ?? throw new InvalidOperationException("Missing event progress state.");
        var pullEngineType = assembly.GetType("SeedModel.Sts2.Sts2EventPullEngine")
            ?? throw new InvalidOperationException("Missing event pull engine.");

        _createSimulationModelMethod = _simulationModelType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing simulation model Create.");
        _createStateMethod = _stateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing event state Create.");
        _startActMethod = _stateType.GetMethod("StartAct", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing StartAct.");
        _consumeRegularCombatMethod = _stateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
        _consumeEliteStopMethod = _stateType.GetMethod("ConsumeEliteStop", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ConsumeEliteStop.");
        _consumeTreasureStopMethod = _stateType.GetMethod("ConsumeTreasureStop", [typeof(GameRng), typeof(GameRng)])
            ?? throw new InvalidOperationException("Missing ConsumeTreasureStop.");
        _consumeShopStopMethod = _stateType.GetMethod("ConsumeShopStop", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ConsumeShopStop.");
        _applyShownEventMethod = _stateType.GetMethod("ApplyShownEvent", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ApplyShownEvent.");
        _totalFloorProperty = _stateType.GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing TotalFloor.");
        _ensureNextEventIsValidMethod = pullEngineType.GetMethod("EnsureNextEventIsValid", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing EnsureNextEventIsValid.");
        _peekNextAllowedEventMethod = pullEngineType.GetMethod("PeekNextAllowedEvent", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing PeekNextAllowedEvent.");
        _consumeShownEventMethod = pullEngineType.GetMethod("ConsumeShownEvent", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ConsumeShownEvent.");

        _simulationModel = _createSimulationModelMethod.Invoke(null, [dataset, options.Character, null, 1, options.AscensionLevel, null])!;
    }

    public object CreateState() => _createStateMethod.Invoke(null, [_simulationModel, _options.Character, 1])!;

    public void StartAct(object state, int actNumber, int initialEventsVisited) => _startActMethod.Invoke(state, [actNumber, initialEventsVisited]);
    public void ConsumeRegularCombat(object state, GameRng rng) => _consumeRegularCombatMethod.Invoke(state, [rng]);
    public void ConsumeEliteStop(object state, GameRng rng) => _consumeEliteStopMethod.Invoke(state, [rng]);
    public void ConsumeTreasureStop(object state, GameRng rng, GameRng rewardsRng) => _consumeTreasureStopMethod.Invoke(state, [rng, rewardsRng]);
    public void ConsumeShopStop(object state, GameRng rng) => _consumeShopStopMethod.Invoke(state, [rng]);

    public void IncrementTotalFloor(object state)
    {
        var value = (int)(_totalFloorProperty.GetValue(state) ?? 0);
        _totalFloorProperty.SetValue(state, value + 1);
    }

    public string ShowEvent(IReadOnlyList<string> pool, object state, GameRng rng)
    {
        _ensureNextEventIsValidMethod.Invoke(null, [pool, state]);
        var eventId = (string?)_peekNextAllowedEventMethod.Invoke(null, [pool, state]) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            _consumeShownEventMethod.Invoke(null, [pool, eventId, state]);
            _applyShownEventMethod.Invoke(state, [eventId, rng]);
        }

        return eventId;
    }
}

sealed class RelicRouteStateFactory
{
    private readonly object _pools;
    private readonly IReadOnlyDictionary<string, string> _rarityMap;
    private readonly MethodInfo _primeMethod;
    private readonly ProbeOptions _options;

    public RelicRouteStateFactory(Sts2RunPreviewer previewer, ProbeOptions options)
    {
        _options = options;
        var previewerType = typeof(Sts2RunPreviewer);
        var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing _world.");
        var world = worldField.GetValue(previewer)
            ?? throw new InvalidOperationException("Missing world.");
        var relicPoolsProperty = world.GetType().GetProperty("RelicPools", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RelicPools.");
        _pools = relicPoolsProperty.GetValue(world)
            ?? throw new InvalidOperationException("Missing relic pools.");
        _rarityMap = (IReadOnlyDictionary<string, string>)(_pools.GetType().GetProperty("RarityMap", BindingFlags.Instance | BindingFlags.Public)!.GetValue(_pools)
            ?? throw new InvalidOperationException("Missing rarity map."));

        var primerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Generation.Sts2RelicShufflePrimer")
            ?? throw new InvalidOperationException("Missing primer type.");
        var primerCtor = primerType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(ctor =>
            {
                var parameters = ctor.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(world.GetType());
            })
            ?? throw new InvalidOperationException("Missing primer constructor.");
        var primer = primerCtor.Invoke([world])
            ?? throw new InvalidOperationException("Unable to create primer.");
        _primeMethod = primerType.GetMethod("Prime", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing Prime method.");
        Primer = primer;
    }

    private object Primer { get; }

    public RelicRouteState Create()
    {
        var upFrontRng = new GameRng(_options.SeedValue, "up_front");
        _primeMethod.Invoke(Primer, [upFrontRng, _options.Character, 1, Sts2AncientAvailability.FromLegacyDarvFlag(true)]);

        var getSharedSequenceMethod = _pools.GetType().GetMethod("GetSharedSequence", BindingFlags.Instance | BindingFlags.Public)!;
        var getCombinedSequenceMethod = _pools.GetType().GetMethod("GetCombinedSequence", BindingFlags.Instance | BindingFlags.Public)!;
        var sharedSequence = (IReadOnlyList<string>)getSharedSequenceMethod.Invoke(_pools, [Sts2AncientAvailability.FromLegacyDarvFlag(true)])!;
        var playerSequence = (IReadOnlyList<string>)getCombinedSequenceMethod.Invoke(_pools, [_options.Character, Sts2AncientAvailability.FromLegacyDarvFlag(true)])!;

        var sharedBag = RelicBag.Create(sharedSequence, _rarityMap, upFrontRng, trackedOnly: false);
        var playerBag = RelicBag.Create(playerSequence, _rarityMap, upFrontRng, trackedOnly: true);
        return new RelicRouteState(sharedBag, playerBag, new GameRng(_options.SeedValue, "treasure_room_relics"), new GameRng(unchecked(_options.SeedValue + 1u), "rewards"));
    }
}

sealed class RelicRouteState
{
    public RelicRouteState(RelicBag sharedBag, RelicBag playerBag, GameRng treasureRng, GameRng rewardsRng)
    {
        SharedBag = sharedBag;
        PlayerBag = playerBag;
        TreasureRng = treasureRng;
        RewardsRng = rewardsRng;
    }

    public RelicBag SharedBag { get; }
    public RelicBag PlayerBag { get; }
    public GameRng TreasureRng { get; }
    public GameRng RewardsRng { get; }

    public void ConsumeRegularCombat()
    {
        _ = RewardsRng.NextInt(10, 21);
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
    }

    public IEnumerable<string> ShowTreasure(int actNumber)
    {
        var rarity = RollRelicRarity(TreasureRng);
        var relic = SharedBag.PullFromFront(rarity);
        if (relic == null)
        {
            return Array.Empty<string>();
        }

        PlayerBag.Remove(relic);
        return [relic];
    }

    public IEnumerable<string> ShowElite(int actNumber)
    {
        _ = RewardsRng.NextInt(25, 36);
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
        _ = RewardsRng.NextFloat();
        var rarity = RollRelicRarity(RewardsRng);
        var relic = PlayerBag.PullFromFront(rarity);
        if (relic == null)
        {
            return Array.Empty<string>();
        }

        SharedBag.Remove(relic);
        return [relic];
    }

    public IEnumerable<string> ShowShop(int actNumber)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shown = new List<string>(3);
        foreach (var rarity in new[] { RollRelicRarity(RewardsRng), RollRelicRarity(RewardsRng), "Shop" })
        {
            var relic = PlayerBag.PullFromBack(rarity, selected);
            if (relic == null)
            {
                continue;
            }

            selected.Add(relic);
            SharedBag.Remove(relic);
            shown.Add(relic);
        }

        return shown;
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
}

sealed class RelicBag
{
    private readonly Dictionary<string, List<string>> _buckets;

    private RelicBag(Dictionary<string, List<string>> buckets)
    {
        _buckets = buckets;
    }

    public static RelicBag Create(IReadOnlyList<string> sequence, IReadOnlyDictionary<string, string> rarityMap, GameRng rng, bool trackedOnly)
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
            rng.Shuffle(list);
        }

        return new RelicBag(buckets);
    }

    public void Remove(string relicId)
    {
        foreach (var list in _buckets.Values)
        {
            list.RemoveAll(id => string.Equals(id, relicId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? PullFromFront(string rarity)
    {
        foreach (var key in EnumerateFallbackRarities(rarity))
        {
            if (_buckets.TryGetValue(key, out var list) && list.Count > 0)
            {
                var relic = list[0];
                list.RemoveAt(0);
                return relic;
            }
        }

        return null;
    }

    public string? PullFromBack(string rarity, IReadOnlySet<string> selected)
    {
        foreach (var key in EnumerateFallbackRarities(rarity))
        {
            if (!_buckets.TryGetValue(key, out var list))
            {
                continue;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (selected.Contains(list[i]))
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
