using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Rng;

namespace SeedModel.Neow;

internal sealed class NeowRewardPreviewer
{
    private static readonly IReadOnlyDictionary<CharacterId, (string StrikeId, string DefendId)> BasicCardMap =
        new Dictionary<CharacterId, (string, string)>
        {
            [CharacterId.Ironclad] = ("STRIKE_IRONCLAD", "DEFEND_IRONCLAD"),
            [CharacterId.Silent] = ("STRIKE_SILENT", "DEFEND_SILENT"),
            [CharacterId.Defect] = ("STRIKE_DEFECT", "DEFEND_DEFECT"),
            [CharacterId.Necrobinder] = ("STRIKE_NECROBINDER", "DEFEND_NECROBINDER"),
            [CharacterId.Regent] = ("STRIKE_REGENT", "DEFEND_REGENT")
        };

    private const uint DefaultPlayerNetId = 1;
    private const string TransformationsSalt = "transformations";
    private const string PlayerRewardsSalt = "rewards";
    private const string CardRewardLabel = "卡牌奖励";
    private const string PotionRewardLabel = "药水";
    private const string ChoiceLabelPrefix = "可选卡牌";
    private const string BundleLabelPrefix = "卡牌包";
    private const string LargeCapsuleLabel = "新增卡牌";
    private const string ClawCardId = "CLAW";
    private const int ScarcityAscensionLevel = 7;

    private readonly IReadOnlyDictionary<string, CardInfo> _cardLookup;
    private readonly IReadOnlyDictionary<string, NeowCardMetadata> _cardMetadata;
    private readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> _cardPools;
    private readonly IReadOnlyList<string> _colorlessCardPool;
    private readonly IReadOnlyDictionary<string, PotionInfo> _potionLookup;
    private readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> _potionPools;
    private readonly IReadOnlyList<string> _sharedPotionPool;
    private readonly IReadOnlyDictionary<string, NeowPotionMetadata> _potionMetadata;

    public NeowRewardPreviewer(NeowOptionDataset dataset)
    {
        _cardLookup = dataset.CardMap;
        _cardMetadata = dataset.CardMetadataMap;
        _cardPools = dataset.CharacterCardPoolMap;
        _colorlessCardPool = dataset.ColorlessCardPoolList;
        _potionLookup = dataset.PotionMap;
        _potionPools = dataset.CharacterPotionPoolMap;
        _sharedPotionPool = dataset.SharedPotionPoolList;
        _potionMetadata = dataset.PotionMetadataMap;
    }

    public IReadOnlyList<RewardDetail> Build(string relicId, NeowGenerationContext context)
    {
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return Array.Empty<RewardDetail>();
        }

        var character = context.Character;

        if (string.Equals(relicId, NeowOptionIds.NeowsTorment, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { CreateCardDetail("添加卡牌", "NEOWS_FURY") };
        }

        if (string.Equals(relicId, NeowOptionIds.CursedPearl, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { CreateCardDetail("添加诅咒", "GREED") };
        }

        if (string.Equals(relicId, NeowOptionIds.LargeCapsule, StringComparison.OrdinalIgnoreCase))
        {
            var largeCapsule = BuildLargeCapsulePreview(character);
            if (largeCapsule.Count > 0)
            {
                return largeCapsule;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.ArcaneScroll, StringComparison.OrdinalIgnoreCase))
        {
            var arcaneScroll = BuildArcaneScrollPreview(context);
            if (arcaneScroll.Count > 0)
            {
                return arcaneScroll;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.LeadPaperweight, StringComparison.OrdinalIgnoreCase))
        {
            var paperweight = BuildLeadPaperweightPreview(context);
            if (paperweight.Count > 0)
            {
                return paperweight;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.MassiveScroll, StringComparison.OrdinalIgnoreCase))
        {
            var massiveScroll = BuildMassiveScrollPreview(context);
            if (massiveScroll.Count > 0)
            {
                return massiveScroll;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.LostCoffer, StringComparison.OrdinalIgnoreCase))
        {
            var lostCoffer = BuildLostCofferPreview(context);
            if (lostCoffer.Count > 0)
            {
                return lostCoffer;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.ScrollBoxes, StringComparison.OrdinalIgnoreCase))
        {
            var scrollBoxes = BuildScrollBoxesPreview(context);
            if (scrollBoxes.Count > 0)
            {
                return scrollBoxes;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.LeafyPoultice, StringComparison.OrdinalIgnoreCase))
        {
            var leafyDetails = BuildLeafyPreview(context, character);
            if (leafyDetails.Count > 0)
            {
                return leafyDetails;
            }
        }

        return Array.Empty<RewardDetail>();
    }

    private RewardDetail CreateCardDetail(string label, string cardId)
    {
        var name = _cardLookup.TryGetValue(cardId, out var info)
            ? info.Name
            : cardId;
        return new RewardDetail(RewardDetailType.Card, label, name, cardId);
    }

    private IReadOnlyList<RewardDetail> BuildLeafyPreview(NeowGenerationContext context, CharacterId character)
    {
        if (!BasicCardMap.TryGetValue(character, out var basics))
        {
            return Array.Empty<RewardDetail>();
        }

        if (!_cardPools.TryGetValue(character, out var pool) || pool.Count == 0)
        {
            return CreateLeafyFallback(basics);
        }

        var rngSeed = unchecked(context.RunSeed + DefaultPlayerNetId);
        var rng = new GameRng(rngSeed, TransformationsSalt);
        var results = new List<RewardDetail>(2);

        AddTransformedDetail(results, "变换卡牌", basics.StrikeId, pool, context.PlayerCount, rng);
        AddTransformedDetail(results, "变换卡牌", basics.DefendId, pool, context.PlayerCount, rng);

        return results.Count > 0 ? results : CreateLeafyFallback(basics);
    }

    private void AddTransformedDetail(
        List<RewardDetail> buffer,
        string label,
        string originalCardId,
        IReadOnlyList<string> pool,
        int playerCount,
        GameRng rng)
    {
        if (TryTransformCard(originalCardId, pool, playerCount, rng, out var transformedId))
        {
            buffer.Add(CreateCardDetail(label, transformedId));
        }
        else
        {
            buffer.Add(CreateCardDetail(label, originalCardId));
        }
    }

    private bool TryTransformCard(
        string originalCardId,
        IReadOnlyList<string> pool,
        int playerCount,
        GameRng rng,
        out string transformedId)
    {
        transformedId = string.Empty;
        if (!_cardMetadata.TryGetValue(originalCardId, out var originalMetadata))
        {
            return false;
        }

        var candidates = new List<string>();
        foreach (var candidateId in pool)
        {
            if (string.Equals(candidateId, originalCardId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_cardMetadata.TryGetValue(candidateId, out var candidateMetadata))
            {
                continue;
            }

            if (ShouldRestrictRarity(originalMetadata.ParsedRarity) &&
                !IsAllowedTransformationRarity(candidateMetadata.ParsedRarity))
            {
                continue;
            }

            if (playerCount <= 1 && candidateMetadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
            {
                continue;
            }

            if (playerCount > 1 && candidateMetadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
            {
                continue;
            }

            candidates.Add(candidateId);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var index = rng.NextInt(candidates.Count);
        transformedId = candidates[index];
        return true;
    }

    private static bool IsScarcityActive(int ascensionLevel) =>
        ascensionLevel >= ScarcityAscensionLevel;

    private static bool ShouldRestrictRarity(CardRarity rarity) =>
        rarity != CardRarity.Status && rarity != CardRarity.Curse;

    private static bool IsAllowedTransformationRarity(CardRarity rarity) =>
        rarity == CardRarity.Common || rarity == CardRarity.Uncommon || rarity == CardRarity.Rare;

    private IReadOnlyList<RewardDetail> CreateLeafyFallback((string StrikeId, string DefendId) basics)
    {
        return new[]
        {
            CreateCardDetail("变换卡牌", basics.StrikeId),
            CreateCardDetail("变换卡牌", basics.DefendId)
        };
    }

    private IReadOnlyList<RewardDetail> BuildLargeCapsulePreview(CharacterId character)
    {
        if (!BasicCardMap.TryGetValue(character, out var basics))
        {
            return Array.Empty<RewardDetail>();
        }

        return new[]
        {
            CreateCardDetail(LargeCapsuleLabel, basics.StrikeId),
            CreateCardDetail(LargeCapsuleLabel, basics.DefendId)
        };
    }

    private IReadOnlyList<RewardDetail> BuildArcaneScrollPreview(NeowGenerationContext context)
    {
        if (!_cardPools.TryGetValue(context.Character, out var pool) || pool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var rng = CreatePlayerRewardsRng(context);
        var scarcityActive = IsScarcityActive(context.AscensionLevel);
        var cards = RollCards(
            rng,
            pool,
            CardRarityOddsType.Uniform,
            1,
            context.PlayerCount,
            scarcityActive,
            metadata => metadata.ParsedRarity == CardRarity.Rare,
            simulateUpgradeRoll: false);

        return BuildCardDetails(cards, CardRewardLabel);
    }

    private IReadOnlyList<RewardDetail> BuildLeadPaperweightPreview(NeowGenerationContext context)
    {
        if (_colorlessCardPool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var rng = CreatePlayerRewardsRng(context);
        var scarcityActive = IsScarcityActive(context.AscensionLevel);
        var cards = RollCards(
            rng,
            _colorlessCardPool,
            CardRarityOddsType.RegularEncounter,
            2,
            context.PlayerCount,
            scarcityActive);

        return BuildCardDetails(cards, ChoiceLabelPrefix, includeIndex: true);
    }

    private IReadOnlyList<RewardDetail> BuildMassiveScrollPreview(NeowGenerationContext context)
    {
        var mergedPool = new HashSet<string>(_colorlessCardPool, StringComparer.OrdinalIgnoreCase);
        if (_cardPools.TryGetValue(context.Character, out var characterPool))
        {
            foreach (var cardId in characterPool)
            {
                mergedPool.Add(cardId);
            }
        }

        if (mergedPool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var rng = CreatePlayerRewardsRng(context);
        var scarcityActive = IsScarcityActive(context.AscensionLevel);
        var cards = RollCards(
            rng,
            mergedPool,
            CardRarityOddsType.RegularEncounter,
            3,
            context.PlayerCount,
            scarcityActive,
            metadata => metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly);

        return BuildCardDetails(cards, ChoiceLabelPrefix, includeIndex: true);
    }

    private IReadOnlyList<RewardDetail> BuildLostCofferPreview(NeowGenerationContext context)
    {
        if (!_cardPools.TryGetValue(context.Character, out var pool) || pool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var rng = CreatePlayerRewardsRng(context);
        var details = new List<RewardDetail>();

        var scarcityActive = IsScarcityActive(context.AscensionLevel);
        var cards = RollCards(
            rng,
            pool,
            CardRarityOddsType.RegularEncounter,
            3,
            context.PlayerCount,
            scarcityActive);
        details.AddRange(BuildCardDetails(cards, CardRewardLabel, includeIndex: true));

        var potionId = RollPotion(rng, context.Character);
        if (!string.IsNullOrWhiteSpace(potionId))
        {
            details.Add(CreatePotionDetail(PotionRewardLabel, potionId));
        }

        return details;
    }

    private IReadOnlyList<RewardDetail> BuildScrollBoxesPreview(NeowGenerationContext context)
    {
        var rng = CreatePlayerRewardsRng(context);
        var bundles = GenerateScrollBoxBundles(rng, context.Character, context.PlayerCount);
        if (bundles.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var details = new List<RewardDetail>();
        for (var i = 0; i < bundles.Count; i++)
        {
            var label = $"{BundleLabelPrefix}{i + 1}";
            foreach (var cardId in bundles[i])
            {
                details.Add(CreateCardDetail(label, cardId));
            }
        }

        return details;
    }

    private GameRng CreatePlayerRewardsRng(NeowGenerationContext context)
    {
        var seed = unchecked(context.RunSeed + DefaultPlayerNetId);
        return new GameRng(seed, PlayerRewardsSalt);
    }

    private IReadOnlyList<string> RollCards(
        GameRng rng,
        IEnumerable<string> pool,
        CardRarityOddsType oddsType,
        int count,
        int playerCount,
        bool scarcityActive,
        Func<NeowCardMetadata, bool>? predicate = null,
        bool simulateUpgradeRoll = true)
    {
        var candidates = new List<CardCandidate>();
        foreach (var cardId in pool)
        {
            if (!_cardMetadata.TryGetValue(cardId, out var metadata))
            {
                continue;
            }

            if (!IsCardAllowedForPlayer(metadata, playerCount))
            {
                continue;
            }

            if (predicate != null && !predicate(metadata))
            {
                continue;
            }

            candidates.Add(new CardCandidate(cardId, metadata));
        }

        if (candidates.Count == 0 || count <= 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var available = candidates.Where(c => !used.Contains(c.Id)).ToList();
            if (available.Count == 0)
            {
                break;
            }

            var pick = oddsType == CardRarityOddsType.Uniform
                ? PickUniformCard(rng, available)
                : PickWeightedCard(rng, available, oddsType, scarcityActive);

            if (pick == null)
            {
                break;
            }

            results.Add(pick.Id);
            used.Add(pick.Id);

            if (simulateUpgradeRoll)
            {
                SimulateUpgradeRoll(pick.Metadata, rng);
            }
        }

        return results;
    }

    private CardCandidate? PickUniformCard(GameRng rng, List<CardCandidate> candidates)
    {
        var filtered = candidates
            .Where(c => c.Metadata.ParsedRarity != CardRarity.Basic && c.Metadata.ParsedRarity != CardRarity.Ancient)
            .ToList();
        if (filtered.Count == 0)
        {
            filtered = candidates;
        }

        if (filtered.Count == 0)
        {
            return null;
        }

        var index = rng.NextInt(filtered.Count);
        return filtered[index];
    }

    private CardCandidate? PickWeightedCard(GameRng rng, List<CardCandidate> candidates, CardRarityOddsType oddsType, bool scarcityActive)
    {
        var allowed = candidates
            .Select(c => c.Metadata.ParsedRarity)
            .Where(r => r == CardRarity.Common || r == CardRarity.Uncommon || r == CardRarity.Rare)
            .Distinct()
            .OrderBy(GetRarityRank)
            .ToList();

        if (allowed.Count == 0)
        {
            return PickUniformCard(rng, candidates);
        }

        var rarity = RollCardRarity(rng, oddsType, scarcityActive);
        var guard = 0;
        while (!allowed.Contains(rarity) && rarity != CardRarity.None && guard++ < 3)
        {
            rarity = GetNextHighestRarity(rarity);
        }

        if (rarity == CardRarity.None)
        {
            rarity = allowed.First();
        }

        var pool = candidates.Where(c => c.Metadata.ParsedRarity == rarity).ToList();
        if (pool.Count == 0)
        {
            pool = candidates;
        }

        if (pool.Count == 0)
        {
            return null;
        }

        var index = rng.NextInt(pool.Count);
        return pool[index];
    }

    private static CardRarity RollCardRarity(GameRng rng, CardRarityOddsType oddsType, bool scarcityActive)
    {
        var (commonOdds, uncommonOdds, rareOdds) = GetRarityOdds(oddsType, scarcityActive);
        var roll = rng.NextDouble();
        if (roll < rareOdds)
        {
            return CardRarity.Rare;
        }

        if (roll < rareOdds + uncommonOdds)
        {
            return CardRarity.Uncommon;
        }

        return CardRarity.Common;
    }

    private static (double Common, double Uncommon, double Rare) GetRarityOdds(CardRarityOddsType oddsType, bool scarcityActive)
    {
        switch (oddsType)
        {
            case CardRarityOddsType.RegularEncounter:
                return scarcityActive
                    ? (0.615, 0.37, 0.0149)
                    : (0.6, 0.37, 0.03);
            case CardRarityOddsType.EliteEncounter:
                return scarcityActive
                    ? (0.549, 0.4, 0.05)
                    : (0.5, 0.4, 0.1);
            case CardRarityOddsType.BossEncounter:
                return (0, 0, 1);
            case CardRarityOddsType.Shop:
                return scarcityActive
                    ? (0.585, 0.37, 0.045)
                    : (0.54, 0.37, 0.09);
            case CardRarityOddsType.Uniform:
                return (1d / 3d, 1d / 3d, 1d / 3d);
            default:
                return (1, 0, 0);
        }
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Basic => CardRarity.Common,
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.None
        };

    private static int GetRarityRank(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Common => 0,
            CardRarity.Uncommon => 1,
            CardRarity.Rare => 2,
            _ => 3
        };

    private static bool IsCardAllowedForPlayer(NeowCardMetadata metadata, int playerCount)
    {
        if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
        {
            return false;
        }

        if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
        {
            return false;
        }

        return true;
    }

    private static void SimulateUpgradeRoll(NeowCardMetadata metadata, GameRng rng)
    {
        // The actual game always consumes one reward-RNG roll to determine whether the
        // generated card should be upgraded (unless CardCreationFlags.NoUpgradeRoll is
        // set). We do not currently track per-card upgrade eligibility, but consuming
        // one value keeps the RNG stream aligned with the base game.
        rng.NextDouble();
    }

    private IReadOnlyList<RewardDetail> BuildCardDetails(IReadOnlyList<string> cardIds, string label, bool includeIndex = false)
    {
        if (cardIds.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        var details = new List<RewardDetail>(cardIds.Count);
        for (var i = 0; i < cardIds.Count; i++)
        {
            var displayLabel = includeIndex ? $"{label}{i + 1}" : label;
            details.Add(CreateCardDetail(displayLabel, cardIds[i]));
        }

        return details;
    }

    private string? RollPotion(GameRng rng, CharacterId character)
    {
        var pool = new List<string>();
        if (_potionPools.TryGetValue(character, out var characterPotions))
        {
            pool.AddRange(characterPotions);
        }

        pool.AddRange(_sharedPotionPool);

        var candidates = pool
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => (_potionMetadata.TryGetValue(id, out var metadata), id, metadata))
            .Where(tuple => tuple.metadata != null)
            .Select(tuple => new { tuple.id, tuple.metadata })
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var rarity = RollPotionRarity(rng);
        var options = candidates.Where(c => c.metadata!.ParsedRarity == rarity).ToList();
        if (options.Count == 0)
        {
            options = candidates;
        }

        var pick = options[rng.NextInt(options.Count)];
        return pick.id;
    }

    private static PotionRarity RollPotionRarity(GameRng rng)
    {
        var roll = rng.NextDouble();
        if (roll <= 0.1)
        {
            return PotionRarity.Rare;
        }

        if (roll <= 0.35)
        {
            return PotionRarity.Uncommon;
        }

        return PotionRarity.Common;
    }

    private RewardDetail CreatePotionDetail(string label, string potionId)
    {
        var name = _potionLookup.TryGetValue(potionId, out var info) ? info.Name : potionId;
        return new RewardDetail(RewardDetailType.Potion, label, name, potionId);
    }

    private IReadOnlyList<IReadOnlyList<string>> GenerateScrollBoxBundles(GameRng rng, CharacterId character, int playerCount)
    {
        if (!_cardPools.TryGetValue(character, out var pool) || pool.Count == 0)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }

        var commons = new List<string>();
        var uncommons = new List<string>();
        foreach (var cardId in pool)
        {
            if (!_cardMetadata.TryGetValue(cardId, out var metadata))
            {
                continue;
            }

            if (!IsCardAllowedForPlayer(metadata, playerCount))
            {
                continue;
            }

            if (metadata.ParsedRarity == CardRarity.Common)
            {
                commons.Add(cardId);
            }
            else if (metadata.ParsedRarity == CardRarity.Uncommon)
            {
                uncommons.Add(cardId);
            }
        }

        if (commons.Count == 0 || uncommons.Count == 0)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new List<IReadOnlyList<string>>(2);

        for (var i = 0; i < 2; i++)
        {
            if (character == CharacterId.Defect && rng.NextInt(100) < 1)
            {
                bundles.Add(new[] { ClawCardId, ClawCardId, ClawCardId });
                continue;
            }

            var picks = new List<string>();
            for (var c = 0; c < 2; c++)
            {
                var availableCommons = commons.Where(id => !usedIds.Contains(id)).ToList();
                if (availableCommons.Count == 0)
                {
                    break;
                }

                var pick = availableCommons[rng.NextInt(availableCommons.Count)];
                picks.Add(pick);
                usedIds.Add(pick);
            }

            var availableUncommons = uncommons.Where(id => !usedIds.Contains(id)).ToList();
            if (availableUncommons.Count > 0)
            {
                var pick = availableUncommons[rng.NextInt(availableUncommons.Count)];
                picks.Add(pick);
                usedIds.Add(pick);
            }

            bundles.Add(picks);
        }

        return bundles;
    }

    private sealed record CardCandidate(string Id, NeowCardMetadata Metadata);
}
