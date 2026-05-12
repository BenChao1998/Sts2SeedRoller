using SeedModel.Neow;

var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(FindWorkspaceRoot(), "data", "0.105.1", "neow", "options.json"));
var previewer = new NeowRewardPreviewer(dataset);
var context = NeowGenerationContext.Create(
    seed: 0xCF6D924C,
    character: CharacterId.Silent,
    ascensionLevel: 10);

var expected = new[]
{
    "DRAIN_POWER",
    "FUSION",
    "HEMOKINESIS",
    "BOOST_AWAY",
    "BREAKTHROUGH",
    "REAVE"
};

var candidates = new[] { CharacterId.Defect, CharacterId.Ironclad, CharacterId.Necrobinder, CharacterId.Regent };

foreach (var permutation in Permute(candidates, 0))
{
    var cards = Simulate(previewer, context, permutation);
    if (cards.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("MATCH: " + string.Join(", ", permutation));
        Console.WriteLine(string.Join(" | ", cards));
        return;
    }
}

foreach (var permutation in Permute(candidates, 0).Take(10))
{
    var cards = Simulate(previewer, context, permutation);
    Console.WriteLine($"{string.Join(", ", permutation)} => {string.Join(" | ", cards)}");
}

static string[] Simulate(
    NeowRewardPreviewer previewer,
    NeowGenerationContext context,
    CharacterId[] order)
{
    var method = typeof(NeowRewardPreviewer).GetMethod("BuildKaleidoscopePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Method not found.");

    var field = typeof(NeowRewardPreviewer).GetField("_cardPools", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Field not found.");

    var original = (IReadOnlyDictionary<CharacterId, IReadOnlyList<string>>)field.GetValue(previewer)!;
    var reordered = new Dictionary<CharacterId, IReadOnlyList<string>>();
    foreach (var character in order)
    {
        reordered[character] = original[character];
    }

    reordered[context.Character] = original[context.Character];
    field.SetValue(previewer, reordered);

    var result = (IReadOnlyList<RewardDetail>)method.Invoke(previewer, [context])!;
    return result.Select(detail => detail.Id ?? string.Empty).ToArray();
}

static IEnumerable<CharacterId[]> Permute(CharacterId[] values, int index)
{
    if (index == values.Length)
    {
        yield return values.ToArray();
        yield break;
    }

    for (var i = index; i < values.Length; i++)
    {
        (values[index], values[i]) = (values[i], values[index]);
        foreach (var permutation in Permute(values, index + 1))
        {
            yield return permutation;
        }

        (values[index], values[i]) = (values[i], values[index]);
    }
}

static string FindWorkspaceRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (Directory.Exists(Path.Combine(current, "src")) &&
            Directory.Exists(Path.Combine(current, "data")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName ?? string.Empty;
    }

    throw new InvalidOperationException("Workspace root not found.");
}
