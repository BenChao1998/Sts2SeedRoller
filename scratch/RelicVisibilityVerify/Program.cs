using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;
using System.Text.Json;
using System.Reflection;

var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine("data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine("data", "0.103.2", "sts2", "acts.json"));
var dataset = JsonSerializer.Deserialize<NeowOptionDataset>(File.ReadAllText(Path.Combine("data", "0.103.2", "neow", "options.json")))
    ?? throw new InvalidOperationException("Failed to load dataset.");

var seedText = "PCKDQFERHM";
var seedValue = SeedFormatter.ToUIntSeed(seedText);
var analysisPools = previewer.AnalyzePools(new Sts2SeedAnalysisRequest
{
    SeedText = seedText,
    SeedValue = seedValue,
    Character = CharacterId.Silent,
    AscensionLevel = 0,
    IncludeDarvSharedAncient = true
});

var eventPoolByAct = analysisPools.Acts.ToDictionary(
    act => act.ActNumber,
    act => act.FullEventPool
        .Select(static eventId => NormalizeEventPoolId(eventId))
        .Where(static eventId => !string.IsNullOrWhiteSpace(eventId))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToArray());
var relicPoolIds = analysisPools.SharedRelicPools
    .Concat(analysisPools.PlayerRelicPools)
    .SelectMany(group => group.Relics)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
    .ToArray();

Console.WriteLine("=== Route duplicate check (8000 samples per profile) ===");
foreach (var duplicateSummary in AnalyzeRouteDuplicates(seedValue, samples: 8_000))
{
    Console.WriteLine($"{duplicateSummary.ProfileId}: unique={duplicateSummary.UniqueRoutes}, duplicateSamples={duplicateSummary.DuplicateSamples}, repeatedRouteKinds={duplicateSummary.RepeatedRouteKinds}, maxHit={duplicateSummary.MaxFrequency}");
}
Console.WriteLine();

foreach (var samples in new[] { 8_000, 16_000, 32_000 })
{
    var relicAnalysis = previewer.AnalyzeRelicVisibility(dataset, new Sts2RelicVisibilityRequest
    {
        SeedText = seedText,
        SeedValue = seedValue,
        Character = CharacterId.Silent,
        AscensionLevel = 0,
        PlayerCount = 1,
        Samples = samples,
        EarlyWindow = 5,
        IncludeDarvSharedAncient = true
    });

    var eventAnalysis = previewer.AnalyzeEventVisibility(dataset, new Sts2EventVisibilityRequest
    {
        SeedText = seedText,
        SeedValue = seedValue,
        Character = CharacterId.Silent,
        AscensionLevel = 0,
        PlayerCount = 1,
        Samples = samples,
        EarlyWindow = 5,
        IncludeDarvSharedAncient = true
    });

    var seenRelicIds = relicAnalysis.Profiles
        .SelectMany(profile => profile.SeenRelics)
        .Select(item => item.RelicId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var missingRelics = relicPoolIds
        .Where(id => !seenRelicIds.Contains(id))
        .ToArray();

    var seenEventsByAct = eventAnalysis.Profiles
        .Where(profile => !profile.IsComposite)
        .SelectMany(profile => profile.SeenEvents)
        .GroupBy(item => item.ActNumber)
        .ToDictionary(
            group => group.Key,
            group => group.Select(item => item.EventId).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase));

    Console.WriteLine($"=== Samples: {samples} ===");
    Console.WriteLine($"Relics in preview pools: {relicPoolIds.Length}, zero-hit relics: {missingRelics.Length}");
    if (missingRelics.Length > 0)
    {
        Console.WriteLine($"Zero-hit relic ids: {string.Join(", ", missingRelics)}");
    }

    foreach (var act in eventPoolByAct.OrderBy(pair => pair.Key))
    {
        seenEventsByAct.TryGetValue(act.Key, out var seenEventIds);
        seenEventIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingEvents = act.Value
            .Where(id => !seenEventIds.Contains(id))
            .ToArray();

        Console.WriteLine($"Act {act.Key} events in pool: {act.Value.Length}, zero-hit events: {missingEvents.Length}");
        if (missingEvents.Length > 0)
        {
            Console.WriteLine($"Act {act.Key} zero-hit event ids: {string.Join(", ", missingEvents)}");
        }
    }

    Console.WriteLine();
}

static string NormalizeEventPoolId(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return string.Empty;
    }

    var text = raw.Trim();
    var builder = new System.Text.StringBuilder(text.Length + 8);
    for (var i = 0; i < text.Length; i++)
    {
        var current = text[i];
        var previous = i > 0 ? text[i - 1] : '\0';
        var next = i + 1 < text.Length ? text[i + 1] : '\0';
        var startsWord = i > 0 &&
                         char.IsUpper(current) &&
                         (char.IsLower(previous) || char.IsDigit(previous) ||
                          (char.IsUpper(previous) && char.IsLower(next)));

        if (startsWord && builder.Length > 0 && builder[^1] != '_')
        {
            builder.Append('_');
        }

        builder.Append(char.ToUpperInvariant(current));
    }

    return builder.ToString();
}

static IReadOnlyList<RouteDuplicateSummary> AnalyzeRouteDuplicates(uint seedValue, int samples)
{
    var analyzerType = typeof(Sts2RunPreviewer).Assembly.GetType("SeedModel.Sts2.Sts2EventVisibilityAnalyzer")
        ?? throw new InvalidOperationException("Failed to load Sts2EventVisibilityAnalyzer type.");
    var routeProfileType = analyzerType.GetNestedType("RouteProfile", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Failed to load RouteProfile type.");
    var actRouteProfileType = analyzerType.GetNestedType("ActRouteProfile", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Failed to load ActRouteProfile type.");

    var routeProfiles = (System.Collections.IEnumerable?)(routeProfileType
        .GetProperty("All", BindingFlags.Public | BindingFlags.Static)
        ?.GetValue(null))
        ?? throw new InvalidOperationException("Failed to load route profiles.");

    var result = new List<RouteDuplicateSummary>();
    foreach (var profile in routeProfiles.Cast<object>())
    {
        var profileId = (string?)(routeProfileType.GetProperty("Id")?.GetValue(profile)) ?? "unknown";
        var acts = ((System.Collections.IEnumerable?)(routeProfileType.GetProperty("Acts")?.GetValue(profile)))
            ?.Cast<object>()
            .ToArray()
            ?? Array.Empty<object>();

        var rng = new GameRng(seedValue, $"event_visibility_{profileId}");
        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var sample = 0; sample < samples; sample++)
        {
            var signature = BuildRouteSignature(rng, acts, actRouteProfileType);
            frequency[signature] = frequency.GetValueOrDefault(signature) + 1;
        }

        result.Add(new RouteDuplicateSummary(
            profileId,
            UniqueRoutes: frequency.Count,
            DuplicateSamples: samples - frequency.Count,
            RepeatedRouteKinds: frequency.Count(pair => pair.Value > 1),
            MaxFrequency: frequency.Count == 0 ? 0 : frequency.Values.Max()));
    }

    return result;
}

static string BuildRouteSignature(GameRng rng, IReadOnlyList<object> acts, Type actRouteProfileType)
{
    var chunks = new List<string>(acts.Count);
    for (var actIndex = 0; actIndex < acts.Count; actIndex++)
    {
        var act = acts[actIndex];
        var actNumber = actIndex + 1;
        var unknownCounts = actRouteProfileType.GetProperty("UnknownCounts")?.GetValue(act)
            ?? throw new InvalidOperationException("Missing UnknownCounts.");
        var eventCounts = actRouteProfileType.GetProperty("EventCounts")?.GetValue(act)
            ?? throw new InvalidOperationException("Missing EventCounts.");
        var betweenUnknownCombats = actRouteProfileType.GetProperty("BetweenUnknownCombats")?.GetValue(act)
            ?? throw new InvalidOperationException("Missing BetweenUnknownCombats.");
        var ancientVisitChance = (double?)(actRouteProfileType.GetProperty("AncientVisitChance")?.GetValue(act))
            ?? 0d;
        var buildMajorStopsMethod = actRouteProfileType.GetMethod("BuildMajorStops")
            ?? throw new InvalidOperationException("Missing BuildMajorStops.");

        var visitsAncient = actNumber > 1 && rng.NextDouble() < ancientVisitChance;
        var unknownCount = SampleDistribution(unknownCounts, rng);
        var eventCount = Math.Min(SampleDistribution(eventCounts, rng), unknownCount);
        var eventSlots = PickSlots(rng, unknownCount, eventCount).OrderBy(value => value).ToArray();
        var majorStops = ((System.Collections.IEnumerable?)buildMajorStopsMethod.Invoke(act, [rng]))
            ?.Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .ToArray()
            ?? Array.Empty<string>();
        var gapStops = DistributeStopsAcrossGaps(majorStops, unknownCount + 1, biasEarlyUnknowns: actNumber == 1);
        var combatRolls = Enumerable.Range(0, unknownCount)
            .Select(_ => SampleDistribution(betweenUnknownCombats, rng))
            .ToArray();

        chunks.Add(
            $"A{actNumber}[anc={(visitsAncient ? 1 : 0)}|u={unknownCount}|e={eventCount}|slots={string.Join(",", eventSlots)}|stops={string.Join("/", gapStops.Select(gap => string.Join("+", gap)))}|cmb={string.Join(",", combatRolls)}]");
    }

    return string.Join(";", chunks);
}

static int SampleDistribution(object distribution, GameRng rng)
{
    var sampleMethod = distribution.GetType().GetMethod("Sample")
        ?? throw new InvalidOperationException("Missing distribution sample method.");
    return (int)(sampleMethod.Invoke(distribution, [rng]) ?? 0);
}

static List<List<string>> DistributeStopsAcrossGaps(
    IReadOnlyList<string> stops,
    int gapCount,
    bool biasEarlyUnknowns)
{
    var gaps = Enumerable.Range(0, Math.Max(1, gapCount))
        .Select(_ => new List<string>())
        .ToList();

    if (stops.Count == 0)
    {
        return gaps;
    }

    if (stops.Count == 1)
    {
        var gapIndex = string.Equals(stops[0], "Ancient", StringComparison.OrdinalIgnoreCase) ? 0 : Math.Min(gaps.Count - 1, 1);
        gaps[gapIndex].Add(stops[0]);
        return gaps;
    }

    for (var i = 0; i < stops.Count; i++)
    {
        var gap = biasEarlyUnknowns
            ? (int)Math.Round((double)(i + 1) * (gaps.Count - 1) / Math.Max(2, stops.Count + 1))
            : (int)Math.Round((double)i * (gaps.Count - 1) / Math.Max(1, stops.Count - 1));
        gap = Math.Clamp(gap, 0, gaps.Count - 1);
        gaps[gap].Add(stops[i]);
    }

    return gaps;
}

static HashSet<int> PickSlots(GameRng rng, int total, int count)
{
    if (count <= 0 || total <= 0)
    {
        return [];
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

    return slots.Take(count).ToHashSet();
}

sealed record RouteDuplicateSummary(
    string ProfileId,
    int UniqueRoutes,
    int DuplicateSamples,
    int RepeatedRouteKinds,
    int MaxFrequency);
