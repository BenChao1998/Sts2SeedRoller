using SeedModel.Neow;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedModel.Sts2;

if (args.Length > 0 && string.Equals(args[0], "poolcheck", StringComparison.OrdinalIgnoreCase))
{
    RunPoolCheck(args.Skip(1).ToArray());
    return;
}

if (args.Length > 0 && string.Equals(args[0], "preview", StringComparison.OrdinalIgnoreCase))
{
    RunPreview(args.Skip(1).ToArray());
    return;
}

var workspace = @"E:\github project\Sts2SeedRoller";
var optionPath = Path.Combine(workspace, "data", "0.103.2", "ancients", "options.json");
var actPath = Path.Combine(workspace, "data", "0.103.2", "sts2", "acts.json");
var datasetPath = Path.Combine(workspace, "data", "0.103.2", "neow", "options.json");

var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
using var datasetStream = File.OpenRead(datasetPath);
var dataset = NeowOptionDataLoader.Load(datasetStream);
var evaluator = new SeedRunEvaluator(dataset, previewer);

var seedText = args.Length > 0 ? args[0] : "GNN1CSP98F";
var cardId = args.Length > 1 ? args[1] : "BLOODLETTING";
var thresholdPercent = args.Length > 2 ? double.Parse(args[2]) : 50d;
var sampleCount = args.Length > 3 ? int.Parse(args[3]) : 200;

var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
var context = new SeedRunEvaluationContext
{
    SeedText = seedText,
    RunSeed = seedValue,
    Character = CharacterId.Silent,
    UnlockedCharacters = new[]
    {
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    },
    PlayerCount = 1,
    AscensionLevel = 10,
    IncludeAct2 = true,
    IncludeAct3 = true
};

var filter = new SeedRunFilter
{
    NeowFilter = NeowOptionFilter.Create(null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<NeowDerivedBindingFilter>()),
    AncientFilter = new Sts2AncientFilter
    {
        Act2AncientId = "OROBAS",
        Act2OptionIds = new[] { "SEA_GLASS" },
        Act2SeaGlassOtherCharacterIds = new[] { CharacterId.Ironclad.ToString() },
        Act2SeaGlassCardIds = new[] { cardId },
        Act2SeaGlassCardSeenThreshold = thresholdPercent / 100d,
        SeaGlassPreviewSamples = sampleCount
    },
    PoolFilter = Sts2PoolFilter.Empty,
    ShopFilter = Sts2ShopFilter.Empty
};

var match = evaluator.Evaluate(context, filter);
Console.WriteLine($"seed={seedText}");
Console.WriteLine($"card={cardId} threshold={thresholdPercent}% samples={sampleCount}");
Console.WriteLine($"final={match.IsFinalMatch} ancient={match.AncientFilterMatched}");
if (match.Sts2Preview != null)
{
    foreach (var act in match.Sts2Preview.Acts)
    {
        Console.WriteLine($"Act {act.ActNumber}: {act.AncientId}");
        foreach (var option in act.AncientOptions.Where(o => o.OptionId == "SEA_GLASS"))
        {
            Console.WriteLine($"  target={option.ContextCharacterId}");
            if (option.SeaGlassPreview != null)
            {
                foreach (var ranked in option.SeaGlassPreview.RankedCards.Take(12))
                {
                    Console.WriteLine($"  {ranked.CardId}: {ranked.SeenProbability:P1}");
                }
            }
        }
    }
}

static void RunPoolCheck(string[] args)
{
    var workspace = @"E:\github project\Sts2SeedRoller";
    var optionPath = Path.Combine(workspace, "data", "0.103.2", "ancients", "options.json");
    var actPath = Path.Combine(workspace, "data", "0.103.2", "sts2", "acts.json");
    var datasetPath = Path.Combine(workspace, "data", "0.103.2", "neow", "options.json");

    using var datasetStream = File.OpenRead(datasetPath);
    var dataset = NeowOptionDataLoader.Load(datasetStream);
    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var evaluator = new SeedRunEvaluator(dataset, previewer);

    var seedText = args.Length > 0 ? args[0] : "GNN1CSP98F";
    var targetCharacter = args.Length > 1 && Enum.TryParse<CharacterId>(args[1], true, out var parsedCharacter)
        ? parsedCharacter
        : CharacterId.Ironclad;
    var sampleCount = args.Length > 2 ? int.Parse(args[2]) : 1000;
    var startCharacter = args.Length > 3 && Enum.TryParse<CharacterId>(args[3], true, out var parsedStartCharacter)
        ? parsedStartCharacter
        : CharacterId.Silent;

    var rewardPool = BuildRewardPool(dataset, targetCharacter, playerCount: 1);
    var rewardPoolSet = rewardPool.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var triggerCardId = args.Length > 4 && !string.IsNullOrWhiteSpace(args[4])
        ? args[4]
        : rewardPool.FirstOrDefault() ?? "BLOODLETTING";

    Console.WriteLine($"target={targetCharacter}");
    Console.WriteLine($"start={startCharacter}");
    Console.WriteLine($"triggerCard={triggerCardId}");
    Console.WriteLine($"reward pool count={rewardPool.Count}");
    foreach (var rarityGroup in rewardPool
                 .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
                 .OrderBy(group => group.Key.ToString()))
    {
        Console.WriteLine($"{rarityGroup.Key}: {rarityGroup.Count()}");
    }

    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var context = new SeedRunEvaluationContext
    {
        SeedText = seedText,
        RunSeed = seedValue,
        Character = startCharacter,
        UnlockedCharacters = new[]
        {
            CharacterId.Ironclad,
            CharacterId.Silent,
            CharacterId.Defect,
            CharacterId.Necrobinder,
            CharacterId.Regent
        },
        PlayerCount = 1,
        AscensionLevel = 10,
        IncludeAct2 = true,
        IncludeAct3 = true
    };

    var filter = new SeedRunFilter
    {
        NeowFilter = NeowOptionFilter.Create(null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<NeowDerivedBindingFilter>()),
        AncientFilter = new Sts2AncientFilter
        {
            Act2AncientId = "OROBAS",
            Act2OptionIds = new[] { "SEA_GLASS" },
            Act2SeaGlassOtherCharacterIds = new[] { targetCharacter.ToString() },
            Act2SeaGlassCardIds = new[] { triggerCardId },
            Act2SeaGlassCardSeenThreshold = 0d,
            SeaGlassPreviewSamples = sampleCount
        },
        PoolFilter = Sts2PoolFilter.Empty,
        ShopFilter = Sts2ShopFilter.Empty
    };

    var match = evaluator.Evaluate(context, filter);
    var preview = match.Sts2Preview;
    var seaGlassOption = preview?.Acts
        .SelectMany(static act => act.AncientOptions)
        .FirstOrDefault(option => string.Equals(option.OptionId, "SEA_GLASS", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(option.ContextCharacterId, targetCharacter.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                  option.SeaGlassPreview != null);

    if (seaGlassOption?.SeaGlassPreview == null)
    {
        Console.WriteLine("sea glass preview not found");
        return;
    }

    var rankedCards = seaGlassOption.SeaGlassPreview.RankedCards;
    var outOfPool = rankedCards
        .Where(card => !rewardPoolSet.Contains(card.CardId))
        .ToList();

    Console.WriteLine($"ranked cards={rankedCards.Count}");
    Console.WriteLine($"out of target reward pool={outOfPool.Count}");
    foreach (var card in outOfPool.Take(20))
    {
        Console.WriteLine($"OUT {card.CardId} {card.SeenProbability:P1}");
    }

    foreach (var card in rankedCards.Take(20))
    {
        var rarity = dataset.CardMetadataMap.TryGetValue(card.CardId, out var metadata)
            ? metadata.ParsedRarity.ToString()
            : "Unknown";
        Console.WriteLine($"{card.CardId} {rarity} {card.SeenProbability:P1}");
    }
}

static void RunPreview(string[] args)
{
    var workspace = @"E:\github project\Sts2SeedRoller";
    var optionPath = Path.Combine(workspace, "data", "0.103.2", "ancients", "options.json");
    var actPath = Path.Combine(workspace, "data", "0.103.2", "sts2", "acts.json");

    var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actPath);
    var seedText = args.Length > 0 ? args[0] : "GNN1CSP98F";
    var startCharacter = args.Length > 1 && Enum.TryParse<CharacterId>(args[1], true, out var parsedCharacter)
        ? parsedCharacter
        : CharacterId.Silent;
    var sampleCount = args.Length > 2 ? int.Parse(args[2]) : 8000;

    var request = new Sts2RunRequest
    {
        SeedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText)),
        SeedText = seedText,
        Character = startCharacter,
        UnlockedCharacters = new[]
        {
            CharacterId.Ironclad,
            CharacterId.Silent,
            CharacterId.Defect,
            CharacterId.Necrobinder,
            CharacterId.Regent
        },
        PlayerCount = 1,
        AscensionLevel = 10,
        IncludeAct2 = true,
        IncludeAct3 = true,
        SeaGlassPreviewSamples = sampleCount
    };

    var preview = previewer.Preview(request);
    Console.WriteLine($"start={startCharacter} seed={seedText}");
    foreach (var act in preview.Acts)
    {
        Console.WriteLine($"Act {act.ActNumber}: {act.AncientId}");
        foreach (var option in act.AncientOptions.Where(option => string.Equals(option.OptionId, "SEA_GLASS", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  target={option.ContextCharacterId}");
            if (option.SeaGlassPreview != null)
            {
                foreach (var ranked in option.SeaGlassPreview.RankedCards.Take(8))
                {
                    Console.WriteLine($"  {ranked.CardId}: {ranked.SeenProbability:P1}");
                }
            }
        }
    }
}

static List<string> BuildRewardPool(NeowOptionDataset dataset, CharacterId character, int playerCount)
{
    if (!dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards))
    {
        return new List<string>();
    }

    return characterCards
        .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                         IsCardAllowedForReward(metadata, playerCount))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static bool IsCardAllowedForReward(NeowCardMetadata metadata, int playerCount)
{
    if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
    {
        return false;
    }

    if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
    {
        return false;
    }

    return metadata.CanBeGeneratedInCombat &&
           metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
}
