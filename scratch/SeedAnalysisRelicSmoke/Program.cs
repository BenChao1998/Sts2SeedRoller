using SeedModel.Neow;
using SeedModel.Seeds;
using SeedModel.Sts2;

var root = Directory.GetCurrentDirectory();
var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(root, "data", "0.103.2", "neow", "options.json"));

var seedText = GetArg("--seed") ?? "24LFHQHD2C";
if (!SeedFormatter.TryNormalize(seedText, out var normalizedSeed, out var error))
{
    throw new InvalidOperationException(error);
}

var character = ParseCharacter(GetArg("--character") ?? "Ironclad");
var ascension = int.TryParse(GetArg("--ascension"), out var parsedAscension) ? parsedAscension : 10;
var samples = int.TryParse(GetArg("--samples"), out var parsedSamples) ? parsedSamples : 1000;
var top = int.TryParse(GetArg("--top"), out var parsedTop) ? parsedTop : 20;

var analysis = previewer.AnalyzeRelicVisibility(dataset, new Sts2RelicVisibilityRequest
{
    SeedText = normalizedSeed,
    SeedValue = SeedFormatter.ToUIntSeed(normalizedSeed),
    Character = character,
    UnlockedCharacters =
    [
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    ],
    AscensionLevel = ascension,
    PlayerCount = 1,
    Samples = samples,
    EarlyWindow = 5,
    IncludeDarvSharedAncient = true,
    UseExactRouteCoverage = true
});

Console.WriteLine($"seed={normalizedSeed} character={character} asc={ascension} samples={samples}");
foreach (var profile in analysis.Profiles)
{
    Console.WriteLine($"profile={profile.Id} title={profile.Title}");
    foreach (var relic in profile.SeenRelics.Take(top))
    {
        Console.WriteLine($"{relic.RelicId}|seen={relic.SeenProbability:P1}|early={relic.EarlyProbability:P1}|source={relic.MostCommonSource}");
    }

    var girya = profile.SeenRelics.FirstOrDefault(item => string.Equals(item.RelicId, "GIRYA", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine(girya == null
        ? "GIRYA|missing"
        : $"GIRYA|seen={girya.SeenProbability:P1}|early={girya.EarlyProbability:P1}|source={girya.MostCommonSource}");
}

static string? GetArg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static CharacterId ParseCharacter(string raw)
{
    return Enum.TryParse<CharacterId>(raw, ignoreCase: true, out var character)
        ? character
        : throw new InvalidOperationException($"Unknown character: {raw}");
}
