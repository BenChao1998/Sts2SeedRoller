using System.Text;
using OfficialCardMultiplayerConstraint = MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint;
using OfficialCardRarity = MegaCrit.Sts2.Core.Entities.Cards.CardRarity;
using OfficialCardType = MegaCrit.Sts2.Core.Entities.Cards.CardType;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;
using SeedModel.Neow;

Console.OutputEncoding = Encoding.UTF8;

var root = FindWorkspaceRoot();
var characterArg = args.Length > 0 ? args[0] : "Silent";
var seedText = args.Length > 1 ? args[1] : "V8DFWW6W3V";
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(root, "data", "0.103.2", "neow", "options.json"));
if (!Enum.TryParse<CharacterId>(characterArg, ignoreCase: true, out var seedCharacter))
{
    throw new InvalidOperationException($"Unsupported SeedModel character: {characterArg}");
}

ModelDb.Init();

var officialPool = GetOfficialPool(characterArg);
var officialUnlocked = officialPool
    .GetUnlockedCards(UnlockState.all, OfficialCardMultiplayerConstraint.SingleplayerOnly)
    .ToList();
var officialMerchant = officialUnlocked
    .Where(card => card.Rarity != OfficialCardRarity.Basic)
    .Where(card => card.MultiplayerConstraint != OfficialCardMultiplayerConstraint.MultiplayerOnly)
    .ToList();
var officialCombat = CardFactory.FilterForCombat(officialUnlocked)
    .Where(card => card.MultiplayerConstraint != OfficialCardMultiplayerConstraint.MultiplayerOnly)
    .ToList();

var datasetPool = dataset.CharacterCardPoolMap.TryGetValue(seedCharacter, out var pool)
    ? pool.ToList()
    : throw new InvalidOperationException($"Missing dataset card pool for {seedCharacter}");
var datasetSinglePlayer = datasetPool
    .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                     metadata.ParsedConstraint != CardMultiplayerConstraint.MultiplayerOnly)
    .ToList();
var datasetMerchant = datasetSinglePlayer
    .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                     metadata.ParsedRarity is not CardRarity.Basic)
    .ToList();
var datasetCombat = datasetSinglePlayer
    .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                     metadata.CanBeGeneratedInCombat &&
                     metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare &&
                     metadata.ParsedConstraint != CardMultiplayerConstraint.MultiplayerOnly)
    .ToList();

DumpComparison("official.unlocked", officialUnlocked.Select(card => card.Id.Entry));
DumpComparison("dataset.singlePlayer", datasetSinglePlayer);
Console.WriteLine($"fullPoolEqual={SequenceEqual(officialUnlocked.Select(card => card.Id.Entry), datasetSinglePlayer)}");
Console.WriteLine();

DumpComparison("official.merchant", officialMerchant.Select(card => card.Id.Entry));
DumpComparison("dataset.merchant", datasetMerchant);
Console.WriteLine($"merchantEqual={SequenceEqual(officialMerchant.Select(card => card.Id.Entry), datasetMerchant)}");
Console.WriteLine();

DumpComparison("official.combat", officialCombat.Select(card => card.Id.Entry));
DumpComparison("dataset.combat", datasetCombat);
Console.WriteLine($"combatEqual={SequenceEqual(officialCombat.Select(card => card.Id.Entry), datasetCombat)}");
Console.WriteLine();

PrintDiff("merchant", officialMerchant.Select(card => card.Id.Entry).ToList(), datasetMerchant);
PrintDiff("combat", officialCombat.Select(card => card.Id.Entry).ToList(), datasetCombat);
PrintSetDiff("full", officialUnlocked.Select(card => card.Id.Entry).ToList(), datasetSinglePlayer);
PrintSetDiff("merchant", officialMerchant.Select(card => card.Id.Entry).ToList(), datasetMerchant);
PrintSetDiff("combat", officialCombat.Select(card => card.Id.Entry).ToList(), datasetCombat);
PrintMerchantBuckets(officialMerchant, datasetMerchant, dataset);

static void DumpComparison(string label, IEnumerable<string> ids)
{
    var list = ids.ToList();
    Console.WriteLine($"{label}.count={list.Count}");
    Console.WriteLine($"{label}.head={string.Join(" / ", list.Take(20))}");
}

static bool SequenceEqual(IEnumerable<string> left, IEnumerable<string> right)
{
    return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
}

static void PrintDiff(string label, IReadOnlyList<string> official, IReadOnlyList<string> dataset)
{
    var max = Math.Max(official.Count, dataset.Count);
    for (var i = 0; i < max; i++)
    {
        var left = i < official.Count ? official[i] : "(missing)";
        var right = i < dataset.Count ? dataset[i] : "(missing)";
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Console.WriteLine($"{label}.firstDiff.index={i}");
        Console.WriteLine($"{label}.official={left}");
        Console.WriteLine($"{label}.dataset={right}");
        return;
    }

    Console.WriteLine($"{label}.firstDiff.index=-1");
}

static void PrintSetDiff(string label, IReadOnlyList<string> official, IReadOnlyList<string> dataset)
{
    var officialOnly = official.Except(dataset, StringComparer.OrdinalIgnoreCase).ToList();
    var datasetOnly = dataset.Except(official, StringComparer.OrdinalIgnoreCase).ToList();
    Console.WriteLine($"{label}.officialOnly={string.Join(" / ", officialOnly)}");
    Console.WriteLine($"{label}.datasetOnly={string.Join(" / ", datasetOnly)}");
}

static void PrintMerchantBuckets(
    IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel> officialMerchant,
    IReadOnlyList<string> datasetMerchant,
    NeowOptionDataset dataset)
{
    foreach (var cardType in new[] { OfficialCardType.Attack, OfficialCardType.Skill, OfficialCardType.Power })
    {
        foreach (var rarity in new[] { OfficialCardRarity.Common, OfficialCardRarity.Uncommon, OfficialCardRarity.Rare, OfficialCardRarity.Ancient })
        {
            var official = officialMerchant
                .Where(card => card.Type == cardType && card.Rarity == rarity)
                .Select(card => card.Id.Entry)
                .ToList();
            var runtime = datasetMerchant
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 string.Equals(metadata.Type, cardType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(metadata.Rarity, rarity.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (SequenceEqual(official, runtime))
            {
                continue;
            }

            Console.WriteLine($"bucket.{cardType}.{rarity}.official={string.Join(" / ", official)}");
            Console.WriteLine($"bucket.{cardType}.{rarity}.dataset={string.Join(" / ", runtime)}");
        }
    }
}

static CardPoolModel GetOfficialPool(string character)
{
    return character.ToUpperInvariant() switch
    {
        "IRONCLAD" => ModelDb.CardPool<IroncladCardPool>(),
        "SILENT" => ModelDb.CardPool<SilentCardPool>(),
        "DEFECT" => ModelDb.CardPool<DefectCardPool>(),
        "NECROBINDER" => ModelDb.CardPool<NecrobinderCardPool>(),
        "REGENT" => ModelDb.CardPool<RegentCardPool>(),
        _ => throw new InvalidOperationException($"Unsupported official character: {character}")
    };
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

    throw new InvalidOperationException("Failed to locate workspace root.");
}
