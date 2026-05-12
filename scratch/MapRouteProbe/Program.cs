using System.Collections;
using System.Reflection;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

var root = @"E:\github project\Sts2SeedRoller";
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

var assembly = previewerType.Assembly;
var mapStateType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardActMapState")
    ?? throw new InvalidOperationException("Failed to find StandardActMapState type.");
var mapPointType = assembly.GetType("SeedModel.Sts2.Sts2StandardShopPreviewer+StandardMapPoint")
    ?? throw new InvalidOperationException("Failed to find StandardMapPoint type.");

var createMapMethod = mapStateType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
    ?? throw new InvalidOperationException("Failed to find StandardActMapState.Create.");
var startingMapPointProperty = mapStateType.GetProperty("StartingMapPoint", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Failed to find StartingMapPoint property.");
var findAllPathsMethod = mapStateType.GetMethod("FindAllPaths", BindingFlags.Static | BindingFlags.NonPublic, [mapPointType])
    ?? throw new InvalidOperationException("Failed to find FindAllPaths method.");

var acts = ((IEnumerable)actsProperty.GetValue(world)!)
    .Cast<object>()
    .ToList();

object GetAct(int actNumber) =>
    acts.First(act =>
        (int)(act.GetType().GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public)!.GetValue(act) ?? 0) == actNumber);

var act2 = GetAct(2);
var act3 = GetAct(3);

var seedText = args.Length > 0 ? args[0] : "24LFHQHD2C";
if (!SeedFormatter.TryNormalize(seedText, out var normalizedSeed, out var error))
{
    throw new InvalidOperationException(error);
}
var runSeed = SeedFormatter.ToUIntSeed(normalizedSeed);

var act1 = resolveActOneMethod.Invoke(world, [runSeed, null, false])
    ?? GetAct(1);
var act1Paths = GetPathsForAct(createMapMethod, startingMapPointProperty, findAllPathsMethod, act1, runSeed, 1);
var act2Paths = GetPathsForAct(createMapMethod, startingMapPointProperty, findAllPathsMethod, act2, runSeed, 2);
var act3Paths = GetPathsForAct(createMapMethod, startingMapPointProperty, findAllPathsMethod, act3, runSeed, 3);

Console.WriteLine($"Seed={normalizedSeed} ({runSeed})");
PrintActRouteStats(1, act1Paths);
PrintActRouteStats(2, act2Paths);
PrintActRouteStats(3, act3Paths);
Console.WriteLine($"Total combinations={(long)act1Paths.Count * act2Paths.Count * act3Paths.Count}");

static List<List<string>> GetPathsForAct(
    MethodInfo createMapMethod,
    PropertyInfo startingMapPointProperty,
    MethodInfo findAllPathsMethod,
    object act,
    uint runSeed,
    int actNumber)
{
    var map = createMapMethod.Invoke(null, [act, false, 0, new GameRng(runSeed, $"act_{actNumber}_map")])
        ?? throw new InvalidOperationException($"Failed to create act {actNumber} map.");
    var startingMapPoint = startingMapPointProperty.GetValue(map)
        ?? throw new InvalidOperationException($"Failed to get act {actNumber} starting point.");
    var allPaths = (IEnumerable)(findAllPathsMethod.Invoke(null, [startingMapPoint])
        ?? throw new InvalidOperationException($"Failed to enumerate act {actNumber} paths."));

    var result = new List<List<string>>();
    foreach (var path in allPaths)
    {
        var pointTypes = new List<string>();
        foreach (var rawPoint in (IEnumerable)path)
        {
            var pointType = rawPoint.GetType().GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rawPoint)?.ToString()
                ?? "Unassigned";
            var coord = rawPoint.GetType().GetProperty("Coord", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rawPoint);
            var row = (int?)coord?.GetType().GetProperty("Row", BindingFlags.Instance | BindingFlags.Public)?.GetValue(coord) ?? 0;
            if (row == 0 || string.Equals(pointType, "Boss", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pointTypes.Add(pointType);
        }

        result.Add(pointTypes);
    }

    return result;
}

static void PrintActRouteStats(int actNumber, IReadOnlyList<List<string>> paths)
{
    Console.WriteLine($"Act {actNumber} routeCount={paths.Count}");
    PrintCounter("Treasure", paths.Select(path => path.Count(point => string.Equals(point, "Treasure", StringComparison.OrdinalIgnoreCase))).ToList());
    PrintCounter("Elite", paths.Select(path => path.Count(point => string.Equals(point, "Elite", StringComparison.OrdinalIgnoreCase))).ToList());
    PrintCounter("Shop", paths.Select(path => path.Count(point => string.Equals(point, "Shop", StringComparison.OrdinalIgnoreCase))).ToList());
    PrintCounter("RestSite", paths.Select(path => path.Count(point => string.Equals(point, "RestSite", StringComparison.OrdinalIgnoreCase))).ToList());
    var examples = paths.Take(3).Select(path => string.Join(" -> ", path));
    Console.WriteLine($"  examples: {string.Join(" || ", examples)}");
}

static void PrintCounter(string label, IReadOnlyList<int> counts)
{
    var grouped = counts
        .GroupBy(x => x)
        .OrderBy(group => group.Key)
        .Select(group => $"{group.Key}x={group.Count()}");
    Console.WriteLine($"  {label}: {string.Join(", ", grouped)}");
}
