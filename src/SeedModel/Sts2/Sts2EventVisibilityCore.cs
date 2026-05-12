using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2;

internal static class Sts2EventPullEngine
{
    public static string PeekNextAllowedEvent(IReadOnlyList<string> fullPool, Sts2EventProgressState state)
    {
        if (fullPool.Count == 0)
        {
            return string.Empty;
        }

        for (var offset = 0; offset < fullPool.Count; offset++)
        {
            var index = (state.EventsVisitedInAct + offset) % fullPool.Count;
            var candidate = Sts2EventIdNormalizer.FromPoolItem(fullPool[index]);
            if (state.VisitedEvents.Contains(candidate))
            {
                continue;
            }

            if (!Sts2EventRuleRegistry.IsAllowed(candidate, state))
            {
                continue;
            }

            return candidate;
        }

        return Sts2EventIdNormalizer.FromPoolItem(fullPool[state.EventsVisitedInAct % fullPool.Count]);
    }

    public static void EnsureNextEventIsValid(IReadOnlyList<string> fullPool, Sts2EventProgressState state)
    {
        if (fullPool.Count == 0)
        {
            return;
        }

        for (var offset = 0; offset < fullPool.Count; offset++)
        {
            var index = state.EventsVisitedInAct % fullPool.Count;
            var candidate = Sts2EventIdNormalizer.FromPoolItem(fullPool[index]);
            if (!state.VisitedEvents.Contains(candidate) &&
                Sts2EventRuleRegistry.IsAllowed(candidate, state))
            {
                return;
            }

            state.EventsVisitedInAct++;
        }
    }

    public static void ConsumeShownEvent(IReadOnlyList<string> fullPool, string eventId, Sts2EventProgressState state)
    {
        if (fullPool.Count == 0 || string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        var normalizedEventId = Sts2EventIdNormalizer.FromPoolItem(eventId);

        for (var offset = 0; offset < fullPool.Count; offset++)
        {
            var index = (state.EventsVisitedInAct + offset) % fullPool.Count;
            var candidate = Sts2EventIdNormalizer.FromPoolItem(fullPool[index]);
            if (!string.Equals(candidate, normalizedEventId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            state.EventsVisitedInAct += offset + 1;
            state.VisitedEvents.Add(normalizedEventId);
            return;
        }

        state.VisitedEvents.Add(normalizedEventId);
    }
}

internal static class Sts2EventRuleRegistry
{
    public static bool IsAllowed(string eventId, Sts2EventProgressState state)
    {
        var normalizedEventId = Sts2EventIdNormalizer.FromPoolItem(eventId);
        return normalizedEventId switch
        {
            "AMALGAMATOR" => state.BasicStrikeCount >= 2 && state.BasicDefendCount >= 2,
            "BRAIN_LEECH" => state.CurrentActIndex < 2,
            "BYRDONIS_NEST" => !state.HasEventPet,
            "COLORFUL_PHILOSOPHERS" => state.UnlockedCharacterPoolCount > 1,
            "COLOSSAL_FLOWER" => state.CurrentHp >= 19,
            "CRYSTAL_SPHERE" => state.CurrentActIndex > 0 && state.CurrentGold >= 100,
            "DOLL_ROOM" => state.CurrentActIndex == 1,
            "ENDLESS_CONVEYOR" => state.CurrentGold >= 120,
            "FAKE_MERCHANT" => state.CurrentActIndex >= 1 &&
                               state.PlayerCount == 1 &&
                               (state.CurrentGold >= 100 || state.HasFoulPotion),
            "FIELD_OF_MAN_SIZED_HOLES" => state.HasPerfectFitTarget,
            "GRAVE_OF_THE_FORGOTTEN" => state.HasSoulsPowerTarget,
            "MORPHIC_GROVE" => state.CurrentGold >= 100 && state.TransformableCardCount >= 2,
            "POTION_COURIER" => state.CurrentActIndex > 0,
            "PUNCH_OFF" => state.TotalFloor >= 6,
            "RANWID_THE_ELDER" => state.CurrentActIndex >= 1 &&
                                  state.CurrentGold >= 100 &&
                                  state.PotionCount >= 1 &&
                                  state.TradableRelicCount >= 1,
            "RELIC_TRADER" => state.CurrentActIndex >= 1 && state.TradableRelicCount >= 5,
            "ROOM_FULL_OF_CHEESE" => state.CurrentActIndex < 2,
            "ROUND_TEA_PARTY" => state.CurrentHp >= 12,
            "SLIPPERY_BRIDGE" => state.TotalFloor > 6 && state.HasRemovableCard,
            "SPIRALING_WHIRLPOOL" => state.HasSpiralTarget,
            "STONE_OF_ALL_TIME" => state.CurrentActIndex == 1 && state.PotionCount >= 1,
            "SYMBIOTE" => state.CurrentActIndex > 0,
            "TEA_MASTER" => state.CurrentActIndex < 2 && state.CurrentGold >= 150,
            "THE_FUTURE_OF_POTIONS" => state.PotionCount >= 2,
            "THE_LEGENDS_WERE_TRUE" => state.CurrentActIndex == 0 &&
                                       state.DeckCount > 0 &&
                                       state.CurrentHp >= 10,
            "TRASH_HEAP" => state.CurrentHp > 5,
            "UNREST_SITE" => state.CurrentHp * 10 <= state.MaxHp * 7,
            "WAR_HISTORIAN_REPY" => false,
            "WATERLOGGED_SCRIPTORIUM" => state.CurrentGold >= 55,
            "WELCOME_TO_WONGOS" => state.CurrentActIndex == 1 && state.CurrentGold >= 100,
            "WHISPERING_HOLLOW" => state.CurrentGold >= 44,
            "WOOD_CARVINGS" => state.HasRemovableBasicCard,
            "ZEN_WEAVER" => state.CurrentGold >= 125,
            _ => true
        };
    }
}

internal enum Sts2SelfHelpBookBranch
{
    ReadTheBack = 0,
    ReadPassage = 1,
    ReadEntireBook = 2
}

internal sealed class Sts2EventVisibilitySimulationModel
{
    private const double DefaultPotionPickChance = 0.65;
    private const double DefaultCombatCardPickChance = 0.58;
    private const double DefaultEliteCardPickChance = 0.78;
    private const double DefaultShopCardPickChance = 0.28;
    private const double DefaultShopPotionChance = 0.33;
    private const double DefaultShopRelicChance = 0.26;
    private const double DefaultShopRemovalChance = 0.34;

    private Sts2EventVisibilitySimulationModel(
        IReadOnlyDictionary<string, NeowCardMetadata> cardMetadata,
        IReadOnlyDictionary<CardRarity, string[]> cardPoolByRarity,
        IReadOnlyDictionary<PotionRarity, string[]> potionPoolByRarity,
        CharacterRunDefaults defaults,
        int unlockedCharacterPoolCount,
        IReadOnlySet<string> exhaustCards,
        IReadOnlySet<string> unplayableCards,
        IReadOnlySet<string> uponPickupRelics,
        IReadOnlySet<string> petRelics,
        IReadOnlyDictionary<string, string> relicRarityMap)
    {
        CardMetadata = cardMetadata;
        CardPoolByRarity = cardPoolByRarity;
        PotionPoolByRarity = potionPoolByRarity;
        Defaults = defaults;
        UnlockedCharacterPoolCount = unlockedCharacterPoolCount;
        ExhaustCards = exhaustCards;
        UnplayableCards = unplayableCards;
        UponPickupRelics = uponPickupRelics;
        PetRelics = petRelics;
        RelicRarityMap = relicRarityMap;
    }

    public IReadOnlyDictionary<string, NeowCardMetadata> CardMetadata { get; }

    public IReadOnlyDictionary<CardRarity, string[]> CardPoolByRarity { get; }

    public IReadOnlyDictionary<PotionRarity, string[]> PotionPoolByRarity { get; }

    public CharacterRunDefaults Defaults { get; }

    public int UnlockedCharacterPoolCount { get; }

    public IReadOnlySet<string> ExhaustCards { get; }

    public IReadOnlySet<string> UnplayableCards { get; }

    public IReadOnlySet<string> UponPickupRelics { get; }

    public IReadOnlySet<string> PetRelics { get; }

    public IReadOnlyDictionary<string, string> RelicRarityMap { get; }

    public double PotionPickChance => DefaultPotionPickChance;

    public double CombatCardPickChance => DefaultCombatCardPickChance;

    public double EliteCardPickChance => DefaultEliteCardPickChance;

    public double ShopCardPickChance => DefaultShopCardPickChance;

    public double ShopPotionChance => DefaultShopPotionChance;

    public double ShopRelicChance => DefaultShopRelicChance;

    public double ShopRemovalChance => DefaultShopRemovalChance;

    public static Sts2EventVisibilitySimulationModel Create(
        NeowOptionDataset dataset,
        CharacterId character,
        IReadOnlyList<CharacterId>? unlockedCharacters,
        int playerCount,
        int ascensionLevel,
        string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var defaults = CharacterRunDefaults.For(character, ascensionLevel);
        var cardPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
            ? characterCards
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) && IsCardAllowed(metadata, playerCount))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var cardPoolByRarity = cardPool
            .Where(cardId => dataset.CardMetadataMap.ContainsKey(cardId))
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var potionPool = new List<string>(dataset.SharedPotionPoolList);
        if (dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions))
        {
            potionPool.AddRange(characterPotions);
        }

        var potionPoolByRarity = potionPool
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var keywords = SourceKeywordCatalog.Load(workspaceRoot, dataset.Version);
        var relicRarityMap = dataset.RelicMetadataMap.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Rarity,
            StringComparer.OrdinalIgnoreCase);

        var unlockedCount = unlockedCharacters == null || unlockedCharacters.Count == 0
            ? 5
            : unlockedCharacters
                .Where(id => Enum.IsDefined(typeof(CharacterId), id))
                .Distinct()
                .Count();

        return new Sts2EventVisibilitySimulationModel(
            dataset.CardMetadataMap,
            cardPoolByRarity,
            potionPoolByRarity,
            defaults,
            Math.Max(1, unlockedCount),
            keywords.ExhaustCards,
            keywords.UnplayableCards,
            keywords.UponPickupRelics,
            keywords.PetRelics,
            relicRarityMap);
    }

    public bool CanReceivePerfectFit(string cardId)
    {
        var normalizedId = Sts2EventIdNormalizer.FromAny(cardId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        if (UnplayableCards.Contains(normalizedId))
        {
            return false;
        }

        if (!CardMetadata.TryGetValue(normalizedId, out var metadata))
        {
            return true;
        }

        return metadata.ParsedRarity is not (CardRarity.Status or CardRarity.Curse or CardRarity.Quest);
    }

    public bool CanReceiveSoulsPower(string cardId)
    {
        var normalizedId = Sts2EventIdNormalizer.FromAny(cardId);
        return CanReceivePerfectFit(normalizedId) &&
               ExhaustCards.Contains(normalizedId);
    }

    public bool IsTradableRelic(string relicId)
    {
        var normalizedId = Sts2EventIdNormalizer.FromAny(relicId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        if (UponPickupRelics.Contains(normalizedId) || PetRelics.Contains(normalizedId))
        {
            return false;
        }

        if (!RelicRarityMap.TryGetValue(normalizedId, out var rarity))
        {
            return true;
        }

        return !string.Equals(rarity, "Starter", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(rarity, "Event", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(rarity, "Ancient", StringComparison.OrdinalIgnoreCase);
    }

    public string? RollCombatRewardCard(GameRng rng, ref float rareOffset, int ascensionLevel)
    {
        var rarity = RollCardRarity(rng, ref rareOffset, ascensionLevel, CardRarityOddsType.RegularEncounter);
        return RollCardByRarity(rng, rarity);
    }

    public string? RollEliteRewardCard(GameRng rng, ref float rareOffset, int ascensionLevel)
    {
        var rarity = RollCardRarity(rng, ref rareOffset, ascensionLevel, CardRarityOddsType.EliteEncounter);
        return RollCardByRarity(rng, rarity);
    }

    public string? RollBossRewardCard(GameRng rng, ref float rareOffset, int ascensionLevel)
    {
        var rarity = RollCardRarity(rng, ref rareOffset, ascensionLevel, CardRarityOddsType.BossEncounter);
        return RollCardByRarity(rng, rarity);
    }

    public string? RollShopCard(GameRng rng, ref float rareOffset, int ascensionLevel)
    {
        var rarity = RollCardRarity(rng, ref rareOffset, ascensionLevel, CardRarityOddsType.Shop);
        return RollCardByRarity(rng, rarity);
    }

    public string? RollPotion(GameRng rng)
    {
        var rarity = RollPotionRarity(rng);
        if (PotionPoolByRarity.TryGetValue(rarity, out var pool) && pool.Length > 0)
        {
            return rng.NextItem(pool);
        }

        foreach (var fallbackPool in PotionPoolByRarity.Values.Where(candidatePool => candidatePool.Length > 0))
        {
            var potionId = rng.NextItem(fallbackPool);
            if (!string.IsNullOrWhiteSpace(potionId))
            {
                return potionId;
            }
        }

        return null;
    }

    private string? RollCardByRarity(GameRng rng, CardRarity rarity)
    {
        var current = rarity;
        while (current != CardRarity.None)
        {
            if (CardPoolByRarity.TryGetValue(current, out var pool) && pool.Length > 0)
            {
                return rng.NextItem(pool);
            }

            current = GetNextHighestRarity(current);
        }

        return null;
    }

    private static bool IsCardAllowed(NeowCardMetadata metadata, int playerCount)
    {
        if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
        {
            return false;
        }

        if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
        {
            return false;
        }

        return metadata.ParsedRarity is CardRarity.Basic or CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
    }

    private static CardRarity RollCardRarity(GameRng rng, ref float rareOffset, int ascensionLevel, CardRarityOddsType oddsType)
    {
        var roll = rng.NextFloat();
        var rareOdds = GetBaseCardOdds(oddsType, CardRarity.Rare, ascensionLevel) + rareOffset;
        CardRarity rarity;
        if (roll < rareOdds)
        {
            rarity = CardRarity.Rare;
        }
        else if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon, ascensionLevel) + rareOdds)
        {
            rarity = CardRarity.Uncommon;
        }
        else
        {
            rarity = CardRarity.Common;
        }

        if (rarity == CardRarity.Rare)
        {
            rareOffset = -0.05f;
        }
        else
        {
            rareOffset = Math.Min(
                rareOffset + (ascensionLevel >= 7 ? 0.005f : 0.01f),
                0.4f);
        }

        return rarity;
    }

    private static float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity, int ascensionLevel)
    {
        var scarcityActive = ascensionLevel >= 7;
        return oddsType switch
        {
            CardRarityOddsType.EliteEncounter => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
                CardRarity.Uncommon => 0.4f,
                CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
                _ => 0f
            },
            CardRarityOddsType.Shop => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.585f : 0.54f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.045f : 0.09f,
                _ => 0f
            },
            _ => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
                _ => 0f
            }
        };
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Basic => CardRarity.Common,
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.None
        };

    private static PotionRarity RollPotionRarity(GameRng rng)
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

    private sealed class SourceKeywordCatalog
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, SourceKeywordCatalog> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SourceKeywordCatalog Empty = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private SourceKeywordCatalog(
            IReadOnlySet<string> exhaustCards,
            IReadOnlySet<string> unplayableCards,
            IReadOnlySet<string> uponPickupRelics,
            IReadOnlySet<string> petRelics)
        {
            ExhaustCards = exhaustCards;
            UnplayableCards = unplayableCards;
            UponPickupRelics = uponPickupRelics;
            PetRelics = petRelics;
        }

        public IReadOnlySet<string> ExhaustCards { get; }

        public IReadOnlySet<string> UnplayableCards { get; }

        public IReadOnlySet<string> UponPickupRelics { get; }

        public IReadOnlySet<string> PetRelics { get; }

        public static SourceKeywordCatalog Load(string? workspaceRoot, string? version)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            {
                return Empty;
            }

            var cacheKey = $"{workspaceRoot}|{version}";
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                var cardsRoot = ResolveOfficialModelsRoot(workspaceRoot, version, "Cards");
                var relicsRoot = ResolveOfficialModelsRoot(workspaceRoot, version, "Relics");
                if (cardsRoot == null && relicsRoot == null)
                {
                    Cache[cacheKey] = Empty;
                    return Empty;
                }

                var exhaustCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var unplayableCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uponPickupRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var petRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(cardsRoot))
                {
                    foreach (var filePath in Directory.EnumerateFiles(cardsRoot, "*.cs", SearchOption.AllDirectories))
                    {
                        var cardId = Sts2EventIdNormalizer.FromPoolItem(Path.GetFileNameWithoutExtension(filePath));
                        if (string.IsNullOrWhiteSpace(cardId))
                        {
                            continue;
                        }

                        var source = File.ReadAllText(filePath);
                        if (source.Contains("CardKeyword.Exhaust", StringComparison.Ordinal))
                        {
                            exhaustCards.Add(cardId);
                        }

                        if (source.Contains("CardKeyword.Unplayable", StringComparison.Ordinal))
                        {
                            unplayableCards.Add(cardId);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(relicsRoot))
                {
                    foreach (var filePath in Directory.EnumerateFiles(relicsRoot, "*.cs", SearchOption.AllDirectories))
                    {
                        var relicId = Sts2EventIdNormalizer.FromPoolItem(Path.GetFileNameWithoutExtension(filePath));
                        if (string.IsNullOrWhiteSpace(relicId))
                        {
                            continue;
                        }

                        var source = File.ReadAllText(filePath);
                        if (source.Contains("public override bool HasUponPickupEffect => true", StringComparison.Ordinal))
                        {
                            uponPickupRelics.Add(relicId);
                        }

                        if (source.Contains("public override bool SpawnsPets => true", StringComparison.Ordinal))
                        {
                            petRelics.Add(relicId);
                        }
                    }
                }

                var catalog = new SourceKeywordCatalog(
                    exhaustCards,
                    unplayableCards,
                    uponPickupRelics,
                    petRelics);
                Cache[cacheKey] = catalog;
                return catalog;
            }
        }

        private static string? ResolveOfficialModelsRoot(string workspaceRoot, string? version, string leafDirectory)
        {
            if (!Directory.Exists(workspaceRoot))
            {
                return null;
            }

            // Only opt into official source scanning from explicit, dedicated folders.
            // Avoid letting arbitrary workspace subdirectories silently change event logic.
            var preferredNames = new List<string>
            {
                "official-data-models",
                ".official-data-models"
            };

            if (!string.IsNullOrWhiteSpace(version))
            {
                preferredNames.Add($"official-data-models-{version}");
                preferredNames.Add($".official-data-models-{version}");
            }

            var preferred = preferredNames
                .Select(name => Path.Combine(workspaceRoot, name))
                .Where(Directory.Exists);

            foreach (var directory in preferred)
            {
                var candidate = Path.Combine(directory, "src", "Core", "Models", leafDirectory);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}

internal sealed class Sts2EventProgressState
{
    private static readonly HashSet<string> NonRemovableCardIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASCENDERS_BANE"
    };

    private readonly Sts2EventVisibilitySimulationModel _model;
    private readonly List<DeckCard> _deck = [];
    private readonly List<string> _potions = [];
    private readonly HashSet<string> _ownedRelics = new(StringComparer.OrdinalIgnoreCase);

    private Sts2EventProgressState(
        Sts2EventVisibilitySimulationModel model,
        CharacterId character,
        int playerCount,
        int currentGold,
        int currentHp,
        int maxHp)
    {
        _model = model;
        Character = character;
        PlayerCount = Math.Max(1, playerCount);
        CurrentGold = currentGold;
        CurrentHp = currentHp;
        MaxHp = maxHp;
        PotionChance = 0.4f;
        CardRareOffset = -0.05f;
    }

    private sealed class DeckCard
    {
        public DeckCard(string cardId)
        {
            CardId = cardId;
        }

        public string CardId { get; }

        public bool HasEnchantment { get; set; }
    }

    public CharacterId Character { get; }

    public int CurrentActNumber { get; private set; }

    public int CurrentActIndex => Math.Max(0, CurrentActNumber - 1);

    public int TotalFloor { get; set; }

    public int CurrentGold { get; private set; }

    public int CurrentHp { get; private set; }

    public int MaxHp { get; private set; }

    public int EventsVisitedInAct { get; set; }

    public int PlayerCount { get; }

    public int UnlockedCharacterPoolCount => _model.UnlockedCharacterPoolCount;

    public int PotionCount => _potions.Count;

    public bool HasFoulPotion => _potions.Any(potionId => string.Equals(potionId, "FOUL_POTION", StringComparison.OrdinalIgnoreCase));

    public int TradableRelicCount { get; private set; }

    public int DeckCount => _deck.Count;

    public int TransformableCardCount => _deck.Count(card => IsTransformableCard(card.CardId));

    public bool HasRemovableCard => _deck.Any(card => IsRemovableCard(card.CardId));

    public bool HasRemovableBasicCard => _deck.Any(card => IsRemovableBasicCard(card.CardId));

    public bool HasSpiralTarget => _deck.Any(IsSpiralTarget);

    public bool HasPerfectFitTarget => _deck.Any(card => !card.HasEnchantment && _model.CanReceivePerfectFit(card.CardId));

    public bool HasSoulsPowerTarget => _deck.Any(card => !card.HasEnchantment && _model.CanReceiveSoulsPower(card.CardId));

    public bool HasByrdonisEgg => _deck.Any(card => string.Equals(card.CardId, "BYRDONIS_EGG", StringComparison.OrdinalIgnoreCase));

    public bool ContainsCard(string cardId) =>
        !string.IsNullOrWhiteSpace(cardId) &&
        _deck.Any(card => string.Equals(card.CardId, cardId, StringComparison.OrdinalIgnoreCase));

    public bool IsTradableRelic(string relicId) => _model.IsTradableRelic(relicId);

    public IReadOnlyList<string> GetPotionIds() => _potions.ToArray();

    public int BasicStrikeCount => _deck.Count(card => card.CardId.StartsWith("STRIKE_", StringComparison.OrdinalIgnoreCase) && IsRemovableCard(card.CardId));

    public int BasicDefendCount => _deck.Count(card => card.CardId.StartsWith("DEFEND_", StringComparison.OrdinalIgnoreCase) && IsRemovableCard(card.CardId));

    public bool HasEventPet { get; private set; }

    public HashSet<string> VisitedEvents { get; } = new(StringComparer.OrdinalIgnoreCase);

    private float PotionChance { get; set; }

    private float CardRareOffset { get; set; }

    public static Sts2EventProgressState Create(
        Sts2EventVisibilitySimulationModel model,
        CharacterId character,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(model);

        var state = new Sts2EventProgressState(
            model,
            character,
            playerCount,
            model.Defaults.StartingGold,
            model.Defaults.StartingMaxHp,
            model.Defaults.StartingMaxHp);

        foreach (var cardId in model.Defaults.StartingDeck)
        {
            state._deck.Add(new DeckCard(Sts2EventIdNormalizer.FromAny(cardId)));
        }

        foreach (var relicId in model.Defaults.StartingRelics)
        {
            state.GainRelic(relicId);
        }

        return state;
    }

    public void StartAct(int actNumber, int initialEventsVisited = 0)
    {
        CurrentActNumber = actNumber;
        EventsVisitedInAct = Math.Max(0, initialEventsVisited);
    }

    public Sts2EventProgressState Clone()
    {
        var clone = new Sts2EventProgressState(_model, Character, PlayerCount, CurrentGold, CurrentHp, MaxHp)
        {
            CurrentActNumber = CurrentActNumber,
            TotalFloor = TotalFloor,
            EventsVisitedInAct = EventsVisitedInAct,
            TradableRelicCount = TradableRelicCount,
            HasEventPet = HasEventPet,
            PotionChance = PotionChance,
            CardRareOffset = CardRareOffset
        };

        foreach (var card in _deck)
        {
            clone._deck.Add(new DeckCard(card.CardId)
            {
                HasEnchantment = card.HasEnchantment
            });
        }

        foreach (var potionId in _potions)
        {
            clone._potions.Add(potionId);
        }

        foreach (var relicId in _ownedRelics)
        {
            clone._ownedRelics.Add(relicId);
        }

        foreach (var eventId in VisitedEvents)
        {
            clone.VisitedEvents.Add(eventId);
        }

        return clone;
    }

    public IReadOnlyList<Sts2SelfHelpBookBranch> GetAvailableSelfHelpBookBranches()
    {
        var branches = new List<Sts2SelfHelpBookBranch>(3);
        if (HasEnchantableCardOfType(CardType.Attack))
        {
            branches.Add(Sts2SelfHelpBookBranch.ReadTheBack);
        }

        if (HasEnchantableCardOfType(CardType.Skill))
        {
            branches.Add(Sts2SelfHelpBookBranch.ReadPassage);
        }

        if (HasEnchantableCardOfType(CardType.Power))
        {
            branches.Add(Sts2SelfHelpBookBranch.ReadEntireBook);
        }

        return branches;
    }

    public void ApplySelfHelpBook(Sts2SelfHelpBookBranch branch)
    {
        _ = branch switch
        {
            Sts2SelfHelpBookBranch.ReadTheBack => EnchantFirstEligibleCardByType(CardType.Attack),
            Sts2SelfHelpBookBranch.ReadPassage => EnchantFirstEligibleCardByType(CardType.Skill),
            Sts2SelfHelpBookBranch.ReadEntireBook => EnchantFirstEligibleCardByType(CardType.Power),
            _ => EnchantFirstEligibleCardByType(CardType.Attack)
        };
    }

    public void ApplyAct1OpeningOption(NeowOptionResult? selectedOption)
    {
        if (selectedOption == null)
        {
            return;
        }

        var isLeafyPoultice = string.Equals(
            selectedOption.RelicId,
            NeowOptionIds.LeafyPoultice,
            StringComparison.OrdinalIgnoreCase);
        var leafyTransformCards = isLeafyPoultice
            ? selectedOption.Details
                .Where(detail => detail.Type == RewardDetailType.Card && !string.IsNullOrWhiteSpace(detail.ModelId))
                .Select(detail => detail.ModelId!)
                .ToList()
            : null;

        GainRelic(selectedOption.RelicId);
        foreach (var detail in selectedOption.Details)
        {
            switch (detail.Type)
            {
                case RewardDetailType.Card when !string.IsNullOrWhiteSpace(detail.ModelId):
                    if (!isLeafyPoultice)
                    {
                        GainCard(detail.ModelId);
                    }
                    break;
                case RewardDetailType.Potion when !string.IsNullOrWhiteSpace(detail.ModelId):
                    GainPotion(detail.ModelId);
                    break;
                case RewardDetailType.Relic when !string.IsNullOrWhiteSpace(detail.ModelId):
                    GainRelic(detail.ModelId);
                    break;
                case RewardDetailType.Gold when detail.Amount is > 0:
                    GainGold(detail.Amount.Value);
                    break;
            }
        }

        switch (selectedOption.RelicId.ToUpperInvariant())
        {
            case NeowOptionIds.LeafyPoultice:
                ApplyLeafyPoulticeOpeningOption(leafyTransformCards);
                break;
            case NeowOptionIds.GoldenPearl:
                GainGold(150);
                break;
            case NeowOptionIds.NeowsTorment:
                GainCard("NEOWS_FURY");
                break;
        }
    }

    public void ApplyNeowStartHeal()
    {
        CurrentHp = 0;
        ApplyAncientActStartHeal();
    }

    public void ConsumeRegularCombat(GameRng rng)
    {
        ConsumeRegularCombat(rng, rng);
    }

    public void ConsumeRegularCombat(GameRng rng, GameRng rewardsRng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rewardsRng);

        SpendHp(rng.NextInt(GetRegularCombatDamageMin(), GetRegularCombatDamageMax() + 1));
        var hasPotionReward = RollPotionRewardChance(rewardsRng, isElite: false);
        GainGold(rewardsRng.NextInt(10, 21));
        if (hasPotionReward &&
            rng.NextDouble() < _model.PotionPickChance)
        {
            GainPotion(_model.RollPotion(rewardsRng));
        }

        if (rng.NextDouble() < _model.CombatCardPickChance)
        {
            var rareOffset = CardRareOffset;
            GainCard(_model.RollCombatRewardCard(rewardsRng, ref rareOffset, _model.Defaults.AscensionLevel));
            CardRareOffset = rareOffset;
        }

        ApplyCombatRewardRelicEffects(isBoss: false);

        ApplyPostCombatHeal();
    }

    public void ConsumeEliteStop(GameRng rng)
    {
        ConsumeEliteStop(rng, rng);
    }

    public void ConsumeEliteStop(GameRng rng, GameRng rewardsRng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rewardsRng);

        SpendHp(rng.NextInt(GetEliteDamageMin(), GetEliteDamageMax() + 1));
        var hasPotionReward = RollPotionRewardChance(rewardsRng, isElite: true);
        GainGold(rewardsRng.NextInt(25, 36));
        if (hasPotionReward &&
            rng.NextDouble() < _model.PotionPickChance)
        {
            GainPotion(_model.RollPotion(rewardsRng));
        }

        if (rng.NextDouble() < _model.EliteCardPickChance)
        {
            var rareOffset = CardRareOffset;
            GainCard(_model.RollEliteRewardCard(rewardsRng, ref rareOffset, _model.Defaults.AscensionLevel));
            CardRareOffset = rareOffset;
        }

        ApplyCombatRewardRelicEffects(isBoss: false);

        ApplyPostCombatHeal();
    }

    public void ConsumeBossStop(GameRng rng)
    {
        ConsumeBossStop(rng, rng);
    }

    public void ConsumeBossStop(GameRng rng, GameRng rewardsRng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rewardsRng);

        var goldAmount = HasAscension(3) ? 75 : 100;
        GainGold(rewardsRng.NextInt(goldAmount, goldAmount + 1));
        if (RollPotionRewardChance(rewardsRng, isElite: false) &&
            rng.NextDouble() < _model.PotionPickChance)
        {
            GainPotion(_model.RollPotion(rewardsRng));
        }

        var rareOffset = CardRareOffset;
        var bossCard = _model.RollBossRewardCard(rewardsRng, ref rareOffset, _model.Defaults.AscensionLevel);
        CardRareOffset = rareOffset;
        if (!string.IsNullOrWhiteSpace(bossCard))
        {
            GainCard(bossCard);
        }

        ApplyCombatRewardRelicEffects(isBoss: CurrentActIndex >= 2);

        ApplyPostCombatHeal();
    }

    public void ApplyAncientActStartHeal()
    {
        var missingHp = Math.Max(0, MaxHp - CurrentHp);
        if (missingHp <= 0)
        {
            return;
        }

        var healAmount = HasAscension(2)
            ? (int)(missingHp * 0.8f)
            : missingHp;
        Heal(healAmount);
    }

    public void ConsumeTreasureStop(GameRng rng, GameRng rewardsRng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rewardsRng);
        var gold = rewardsRng.NextInt(42, 53);
        if (HasAscension(3))
        {
            gold = (int)(gold * 0.75);
        }

        GainGold(gold);
    }

    public void ConsumeTreasureStop(GameRng rng)
    {
        ConsumeTreasureStop(rng, rng);
    }

    public void ConsumeShopStop(GameRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (CurrentGold >= 75 &&
            HasRemovableBasicCard &&
            rng.NextDouble() < _model.ShopRemovalChance)
        {
            SpendGold(rng.NextInt(75, 111));
            RemoveFirstRemovableBasicCard();
        }

        if (CurrentGold >= 120 &&
            rng.NextDouble() < _model.ShopRelicChance)
        {
            SpendGold(rng.NextInt(120, 181));
            GainAbstractRelic(rng, tradableChance: 0.76, petChance: 0.01);
        }

        if (CurrentGold >= 55 &&
            rng.NextDouble() < _model.ShopPotionChance)
        {
            SpendGold(rng.NextInt(55, 86));
            GainPotion(_model.RollPotion(rng));
        }

        if (CurrentGold >= 50 &&
            rng.NextDouble() < _model.ShopCardPickChance)
        {
            SpendGold(rng.NextInt(50, 101));
            var rareOffset = CardRareOffset;
            GainCard(_model.RollShopCard(rng, ref rareOffset, _model.Defaults.AscensionLevel));
            CardRareOffset = rareOffset;
        }
    }

    public void ConsumeUnknownNonEvent(GameRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var roll = rng.NextDouble();
        if (roll < 0.62)
        {
            ConsumeRegularCombat(rng);
            return;
        }

        if (roll < 0.80)
        {
            GainGold(rng.NextInt(18, 46));
            SpendHp(rng.NextInt(0, 4));
            return;
        }

        if (roll < 0.91)
        {
            GainPotion(_model.RollPotion(rng));
            return;
        }

        Heal(rng.NextInt(3, 8));
    }

    public void ApplyShownEvent(string eventId, GameRng rng)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        switch (Sts2EventIdNormalizer.FromPoolItem(eventId))
        {
            case "BYRDONIS_NEST":
                HasEventPet = true;
                GainCard("BYRDONIS_EGG");
                break;
            case "SELF_HELP_BOOK":
                EnchantFirstEligibleCard();
                break;
            case "WOOD_CARVINGS":
                RemoveFirstRemovableBasicCard();
                break;
            case "SLIPPERY_BRIDGE":
                RemoveFirstRemovableCard();
                break;
            case "POTION_COURIER":
                GainPotion(_model.RollPotion(rng));
                break;
            case "THE_FUTURE_OF_POTIONS":
                GainPotion(_model.RollPotion(rng));
                if (_potions.Count < 3 && rng.NextDouble() < 0.6)
                {
                    GainPotion(_model.RollPotion(rng));
                }
                break;
            case "RELIC_TRADER":
                LoseTradableRelic();
                break;
            case "SUNKEN_TREASURY":
                // The live event always rolls both chest previews first:
                // small chest = 60 + (NextInt(16) - 8)
                // large chest = 333 + (NextInt(61) - 30)
                // Even on the no-curse branch we must still consume the
                // large-chest preview roll to keep later event RNG aligned.
                var smallChestGold = 60 + (rng.NextInt(16) - 8);
                _ = rng.NextInt(61);
                GainGold(smallChestGold);
                break;
            case "RANWID_THE_ELDER":
                SpendGold(100);
                LoseTradableRelic();
                LosePotion();
                break;
            case "CRYSTAL_SPHERE":
                SpendGold(100);
                break;
            case "ENDLESS_CONVEYOR":
                SpendGold(120);
                break;
            case "FAKE_MERCHANT":
                if (CurrentGold >= 100)
                {
                    SpendGold(100);
                }
                else
                {
                    LosePotion("FOUL_POTION");
                }
                break;
            case "MORPHIC_GROVE":
                SpendGold(100);
                RemoveFirstTransformableCard();
                RemoveFirstTransformableCard();
                break;
            case "ROUND_TEA_PARTY":
            case "COLOSSAL_FLOWER":
                Heal(rng.NextInt(4, 11));
                break;
            case "WATERLOGGED_SCRIPTORIUM":
                SpendGold(55);
                break;
            case "WELCOME_TO_WONGOS":
                SpendGold(100);
                break;
            case "WHISPERING_HOLLOW":
                SpendGold(44);
                break;
            case "ZEN_WEAVER":
                SpendGold(125);
                break;
            case "TEA_MASTER":
                SpendGold(150);
                break;
        }
    }

    private void GainCard(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        _deck.Add(new DeckCard(Sts2EventIdNormalizer.FromAny(cardId)));
    }

    private void GainPotion(string? potionId)
    {
        if (string.IsNullOrWhiteSpace(potionId) || _potions.Count >= 3)
        {
            return;
        }

        _potions.Add(Sts2EventIdNormalizer.FromAny(potionId));
    }

    private void LosePotion(string? preferredPotionId = null)
    {
        if (_potions.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(preferredPotionId))
        {
            var normalizedId = Sts2EventIdNormalizer.FromAny(preferredPotionId);
            var index = _potions.FindIndex(item => string.Equals(item, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _potions.RemoveAt(index);
                return;
            }
        }

        _potions.RemoveAt(0);
    }

    private void GainRelic(string relicId)
    {
        var normalizedId = Sts2EventIdNormalizer.FromAny(relicId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        _ownedRelics.Add(normalizedId);

        if (_model.PetRelics.Contains(normalizedId))
        {
            HasEventPet = true;
        }

        if (_model.IsTradableRelic(normalizedId))
        {
            TradableRelicCount++;
        }
    }

    private void GainAbstractRelic(GameRng rng, double tradableChance, double petChance)
    {
        if (rng.NextDouble() < tradableChance)
        {
            TradableRelicCount++;
        }

        if (!HasEventPet && rng.NextDouble() < petChance)
        {
            HasEventPet = true;
        }
    }

    private void LoseTradableRelic()
    {
        if (TradableRelicCount > 0)
        {
            TradableRelicCount--;
        }
    }

    private bool RollPotionRewardChance(GameRng rng, bool isElite)
    {
        var current = PotionChance;
        var roll = rng.NextFloat();
        if (roll < current)
        {
            PotionChance = Math.Max(0.1f, PotionChance - 0.1f);
        }
        else
        {
            PotionChance = Math.Min(0.9f, PotionChance + 0.1f);
        }

        var eliteBonus = isElite ? 0.125f : 0f;
        return roll < current + eliteBonus;
    }

    private void GainGold(int amount)
    {
        if (amount > 0)
        {
            CurrentGold += amount;
        }
    }

    private void SpendGold(int amount)
    {
        if (amount > 0)
        {
            CurrentGold = Math.Max(0, CurrentGold - amount);
        }
    }

    private void SpendHp(int amount)
    {
        if (amount > 0)
        {
            CurrentHp = Math.Max(1, CurrentHp - amount);
        }
    }

    private void Heal(int amount)
    {
        if (amount > 0)
        {
            CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
        }
    }

    private void LoseMaxHp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        MaxHp = Math.Max(1, MaxHp - amount);
        CurrentHp = Math.Min(CurrentHp, MaxHp);
    }

    private void ApplyPostCombatHeal()
    {
        if (Character == CharacterId.Ironclad)
        {
            Heal(6);
        }
    }

    private void ApplyCombatRewardRelicEffects(bool isBoss)
    {
        if (_ownedRelics.Contains("AMETHYST_AUBERGINE") && !isBoss)
        {
            GainGold(15);
        }
    }

    public void ApplyGoldSpend(int amount)
    {
        SpendGold(amount);
    }

    public void ApplyLoseTradableRelic()
    {
        LoseTradableRelic();
    }

    public void ApplyLosePotion(string? preferredPotionId = null)
    {
        LosePotion(preferredPotionId);
    }

    public void ApplyObtainedRelic(string relicId)
    {
        GainRelic(relicId);
    }

    public void ApplyObtainedRelics(IEnumerable<string> relicIds)
    {
        ArgumentNullException.ThrowIfNull(relicIds);

        foreach (var relicId in relicIds)
        {
            GainRelic(relicId);
        }
    }

    private bool HasAscension(int level)
    {
        return _model.Defaults.AscensionLevel >= level;
    }

    private int GetRegularCombatDamageMin() =>
        CurrentActNumber switch
        {
            1 => 2,
            2 => 3,
            _ => 4
        };

    private int GetRegularCombatDamageMax() =>
        CurrentActNumber switch
        {
            1 => 8,
            2 => 11,
            _ => 14
        };

    private int GetEliteDamageMin() =>
        CurrentActNumber switch
        {
            1 => 7,
            2 => 9,
            _ => 12
        };

    private int GetEliteDamageMax() =>
        CurrentActNumber switch
        {
            1 => 14,
            2 => 17,
            _ => 20
        };

    private void RemoveFirstRemovableCard()
    {
        var index = _deck.FindIndex(card => IsRemovableCard(card.CardId));
        if (index >= 0)
        {
            _deck.RemoveAt(index);
        }
    }

    private void RemoveFirstRemovableBasicCard()
    {
        var index = _deck.FindIndex(card => IsRemovableBasicCard(card.CardId));
        if (index >= 0)
        {
            _deck.RemoveAt(index);
        }
    }

    private void RemoveFirstTransformableCard()
    {
        var index = _deck.FindIndex(card => IsTransformableCard(card.CardId));
        if (index >= 0)
        {
            _deck.RemoveAt(index);
        }
    }

    private void RemoveFirstCard(Func<DeckCard, bool> predicate)
    {
        var index = _deck.FindIndex(card => predicate(card));
        if (index >= 0)
        {
            _deck.RemoveAt(index);
        }
    }

    private void ApplyLeafyPoulticeOpeningOption(IReadOnlyList<string>? transformedCardIds)
    {
        LoseMaxHp(12);
        RemoveFirstCard(card => card.CardId.StartsWith("STRIKE_", StringComparison.OrdinalIgnoreCase) && IsTransformableCard(card.CardId));
        RemoveFirstCard(card => card.CardId.StartsWith("DEFEND_", StringComparison.OrdinalIgnoreCase) && IsTransformableCard(card.CardId));

        if (transformedCardIds == null)
        {
            return;
        }

        foreach (var transformedCardId in transformedCardIds)
        {
            GainCard(transformedCardId);
        }
    }

    public void ApplyShopRelicPurchase(string relicId, int price)
    {
        SpendGold(price);
        GainRelic(relicId);
    }

    public void ApplyShopCardRemoval(int price)
    {
        SpendGold(price);
        RemoveFirstRemovableBasicCard();
    }

    public void ApplyShopCardPurchase(string cardId, int price)
    {
        SpendGold(price);
        GainCard(cardId);
    }

    public void ApplyShopPotionPurchase(string potionId, int price)
    {
        SpendGold(price);
        GainPotion(potionId);
    }

    public void ApplyAnonymousShopSpend(int amount)
    {
        SpendGold(amount);
    }

    public void ApplyHatchRestSite()
    {
        var eggIndex = _deck.FindIndex(card => string.Equals(card.CardId, "BYRDONIS_EGG", StringComparison.OrdinalIgnoreCase));
        if (eggIndex < 0)
        {
            return;
        }

        _deck.RemoveAt(eggIndex);
        GainCard("BYRD_SWOOP");
        GainRelic("BYRDPIP");
    }

    private void EnchantFirstEligibleCard()
    {
        var card = _deck.FirstOrDefault(candidate => !candidate.HasEnchantment &&
                                                     _model.CanReceivePerfectFit(candidate.CardId));
        if (card != null)
        {
            card.HasEnchantment = true;
        }
    }

    private bool EnchantFirstEligibleCardByType(CardType cardType)
    {
        var card = _deck.FirstOrDefault(candidate => !candidate.HasEnchantment &&
                                                     HasCardType(candidate.CardId, cardType) &&
                                                     _model.CanReceivePerfectFit(candidate.CardId));
        if (card == null)
        {
            return false;
        }

        card.HasEnchantment = true;
        return true;
    }

    private bool HasEnchantableCardOfType(CardType cardType)
    {
        return _deck.Any(candidate => !candidate.HasEnchantment &&
                                      HasCardType(candidate.CardId, cardType) &&
                                      _model.CanReceivePerfectFit(candidate.CardId));
    }

    private bool HasCardType(string cardId, CardType cardType)
    {
        var normalizedId = Sts2EventIdNormalizer.FromAny(cardId);
        return _model.CardMetadata.TryGetValue(normalizedId, out var metadata)
            ? metadata.ParsedType == cardType
            : false;
    }

    private bool IsRemovableBasicCard(string cardId)
    {
        if (!IsRemovableCard(cardId))
        {
            return false;
        }

        var normalizedId = Sts2EventIdNormalizer.FromAny(cardId);
        if (normalizedId.StartsWith("STRIKE_", StringComparison.OrdinalIgnoreCase) ||
            normalizedId.StartsWith("DEFEND_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _model.CardMetadata.TryGetValue(normalizedId, out var metadata) &&
               metadata.ParsedRarity == CardRarity.Basic;
    }

    private static bool IsRemovableCard(string cardId)
    {
        return !NonRemovableCardIds.Contains(Sts2EventIdNormalizer.FromAny(cardId));
    }

    private static bool IsTransformableCard(string cardId)
    {
        return IsRemovableCard(cardId);
    }

    private static bool IsSpiralTarget(DeckCard card)
    {
        if (card.HasEnchantment)
        {
            return false;
        }

        return IsRemovableCard(card.CardId) &&
               (card.CardId.StartsWith("STRIKE_", StringComparison.OrdinalIgnoreCase) ||
                card.CardId.StartsWith("DEFEND_", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class CharacterRunDefaults
{
    private CharacterRunDefaults(
        int startingGold,
        int startingMaxHp,
        int ascensionLevel,
        IReadOnlyList<string> startingDeck,
        IReadOnlyList<string> startingRelics)
    {
        StartingGold = startingGold;
        StartingMaxHp = startingMaxHp;
        AscensionLevel = ascensionLevel;
        StartingDeck = startingDeck;
        StartingRelics = startingRelics;
    }

    public int StartingGold { get; }

    public int StartingMaxHp { get; }

    public int AscensionLevel { get; }

    public IReadOnlyList<string> StartingDeck { get; }

    public IReadOnlyList<string> StartingRelics { get; }

    public static CharacterRunDefaults For(CharacterId character, int ascensionLevel)
    {
        var (maxHp, deck, relics) = character switch
        {
            CharacterId.Ironclad => (
                80,
                new List<string>
                {
                    "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD",
                    "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD",
                    "BASH"
                },
                new List<string> { "BURNING_BLOOD" }),
            CharacterId.Defect => (
                75,
                new List<string>
                {
                    "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT",
                    "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT",
                    "ZAP", "DUALCAST"
                },
                new List<string> { "CRACKED_CORE" }),
            CharacterId.Necrobinder => (
                66,
                new List<string>
                {
                    "STRIKE_NECROBINDER", "STRIKE_NECROBINDER", "STRIKE_NECROBINDER", "STRIKE_NECROBINDER",
                    "DEFEND_NECROBINDER", "DEFEND_NECROBINDER", "DEFEND_NECROBINDER", "DEFEND_NECROBINDER",
                    "BODYGUARD", "UNLEASH"
                },
                new List<string> { "BOUND_PHYLACTERY" }),
            CharacterId.Regent => (
                75,
                new List<string>
                {
                    "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT",
                    "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT",
                    "FALLING_STAR", "VENERATE"
                },
                new List<string> { "DIVINE_RIGHT" }),
            _ => (
                70,
                new List<string>
                {
                    "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT",
                    "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT",
                    "NEUTRALIZE", "SURVIVOR"
                },
                new List<string> { "RING_OF_THE_SNAKE" })
        };

        if (ascensionLevel >= 10)
        {
            deck.Add("ASCENDERS_BANE");
        }

        return new CharacterRunDefaults(
            startingGold: 99,
            startingMaxHp: maxHp,
            ascensionLevel: ascensionLevel,
            startingDeck: deck,
            startingRelics: relics);
    }
}

internal static class Sts2EventIdNormalizer
{
    public static string FromPoolItem(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        if (text.StartsWith("EVENT.", StringComparison.OrdinalIgnoreCase))
        {
            return FromAny(text);
        }

        var builder = new StringBuilder(text.Length + 8);
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

    public static string FromAny(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        var dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < text.Length - 1)
        {
            text = text[(dotIndex + 1)..];
        }

        var builder = new StringBuilder(text.Length);
        foreach (var current in text)
        {
            if (char.IsLetterOrDigit(current))
            {
                builder.Append(char.ToUpperInvariant(current));
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        return builder.ToString().Trim('_');
    }
}
