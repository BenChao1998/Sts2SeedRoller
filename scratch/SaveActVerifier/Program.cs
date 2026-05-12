using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

var root = FindWorkspaceRoot();
var savePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(root, "存档", "1777459169.run");
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

var runSeed = SeedFormatter.ToUIntSeed(normalizedSeed);
var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));

var previewerType = typeof(Sts2RunPreviewer);
var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Failed to find _world field.");
var world = worldField.GetValue(previewer)
    ?? throw new InvalidOperationException("Failed to resolve world data.");

var worldType = world.GetType();
var actsProperty = worldType.GetProperty("Acts", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Failed to find Acts property.");
var resolveActOneMethod = worldType.GetMethod("ResolveActOne", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Failed to find ResolveActOne method.");

var acts = ((IEnumerable)actsProperty.GetValue(world)!)
    .Cast<object>()
    .ToList();

object GetAct(int actNumber) =>
    acts.First(act =>
        (int)(act.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)!.GetValue(act) ?? 0) == actNumber);

var assembly = previewerType.Assembly;
var mapStateType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardActMapState")
    ?? throw new InvalidOperationException("Failed to find StandardActMapState type.");
var createMapMethod = mapStateType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
    ?? throw new InvalidOperationException("Failed to find StandardActMapState.Create.");
var getAllGeneratedRoutesMethod = mapStateType.GetMethod("GetAllGeneratedRoutes", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Failed to find GetAllGeneratedRoutes.");

Console.WriteLine($"save={savePath}");
Console.WriteLine($"seed={normalizedSeed} runSeed={runSeed} ascension={save.Ascension} character={save.Character} mode={save.GameMode}");
Console.WriteLine($"modifiers={(save.Modifiers?.Count ?? 0)}");
Console.WriteLine();

var historyActs = save.MapPointHistory ?? [];
for (var actIndex = 0; actIndex < historyActs.Count; actIndex++)
{
    var actNumber = actIndex + 1;
    var history = historyActs[actIndex] ?? [];
    var actualSteps = history
        .Select(point => new
        {
            RoomType = NormalizeRoomType(point.Rooms?.FirstOrDefault()?.RoomType, point.MapPointType),
            MapType = NormalizeMapPointType(point.MapPointType),
            RawMapPointType = point.MapPointType
        })
        .Where(entry => !string.IsNullOrWhiteSpace(entry.MapType) || !string.IsNullOrWhiteSpace(entry.RoomType))
        .ToList();
    var actualRoomPath = actualSteps
        .Where(entry => !string.Equals(entry.RawMapPointType, "ancient", StringComparison.OrdinalIgnoreCase))
        .Select(entry => entry.RoomType)
        .Where(static type => !string.IsNullOrWhiteSpace(type))
        .ToList();
    var actualMapPath = actualSteps
        .Where(entry =>
            !string.Equals(entry.RawMapPointType, "ancient", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.RawMapPointType, "boss", StringComparison.OrdinalIgnoreCase))
        .Select(entry => entry.MapType)
        .Where(static type => !string.IsNullOrWhiteSpace(type))
        .ToList();

    var act = actNumber == 1
        ? resolveActOneMethod.Invoke(world, [runSeed, null, false]) ?? GetAct(1)
        : GetAct(actNumber);
    var map = createMapMethod.Invoke(null, [act, false, save.Ascension, new GameRng(runSeed, $"act_{actNumber}_map")])
        ?? throw new InvalidOperationException($"Failed to create act {actNumber} map.");
    var rawRoutes = (IEnumerable)(getAllGeneratedRoutesMethod.Invoke(map, [actNumber])
        ?? throw new InvalidOperationException($"Failed to read act {actNumber} routes."));

    var routes = rawRoutes
        .Cast<object>()
        .Select(ConvertRoute)
        .ToList();

    var exactMapMatches = routes
        .Where(route => StartsWith(route.Types, actualMapPath))
        .ToList();
    var exactRoomMatches = routes
        .Where(route => StartsWith(route.Types, actualRoomPath))
        .ToList();
    var bestMapPrefix = routes.Count == 0 ? 0 : routes.Max(route => SharedPrefixLength(route.Types, actualMapPath));
    var closestMap = routes
        .Where(route => SharedPrefixLength(route.Types, actualMapPath) == bestMapPrefix)
        .Take(5)
        .ToList();
    var bestRoomPrefix = routes.Count == 0 ? 0 : routes.Max(route => SharedPrefixLength(route.Types, actualRoomPath));
    var closestRoom = routes
        .Where(route => SharedPrefixLength(route.Types, actualRoomPath) == bestRoomPrefix)
        .Take(5)
        .ToList();

    Console.WriteLine($"ACT {actNumber}");
    Console.WriteLine($"  save map   : {string.Join(" -> ", actualMapPath)}");
    Console.WriteLine($"  save room  : {string.Join(" -> ", actualRoomPath)}");
    Console.WriteLine($"  routeCount : {routes.Count}");
    Console.WriteLine($"  exactMap   : {exactMapMatches.Count}");
    if (exactMapMatches.Count > 0)
    {
        foreach (var match in exactMapMatches.Take(5))
        {
            Console.WriteLine($"    mapMatch : {match.Display}");
        }
    }
    else
    {
        Console.WriteLine($"  bestMap    : {bestMapPrefix}/{actualMapPath.Count}");
        foreach (var route in closestMap)
        {
            Console.WriteLine($"    mapClose : {route.Display}");
        }
    }

    Console.WriteLine($"  exactRoom  : {exactRoomMatches.Count}");
    if (exactRoomMatches.Count == 0)
    {
        Console.WriteLine($"  bestRoom   : {bestRoomPrefix}/{actualRoomPath.Count}");
        foreach (var route in closestRoom)
        {
            Console.WriteLine($"    roomClose: {route.Display}");
        }
    }

    Console.WriteLine();
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

static GeneratedRouteSnapshot ConvertRoute(object rawRoute)
{
    var nodesProperty = rawRoute.GetType().GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Failed to find route nodes.");
    var nodes = ((IEnumerable)nodesProperty.GetValue(rawRoute)!)
        .Cast<object>()
        .Select(rawNode =>
        {
            var row = (int)(rawNode.GetType().GetProperty("Row", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rawNode) ?? 0);
            var col = (int)(rawNode.GetType().GetProperty("Col", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rawNode) ?? 0);
            var pointType = rawNode.GetType().GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rawNode)?.ToString()
                ?? "Unknown";
            return new GeneratedNodeSnapshot(row, col, pointType);
        })
        .ToList();

    var types = nodes.Select(node => node.PointType).ToList();
    var display = string.Join(" -> ", nodes.Select(node => $"{node.PointType}({node.Row},{node.Col})"));
    return new GeneratedRouteSnapshot(nodes, types, display);
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

static int SharedPrefixLength(IReadOnlyList<string> left, IReadOnlyList<string> right)
{
    var length = Math.Min(left.Count, right.Count);
    for (var i = 0; i < length; i++)
    {
        if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
        {
            return i;
        }
    }

    return length;
}

static string NormalizeRoomType(string? roomType, string? mapPointType)
{
    var source = !string.IsNullOrWhiteSpace(roomType) ? roomType : mapPointType;
    return source?.ToLowerInvariant() switch
    {
        "event" => "Event",
        "monster" => "Monster",
        "elite" => "Elite",
        "treasure" => "Treasure",
        "shop" => "Shop",
        "rest_site" => "RestSite",
        "restsite" => "RestSite",
        "ancient" => "Ancient",
        "boss" => "Boss",
        _ => source ?? string.Empty
    };
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

file sealed class SaveSnapshot
{
    [JsonPropertyName("seed")]
    public string? Seed { get; set; }

    [JsonPropertyName("ascension")]
    public int Ascension { get; set; }

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("game_mode")]
    public string? GameMode { get; set; }

    [JsonPropertyName("modifiers")]
    public List<string>? Modifiers { get; set; }

    [JsonPropertyName("map_point_history")]
    public List<List<SaveMapPointSnapshot>?>? MapPointHistory { get; set; }
}

file sealed class SaveMapPointSnapshot
{
    [JsonPropertyName("map_point_type")]
    public string? MapPointType { get; set; }

    [JsonPropertyName("rooms")]
    public List<SaveRoomSnapshot>? Rooms { get; set; }
}

file sealed class SaveRoomSnapshot
{
    [JsonPropertyName("room_type")]
    public string? RoomType { get; set; }
}

file sealed record GeneratedNodeSnapshot(int Row, int Col, string PointType);

file sealed record GeneratedRouteSnapshot(
    IReadOnlyList<GeneratedNodeSnapshot> Nodes,
    IReadOnlyList<string> Types,
    string Display);
