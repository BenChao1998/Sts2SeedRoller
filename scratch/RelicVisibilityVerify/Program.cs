using SeedModel.Neow;
using SeedModel.Seeds;
using SeedModel.Sts2;
using System.Text.Json;

var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine("data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine("data", "0.103.2", "sts2", "acts.json"));
var dataset = JsonSerializer.Deserialize<NeowOptionDataset>(File.ReadAllText(Path.Combine("data", "0.103.2", "neow", "options.json")))
    ?? throw new InvalidOperationException("Failed to load dataset.");

var seedText = "PCKDQFERHM";
var analysis = previewer.AnalyzeRelicVisibility(dataset, new Sts2RelicVisibilityRequest
{
    SeedText = seedText,
    SeedValue = SeedFormatter.ToUIntSeed(seedText),
    Character = CharacterId.Silent,
    AscensionLevel = 0,
    PlayerCount = 1,
    Samples = 8000,
    EarlyWindow = 5,
    IncludeDarvSharedAncient = true
});

var profile = analysis.Profiles.First(result => string.Equals(result.Id, "balanced", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("Balanced early top 5");
foreach (var item in profile.EarlyRelics.Take(5))
{
    Console.WriteLine($"{item.RelicId}|early={item.EarlyProbability:P1}|seen={item.SeenProbability:P1}|avg={item.AverageFirstOpportunity:F2}|src={item.MostCommonSource}");
}

Console.WriteLine();
Console.WriteLine("Balanced seen top 5");
foreach (var item in profile.SeenRelics.Take(5))
{
    Console.WriteLine($"{item.RelicId}|seen={item.SeenProbability:P1}|early={item.EarlyProbability:P1}|avg={item.AverageFirstOpportunity:F2}|src={item.MostCommonSource}");
}
