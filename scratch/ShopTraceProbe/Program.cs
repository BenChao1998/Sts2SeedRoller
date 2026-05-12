using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedModel.Sts2;

var root = FindWorkspaceRoot();
var savePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(root, "存档", "1777165965.run");
var save = JsonSerializer.Deserialize<SaveSnapshot>(File.ReadAllText(savePath), new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? throw new InvalidOperationException("Failed to parse save.");

if (!SeedFormatter.TryNormalize(save.Seed ?? string.Empty, out var normalizedSeed, out var error))
{
    throw new InvalidOperationException($"Invalid seed: {error}");
}

var character = ResolveSaveCharacter(save);
var unlockedCharacters = new[]
{
    CharacterId.Ironclad,
    CharacterId.Silent,
    CharacterId.Defect,
    CharacterId.Necrobinder,
    CharacterId.Regent
};

var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
var dataset = NeowOptionDataLoader.LoadFromFile(Path.Combine(root, "data", "0.103.2", "neow", "options.json"));
var lockedCardIds = args
    .Skip(1)
    .Where(arg => arg.StartsWith("--lock=", StringComparison.OrdinalIgnoreCase))
    .SelectMany(arg => arg.Substring("--lock=".Length).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
if (lockedCardIds.Count > 0 &&
    dataset.CardPools.TryGetValue(character.ToString(), out var characterPool))
{
    characterPool.RemoveAll(cardId => lockedCardIds.Contains(cardId));
}
var act1Options = new NeowGenerator(dataset).Generate(NeowGenerationContext.Create(
    seed: seedValue,
    playerCount: 1,
    scrollBoxesEligible: true,
    hasRunModifiers: false,
    character: character,
    ascensionLevel: save.Ascension));
var actualAct1RelicId = ResolveActualAct1OpeningRelicId(save);
var act1OpeningOption = act1Options.FirstOrDefault(option => string.Equals(option.RelicId, actualAct1RelicId, StringComparison.OrdinalIgnoreCase))
    ?? act1Options.FirstOrDefault();

var previewer = Sts2RunPreviewer.CreateFromDataFiles(
    Path.Combine(root, "data", "0.103.2", "ancients", "options.zhs.json"),
    Path.Combine(root, "data", "0.103.2", "sts2", "acts.json"));
var actPools = previewer.AnalyzePools(new Sts2SeedAnalysisRequest
{
    SeedText = normalizedSeed,
    SeedValue = seedValue,
    Character = character,
    AscensionLevel = save.Ascension,
    IncludeDarvSharedAncient = true
});
var actPoolMap = actPools.Acts.ToDictionary(
    act => act.ActNumber,
    act => (IReadOnlyList<string>)act.EventPool,
    EqualityComparer<int>.Default);

var request = new Sts2ExactRouteAnalysisRequest
{
    SeedText = normalizedSeed,
    SeedValue = seedValue,
    Character = character,
    AscensionLevel = save.Ascension,
    PlayerCount = 1,
    Act1OpeningOption = act1OpeningOption
};

var analyzerAssembly = typeof(Sts2ExactRouteAnalysis).Assembly;
var previewerType = typeof(Sts2RunPreviewer);
var worldField = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing _world.");
var world = worldField.GetValue(previewer)
    ?? throw new InvalidOperationException("Missing world.");
var analyzerType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer")
    ?? throw new InvalidOperationException("Missing analyzer type.");
var analyzer = Activator.CreateInstance(analyzerType, world, root)
    ?? throw new InvalidOperationException("Failed to create analyzer.");

var simulationModelType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2EventVisibilitySimulationModel")
    ?? throw new InvalidOperationException("Missing simulation model.");
var simulationModelCreate = simulationModelType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing simulation model create.");
var simulationModel = simulationModelCreate.Invoke(null, [dataset, character, unlockedCharacters, 1, save.Ascension, root])
    ?? throw new InvalidOperationException("Failed to create simulation model.");

var rewardFactoryType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
    ?? throw new InvalidOperationException("Missing reward factory type.");
var rewardFactory = Activator.CreateInstance(
        rewardFactoryType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: [world, dataset, request],
        culture: null)
    ?? throw new InvalidOperationException("Failed to create reward factory.");
var createRewardState = rewardFactoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing reward factory create.");

var eventStateType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2EventProgressState")
    ?? throw new InvalidOperationException("Missing event state type.");
var createEventState = eventStateType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing event state create.");
var applyAct1OpeningOption = eventStateType.GetMethod("ApplyAct1OpeningOption", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ApplyAct1OpeningOption.");
var startAct = eventStateType.GetMethod("StartAct", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing StartAct.");
var gameRngType = typeof(GameRng);
var consumeRegularCombat = eventStateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public, binder: null, [gameRngType], modifiers: null)
    ?? throw new InvalidOperationException("Missing ConsumeRegularCombat.");
var consumeTreasureStop = eventStateType.GetMethod("ConsumeTreasureStop", BindingFlags.Instance | BindingFlags.Public, binder: null, [gameRngType], modifiers: null)
    ?? throw new InvalidOperationException("Missing ConsumeTreasureStop.");
var consumeEliteStop = eventStateType.GetMethod("ConsumeEliteStop", BindingFlags.Instance | BindingFlags.Public, binder: null, [gameRngType], modifiers: null)
    ?? throw new InvalidOperationException("Missing ConsumeEliteStop.");
var applyShownEvent = eventStateType.GetMethod("ApplyShownEvent", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ApplyShownEvent.");
var applyShopCardRemoval = eventStateType.GetMethod("ApplyShopCardRemoval", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ApplyShopCardRemoval.");
var gainCard = eventStateType.GetMethod("GainCard", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing GainCard.");
var gainPotion = eventStateType.GetMethod("GainPotion", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing GainPotion.");
var spendGold = eventStateType.GetMethod("SpendGold", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing SpendGold.");
var currentGoldProperty = eventStateType.GetProperty("CurrentGold", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing CurrentGold.");
var deckField = eventStateType.GetField("_deck", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing deck field.");
var cloneEventState = eventStateType.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing event clone.");

var rewardStateType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicState")
    ?? throw new InvalidOperationException("Missing reward state type.");
var cloneRewardState = rewardStateType.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing reward clone.");
var applyRewardAct1OpeningOption = rewardStateType.GetMethod("ApplyAct1OpeningOption", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing reward ApplyAct1OpeningOption.");
var consumeRewardRng = rewardStateType.GetMethod("ConsumeRewardRng", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ConsumeRewardRng.");
var consumeRewardRegularCombat = rewardStateType.GetMethod("ConsumeRegularCombat", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing reward ConsumeRegularCombat.");
var showTreasure = rewardStateType.GetMethod("ShowTreasure", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShowTreasure.");
var showElite = rewardStateType.GetMethod("ShowElite", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShowElite.");
var showShopEntries = rewardStateType.GetMethod("ShowShopEntries", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShowShopEntries.");
var showShopPreview = rewardStateType.GetMethod("ShowShopPreview", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShowShopPreview.");
var resolvePurchasedShopCard = rewardStateType.GetMethod("ResolvePurchasedShopCard", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ResolvePurchasedShopCard.");
var rewardModelProperty = rewardStateType.GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing RewardModel.");
var rewardsRngProperty = rewardStateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing RewardsRng.");
var shopsRngProperty = rewardStateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShopsRng.");
var shownShopCardsProperty = rewardStateType.GetProperty("ShownShopCards", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ShownShopCards.");
var pickMerchantCard = rewardStateType.GetMethod("PickMerchantCard", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing PickMerchantCard.");
var rollShopCardRarity = rewardStateType.GetMethod("RollShopCardRarityWithoutChangingFutureOdds", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing RollShopCardRarityWithoutChangingFutureOdds.");
var rollCardPrice = rewardStateType.GetMethod("RollCardPrice", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing RollCardPrice.");
var rollRelicPrice = rewardStateType.GetMethod("RollRelicPrice", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing RollRelicPrice.");
var rollPotionPrice = rewardStateType.GetMethod("RollPotionPrice", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing RollPotionPrice.");
var rollPotionRarity = rewardStateType.GetMethod("RollPotionRarity", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing RollPotionRarity.");
var isRelicAllowed = rewardStateType.GetMethod("IsRelicAllowed", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Missing IsRelicAllowed.");
var playerBagProperty = rewardStateType.GetProperty("PlayerBag", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing PlayerBag.");
var sharedBagProperty = rewardStateType.GetProperty("SharedBag", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing SharedBag.");

var rewardModel = rewardModelProperty.GetValue(createRewardState.Invoke(rewardFactory, null)!)!;
var getColorlessMerchantPool = rewardModel.GetType().GetMethod("GetColorlessMerchantPool", [typeof(CardRarity)])
    ?? throw new InvalidOperationException("Missing GetColorlessMerchantPool(rarity).");
var getColorlessMerchantPoolAll = rewardModel.GetType().GetMethod("GetColorlessMerchantPool", Type.EmptyTypes)
    ?? throw new InvalidOperationException("Missing GetColorlessMerchantPool().");
var cardPoolByRarityProperty = rewardModel.GetType().GetProperty("CardPoolByRarity", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing CardPoolByRarity.");
var cardMetadataMapProperty = rewardModel.GetType().GetProperty("CardMetadataMap", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing CardMetadataMap.");
var potionPoolProperty = rewardModel.GetType().GetProperty("PotionPool", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing PotionPool.");
var potionPoolByRarityProperty = rewardModel.GetType().GetProperty("PotionPoolByRarity", BindingFlags.Instance | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing PotionPoolByRarity.");

var pullEngineType = analyzerAssembly.GetType("SeedModel.Sts2.Sts2EventPullEngine")
    ?? throw new InvalidOperationException("Missing pull engine.");
var consumeShownEvent = pullEngineType.GetMethod("ConsumeShownEvent", BindingFlags.Static | BindingFlags.Public)
    ?? throw new InvalidOperationException("Missing ConsumeShownEvent.");

var actualAct1 = BuildActualAct(save, 1);
var eventState = createEventState.Invoke(null, [simulationModel, character, 1])
    ?? throw new InvalidOperationException("Failed to create event state.");
var rewardState = createRewardState.Invoke(rewardFactory, null)
    ?? throw new InvalidOperationException("Failed to create reward state.");
applyAct1OpeningOption.Invoke(eventState, [act1OpeningOption]);
applyRewardAct1OpeningOption.Invoke(rewardState, [act1OpeningOption]);
var openingRewardState = cloneRewardState.Invoke(rewardState, null)
    ?? throw new InvalidOperationException("Failed to clone opening reward state.");
startAct.Invoke(eventState, [1, 1]);
IncrementFloor(eventStateType, eventState);
var eventRng = new GameRng(seedValue, "explicit_route_probe");
object? postTreasureRewardState = null;

for (var index = 0; index < actualAct1.Rooms.Count; index++)
{
    var room = actualAct1.Rooms[index];
    if (string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    IncrementFloor(eventStateType, eventState);
    switch (room.RoomType)
    {
        case "Monster":
            var predictedCombat = PredictCombatReward(
                cloneRewardState.Invoke(rewardState, null)
                    ?? throw new InvalidOperationException("Failed to clone combat reward state."),
                rewardModel,
                cardPoolByRarityProperty,
                cardMetadataMapProperty,
                potionPoolProperty,
                potionPoolByRarityProperty,
                ascensionLevel: save.Ascension,
                isElite: false);
            consumeRegularCombat.Invoke(eventState, [eventRng]);
            consumeRewardRegularCombat.Invoke(rewardState, null);
            Console.WriteLine($"trace.floor{index + 1}.monster.predictedCards={string.Join("/", predictedCombat.CardIds)} potion={predictedCombat.PotionId ?? "(none)"}");
            Console.WriteLine($"trace.floor{index + 1}.monster={DescribeRewardState(rewardState)}");
            break;
        case "Treasure":
            consumeTreasureStop.Invoke(eventState, [eventRng]);
            var shownTreasure = ExtractStringList(showTreasure.Invoke(rewardState, [1]));
            var actualTreasure = room.SourceStats?.RelicChoices?
                .FirstOrDefault(choice => choice.WasPicked)?.Choice;
            Console.WriteLine($"trace.floor{index + 1}.treasure.relic={string.Join("/", shownTreasure.DefaultIfEmpty("(none)"))} actual={NormalizeModelId(actualTreasure)}");
            Console.WriteLine($"trace.floor{index + 1}.treasure={DescribeRewardState(rewardState)}");
            postTreasureRewardState = cloneRewardState.Invoke(rewardState, null)
                ?? throw new InvalidOperationException("Failed to clone post-treasure reward state.");
            break;
        case "Event":
            consumeShownEvent.Invoke(null, [actPoolMap[1], room.EventId, eventState]);
            ApplyActualEventState(room.SourceStats, room.EventId, eventState, eventRng, applyShownEvent, deckField, gainPotion);
            Console.WriteLine($"trace.floor{index + 1}.event.{room.EventId}={DescribeRewardState(rewardState)}");
            break;
    }
}

var baselineEventState = cloneEventState.Invoke(eventState, null)
    ?? throw new InvalidOperationException("Failed to clone baseline event state.");
var baselineRewardState = cloneRewardState.Invoke(rewardState, null)
    ?? throw new InvalidOperationException("Failed to clone baseline reward state.");
var actualSecondMonsterCards = actualAct1.Rooms
    .Where(room => string.Equals(room.RoomType, "Monster", StringComparison.OrdinalIgnoreCase))
    .Skip(1)
    .FirstOrDefault()?
    .SourceStats?.CardChoices?
    .Select(choice => NormalizeModelId(choice.Card?.Id))
    .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
    .ToList() ?? [];
var actualSecondMonsterPotion = actualAct1.Rooms
    .Where(room => string.Equals(room.RoomType, "Monster", StringComparison.OrdinalIgnoreCase))
    .Skip(1)
    .FirstOrDefault()?
    .SourceStats?.PotionChoices?
    .FirstOrDefault(choice => choice.WasPicked)?.Choice;

var initialShop = BuildShopSnapshot(
    rewardState,
    1,
    rollShopCardRarity,
    pickMerchantCard,
    rollCardPrice,
    rollRelicPrice,
    rollPotionRarity,
    rollPotionPrice,
    getColorlessMerchantPool,
    getColorlessMerchantPoolAll,
    isRelicAllowed,
    rewardsRngProperty,
    shopsRngProperty,
    playerBagProperty,
    sharedBagProperty);
var actualShopCardIds = actualAct1.Rooms
    .FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase))?
    .SourceStats?.CardChoices?
    .Select(choice => NormalizeModelId(choice.Card?.Id))
    .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
    .ToList() ?? [];
var actualBoughtCardIds = actualAct1.Rooms
    .FirstOrDefault(room => string.Equals(room.RoomType, "Shop", StringComparison.OrdinalIgnoreCase))?
    .SourceStats?.CardsGained?
    .Select(card => NormalizeModelId(card.Id))
    .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
    .ToList() ?? [];
var reflectedShopState = cloneRewardState.Invoke(baselineRewardState, null)
    ?? throw new InvalidOperationException("Failed to clone reflected shop reward state.");
var reflectedShop = showShopPreview.Invoke(reflectedShopState, [1])
    ?? throw new InvalidOperationException("Failed to build reflected shop preview.");

Console.WriteLine($"save={savePath}");
Console.WriteLine($"seed={normalizedSeed}");
Console.WriteLine($"shopInitial.cards={string.Join(" | ", initialShop.AllCards.Select(card => $"{card.CardId}@{card.Price}{(card.IsDiscounted ? "*" : string.Empty)}"))}");
Console.WriteLine($"shopInitial.relics={string.Join(" | ", initialShop.Relics.Select(relic => $"{relic.Id}@{relic.Price}"))}");
Console.WriteLine($"shopInitial.potions={string.Join(" | ", initialShop.Potions.Select(potion => $"{potion.Id}@{potion.Price}"))}");
Console.WriteLine($"rngBeforeShop={DescribeRewardState(rewardState)}");
Console.WriteLine($"bagBeforeShop.player={DescribeBagState(playerBagProperty.GetValue(rewardState)!)}");
Console.WriteLine($"bagBeforeShop.shared={DescribeBagState(sharedBagProperty.GetValue(rewardState)!)}");
Console.WriteLine($"reflectedShop.cards={string.Join(" | ", ExtractPreviewCards(reflectedShop).Select(card => $"{card.CardId}@{card.Price}"))}");
Console.WriteLine($"reflectedShop.relics={string.Join(" | ", ExtractPreviewRelics(reflectedShop).Select(relic => $"{relic.Id}@{relic.Price}"))}");
Console.WriteLine($"reflectedShop.potions={string.Join(" | ", ExtractPreviewPotions(reflectedShop).Select(potion => $"{potion.Id}@{potion.Price}"))}");
Console.WriteLine($"actualShop.cards={string.Join(" | ", actualShopCardIds)}");
Console.WriteLine($"actualShop.bought={string.Join(" | ", actualBoughtCardIds)}");
Console.WriteLine($"postTreasureCombatOffsetMatches={DescribePostTreasureCombatOffsets(
    postTreasureRewardState ?? baselineRewardState,
    cloneRewardState,
    consumeRewardRng,
    rewardModel,
    cardPoolByRarityProperty,
    cardMetadataMapProperty,
    potionPoolProperty,
    potionPoolByRarityProperty,
    save.Ascension,
    actualSecondMonsterCards,
    NormalizeModelId(actualSecondMonsterPotion))}");
Console.WriteLine($"postTreasureCombatOrderMatches={DescribePostTreasureCombatOrderVariants(
    postTreasureRewardState ?? baselineRewardState,
    cloneRewardState,
    rewardModel,
    cardPoolByRarityProperty,
    cardMetadataMapProperty,
    potionPoolProperty,
    potionPoolByRarityProperty,
    save.Ascension,
    actualSecondMonsterCards,
    NormalizeModelId(actualSecondMonsterPotion))}");
Console.WriteLine($"postTreasureOrderShopMatches={DescribePostTreasureOrderShopMatches(
    postTreasureRewardState ?? baselineRewardState,
    cloneRewardState,
    rewardModel,
    cardPoolByRarityProperty,
    cardMetadataMapProperty,
    potionPoolProperty,
    potionPoolByRarityProperty,
    save.Ascension,
    1,
    rollShopCardRarity,
    pickMerchantCard,
    rollCardPrice,
    rollRelicPrice,
    rollPotionRarity,
    rollPotionPrice,
    getColorlessMerchantPool,
    getColorlessMerchantPoolAll,
    isRelicAllowed,
    rewardsRngProperty,
    shopsRngProperty,
    playerBagProperty,
    sharedBagProperty,
    actualShopCardIds,
    actualBoughtCardIds)}");
Console.WriteLine($"preShopRewardOffsetMatches={DescribePreShopRewardOffsets(
    baselineRewardState,
    consumeRewardRng,
    1,
    rollShopCardRarity,
    pickMerchantCard,
    rollCardPrice,
    rollRelicPrice,
    rollPotionRarity,
    rollPotionPrice,
    getColorlessMerchantPool,
    getColorlessMerchantPoolAll,
    isRelicAllowed,
    rewardsRngProperty,
    shopsRngProperty,
    playerBagProperty,
    sharedBagProperty,
    actualShopCardIds,
    actualBoughtCardIds)}");

var actionPermutations = GetPermutations(["REMOVE", "FOOTWORK", "LEADING_STRIKE"]);
var printedDetailedTrace = false;
foreach (var actions in actionPermutations)
{
    var trialEventState = cloneEventState.Invoke(baselineEventState, null)
        ?? throw new InvalidOperationException("Failed to clone event state.");
    var trialRewardState = cloneRewardState.Invoke(baselineRewardState, null)
        ?? throw new InvalidOperationException("Failed to clone reward state.");
    var trialShopPreviewState = cloneRewardState.Invoke(baselineRewardState, null)
        ?? throw new InvalidOperationException("Failed to clone preview reward state.");
    var trialShop = BuildShopSnapshot(
        trialShopPreviewState,
        1,
        rollShopCardRarity,
        pickMerchantCard,
        rollCardPrice,
        rollRelicPrice,
        rollPotionRarity,
        rollPotionPrice,
        getColorlessMerchantPool,
        getColorlessMerchantPoolAll,
        isRelicAllowed,
        rewardsRngProperty,
        shopsRngProperty,
        playerBagProperty,
        sharedBagProperty);
    var liveShopEntries = ExtractShopRelics(showShopEntries.Invoke(trialRewardState, [1]));
    Console.WriteLine($"  liveShop.relics={string.Join(" | ", liveShopEntries.Select(relic => $"{relic.Id}@{relic.Price}"))}");
    Console.WriteLine($"  liveShop.rng={DescribeRewardState(trialRewardState)}");

    var okay = true;
    foreach (var action in actions)
    {
        Console.WriteLine($"  before {action}: {string.Join(" | ", trialShop.AllCards.Select(card => $"{card.CardId}@{card.Price}{(card.IsDiscounted ? "*" : string.Empty)}"))}");
        if (string.Equals(action, "REMOVE", StringComparison.OrdinalIgnoreCase))
        {
            applyShopCardRemoval.Invoke(trialEventState, [100]);
            continue;
        }

        if (!PurchaseCard(
                trialShop,
                action,
                trialRewardState,
                trialEventState,
                spendGold,
                gainCard,
                resolvePurchasedShopCard,
                shownShopCardsProperty))
        {
            okay = false;
            Console.WriteLine($"  failed on {action}");
            break;
        }
    }

    if (!okay)
    {
        Console.WriteLine($"trial {string.Join(" -> ", actions)} => unavailable");
        continue;
    }

    var results = new List<string>();
    for (var extraRewardDraws = 0; extraRewardDraws <= 12; extraRewardDraws++)
    {
        var postShopEventState = cloneEventState.Invoke(trialEventState, null)
            ?? throw new InvalidOperationException("Failed to clone post-shop event state.");
        var postShopRewardState = cloneRewardState.Invoke(trialRewardState, null)
            ?? throw new InvalidOperationException("Failed to clone post-shop reward state.");
        if (!printedDetailedTrace && extraRewardDraws == 0)
        {
            PrintPostShopTrace(
                actualAct1.Rooms,
                postShopEventState,
                postShopRewardState,
                eventRng.Seed,
                1,
                actPoolMap,
                consumeShownEvent,
                applyShownEvent,
                gainPotion,
                deckField,
                consumeRegularCombat,
                consumeRewardRegularCombat,
                consumeTreasureStop,
                consumeEliteStop,
                showTreasure,
                showElite,
                consumeRewardRng,
                selfHelpExtraRewardDraws: 0,
                playerBagProperty,
                sharedBagProperty,
                $"trace {string.Join(" -> ", actions)}");
            printedDetailedTrace = true;
        }

        var eliteRelic = SimulateAfterShopToElite(
            actualAct1.Rooms,
            postShopEventState,
            postShopRewardState,
            eventRng.Seed,
            1,
            actPoolMap,
            consumeShownEvent,
            applyShownEvent,
            gainPotion,
            deckField,
            consumeRegularCombat,
            consumeRewardRegularCombat,
            consumeTreasureStop,
            consumeEliteStop,
            showTreasure,
            showElite,
            consumeRewardRng,
            extraRewardDraws);
        results.Add($"{extraRewardDraws}:{eliteRelic}");
    }

    Console.WriteLine($"trial {string.Join(" -> ", actions)} => gold={(int)currentGoldProperty.GetValue(trialEventState)!} eliteByOffset={string.Join(" | ", results)}");
}

static string SimulateAfterShopToElite(
    IReadOnlyList<ActualRoomSnapshot> rooms,
    object eventState,
    object rewardState,
    uint seedValue,
    int actNumber,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap,
    MethodInfo consumeShownEvent,
    MethodInfo applyShownEvent,
    MethodInfo gainPotion,
    FieldInfo deckField,
    MethodInfo consumeRegularCombat,
    MethodInfo consumeRewardRegularCombat,
    MethodInfo consumeTreasureStop,
    MethodInfo consumeEliteStop,
    MethodInfo showTreasure,
    MethodInfo showElite,
    MethodInfo consumeRewardRng,
    int selfHelpExtraRewardDraws)
{
    var eventRng = new GameRng(seedValue, "explicit_route_probe");
    var totalFloorProperty = eventState.GetType().GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TotalFloor.");
    totalFloorProperty.SetValue(eventState, 7);
    for (var i = 6; i < rooms.Count; i++)
    {
        var room = rooms[i];
        totalFloorProperty.SetValue(eventState, (int)totalFloorProperty.GetValue(eventState)! + 1);
        switch (room.RoomType)
        {
            case "Event":
                consumeShownEvent.Invoke(null, [actPoolMap[actNumber], room.EventId, eventState]);
                ApplyActualEventState(room.SourceStats, room.EventId, eventState, eventRng, applyShownEvent, deckField, gainPotion);
                if (string.Equals(room.EventId, "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase))
                {
                    consumeRewardRng.Invoke(rewardState, [selfHelpExtraRewardDraws]);
                }
                break;
            case "RestSite":
                break;
            case "Treasure":
                consumeTreasureStop.Invoke(eventState, [eventRng]);
                _ = showTreasure.Invoke(rewardState, [actNumber]);
                break;
            case "Monster":
                consumeRegularCombat.Invoke(eventState, [eventRng]);
                consumeRewardRegularCombat.Invoke(rewardState, null);
                break;
            case "Elite":
                consumeEliteStop.Invoke(eventState, [eventRng]);
                var relics = ExtractStringList(showElite.Invoke(rewardState, [actNumber]));
                return relics.FirstOrDefault() ?? string.Empty;
        }
    }

    return string.Empty;
}

static bool PurchaseCard(
    ShopSnapshot shop,
    string cardId,
    object rewardState,
    object eventState,
    MethodInfo spendGold,
    MethodInfo gainCard,
    MethodInfo resolvePurchasedShopCard,
    PropertyInfo shownShopCardsProperty)
{
    var offer = shop.AllCards.FirstOrDefault(card => string.Equals(card.CardId, cardId, StringComparison.OrdinalIgnoreCase));
    if (offer == null)
    {
        return false;
    }

    spendGold.Invoke(eventState, [offer.Price]);
    gainCard.Invoke(eventState, [offer.CardId]);
    resolvePurchasedShopCard.Invoke(rewardState, [offer.CardId]);

    var shownCards = ExtractShownShopCards(shownShopCardsProperty.GetValue(rewardState));
    var replacement = shownCards.FirstOrDefault(card => string.Equals(card.CardId, offer.CardId, StringComparison.OrdinalIgnoreCase));
    if (replacement == null)
    {
        shop.Remove(offer);
    }
    else
    {
        shop.Replace(offer, replacement);
    }

    return true;
}

static void PrintPostShopTrace(
    IReadOnlyList<ActualRoomSnapshot> rooms,
    object eventState,
    object rewardState,
    uint seedValue,
    int actNumber,
    IReadOnlyDictionary<int, IReadOnlyList<string>> actPoolMap,
    MethodInfo consumeShownEvent,
    MethodInfo applyShownEvent,
    MethodInfo gainPotion,
    FieldInfo deckField,
    MethodInfo consumeRegularCombat,
    MethodInfo consumeRewardRegularCombat,
    MethodInfo consumeTreasureStop,
    MethodInfo consumeEliteStop,
    MethodInfo showTreasure,
    MethodInfo showElite,
    MethodInfo consumeRewardRng,
    int selfHelpExtraRewardDraws,
    PropertyInfo playerBagProperty,
    PropertyInfo sharedBagProperty,
    string label)
{
    Console.WriteLine(label);
    Console.WriteLine($"  rng.afterShop={DescribeRewardState(rewardState)}");
    Console.WriteLine($"  bag.afterShop.player={DescribeBagState(playerBagProperty.GetValue(rewardState)!)}");
    Console.WriteLine($"  bag.afterShop.shared={DescribeBagState(sharedBagProperty.GetValue(rewardState)!)}");

    var eventRng = new GameRng(seedValue, "explicit_route_probe");
    var totalFloorProperty = eventState.GetType().GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TotalFloor.");
    totalFloorProperty.SetValue(eventState, 7);

    for (var i = 6; i < rooms.Count; i++)
    {
        var room = rooms[i];
        totalFloorProperty.SetValue(eventState, (int)totalFloorProperty.GetValue(eventState)! + 1);
        switch (room.RoomType)
        {
            case "Event":
                consumeShownEvent.Invoke(null, [actPoolMap[actNumber], room.EventId, eventState]);
                ApplyActualEventState(room.SourceStats, room.EventId, eventState, eventRng, applyShownEvent, deckField, gainPotion);
                if (string.Equals(room.EventId, "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase))
                {
                    consumeRewardRng.Invoke(rewardState, [selfHelpExtraRewardDraws]);
                }
                break;
            case "RestSite":
                break;
            case "Treasure":
                consumeTreasureStop.Invoke(eventState, [eventRng]);
                _ = showTreasure.Invoke(rewardState, [actNumber]);
                break;
            case "Monster":
                consumeRegularCombat.Invoke(eventState, [eventRng]);
                consumeRewardRegularCombat.Invoke(rewardState, null);
                break;
            case "Elite":
                consumeEliteStop.Invoke(eventState, [eventRng]);
                var relics = ExtractStringList(showElite.Invoke(rewardState, [actNumber]));
                Console.WriteLine($"  floor{room.Floor}.elite={string.Join(" / ", relics)}");
                Console.WriteLine($"  bag.beforeEliteReward.player={DescribeBagState(playerBagProperty.GetValue(rewardState)!)}");
                Console.WriteLine($"  bag.beforeEliteReward.shared={DescribeBagState(sharedBagProperty.GetValue(rewardState)!)}");
                return;
        }

        if (room.Floor is 7 or 9)
        {
            Console.WriteLine($"  floor{room.Floor}.{room.RoomType}.rng={DescribeRewardState(rewardState)}");
            Console.WriteLine($"  floor{room.Floor}.{room.RoomType}.player={DescribeBagState(playerBagProperty.GetValue(rewardState)!)}");
            Console.WriteLine($"  floor{room.Floor}.{room.RoomType}.shared={DescribeBagState(sharedBagProperty.GetValue(rewardState)!)}");
        }
    }
}

static ShopSnapshot BuildShopSnapshot(
    object rewardState,
    int actNumber,
    MethodInfo rollShopCardRarity,
    MethodInfo pickMerchantCard,
    MethodInfo rollCardPrice,
    MethodInfo rollRelicPrice,
    MethodInfo rollPotionRarity,
    MethodInfo rollPotionPrice,
    MethodInfo getColorlessMerchantPool,
    MethodInfo getColorlessMerchantPoolAll,
    MethodInfo isRelicAllowed,
    PropertyInfo rewardsRngProperty,
    PropertyInfo shopsRngProperty,
    PropertyInfo playerBagProperty,
    PropertyInfo sharedBagProperty)
{
    var rewardModel = rewardState.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rewardState)!;
    var shopsRng = (GameRng)shopsRngProperty.GetValue(rewardState)!;
    var rewardsRng = (GameRng)rewardsRngProperty.GetValue(rewardState)!;
    var playerBag = playerBagProperty.GetValue(rewardState)!;
    var sharedBag = sharedBagProperty.GetValue(rewardState)!;
    var pullFromBack = playerBag.GetType().GetMethod("PullFromBack", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PullFromBack.");
    var removeShared = sharedBag.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing shared remove.");

    var discountedSlot = shopsRng.NextInt(5);
    var allCards = new List<CardOffer>();
    var selectedCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var coloredTypes = new[] { CardType.Attack, CardType.Attack, CardType.Skill, CardType.Skill, CardType.Power };
    for (var i = 0; i < coloredTypes.Length; i++)
    {
        var rarity = (CardRarity)rollShopCardRarity.Invoke(rewardState, null)!;
        var cardId = pickMerchantCard.Invoke(rewardState, [selectedCards, coloredTypes[i], rarity])?.ToString();
        if (string.IsNullOrWhiteSpace(cardId))
        {
            continue;
        }

        selectedCards.Add(cardId);
        _ = rewardsRng.NextFloat();
        var price = (int)rollCardPrice.Invoke(rewardState, [cardId, false, false])!;
        if (i == discountedSlot)
        {
            price = (int)rollCardPrice.Invoke(rewardState, [cardId, true, false])!;
        }
        allCards.Add(new CardOffer(cardId, price, i == discountedSlot, false, coloredTypes[i], null));
    }

    var colorlessRarities = new[] { CardRarity.Uncommon, CardRarity.Rare };
    foreach (var rarity in colorlessRarities)
    {
        var pool = ((IEnumerable<string>)getColorlessMerchantPool.Invoke(rewardModel, [rarity])!)
            .Where(cardId => !selectedCards.Contains(cardId))
            .ToArray();
        if (pool.Length == 0)
        {
            pool = ((IEnumerable<string>)getColorlessMerchantPoolAll.Invoke(rewardModel, null)!)
                .Where(cardId => !selectedCards.Contains(cardId))
                .ToArray();
        }

        var cardId = shopsRng.NextItem(pool);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            continue;
        }

        selectedCards.Add(cardId);
        _ = rewardsRng.NextFloat();
        var price = (int)rollCardPrice.Invoke(rewardState, [cardId, false, true])!;
        allCards.Add(new CardOffer(cardId, price, false, true, null, rarity));
    }

    var selectedRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var relics = new List<ShopRelicRecord>();
    foreach (var rarity in new[] { RollRelicRarity(rewardsRng), RollRelicRarity(rewardsRng), "Shop" })
    {
        var relicId = pullFromBack.Invoke(playerBag, [rarity, selectedRelics, ShopBlockedRelics(), CreateRelicAllowedFilter(rewardState, isRelicAllowed, actNumber), null])?.ToString();
        if (string.IsNullOrWhiteSpace(relicId))
        {
            continue;
        }

        selectedRelics.Add(relicId);
        removeShared.Invoke(sharedBag, [relicId]);
        relics.Add(new ShopRelicRecord(relicId, (int)rollRelicPrice.Invoke(rewardState, [relicId])!));
    }

    var rewardModelType = rewardModel.GetType();
    var potionPoolProperty = rewardModelType.GetProperty("PotionPool", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionPool.");
    var potionPoolByRarityProperty = rewardModelType.GetProperty("PotionPoolByRarity", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionPoolByRarity.");
    var potionPool = ((IEnumerable<string>)potionPoolProperty.GetValue(rewardModel)!).ToArray();
    var potionPoolByRarity = (IDictionary)potionPoolByRarityProperty.GetValue(rewardModel)!;
    var selectedPotions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var potions = new List<ShopPotionRecord>();
    for (var i = 0; i < 3; i++)
    {
        var rarity = rollPotionRarity.Invoke(null, [shopsRng])!;
        var rarityPool = potionPoolByRarity[rarity] as IEnumerable<string>;
        var candidates = (rarityPool ?? Array.Empty<string>())
            .Where(candidate => !selectedPotions.Contains(candidate))
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = potionPool.Where(candidate => !selectedPotions.Contains(candidate)).ToArray();
        }

        var potionId = shopsRng.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(potionId))
        {
            continue;
        }

        selectedPotions.Add(potionId);
        potions.Add(new ShopPotionRecord(potionId, (int)rollPotionPrice.Invoke(rewardState, [rarity])!));
    }

    return new ShopSnapshot(rewardModel, allCards, relics, potions);
}

static string DescribePreShopRewardOffsets(
    object baselineRewardState,
    MethodInfo consumeRewardRng,
    int actNumber,
    MethodInfo rollShopCardRarity,
    MethodInfo pickMerchantCard,
    MethodInfo rollCardPrice,
    MethodInfo rollRelicPrice,
    MethodInfo rollPotionRarity,
    MethodInfo rollPotionPrice,
    MethodInfo getColorlessMerchantPool,
    MethodInfo getColorlessMerchantPoolAll,
    MethodInfo isRelicAllowed,
    PropertyInfo rewardsRngProperty,
    PropertyInfo shopsRngProperty,
    PropertyInfo playerBagProperty,
    PropertyInfo sharedBagProperty,
    IReadOnlyList<string> actualSkippedCardIds,
    IReadOnlyList<string> actualBoughtCardIds)
{
    if (actualSkippedCardIds.Count == 0 && actualBoughtCardIds.Count == 0)
    {
        return "(no actual shop cards)";
    }

    var cloneMethod = baselineRewardState.GetType().GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing reward clone.");
    var rareOffsetProperty = baselineRewardState.GetType().GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRareOffset.");
    var expectedIds = actualSkippedCardIds
        .Concat(actualBoughtCardIds)
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .OrderBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var results = new List<string>();
    var baselineRareOffset = (float)(rareOffsetProperty.GetValue(baselineRewardState) ?? -0.05f);

    for (var extraRewardDraws = 0; extraRewardDraws <= 20; extraRewardDraws++)
    {
        foreach (var rareOffset in new[]
                 {
                     baselineRareOffset,
                     baselineRareOffset - 0.015f,
                     baselineRareOffset - 0.01f,
                     baselineRareOffset - 0.005f,
                     baselineRareOffset + 0.005f,
                     baselineRareOffset + 0.01f,
                     baselineRareOffset + 0.015f
                 }.Distinct())
        {
            var probeState = cloneMethod.Invoke(baselineRewardState, null)
                ?? throw new InvalidOperationException("Failed to clone pre-shop reward state.");
            consumeRewardRng.Invoke(probeState, [extraRewardDraws]);
            rareOffsetProperty.SetValue(probeState, rareOffset);
            var probeShop = BuildShopSnapshot(
                probeState,
                actNumber,
                rollShopCardRarity,
                pickMerchantCard,
                rollCardPrice,
                rollRelicPrice,
                rollPotionRarity,
                rollPotionPrice,
                getColorlessMerchantPool,
                getColorlessMerchantPoolAll,
                isRelicAllowed,
                rewardsRngProperty,
                shopsRngProperty,
                playerBagProperty,
                sharedBagProperty);
            var actualSetMatches = probeShop.AllCards
                .Select(card => card.CardId)
                .OrderBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(expectedIds, StringComparer.OrdinalIgnoreCase);
            var overlap = probeShop.AllCards
                .Select(card => card.CardId)
                .Count(cardId => expectedIds.Contains(cardId, StringComparer.OrdinalIgnoreCase));
            if (actualSetMatches || overlap >= Math.Max(5, expectedIds.Length - 1))
            {
                results.Add($"{extraRewardDraws}@{rareOffset:0.###}:set={actualSetMatches},overlap={overlap},cards={string.Join("/", probeShop.AllCards.Select(card => card.CardId))}");
            }
        }
    }

    return results.Count > 0 ? string.Join(" || ", results) : "(no close matches in 0..20)";
}

static string DescribePostTreasureCombatOffsets(
    object baselineRewardState,
    MethodInfo cloneMethod,
    MethodInfo consumeRewardRng,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    IReadOnlyList<string> expectedCardIds,
    string? expectedPotionId)
{
    if (expectedCardIds.Count == 0)
    {
        return "(no actual second-combat cards)";
    }

    var rareOffsetProperty = baselineRewardState.GetType().GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRareOffset.");
    var potionChanceProperty = baselineRewardState.GetType().GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionChance.");
    var baselineRareOffset = (float)(rareOffsetProperty.GetValue(baselineRewardState) ?? -0.05f);
    var baselinePotionChance = (float)(potionChanceProperty.GetValue(baselineRewardState) ?? 0.4f);
    var results = new List<string>();
    for (var extraRewardDraws = 0; extraRewardDraws <= 12; extraRewardDraws++)
    {
        foreach (var rareOffset in new[]
        {
            baselineRareOffset,
            baselineRareOffset - 0.015f,
            baselineRareOffset - 0.01f,
            baselineRareOffset - 0.005f,
            baselineRareOffset + 0.005f,
            baselineRareOffset + 0.01f,
            baselineRareOffset + 0.015f
        })
        {
            foreach (var potionChance in new[]
            {
                baselinePotionChance,
                Math.Clamp(baselinePotionChance - 0.1f, 0f, 1f),
                Math.Clamp(baselinePotionChance + 0.1f, 0f, 1f)
            })
            {
                var probeState = cloneMethod.Invoke(baselineRewardState, null)
                    ?? throw new InvalidOperationException("Failed to clone post-treasure reward state.");
                consumeRewardRng.Invoke(probeState, [extraRewardDraws]);
                rareOffsetProperty.SetValue(probeState, rareOffset);
                potionChanceProperty.SetValue(probeState, potionChance);
                var prediction = PredictCombatReward(
                    probeState,
                    rewardModel,
                    cardPoolByRarityProperty,
                    cardMetadataMapProperty,
                    potionPoolProperty,
                    potionPoolByRarityProperty,
                    ascensionLevel,
                    isElite: false);
                var cards = prediction.CardIds.ToArray();
                var cardsMatch = cards.SequenceEqual(expectedCardIds, StringComparer.OrdinalIgnoreCase);
                var potionMatch = string.Equals(prediction.PotionId, expectedPotionId, StringComparison.OrdinalIgnoreCase);
                var overlap = cards.Count(cardId => expectedCardIds.Contains(cardId, StringComparer.OrdinalIgnoreCase));
                if ((cardsMatch && potionMatch) || overlap >= 2)
                {
                    results.Add($"{extraRewardDraws}@rare={rareOffset:0.###}@potion={potionChance:0.###}:cards={string.Join("/", cards)};potionOut={prediction.PotionId ?? "(none)"};matchCards={cardsMatch};matchPotion={potionMatch};overlap={overlap}");
                }
            }
        }
    }

    return results.Count > 0 ? string.Join(" || ", results) : "(no close matches in 0..12 with rare/potion variations)";
}

static string DescribePostTreasureCombatOrderVariants(
    object baselineRewardState,
    MethodInfo cloneMethod,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    IReadOnlyList<string> expectedCardIds,
    string? expectedPotionId)
{
    if (expectedCardIds.Count == 0)
    {
        return "(no actual second-combat cards)";
    }

    var results = new List<string>();
    foreach (var order in new[] { "GPC", "GCP", "PGC", "PCG", "CGP", "CPG" })
    {
        var probeState = cloneMethod.Invoke(baselineRewardState, null)
            ?? throw new InvalidOperationException("Failed to clone post-treasure reward state.");
        var prediction = PredictCombatRewardWithOrder(
            probeState,
            rewardModel,
            cardPoolByRarityProperty,
            cardMetadataMapProperty,
            potionPoolProperty,
            potionPoolByRarityProperty,
            ascensionLevel,
            isElite: false,
            order);
        var cards = prediction.CardIds.ToArray();
        var cardsMatch = cards.SequenceEqual(expectedCardIds, StringComparer.OrdinalIgnoreCase);
        var potionMatch = string.Equals(prediction.PotionId, expectedPotionId, StringComparison.OrdinalIgnoreCase);
        var overlap = cards.Count(cardId => expectedCardIds.Contains(cardId, StringComparer.OrdinalIgnoreCase));
        if ((cardsMatch && potionMatch) || overlap >= 2)
        {
            results.Add($"{order}:cards={string.Join("/", cards)};potionOut={prediction.PotionId ?? "(none)"};matchCards={cardsMatch};matchPotion={potionMatch};overlap={overlap}");
        }
    }

    return results.Count > 0 ? string.Join(" || ", results) : "(no close matches across G/P/C order variants)";
}

static string DescribePostTreasureOrderShopMatches(
    object baselineRewardState,
    MethodInfo cloneMethod,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    int actNumber,
    MethodInfo rollShopCardRarity,
    MethodInfo pickMerchantCard,
    MethodInfo rollCardPrice,
    MethodInfo rollRelicPrice,
    MethodInfo rollPotionRarity,
    MethodInfo rollPotionPrice,
    MethodInfo getColorlessMerchantPool,
    MethodInfo getColorlessMerchantPoolAll,
    MethodInfo isRelicAllowed,
    PropertyInfo rewardsRngProperty,
    PropertyInfo shopsRngProperty,
    PropertyInfo playerBagProperty,
    PropertyInfo sharedBagProperty,
    IReadOnlyList<string> actualSkippedCardIds,
    IReadOnlyList<string> actualBoughtCardIds)
{
    var expectedIds = actualSkippedCardIds
        .Concat(actualBoughtCardIds)
        .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
        .OrderBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var results = new List<string>();
    foreach (var order in new[] { "GPC", "GCP", "PGC", "PCG", "CGP", "CPG" })
    {
        var probeState = cloneMethod.Invoke(baselineRewardState, null)
            ?? throw new InvalidOperationException("Failed to clone post-treasure reward state.");
        ApplyCombatRewardOrder(
            probeState,
            rewardModel,
            cardPoolByRarityProperty,
            cardMetadataMapProperty,
            potionPoolProperty,
            potionPoolByRarityProperty,
            ascensionLevel,
            isElite: false,
            order);
        var probeShop = BuildShopSnapshot(
            probeState,
            actNumber,
            rollShopCardRarity,
            pickMerchantCard,
            rollCardPrice,
            rollRelicPrice,
            rollPotionRarity,
            rollPotionPrice,
            getColorlessMerchantPool,
            getColorlessMerchantPoolAll,
            isRelicAllowed,
            rewardsRngProperty,
            shopsRngProperty,
            playerBagProperty,
            sharedBagProperty);
        var overlap = probeShop.AllCards
            .Select(card => card.CardId)
            .Count(cardId => expectedIds.Contains(cardId, StringComparer.OrdinalIgnoreCase));
        if (overlap >= 5)
        {
            results.Add($"{order}:overlap={overlap},cards={string.Join("/", probeShop.AllCards.Select(card => card.CardId))}");
        }
    }

    return results.Count > 0 ? string.Join(" || ", results) : "(no close shop matches across G/P/C order variants)";
}

static CombatRewardPrediction PredictCombatReward(
    object rewardState,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    bool isElite)
{
    var stateType = rewardState.GetType();
    var rewardsRng = (GameRng)(stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing RewardsRng."));
    var cardRareOffset = (float)(stateType.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? -0.05f);
    var potionChance = (float)(stateType.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? 0.4f);

    _ = isElite ? rewardsRng.NextInt(35, 46) : rewardsRng.NextInt(10, 21);
    var potionRoll = rewardsRng.NextFloat();
    string? potionId = null;
    if (potionRoll < potionChance + (isElite ? 0.125f : 0f))
    {
        var potionRarity = RollPotionRarity(rewardsRng);
        var potionPools = (IDictionary)potionPoolByRarityProperty.GetValue(rewardModel)!;
        var rarityPool = potionPools[potionRarity] as IEnumerable<string>;
        var allPotions = ((IEnumerable<string>)potionPoolProperty.GetValue(rewardModel)!).ToArray();
        var candidates = (rarityPool ?? Array.Empty<string>()).ToArray();
        if (candidates.Length == 0)
        {
            candidates = allPotions;
        }

        potionId = rewardsRng.NextItem(candidates);
    }

    var result = new List<string>(3);
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var cardPools = (IDictionary)cardPoolByRarityProperty.GetValue(rewardModel)!;
    var cardMetadataMap = (IDictionary)cardMetadataMapProperty.GetValue(rewardModel)!;
    for (var i = 0; i < 3; i++)
    {
        var roll = rewardsRng.NextFloat();
        var rareOdds = GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Rare, ascensionLevel) + cardRareOffset;
        CardRarity rarity;
        if (roll < rareOdds)
        {
            rarity = CardRarity.Rare;
        }
        else if (roll < GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Uncommon, ascensionLevel) + rareOdds)
        {
            rarity = CardRarity.Uncommon;
        }
        else
        {
            rarity = CardRarity.Common;
        }

        cardRareOffset = rarity == CardRarity.Rare
            ? -0.05f
            : Math.Min(cardRareOffset + (ascensionLevel >= 7 ? 0.005f : 0.01f), 0.4f);

        while (true)
        {
            var pool = cardPools[rarity] as IEnumerable<string>;
            var candidates = (pool ?? Array.Empty<string>()).Where(cardId => !used.Contains(cardId)).ToArray();
            if (candidates.Length > 0)
            {
                var cardId = rewardsRng.NextItem(candidates);
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    result.Add(cardId);
                    used.Add(cardId);
                    _ = rewardsRng.NextFloat();
                }

                break;
            }

            rarity = rarity switch
            {
                CardRarity.Common => CardRarity.Uncommon,
                CardRarity.Uncommon => CardRarity.Rare,
                _ => CardRarity.None
            };
            if (rarity == CardRarity.None)
            {
                break;
            }
        }
    }

    return new CombatRewardPrediction(result, potionId);
}

static CombatRewardPrediction PredictCombatRewardWithOrder(
    object rewardState,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    bool isElite,
    string order)
{
    var stateType = rewardState.GetType();
    var rewardsRng = (GameRng)(stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing RewardsRng."));
    var cardRareOffset = (float)(stateType.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? -0.05f);
    var potionChance = (float)(stateType.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? 0.4f);

    string? potionId = null;
    var result = new List<string>(3);
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var cardPools = (IDictionary)cardPoolByRarityProperty.GetValue(rewardModel)!;
    var allPotions = ((IEnumerable<string>)potionPoolProperty.GetValue(rewardModel)!).ToArray();
    var potionPools = (IDictionary)potionPoolByRarityProperty.GetValue(rewardModel)!;

    void DoGold() => _ = isElite ? rewardsRng.NextInt(35, 46) : rewardsRng.NextInt(10, 21);

    void DoPotion()
    {
        var potionRoll = rewardsRng.NextFloat();
        var threshold = potionChance + (isElite ? 0.25f : 0f);
        if (potionRoll < threshold)
        {
            var potionRarity = RollPotionRarity(rewardsRng);
            var rarityPool = potionPools[potionRarity] as IEnumerable<string>;
            var candidates = (rarityPool ?? Array.Empty<string>()).ToArray();
            if (candidates.Length == 0)
            {
                candidates = allPotions;
            }

            potionId = rewardsRng.NextItem(candidates);
            potionChance -= 0.1f;
        }
        else
        {
            potionChance += 0.1f;
        }
    }

    void DoCards()
    {
        for (var i = 0; i < 3; i++)
        {
            var roll = rewardsRng.NextFloat();
            var rareOdds = GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Rare, ascensionLevel) + cardRareOffset;
            CardRarity rarity;
            if (roll < rareOdds)
            {
                rarity = CardRarity.Rare;
            }
            else if (roll < GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Uncommon, ascensionLevel) + rareOdds)
            {
                rarity = CardRarity.Uncommon;
            }
            else
            {
                rarity = CardRarity.Common;
            }

            cardRareOffset = rarity == CardRarity.Rare
                ? -0.05f
                : Math.Min(cardRareOffset + (ascensionLevel >= 7 ? 0.005f : 0.01f), 0.4f);

            while (true)
            {
                var pool = cardPools[rarity] as IEnumerable<string>;
                var candidates = (pool ?? Array.Empty<string>()).Where(cardId => !used.Contains(cardId)).ToArray();
                if (candidates.Length > 0)
                {
                    var cardId = rewardsRng.NextItem(candidates);
                    if (!string.IsNullOrWhiteSpace(cardId))
                    {
                        result.Add(cardId);
                        used.Add(cardId);
                        _ = rewardsRng.NextFloat();
                    }

                    break;
                }

                rarity = rarity switch
                {
                    CardRarity.Common => CardRarity.Uncommon,
                    CardRarity.Uncommon => CardRarity.Rare,
                    _ => CardRarity.None
                };
                if (rarity == CardRarity.None)
                {
                    break;
                }
            }
        }
    }

    foreach (var step in order)
    {
        switch (step)
        {
            case 'G':
                DoGold();
                break;
            case 'P':
                DoPotion();
                break;
            case 'C':
                DoCards();
                break;
        }
    }

    return new CombatRewardPrediction(result, potionId);
}

static void ApplyCombatRewardOrder(
    object rewardState,
    object rewardModel,
    PropertyInfo cardPoolByRarityProperty,
    PropertyInfo cardMetadataMapProperty,
    PropertyInfo potionPoolProperty,
    PropertyInfo potionPoolByRarityProperty,
    int ascensionLevel,
    bool isElite,
    string order)
{
    var stateType = rewardState.GetType();
    var rewardsRng = (GameRng)(stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing RewardsRng."));
    var cardRareOffsetProperty = stateType.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CardRareOffset.");
    var potionChanceProperty = stateType.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing PotionChance.");
    var currentRewardCardsProperty = stateType.GetProperty("CurrentRewardCards", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentRewardCards.");
    var cardRareOffset = (float)(cardRareOffsetProperty.GetValue(rewardState) ?? -0.05f);
    var potionChance = (float)(potionChanceProperty.GetValue(rewardState) ?? 0.4f);
    var currentRewardCards = currentRewardCardsProperty.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing CurrentRewardCards instance.");
    var addMethod = currentRewardCards.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentRewardCards.Add.");
    var clearMethod = currentRewardCards.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentRewardCards.Clear.");
    var countProperty = currentRewardCards.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing CurrentRewardCards.Count.");

    var cardPools = (IDictionary)cardPoolByRarityProperty.GetValue(rewardModel)!;
    var allPotions = ((IEnumerable<string>)potionPoolProperty.GetValue(rewardModel)!).ToArray();
    var potionPools = (IDictionary)potionPoolByRarityProperty.GetValue(rewardModel)!;

    void DoGold() => _ = isElite ? rewardsRng.NextInt(35, 46) : rewardsRng.NextInt(10, 21);

    void DoPotion()
    {
        var potionRoll = rewardsRng.NextFloat();
        var threshold = potionChance + (isElite ? 0.25f : 0f);
        if (potionRoll < threshold)
        {
            var potionRarity = RollPotionRarity(rewardsRng);
            var rarityPool = potionPools[potionRarity] as IEnumerable<string>;
            var candidates = (rarityPool ?? Array.Empty<string>()).ToArray();
            if (candidates.Length == 0)
            {
                candidates = allPotions;
            }

            _ = rewardsRng.NextItem(candidates);
            potionChance -= 0.1f;
        }
        else
        {
            potionChance += 0.1f;
        }
    }

    void DoCards()
    {
        for (var i = 0; i < 3; i++)
        {
            var roll = rewardsRng.NextFloat();
            var rareOdds = GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Rare, ascensionLevel) + cardRareOffset;
            CardRarity rarity;
            if (roll < rareOdds)
            {
                rarity = CardRarity.Rare;
            }
            else if (roll < GetBaseCardOdds(isElite ? CardRarityOddsType.EliteEncounter : CardRarityOddsType.RegularEncounter, CardRarity.Uncommon, ascensionLevel) + rareOdds)
            {
                rarity = CardRarity.Uncommon;
            }
            else
            {
                rarity = CardRarity.Common;
            }

            cardRareOffset = rarity == CardRarity.Rare
                ? -0.05f
                : Math.Min(cardRareOffset + (ascensionLevel >= 7 ? 0.005f : 0.01f), 0.4f);

            while (true)
            {
                var pool = cardPools[rarity] as IEnumerable<string>;
                var candidates = (pool ?? Array.Empty<string>())
                    .Where(cardId => !(bool)(currentRewardCards.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public)!.Invoke(currentRewardCards, [cardId]) ?? false))
                    .ToArray();
                if (candidates.Length > 0)
                {
                    var cardId = rewardsRng.NextItem(candidates);
                    if (!string.IsNullOrWhiteSpace(cardId))
                    {
                        addMethod.Invoke(currentRewardCards, [cardId]);
                        _ = rewardsRng.NextFloat();
                        if ((int)(countProperty.GetValue(currentRewardCards) ?? 0) >= 3)
                        {
                            clearMethod.Invoke(currentRewardCards, null);
                        }
                    }

                    break;
                }

                rarity = rarity switch
                {
                    CardRarity.Common => CardRarity.Uncommon,
                    CardRarity.Uncommon => CardRarity.Rare,
                    _ => CardRarity.None
                };
                if (rarity == CardRarity.None)
                {
                    break;
                }
            }
        }
    }

    foreach (var step in order)
    {
        switch (step)
        {
            case 'G':
                DoGold();
                break;
            case 'P':
                DoPotion();
                break;
            case 'C':
                DoCards();
                break;
        }
    }

    cardRareOffsetProperty.SetValue(rewardState, cardRareOffset);
    potionChanceProperty.SetValue(rewardState, potionChance);
}

static float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity, int ascensionLevel)
{
    var scarcityActive = ascensionLevel >= 7;
    return oddsType switch
    {
        CardRarityOddsType.RegularEncounter => rarity switch
        {
            CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
            CardRarity.Uncommon => 0.37f,
            CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
            _ => 0f
        },
        CardRarityOddsType.EliteEncounter => rarity switch
        {
            CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
            CardRarity.Uncommon => 0.4f,
            CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
            _ => 0f
        },
        _ => 0f
    };
}

static PotionRarity RollPotionRarity(GameRng rng)
{
    var roll = rng.NextFloat();
    if (roll <= 0.1f)
    {
        return PotionRarity.Rare;
    }

    if (roll <= 0.35f)
    {
        return PotionRarity.Uncommon;
    }

    return PotionRarity.Common;
}

static Func<string, bool> CreateRelicAllowedFilter(object rewardState, MethodInfo isRelicAllowed, int actNumber) =>
    relicId => (bool)(isRelicAllowed.Invoke(rewardState, [relicId, actNumber]) ?? false);

static string DescribeBagState(object bag)
{
    var bucketsField = bag.GetType().GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing bag buckets.");
    var buckets = (IDictionary)bucketsField.GetValue(bag)!;
    var parts = new List<string>();
    foreach (DictionaryEntry entry in buckets)
    {
        var items = ((IEnumerable)entry.Value).Cast<object>().Select(item => item.ToString() ?? string.Empty).ToList();
        var head = string.Join(",", items.Take(3));
        var tail = string.Join(",", items.Skip(Math.Max(0, items.Count - 3)));
        parts.Add($"{entry.Key}[{items.Count}]<{head}>...<{tail}>");
    }

    return string.Join(" | ", parts);
}

static string DescribeRewardState(object rewardState)
{
    var type = rewardState.GetType();
    var rewardsRng = (GameRng)(type.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing rewards rng."));
    var shopsRng = (GameRng)(type.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing shops rng."));
    var treasureRng = (GameRng)(type.GetProperty("TreasureRng", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rewardState)
        ?? throw new InvalidOperationException("Missing treasure rng."));
    var rareOffset = type.GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState);
    var potionChance = type.GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardState);
    return $"rewards={rewardsRng.Counter},shops={shopsRng.Counter},treasure={treasureRng.Counter},rareOffset={rareOffset},potionChance={potionChance}";
}

static List<ShopRelicRecord> ExtractShopRelics(object? entries)
{
    if (entries is not IEnumerable enumerable)
    {
        return [];
    }

    return enumerable
        .Cast<object>()
        .Select(entry =>
        {
            var type = entry.GetType();
            return new ShopRelicRecord(
                type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty,
                (int)(type.GetProperty("Price", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? 0));
        })
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
        .ToList();
}

static List<CardOffer> ExtractPreviewCards(object preview)
{
    var type = preview.GetType();
    var colored = ExtractPreviewCardList(type.GetProperty("ColoredCards", BindingFlags.Instance | BindingFlags.Public)?.GetValue(preview));
    var colorless = ExtractPreviewCardList(type.GetProperty("ColorlessCards", BindingFlags.Instance | BindingFlags.Public)?.GetValue(preview));
    return colored.Concat(colorless).ToList();
}

static List<CardOffer> ExtractPreviewCardList(object? entries)
{
    if (entries is not IEnumerable enumerable)
    {
        return [];
    }

    return enumerable
        .Cast<object>()
        .Select(entry =>
        {
            var type = entry.GetType();
            return new CardOffer(
                type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty,
                (int)(type.GetProperty("Price", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? 0),
                false,
                false,
                null,
                null);
        })
        .Where(entry => !string.IsNullOrWhiteSpace(entry.CardId))
        .ToList();
}

static List<ShopRelicRecord> ExtractPreviewRelics(object preview)
{
    var type = preview.GetType();
    return ExtractShopRelics(type.GetProperty("Relics", BindingFlags.Instance | BindingFlags.Public)?.GetValue(preview));
}

static List<ShopPotionRecord> ExtractPreviewPotions(object preview)
{
    var type = preview.GetType();
    var entries = type.GetProperty("Potions", BindingFlags.Instance | BindingFlags.Public)?.GetValue(preview);
    if (entries is not IEnumerable enumerable)
    {
        return [];
    }

    return enumerable
        .Cast<object>()
        .Select(entry =>
        {
            var entryType = entry.GetType();
            return new ShopPotionRecord(
                entryType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty,
                (int)(entryType.GetProperty("Price", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? 0));
        })
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
        .ToList();
}

static List<CardOffer> ExtractShownShopCards(object? entries)
{
    if (entries is not IEnumerable enumerable)
    {
        return [];
    }

    return enumerable
        .Cast<object>()
        .Select(entry =>
        {
            var type = entry.GetType();
            var cardTypeValue = type.GetProperty("CardType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry);
            var fixedRarityValue = type.GetProperty("FixedRarity", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry);
            return new CardOffer(
                type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString() ?? string.Empty,
                (int)(type.GetProperty("Price", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? 0),
                (bool)(type.GetProperty("IsDiscounted", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? false),
                (bool)(type.GetProperty("IsColorless", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) ?? false),
                cardTypeValue is CardType cardType ? cardType : null,
                fixedRarityValue is CardRarity fixedRarity ? fixedRarity : null);
        })
        .Where(entry => !string.IsNullOrWhiteSpace(entry.CardId))
        .ToList();
}

static string RollRelicRarity(GameRng rng)
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

static HashSet<string> ShopBlockedRelics() => new(StringComparer.OrdinalIgnoreCase)
{
    "AMETHYST_AUBERGINE",
    "BOWLER_HAT",
    "LUCKY_FYSH",
    "OLD_COIN",
    "THE_COURIER"
};

static void ApplyActualEventState(
    SavePlayerStats? stats,
    string eventId,
    object eventState,
    GameRng eventRng,
    MethodInfo applyShownEvent,
    FieldInfo deckField,
    MethodInfo gainPotion)
{
    var normalizedEventId = NormalizeModelId(eventId);
    if (string.Equals(normalizedEventId, "SELF_HELP_BOOK", StringComparison.OrdinalIgnoreCase) &&
        ApplyActualSelfHelpBookState(stats, eventState, deckField))
    {
        return;
    }

    if (string.Equals(normalizedEventId, "DROWNING_BEACON", StringComparison.OrdinalIgnoreCase) &&
        ApplyActualDrowningBeaconState(stats, eventState, gainPotion))
    {
        return;
    }

    applyShownEvent.Invoke(eventState, [eventId, eventRng]);
}

static bool ApplyActualSelfHelpBookState(SavePlayerStats? stats, object eventState, FieldInfo deckField)
{
    var choiceKey = stats?.EventChoices?.FirstOrDefault()?.Title?.Key;
    if (string.IsNullOrWhiteSpace(choiceKey))
    {
        return false;
    }

    var typeRestriction = choiceKey.IndexOf("READ_THE_BACK", StringComparison.OrdinalIgnoreCase) >= 0
        ? "Attack"
        : choiceKey.IndexOf("READ_PASSAGE", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Skill"
            : choiceKey.IndexOf("READ_ENTIRE_BOOK", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Power"
                : null;
    if (typeRestriction == null)
    {
        return false;
    }

    var deck = (IList?)deckField.GetValue(eventState);
    if (deck == null)
    {
        return false;
    }

    foreach (var card in deck.Cast<object>())
    {
        var cardId = NormalizeModelId(card.GetType().GetProperty("CardId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(card)?.ToString());
        if (string.IsNullOrWhiteSpace(cardId))
        {
            continue;
        }

        var matches = typeRestriction switch
        {
            "Attack" => cardId.Contains("STRIKE", StringComparison.OrdinalIgnoreCase) || cardId.Contains("KNIVES", StringComparison.OrdinalIgnoreCase),
            "Skill" => cardId.Contains("DEFEND", StringComparison.OrdinalIgnoreCase) || cardId.Contains("BACKFLIP", StringComparison.OrdinalIgnoreCase),
            "Power" => cardId.Contains("FOOTWORK", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!matches)
        {
            continue;
        }

        var hasEnchantmentProperty = card.GetType().GetProperty("HasEnchantment", BindingFlags.Instance | BindingFlags.Public);
        hasEnchantmentProperty?.SetValue(card, true);
        return true;
    }

    return true;
}

static bool ApplyActualDrowningBeaconState(SavePlayerStats? stats, object eventState, MethodInfo gainPotion)
{
    var choiceKey = stats?.EventChoices?.FirstOrDefault()?.Title?.Key;
    if (string.IsNullOrWhiteSpace(choiceKey) ||
        choiceKey.IndexOf("BOTTLE", StringComparison.OrdinalIgnoreCase) < 0)
    {
        return false;
    }

    var pickedPotion = stats?.PotionChoices?.FirstOrDefault(choice => choice.WasPicked)?.Choice;
    if (!string.IsNullOrWhiteSpace(pickedPotion))
    {
        gainPotion.Invoke(eventState, [pickedPotion]);
    }

    return true;
}

static IReadOnlyList<string> ExtractStringList(object? value)
{
    if (value is not IEnumerable enumerable)
    {
        return Array.Empty<string>();
    }

    return enumerable.Cast<object>()
        .Select(item => NormalizeModelId(item?.ToString()))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
}

static void IncrementFloor(Type eventStateType, object eventState)
{
    var totalFloorProperty = eventStateType.GetProperty("TotalFloor", BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("Missing TotalFloor.");
    totalFloorProperty.SetValue(eventState, (int)totalFloorProperty.GetValue(eventState)! + 1);
}

static ActualActSnapshot BuildActualAct(SaveSnapshot save, int actNumber)
{
    var history = save.MapPointHistory![actNumber - 1]!;
    var rooms = new List<ActualRoomSnapshot>();
    var floor = 0;
    foreach (var point in history)
    {
        var pointType = NormalizeMapPointType(point.MapPointType);
        if (string.Equals(pointType, "Ancient", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pointType, "Boss", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        floor++;
        var room = point.Rooms?.FirstOrDefault();
        var roomType = NormalizeSaveRoomType(room?.RoomType, pointType);
        rooms.Add(new ActualRoomSnapshot(
            actNumber,
            floor,
            roomType,
            NormalizeModelId(room?.ModelId),
            point.PlayerStats?.FirstOrDefault()));
    }

    return new ActualActSnapshot(actNumber, rooms);
}

static CharacterId ResolveSaveCharacter(SaveSnapshot save)
{
    var value = save.Players?.FirstOrDefault()?.Character ?? string.Empty;
    return NormalizeModelId(value) switch
    {
        "IRONCLAD" => CharacterId.Ironclad,
        "SILENT" => CharacterId.Silent,
        "DEFECT" => CharacterId.Defect,
        "NECROBINDER" => CharacterId.Necrobinder,
        "REGENT" => CharacterId.Regent,
        _ => CharacterId.Ironclad
    };
}

static string? ResolveActualAct1OpeningRelicId(SaveSnapshot save)
{
    var pickedRelic = save.MapPointHistory?
        .FirstOrDefault()?
        .FirstOrDefault()?
        .PlayerStats?
        .FirstOrDefault()?
        .RelicChoices?
        .FirstOrDefault(choice => choice.WasPicked)?
        .Choice;
    return string.IsNullOrWhiteSpace(pickedRelic) ? null : NormalizeRelicId(pickedRelic);
}

static string NormalizeSaveRoomType(string? roomType, string? fallbackPointType)
{
    var normalized = roomType?.ToLowerInvariant() ?? fallbackPointType?.ToLowerInvariant() ?? string.Empty;
    return normalized switch
    {
        "event" => "Event",
        "monster" => "Monster",
        "elite" => "Elite",
        "treasure" => "Treasure",
        "shop" => "Shop",
        "rest_site" => "RestSite",
        "restsite" => "RestSite",
        _ => NormalizeMapPointType(fallbackPointType)
    };
}

static string NormalizeMapPointType(string? mapPointType)
{
    return (mapPointType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "unknown" => "Event",
        "rest_site" => "RestSite",
        "restsite" => "RestSite",
        "monster" => "Monster",
        "elite" => "Elite",
        "treasure" => "Treasure",
        "shop" => "Shop",
        "ancient" => "Ancient",
        "boss" => "Boss",
        _ => string.Empty
    };
}

static string NormalizeModelId(string? modelId)
{
    if (string.IsNullOrWhiteSpace(modelId))
    {
        return string.Empty;
    }

    var normalized = modelId.Trim();
    foreach (var prefix in new[] { "EVENT.", "RELIC.", "ENCOUNTER.", "CARD.", "POTION.", "CHARACTER." })
    {
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
            break;
        }
    }

    return normalized.Replace('-', '_').ToUpperInvariant();
}

static string NormalizeRelicId(string? relicId) => NormalizeModelId(relicId);

static IEnumerable<string[]> GetPermutations(string[] source)
{
    if (source.Length == 1)
    {
        yield return source;
        yield break;
    }

    for (var i = 0; i < source.Length; i++)
    {
        var head = source[i];
        var tail = source.Where((_, index) => index != i).ToArray();
        foreach (var tailPermutation in GetPermutations(tail))
        {
            yield return [head, .. tailPermutation];
        }
    }
}

static string FindWorkspaceRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Sts2SeedRoller.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate workspace root.");
}

sealed record ActualActSnapshot(int ActNumber, IReadOnlyList<ActualRoomSnapshot> Rooms);

sealed record ActualRoomSnapshot(int ActNumber, int Floor, string RoomType, string EventId, SavePlayerStats? SourceStats);

sealed record CardOffer(string CardId, int Price, bool IsDiscounted, bool IsColorless, CardType? CardType, CardRarity? FixedRarity);

sealed record ShopRelicRecord(string Id, int Price);

sealed record ShopPotionRecord(string Id, int Price);

sealed record CombatRewardPrediction(IReadOnlyList<string> CardIds, string? PotionId);

sealed class ShopSnapshot
{
    public ShopSnapshot(object rewardModel, List<CardOffer> allCards, List<ShopRelicRecord> relics, List<ShopPotionRecord> potions)
    {
        RewardModel = rewardModel;
        AllCards = allCards;
        Relics = relics;
        Potions = potions;
    }

    public object RewardModel { get; }

    public List<CardOffer> AllCards { get; }

    public List<ShopRelicRecord> Relics { get; }

    public List<ShopPotionRecord> Potions { get; }

    public void Replace(CardOffer oldOffer, CardOffer newOffer)
    {
        var index = AllCards.FindIndex(card => ReferenceEquals(card, oldOffer) || card.Equals(oldOffer));
        if (index >= 0)
        {
            AllCards[index] = newOffer;
        }
    }

    public void Remove(CardOffer offer)
    {
        AllCards.RemoveAll(card => card.Equals(offer));
    }
}

sealed class SaveSnapshot
{
    [JsonPropertyName("seed")]
    public string? Seed { get; set; }

    [JsonPropertyName("ascension")]
    public int Ascension { get; set; }

    [JsonPropertyName("map_point_history")]
    public List<List<SaveMapPoint>?>? MapPointHistory { get; set; }

    [JsonPropertyName("players")]
    public List<SavePlayer>? Players { get; set; }
}

sealed class SavePlayer
{
    [JsonPropertyName("character")]
    public string? Character { get; set; }
}

sealed class SaveMapPoint
{
    [JsonPropertyName("map_point_type")]
    public string? MapPointType { get; set; }

    [JsonPropertyName("rooms")]
    public List<SaveRoom>? Rooms { get; set; }

    [JsonPropertyName("player_stats")]
    public List<SavePlayerStats>? PlayerStats { get; set; }
}

sealed class SaveRoom
{
    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("room_type")]
    public string? RoomType { get; set; }
}

sealed class SavePlayerStats
{
    [JsonPropertyName("current_gold")]
    public int? CurrentGold { get; set; }

    [JsonPropertyName("card_choices")]
    public List<SaveCardChoice>? CardChoices { get; set; }

    [JsonPropertyName("cards_gained")]
    public List<SaveCardGain>? CardsGained { get; set; }

    [JsonPropertyName("relic_choices")]
    public List<SaveRelicChoice>? RelicChoices { get; set; }

    [JsonPropertyName("potion_choices")]
    public List<SavePotionChoice>? PotionChoices { get; set; }

    [JsonPropertyName("event_choices")]
    public List<SaveEventChoice>? EventChoices { get; set; }
}

sealed class SaveCardChoice
{
    [JsonPropertyName("card")]
    public SaveCardRef? Card { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SaveCardGain
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

sealed class SaveCardRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

sealed class SaveRelicChoice
{
    [JsonPropertyName("choice")]
    public string? Choice { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SavePotionChoice
{
    [JsonPropertyName("choice")]
    public string? Choice { get; set; }

    [JsonPropertyName("was_picked")]
    public bool WasPicked { get; set; }
}

sealed class SaveEventChoice
{
    [JsonPropertyName("title")]
    public SaveLocalizedRef? Title { get; set; }
}

sealed class SaveLocalizedRef
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
