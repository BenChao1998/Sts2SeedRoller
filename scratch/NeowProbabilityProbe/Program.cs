using SeedModel.Neow;

var dataPath = args.Length > 0 ? args[0] : @"data/0.105.1/neow/options.json";
var dataset = NeowOptionDataLoader.LoadFromFile(dataPath);

var cardMetadata = dataset.CardMetadataMap;
var cardPools = dataset.CharacterCardPoolMap;

var targets = new[] { "CRASH_LANDING", "FLAK_CANNON" };
foreach (var target in targets)
{
    var owners = cardPools
        .Where(entry => entry.Value.Contains(target, StringComparer.OrdinalIgnoreCase))
        .Select(entry => entry.Key.ToString())
        .ToList();
    var metadata = cardMetadata[target];
    Console.WriteLine($"{target}: rarity={metadata.ParsedRarity} constraint={metadata.ParsedConstraint} pools={string.Join(",", owners)}");
}

foreach (var character in new[] { CharacterId.Regent, CharacterId.Defect })
{
    var pool = cardPools[character]
        .Where(id => cardMetadata.TryGetValue(id, out var metadata) && metadata.ParsedConstraint != CardMultiplayerConstraint.MultiplayerOnly)
        .ToList();
    var rareCount = pool.Count(id => cardMetadata[id].ParsedRarity == CardRarity.Rare);
    var uncommonCount = pool.Count(id => cardMetadata[id].ParsedRarity == CardRarity.Uncommon);
    var commonCount = pool.Count(id => cardMetadata[id].ParsedRarity == CardRarity.Common);
    Console.WriteLine($"{character} counts: common={commonCount} uncommon={uncommonCount} rare={rareCount}");
}
