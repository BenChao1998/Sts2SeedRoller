using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2;

/// <summary>
/// Simulates the shop inventory generation using the same RNG logic as the game.
/// </summary>
public sealed class Sts2ShopSimulator
{
    private readonly NeowOptionDataset _dataset;
    private readonly GameRng _rewardsRng; // For rarity rolls (card + relic)
    private readonly GameRng _shopsRng;   // For card/relic/potion selection and prices

    // Card type distribution for the 5 colored card slots: 2 Attack, 2 Skill, 1 Power
    private static readonly CardType[] ColoredCardTypes = new CardType[5]
    {
        CardType.Attack,
        CardType.Attack,
        CardType.Skill,
        CardType.Skill,
        CardType.Power
    };

    // Rarity distribution for colorless cards: 1 Uncommon, 1 Rare (FIXED, not rolled)
    private static readonly CardRarity[] ColorlessCardRarities = new CardRarity[2]
    {
        CardRarity.Uncommon,
        CardRarity.Rare
    };

    /// <summary>
    /// Creates a shop simulator with the given Rewards and Shops RNG seeds.
    /// These correspond to the game's PlayerRngSet: Rng(baseSeed + hash("rewards")) and Rng(baseSeed + hash("shops")).
    /// </summary>
    public Sts2ShopSimulator(NeowOptionDataset dataset, uint rewardsSeed, uint shopsSeed)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _rewardsRng = new GameRng(rewardsSeed);
        _shopsRng = new GameRng(shopsSeed);
    }

    public ShopPreview Preview(CharacterId character)
    {
        // 1. Select discounted slot (0-4) — uses Shops RNG
        var discountedSlot = _shopsRng.NextInt(5);

        // 2. Generate colored cards (5 slots)
        var coloredCards = GenerateColoredCards(character, discountedSlot);

        // 3. Generate colorless cards (2 slots: Uncommon + Rare, FIXED)
        var colorlessCards = GenerateColorlessCards();

        // 4. Generate relics (3 items: 2 random rarity + 1 fixed Shop)
        var relics = GenerateRelics();

        // 5. Generate potions (3 items)
        var potions = GeneratePotions(character);

        return new ShopPreview
        {
            ColoredCards = coloredCards,
            ColorlessCards = colorlessCards,
            Relics = relics,
            Potions = potions,
            DiscountedColoredSlot = discountedSlot
        };
    }

    private List<ShopCardEntry> GenerateColoredCards(CharacterId character, int discountedSlot)
    {
        var result = new List<ShopCardEntry>(5);

        // Get the character card pool — same as Neow rewards
        var pool = GetCardPool(character);

        // Cards already selected (for exclusion from subsequent picks)
        var selectedCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 5; i++)
        {
            var cardType = ColoredCardTypes[i];
            var available = pool.Where(c => !selectedCards.Contains(c)).ToList();

            // Roll rarity: uses Rewards RNG (via PlayerOdds.CardRarity.RollWithoutChangingFutureOdds)
            var rarity = RollCardRarityForColored();

            var cardId = PickCardByTypeAndRarity(available, cardType, ref rarity);

            selectedCards.Add(cardId);

            var basePrice = GetBaseCardPrice(rarity);
            var priceFluctuation = ScaleFloat(_shopsRng.NextFloat(), 0.95f, 1.05f);
            var finalPrice = (int)Math.Round(basePrice * priceFluctuation);

            if (i == discountedSlot)
            {
                finalPrice = (int)Math.Round(finalPrice * 0.5);
            }

            result.Add(new ShopCardEntry { Id = cardId, Price = finalPrice });
        }

        return result;
    }

    private string PickCardByTypeAndRarity(List<string> pool, CardType targetType, ref CardRarity rarity)
    {
        // Mirror CardFactory.CreateForMerchant:
        // 1. Filter by rarity AND type
        // 2. If no matches, try next highest rarity
        // 3. Pick random from matching pool (uses Shops RNG)
        var candidates = new List<string>();

        while (true)
        {
            candidates.Clear();
            foreach (var cardId in pool)
            {
                if (!_dataset.CardMetadataMap.TryGetValue(cardId, out var meta))
                    continue;
                if (meta.ParsedType == targetType && meta.ParsedRarity == rarity)
                    candidates.Add(cardId);
            }

            if (candidates.Count > 0)
                break;

            // Try next highest rarity
            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None || rarity == CardRarity.Basic)
                break;
        }

        if (candidates.Count == 0)
        {
            // Ultimate fallback: any card of the target type
            foreach (var cardId in pool)
            {
                if (!_dataset.CardMetadataMap.TryGetValue(cardId, out var meta))
                    continue;
                if (meta.ParsedType == targetType)
                    candidates.Add(cardId);
            }
        }

        if (candidates.Count == 0)
        {
            // Desperate fallback: any card
            candidates.AddRange(pool.Take(5));
        }

        return _shopsRng.NextItem(candidates) ?? candidates[0];
    }

    private List<ShopCardEntry> GenerateColorlessCards()
    {
        var result = new List<ShopCardEntry>(2);
        // Filter by multiplayerConstraint: in single-player, exclude MultiplayerOnly cards
        // (mirrors FilterForPlayerCount in CardFactory.CreateForMerchant)
        var pool = _dataset.ColorlessCardPoolList
            .Where(c => !IsMultiplayerOnlyCard(c))
            .ToList();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 2; i++)
        {
            // FIXED rarities: Uncommon, Rare (not rolled!)
            var rarity = ColorlessCardRarities[i];
            var candidates = pool.Where(c => !selected.Contains(c) && GetCardRarity(c) == rarity).ToList();

            if (candidates.Count == 0)
            {
                // Fallback: any remaining card
                candidates = pool.Where(c => !selected.Contains(c)).ToList();
            }

            // Card selection: uses Shops RNG
            var cardId = _shopsRng.NextItem(candidates) ?? candidates.FirstOrDefault();
            if (string.IsNullOrEmpty(cardId))
                continue;

            selected.Add(cardId);

            // Price: base * 1.15 (colorless bonus) * fluctuation
            var basePrice = GetBaseCardPrice(rarity);
            basePrice = (int)Math.Round(basePrice * 1.15);
            var priceFluctuation = ScaleFloat(_shopsRng.NextFloat(), 0.95f, 1.05f);
            var finalPrice = (int)Math.Round(basePrice * priceFluctuation);

            result.Add(new ShopCardEntry { Id = cardId, Price = finalPrice });
        }

        return result;
    }

    private List<ShopRelicEntry> GenerateRelics()
    {
        var result = new List<ShopRelicEntry>(3);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build rarity-filtered pools
        var relicPools = BuildRelicPools();

        // First 2 relics: random rarity (rolled using Rewards RNG!)
        for (var i = 0; i < 2; i++)
        {
            var rarity = RollRelicRarityFromRewards();
            var relicId = PickRelicByRarity(rarity, relicPools, selected);

            if (!string.IsNullOrEmpty(relicId))
            {
                selected.Add(relicId);
                var basePrice = GetRelicMerchantCost(relicId);
                var priceFluctuation = ScaleFloat(_shopsRng.NextFloat(), 0.85f, 1.15f);
                var finalPrice = (int)Math.Round(basePrice * priceFluctuation);
                result.Add(new ShopRelicEntry { Id = relicId, Price = finalPrice });
            }
        }

        // Third relic: fixed Shop rarity
        var shopRelicId = PickRelicByRarity(RelicRarity.Shop, relicPools, selected);
        if (!string.IsNullOrEmpty(shopRelicId))
        {
            selected.Add(shopRelicId);
            var basePrice = GetRelicMerchantCost(shopRelicId);
            var priceFluctuation = ScaleFloat(_shopsRng.NextFloat(), 0.85f, 1.15f);
            var finalPrice = (int)Math.Round(basePrice * priceFluctuation);
            result.Add(new ShopRelicEntry { Id = shopRelicId, Price = finalPrice });
        }

        return result;
    }

    private Dictionary<RelicRarity, List<string>> BuildRelicPools()
    {
        // Mirror the game's RelicGrabBag: combine SharedRelicPool + character RelicPool
        // NO shuffling here — pools are pre-shuffled at run initialization
        var allRelics = new List<string>();

        if (_dataset.RelicPoolMap.TryGetValue("Shared", out var shared))
            allRelics.AddRange(shared);

        foreach (var kvp in _dataset.RelicPoolMap)
        {
            if (kvp.Key != "Shared")
                allRelics.AddRange(kvp.Value);
        }

        // Group by rarity
        var byRarity = new Dictionary<RelicRarity, List<string>>();
        foreach (var relicId in allRelics)
        {
            if (RelicBlacklist.Contains(relicId))
                continue;

            if (!_dataset.RelicMetadataMap.TryGetValue(relicId, out var meta))
                continue;

            var rarity = meta.ParsedRarity;
            // Exclude non-shop rarities
            if (rarity != RelicRarity.Common &&
                rarity != RelicRarity.Uncommon &&
                rarity != RelicRarity.Rare &&
                rarity != RelicRarity.Shop)
                continue;

            if (!byRarity.TryGetValue(rarity, out var list))
            {
                list = new List<string>();
                byRarity[rarity] = list;
            }
            list.Add(relicId);
        }

        return byRarity;
    }

    private string? PickRelicByRarity(RelicRarity targetRarity, Dictionary<RelicRarity, List<string>> pools, HashSet<string> selected)
    {
        // Collect all eligible relics (target rarity and higher) for random selection
        var candidates = new List<string>();
        var rarity = targetRarity;
        while (true)
        {
            if (pools.TryGetValue(rarity, out var pool))
            {
                foreach (var relicId in pool)
                {
                    if (!selected.Contains(relicId) && !RelicBlacklist.Contains(relicId))
                    {
                        candidates.Add(relicId);
                    }
                }
            }

            // Try next rarity
            rarity = rarity switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => targetRarity // Stop
            };

            if (rarity == targetRarity)
                break;
        }

        return _shopsRng.NextItem(candidates);
    }

    private List<ShopPotionEntry> GeneratePotions(CharacterId character)
    {
        var result = new List<ShopPotionEntry>(3);

        // Get potion pool: character pool + shared pool
        var pool = new List<string>();
        if (_dataset.CharacterPotionPoolMap.TryGetValue(character, out var charPotions))
            pool.AddRange(charPotions);
        pool.AddRange(_dataset.SharedPotionPoolList);

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 3; i++)
        {
            // Roll rarity: ≤0.1→Rare, ≤0.35→Uncommon, else→Common
            var roll = _shopsRng.NextFloat();
            var rarity = roll <= 0.1f ? PotionRarity.Rare :
                         roll <= 0.35f ? PotionRarity.Uncommon :
                         PotionRarity.Common;

            var candidates = pool.Where(p => !selected.Contains(p) && GetPotionRarity(p) == rarity).ToList();

            if (candidates.Count == 0)
            {
                // Fallback: any remaining potion
                candidates = pool.Where(p => !selected.Contains(p)).ToList();
            }

            var potionId = _shopsRng.NextItem(candidates) ?? candidates.FirstOrDefault();
            if (string.IsNullOrEmpty(potionId))
                continue;

            selected.Add(potionId);

            var basePrice = GetBasePotionPrice(rarity);
            var priceFluctuation = ScaleFloat(_shopsRng.NextFloat(), 0.95f, 1.05f);
            var finalPrice = (int)Math.Round(basePrice * priceFluctuation);

            result.Add(new ShopPotionEntry { Id = potionId, Price = finalPrice });
        }

        return result;
    }

    #region Data Access Helpers

    private static readonly HashSet<string> RelicBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "THE_COURIER",
        "OLD_COIN"
    };

    private bool IsMultiplayerOnlyCard(string cardId)
    {
        if (_dataset.CardMetadataMap.TryGetValue(cardId, out var meta))
        {
            return meta.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly;
        }
        return false;
    }

    private List<string> GetCardPool(CharacterId character)
    {
        // Same pool as Neow rewards: the character's card pool
        if (_dataset.CharacterCardPoolMap.TryGetValue(character, out var pool))
        {
            return pool.ToList();
        }
        return new List<string>();
    }

    private CardRarity RollCardRarityForColored()
    {
        // CRITICAL: Only roll ONCE for all 5 colored cards!
        // This mirrors player.PlayerOdds.CardRarity.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop)
        // which uses Rewards RNG (NOT Shops RNG)
        // At Ascension 10, Scarcity is active: C=0.585, U=0.37, R=0.045
        var (common, uncommon, rare) = NeowRewardPreviewer.GetRarityOdds(CardRarityOddsType.Shop, scarcityActive: true);
        var roll = _rewardsRng.NextDouble();
        if (roll < common)
            return CardRarity.Common;
        if (roll < common + uncommon)
            return CardRarity.Uncommon;
        return CardRarity.Rare;
    }

    private CardRarity GetCardRarity(string cardId)
    {
        if (_dataset.CardMetadataMap.TryGetValue(cardId, out var metadata))
        {
            return metadata.ParsedRarity;
        }
        return CardRarity.Common;
    }

    private int GetBaseCardPrice(CardRarity rarity)
    {
        return rarity switch
        {
            CardRarity.Common => 50,
            CardRarity.Uncommon => 75,
            CardRarity.Rare => 150,
            _ => 50
        };
    }

    private RelicRarity RollRelicRarityFromRewards()
    {
        // Mirror: RelicFactory.RollRarity(Player) which uses player.PlayerRng.Rewards
        // <0.5→Common, <0.83→Uncommon, else→Rare
        var roll = _rewardsRng.NextFloat();
        if (roll < 0.5f)
            return RelicRarity.Common;
        if (roll < 0.83f)
            return RelicRarity.Uncommon;
        return RelicRarity.Rare;
    }

    private int GetRelicMerchantCost(string relicId)
    {
        if (_dataset.RelicMetadataMap.TryGetValue(relicId, out var meta))
        {
            return meta.MerchantCost;
        }
        return 200; // Default fallback
    }

    private PotionRarity GetPotionRarity(string potionId)
    {
        if (_dataset.PotionMetadataMap.TryGetValue(potionId, out var metadata))
        {
            if (Enum.TryParse<PotionRarity>(metadata.Rarity, ignoreCase: true, out var rarity))
                return rarity;
        }
        return PotionRarity.Common;
    }

    private int GetBasePotionPrice(PotionRarity rarity)
    {
        return rarity switch
        {
            PotionRarity.Common => 50,
            PotionRarity.Uncommon => 75,
            PotionRarity.Rare => 100,
            _ => 50
        };
    }

    private static float ScaleFloat(float t, float min, float max)
    {
        return min + t * (max - min);
    }

    private static CardRarity GetNextHighestRarity(CardRarity current)
    {
        return current switch
        {
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.Rare
        };
    }

    #endregion
}
