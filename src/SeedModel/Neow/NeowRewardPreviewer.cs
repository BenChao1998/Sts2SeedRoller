using System;
using System.Collections.Generic;
using System.Globalization;
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
    private const string CardRewardLabel = "\u5361\u724c\u5956\u52b1";
    private const string PotionRewardLabel = "\u836f\u6c34";
    private const string ChoiceLabelPrefix = "\u53ef\u9009\u5361\u724c";
    private const string BundleLabelPrefix = "\u5361\u724c\u5305";
    private const string LargeCapsuleLabel = "\u65b0\u589e\u5361\u724c";
    private const string RelicRewardLabel = "\u9057\u7269\u5956\u52b1";
    private const string CurseRewardLabel = "\u8bc5\u5492";
    private const string UpgradeLabel = "\u5347\u7ea7";
    private const string PotionSlotLabel = "\u836f\u6c34\u680f";
    private const string EffectLabel = "\u6548\u679c";
    private const string AddedCardLabel = "\u6dfb\u52a0\u5361\u724c";
    private const string TransformCardLabel = "\u53d8\u6362\u5361\u724c";
    private const string AddedCurseLabel = "\u6dfb\u52a0\u8bc5\u5492";
    private const string InjuryCardId = "INJURY";
    private const string ClawCardId = "CLAW";
    private const int ScarcityAscensionLevel = 7;

    private static readonly string[] NeowsBonesRelicPool =
    [
        NeowOptionIds.CursedPearl,
        NeowOptionIds.HeftyTablet,
        NeowOptionIds.LargeCapsule,
        NeowOptionIds.LeafyPoultice,
        NeowOptionIds.PrecariousShears,
        NeowOptionIds.SilverCrucible,
        NeowOptionIds.NeowsBones,
        NeowOptionIds.ArcaneScroll,
        NeowOptionIds.BoomingConch,
        NeowOptionIds.GoldenPearl,
        NeowOptionIds.LeadPaperweight,
        NeowOptionIds.LostCoffer,
        NeowOptionIds.NeowsTorment,
        NeowOptionIds.NewLeaf,
        NeowOptionIds.PreciseScissors,
        NeowOptionIds.PhialHolster,
        NeowOptionIds.WingedBoots,
        NeowOptionIds.MassiveScroll,
        NeowOptionIds.LavaRock,
        NeowOptionIds.NeowsTalisman,
        NeowOptionIds.NutritiousOyster,
        NeowOptionIds.Pomander,
        NeowOptionIds.ScrollBoxes,
        NeowOptionIds.SmallCapsule,
        NeowOptionIds.StoneHumidifier
    ];

    private static readonly string[] ModifierGeneratedCurseIds =
    [
        "CLUMSY",
        "DEBT",
        "DECAY",
        "DOUBT",
        "GUILTY",
        InjuryCardId,
        "NORMALITY",
        "REGRET",
        "SHAME",
        "WRITHE"
    ];

    private readonly IReadOnlyDictionary<string, CardInfo> _cardLookup;
    private readonly IReadOnlyDictionary<string, NeowCardMetadata> _cardMetadata;
    private readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> _cardPools;
    private readonly IReadOnlyList<string> _colorlessCardPool;
    private readonly IReadOnlyDictionary<string, PotionInfo> _potionLookup;
    private readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> _potionPools;
    private readonly IReadOnlyList<string> _sharedPotionPool;
    private readonly IReadOnlyDictionary<string, NeowPotionMetadata> _potionMetadata;
    private readonly IReadOnlyDictionary<string, NeowRelicMetadata> _relicMetadata;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _relicPools;
    private readonly IReadOnlyDictionary<string, string> _relicNames;

    private sealed class NeowsBonesPreviewState
    {
        public required NeowGenerationContext Context { get; init; }

        public required GameRng RewardsRng { get; init; }

        public required GameRng TransformationsRng { get; init; }

        public required GameRng CombatPotionGenerationRng { get; init; }

        public required GameRng NicheRng { get; init; }

        public required NeowRelicGrabBag RelicBag { get; init; }
    }

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
        _relicMetadata = dataset.RelicMetadataMap;
        _relicPools = dataset.RelicPoolMap;
        _relicNames = dataset.OptionMap.Values
            .Where(option => !string.IsNullOrWhiteSpace(option.RelicId) && !string.IsNullOrWhiteSpace(option.Title))
            .GroupBy(option => option.RelicId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Title!, StringComparer.OrdinalIgnoreCase);
    }

    public static NeowDetailHint GetDetailHint(string relicId)
    {
        return relicId.ToUpperInvariant() switch
        {
            NeowOptionIds.NeowsTorment => NeowDetailHint.Card,
            NeowOptionIds.CursedPearl => NeowDetailHint.Card,
            NeowOptionIds.HeftyTablet => NeowDetailHint.Card,
            NeowOptionIds.LargeCapsule => NeowDetailHint.Card | NeowDetailHint.Relic,
            NeowOptionIds.ArcaneScroll => NeowDetailHint.Card,
            NeowOptionIds.LeadPaperweight => NeowDetailHint.Card,
            NeowOptionIds.MassiveScroll => NeowDetailHint.Card,
            NeowOptionIds.LostCoffer => NeowDetailHint.Card | NeowDetailHint.Potion,
            NeowOptionIds.ScrollBoxes => NeowDetailHint.Card,
            NeowOptionIds.PhialHolster => NeowDetailHint.Potion | NeowDetailHint.Text,
            NeowOptionIds.NeowsTalisman => NeowDetailHint.Card,
            NeowOptionIds.Pomander => NeowDetailHint.Text,
            NeowOptionIds.SmallCapsule => NeowDetailHint.Relic,
            NeowOptionIds.NeowsBones => NeowDetailHint.Card | NeowDetailHint.Potion | NeowDetailHint.Relic | NeowDetailHint.Text,
            NeowOptionIds.LeafyPoultice => NeowDetailHint.Card,
            _ => NeowDetailHint.None
        };
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
            return new[] { CreateCardDetail(AddedCardLabel, "NEOWS_FURY") };
        }

        if (string.Equals(relicId, NeowOptionIds.CursedPearl, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { CreateCardDetail(AddedCurseLabel, "GREED") };
        }

        if (string.Equals(relicId, NeowOptionIds.HeftyTablet, StringComparison.OrdinalIgnoreCase))
        {
            var heftyTablet = BuildHeftyTabletPreview(context);
            if (heftyTablet.Count > 0)
            {
                return heftyTablet;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.LargeCapsule, StringComparison.OrdinalIgnoreCase))
        {
            var largeCapsule = BuildLargeCapsulePreview(context, character);
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

        if (string.Equals(relicId, NeowOptionIds.PhialHolster, StringComparison.OrdinalIgnoreCase))
        {
            var phialHolster = BuildPhialHolsterPreview(context);
            if (phialHolster.Count > 0)
            {
                return phialHolster;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.NeowsTalisman, StringComparison.OrdinalIgnoreCase))
        {
            var talisman = BuildNeowsTalismanPreview(character);
            if (talisman.Count > 0)
            {
                return talisman;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.Pomander, StringComparison.OrdinalIgnoreCase))
        {
            return BuildPomanderPreview();
        }

        if (string.Equals(relicId, NeowOptionIds.SmallCapsule, StringComparison.OrdinalIgnoreCase))
        {
            var smallCapsule = BuildSmallCapsulePreview(context);
            if (smallCapsule.Count > 0)
            {
                return smallCapsule;
            }
        }

        if (string.Equals(relicId, NeowOptionIds.NeowsBones, StringComparison.OrdinalIgnoreCase))
        {
            var bones = BuildNeowsBonesPreview(context);
            if (bones.Count > 0)
            {
                return bones;
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

    private RewardDetail CreateUpgradedCardDetail(string label, string cardId)
    {
        var name = _cardLookup.TryGetValue(cardId, out var info)
            ? info.Name
            : cardId;
        return new RewardDetail(RewardDetailType.Card, label, $"{name}+", cardId);
    }

    private RewardDetail CreateRelicDetail(string label, string relicId)
    {
        return new RewardDetail(RewardDetailType.Relic, label, ResolveRelicName(relicId), relicId);
    }

    private RewardDetail CreateTextDetail(string label, string value)
    {
        return new RewardDetail(RewardDetailType.Text, label, value);
    }

    private string ResolveRelicName(string relicId)
    {
        if (_relicNames.TryGetValue(relicId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var words = relicId
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant()));
        var displayName = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(displayName) ? relicId : displayName;
    }

    private IReadOnlyList<RewardDetail> BuildLeafyPreview(NeowGenerationContext context, CharacterId character)
    {
        var rngSeed = unchecked(context.RunSeed + DefaultPlayerNetId);
        var rng = new GameRng(rngSeed, TransformationsSalt);
        return BuildLeafyPreview(context, character, rng);
    }

    private IReadOnlyList<RewardDetail> BuildLeafyPreview(
        NeowGenerationContext context,
        CharacterId character,
        GameRng rng)
    {
        if (!BasicCardMap.TryGetValue(character, out var basics))
        {
            return Array.Empty<RewardDetail>();
        }

        if (!_cardPools.TryGetValue(character, out var pool) || pool.Count == 0)
        {
            return CreateLeafyFallback(basics);
        }
        var results = new List<RewardDetail>(2);

        AddTransformedDetail(results, TransformCardLabel, basics.StrikeId, pool, context.PlayerCount, rng);
        AddTransformedDetail(results, TransformCardLabel, basics.DefendId, pool, context.PlayerCount, rng);

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

        transformedId = candidates[rng.NextInt(candidates.Count)];
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
            CreateCardDetail(TransformCardLabel, basics.StrikeId),
            CreateCardDetail(TransformCardLabel, basics.DefendId)
        };
    }

    private IReadOnlyList<RewardDetail> BuildHeftyTabletPreview(NeowGenerationContext context)
    {
        var rng = CreatePlayerRewardsRng(context);
        return BuildHeftyTabletPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildHeftyTabletPreview(NeowGenerationContext context, GameRng rng)
    {
        if (!_cardPools.TryGetValue(context.Character, out var pool) || pool.Count == 0)
        {
            return new[] { CreateCardDetail(CurseRewardLabel, InjuryCardId) };
        }
        var cards = RollCards(
            rng,
            pool,
            CardRarityOddsType.Uniform,
            3,
            context.PlayerCount,
            scarcityActive: false,
            metadata => metadata.ParsedRarity == CardRarity.Rare,
            simulateUpgradeRoll: false);

        var details = BuildCardDetails(cards, ChoiceLabelPrefix, includeIndex: true).ToList();
        details.Add(CreateCardDetail(CurseRewardLabel, InjuryCardId));
        return details;
    }

    private IReadOnlyList<RewardDetail> BuildLargeCapsulePreview(NeowGenerationContext context, CharacterId character)
    {
        var grabBag = CreateRelicGrabBag(context);
        var rng = CreatePlayerRewardsRng(context);
        return BuildLargeCapsulePreview(context, character, grabBag, rng);
    }

    private IReadOnlyList<RewardDetail> BuildLargeCapsulePreview(
        NeowGenerationContext context,
        CharacterId character,
        NeowRelicGrabBag grabBag,
        GameRng rng)
    {
        if (!BasicCardMap.TryGetValue(character, out var basics))
        {
            return Array.Empty<RewardDetail>();
        }

        var details = new List<RewardDetail>(4);

        for (var i = 0; i < 2; i++)
        {
            var rarity = RollRelicRarity(rng);
            var relicId = grabBag.PullFromFront(rarity);
            if (!string.IsNullOrWhiteSpace(relicId))
            {
                details.Add(CreateRelicDetail(RelicRewardLabel, relicId));
            }
        }

        details.Add(CreateCardDetail(LargeCapsuleLabel, basics.StrikeId));
        details.Add(CreateCardDetail(LargeCapsuleLabel, basics.DefendId));
        return details;
    }

    private IReadOnlyList<RewardDetail> BuildArcaneScrollPreview(NeowGenerationContext context)
    {
        var rng = CreatePlayerRewardsRng(context);
        return BuildArcaneScrollPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildArcaneScrollPreview(NeowGenerationContext context, GameRng rng)
    {
        if (!_cardPools.TryGetValue(context.Character, out var pool) || pool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }
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
        var rng = CreatePlayerRewardsRng(context);
        return BuildLeadPaperweightPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildLeadPaperweightPreview(NeowGenerationContext context, GameRng rng)
    {
        if (_colorlessCardPool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }
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
        var rng = CreatePlayerRewardsRng(context);
        return BuildMassiveScrollPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildMassiveScrollPreview(NeowGenerationContext context, GameRng rng)
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
        var rng = CreatePlayerRewardsRng(context);
        return BuildLostCofferPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildLostCofferPreview(NeowGenerationContext context, GameRng rng)
    {
        if (!_cardPools.TryGetValue(context.Character, out var pool) || pool.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }
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
        return BuildScrollBoxesPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildScrollBoxesPreview(NeowGenerationContext context, GameRng rng)
    {
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

    private IReadOnlyList<RewardDetail> BuildPhialHolsterPreview(NeowGenerationContext context)
    {
        var rng = CreateRunRng(context, "combat_potion_generation");
        return BuildPhialHolsterPreview(context, rng);
    }

    private IReadOnlyList<RewardDetail> BuildPhialHolsterPreview(NeowGenerationContext context, GameRng rng)
    {
        var details = new List<RewardDetail>
        {
            CreateTextDetail(PotionSlotLabel, "+1")
        };

        foreach (var potionId in RollPotions(rng, context.Character, 2))
        {
            details.Add(CreatePotionDetail(PotionRewardLabel, potionId));
        }

        return details;
    }

    private IReadOnlyList<RewardDetail> BuildNeowsTalismanPreview(CharacterId character)
    {
        if (!BasicCardMap.TryGetValue(character, out var basics))
        {
            return Array.Empty<RewardDetail>();
        }

        return new[]
        {
            CreateUpgradedCardDetail(UpgradeLabel, basics.StrikeId),
            CreateUpgradedCardDetail(UpgradeLabel, basics.DefendId)
        };
    }

    private IReadOnlyList<RewardDetail> BuildPomanderPreview()
    {
        return new[]
        {
            CreateTextDetail(EffectLabel, "\u4ece\u724c\u7ec4\u4e2d\u9009\u62e9 1 \u5f20\u53ef\u5347\u7ea7\u7684\u5361\u724c")
        };
    }

    private IReadOnlyList<RewardDetail> BuildSmallCapsulePreview(NeowGenerationContext context)
    {
        var rng = CreatePlayerRewardsRng(context);
        var grabBag = CreateRelicGrabBag(context);
        return BuildSmallCapsulePreview(context, grabBag, rng);
    }

    private IReadOnlyList<RewardDetail> BuildSmallCapsulePreview(
        NeowGenerationContext context,
        NeowRelicGrabBag grabBag,
        GameRng rng)
    {
        var relicId = grabBag.PullFromFront(RollRelicRarity(rng));
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return Array.Empty<RewardDetail>();
        }

        return new[] { CreateRelicDetail(RelicRewardLabel, relicId) };
    }

    private IReadOnlyList<RewardDetail> BuildNeowsBonesPreview(NeowGenerationContext context)
    {
        var state = CreateNeowsBonesPreviewState(context);
        var candidates = NeowsBonesRelicPool
            .Where(relicId => !string.Equals(relicId, NeowOptionIds.NeowsBones, StringComparison.OrdinalIgnoreCase))
            .Where(relicId => IsNeowsBonesRelicAllowed(relicId, context))
            .ToList();

        if (candidates.Count == 0)
        {
            return Array.Empty<RewardDetail>();
        }

        state.RewardsRng.Shuffle(candidates);

        var details = new List<RewardDetail>(8);
        foreach (var relicId in candidates.Take(2))
        {
            details.Add(CreateRelicDetail(RelicRewardLabel, relicId));
            AppendNeowsBonesChildDetails(details, relicId, state);
        }

        var curseId = RollModifierGeneratedCurse(state.NicheRng);
        if (!string.IsNullOrWhiteSpace(curseId))
        {
            details.Add(CreateCardDetail(CurseRewardLabel, curseId));
        }

        return details;
    }

    private NeowsBonesPreviewState CreateNeowsBonesPreviewState(NeowGenerationContext context)
    {
        return new NeowsBonesPreviewState
        {
            Context = context,
            RewardsRng = CreatePlayerRewardsRng(context),
            TransformationsRng = new GameRng(unchecked(context.RunSeed + DefaultPlayerNetId), TransformationsSalt),
            CombatPotionGenerationRng = CreateRunRng(context, "combat_potion_generation"),
            NicheRng = CreateRunRng(context, "niche"),
            RelicBag = CreateRelicGrabBag(context)
        };
    }

    private void AppendNeowsBonesChildDetails(
        List<RewardDetail> details,
        string relicId,
        NeowsBonesPreviewState state)
    {
        switch (relicId.ToUpperInvariant())
        {
            case NeowOptionIds.NeowsTorment:
                details.Add(CreateCardDetail(AddedCardLabel, "NEOWS_FURY"));
                return;

            case NeowOptionIds.CursedPearl:
                details.Add(CreateCardDetail(AddedCurseLabel, "GREED"));
                return;

            case NeowOptionIds.HeftyTablet:
                details.AddRange(BuildHeftyTabletPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.LargeCapsule:
                details.AddRange(BuildLargeCapsulePreview(state.Context, state.Context.Character, state.RelicBag, state.RewardsRng));
                return;

            case NeowOptionIds.ArcaneScroll:
                details.AddRange(BuildArcaneScrollPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.LeadPaperweight:
                details.AddRange(BuildLeadPaperweightPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.MassiveScroll:
                details.AddRange(BuildMassiveScrollPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.LostCoffer:
                details.AddRange(BuildLostCofferPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.ScrollBoxes:
                details.AddRange(BuildScrollBoxesPreview(state.Context, state.RewardsRng));
                return;

            case NeowOptionIds.PhialHolster:
                details.AddRange(BuildPhialHolsterPreview(state.Context, state.CombatPotionGenerationRng));
                return;

            case NeowOptionIds.NeowsTalisman:
                details.AddRange(BuildNeowsTalismanPreview(state.Context.Character));
                return;

            case NeowOptionIds.Pomander:
                details.AddRange(BuildPomanderPreview());
                return;

            case NeowOptionIds.SmallCapsule:
                details.AddRange(BuildSmallCapsulePreview(state.Context, state.RelicBag, state.RewardsRng));
                return;

            case NeowOptionIds.LeafyPoultice:
                details.AddRange(BuildLeafyPreview(state.Context, state.Context.Character, state.TransformationsRng));
                return;
        }
    }

    private GameRng CreatePlayerRewardsRng(NeowGenerationContext context)
    {
        var seed = unchecked(context.RunSeed + DefaultPlayerNetId);
        return new GameRng(seed, PlayerRewardsSalt);
    }

    private static GameRng CreateRunRng(NeowGenerationContext context, string name)
    {
        return new RunRngSet(context.RunSeed).Get(name);
    }

    private NeowRelicGrabBag CreateRelicGrabBag(NeowGenerationContext context)
    {
        var rng = CreateRunRng(context, "up_front");
        if (_relicPools.TryGetValue("Shared", out var sharedSequence))
        {
            ShuffleRelicBuckets(BuildRelicBuckets(sharedSequence), rng);
        }

        rng.FastForward(rng.Counter + 1);

        Dictionary<RelicRarity, List<string>>? playerBuckets = null;
        var characterKey = context.Character.ToString();
        var shared = _relicPools.TryGetValue("Shared", out sharedSequence) ? sharedSequence : Array.Empty<string>();
        var character = _relicPools.TryGetValue(characterKey, out var characterSequence) ? characterSequence : Array.Empty<string>();

        for (var i = 0; i < Math.Max(1, context.PlayerCount); i++)
        {
            var combined = new List<string>(shared.Count + character.Count);
            combined.AddRange(shared);
            combined.AddRange(character);
            playerBuckets = BuildRelicBuckets(combined);
            ShuffleRelicBuckets(playerBuckets, rng);
        }

        return new NeowRelicGrabBag(playerBuckets ?? new Dictionary<RelicRarity, List<string>>());
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

        var activeCount = candidates.Count;
        var resultCount = Math.Min(count, activeCount);
        var results = new List<string>(resultCount);

        for (var i = 0; i < resultCount && activeCount > 0; i++)
        {
            var pick = oddsType == CardRarityOddsType.Uniform
                ? PickUniformCard(rng, candidates, activeCount)
                : PickWeightedCard(rng, candidates, activeCount, oddsType, scarcityActive);

            if (pick == null)
            {
                break;
            }

            results.Add(pick.Id);
            activeCount--;
            candidates[pick.Index] = candidates[activeCount];

            if (simulateUpgradeRoll)
            {
                SimulateUpgradeRoll(pick.Metadata, rng);
            }
        }

        return results;
    }

    private PickedCard? PickUniformCard(GameRng rng, List<CardCandidate> candidates, int activeCount)
    {
        var eligibleCount = 0;
        for (var i = 0; i < activeCount; i++)
        {
            var rarity = candidates[i].Metadata.ParsedRarity;
            if (rarity != CardRarity.Basic && rarity != CardRarity.Ancient)
            {
                eligibleCount++;
            }
        }

        if (eligibleCount > 0)
        {
            var target = rng.NextInt(eligibleCount);
            for (var i = 0; i < activeCount; i++)
            {
                var rarity = candidates[i].Metadata.ParsedRarity;
                if (rarity == CardRarity.Basic || rarity == CardRarity.Ancient)
                {
                    continue;
                }

                if (target-- == 0)
                {
                    return new PickedCard(i, candidates[i]);
                }
            }
        }

        if (activeCount == 0)
        {
            return null;
        }

        var fallbackIndex = rng.NextInt(activeCount);
        return new PickedCard(fallbackIndex, candidates[fallbackIndex]);
    }

    private PickedCard? PickWeightedCard(
        GameRng rng,
        List<CardCandidate> candidates,
        int activeCount,
        CardRarityOddsType oddsType,
        bool scarcityActive)
    {
        var hasCommon = false;
        var hasUncommon = false;
        var hasRare = false;
        for (var i = 0; i < activeCount; i++)
        {
            switch (candidates[i].Metadata.ParsedRarity)
            {
                case CardRarity.Common:
                    hasCommon = true;
                    break;
                case CardRarity.Uncommon:
                    hasUncommon = true;
                    break;
                case CardRarity.Rare:
                    hasRare = true;
                    break;
            }
        }

        if (!hasCommon && !hasUncommon && !hasRare)
        {
            return PickUniformCard(rng, candidates, activeCount);
        }

        var rarity = RollCardRarity(rng, oddsType, scarcityActive);
        var guard = 0;
        while (!HasRarity(rarity, hasCommon, hasUncommon, hasRare) && rarity != CardRarity.None && guard++ < 3)
        {
            rarity = GetNextHighestRarity(rarity);
        }

        if (rarity == CardRarity.None)
        {
            rarity = hasCommon
                ? CardRarity.Common
                : hasUncommon
                    ? CardRarity.Uncommon
                    : CardRarity.Rare;
        }

        var rarityCount = 0;
        for (var i = 0; i < activeCount; i++)
        {
            if (candidates[i].Metadata.ParsedRarity == rarity)
            {
                rarityCount++;
            }
        }

        if (rarityCount == 0)
        {
            return PickUniformCard(rng, candidates, activeCount);
        }

        var target = rng.NextInt(rarityCount);
        for (var i = 0; i < activeCount; i++)
        {
            if (candidates[i].Metadata.ParsedRarity != rarity)
            {
                continue;
            }

            if (target-- == 0)
            {
                return new PickedCard(i, candidates[i]);
            }
        }

        return null;
    }

    internal static CardRarity RollCardRarity(GameRng rng, CardRarityOddsType oddsType, bool scarcityActive)
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

    internal static (double Common, double Uncommon, double Rare) GetRarityOdds(CardRarityOddsType oddsType, bool scarcityActive)
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

    internal static RelicRarity RollRelicRarity(GameRng rng)
    {
        var roll = rng.NextFloat();
        if (roll < 0.5f)
        {
            return RelicRarity.Common;
        }

        if (roll < 0.83f)
        {
            return RelicRarity.Uncommon;
        }

        return RelicRarity.Rare;
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Basic => CardRarity.Common,
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.None
        };

    private Dictionary<RelicRarity, List<string>> BuildRelicBuckets(IEnumerable<string> relicIds)
    {
        var result = new Dictionary<RelicRarity, List<string>>();
        foreach (var relicId in relicIds)
        {
            if (!_relicMetadata.TryGetValue(relicId, out var metadata))
            {
                continue;
            }

            var rarity = metadata.ParsedRarity;
            if (rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
            {
                continue;
            }

            if (!result.TryGetValue(rarity, out var list))
            {
                list = new List<string>();
                result[rarity] = list;
            }

            list.Add(relicId);
        }

        return result;
    }

    private static void ShuffleRelicBuckets(Dictionary<RelicRarity, List<string>> buckets, GameRng rng)
    {
        foreach (var list in buckets.Values)
        {
            rng.Shuffle(list);
        }
    }

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
        return RollPotions(rng, character, 1).FirstOrDefault();
    }

    private IReadOnlyList<string> RollPotions(GameRng rng, CharacterId character, int count)
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
            .Select(tuple => new PotionCandidate(tuple.id, tuple.metadata!))
            .ToList();

        if (candidates.Count == 0 || count <= 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            if (candidates.Count == 0)
            {
                break;
            }

            var rarity = RollPotionRarity(rng);
            var options = candidates.Where(candidate => candidate.Metadata.ParsedRarity == rarity).ToList();
            if (options.Count == 0)
            {
                options = candidates;
            }

            var pick = options[rng.NextInt(options.Count)];
            results.Add(pick.Id);
            candidates.RemoveAll(candidate => string.Equals(candidate.Id, pick.Id, StringComparison.OrdinalIgnoreCase));
        }

        return results;
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

    private static bool IsNeowsBonesRelicAllowed(string relicId, NeowGenerationContext context)
    {
        if (string.Equals(relicId, NeowOptionIds.MassiveScroll, StringComparison.OrdinalIgnoreCase))
        {
            return context.PlayerCount > 1;
        }

        if (string.Equals(relicId, NeowOptionIds.SilverCrucible, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relicId, NeowOptionIds.WingedBoots, StringComparison.OrdinalIgnoreCase))
        {
            return context.PlayerCount <= 1;
        }

        return true;
    }

    private string? RollModifierGeneratedCurse(GameRng rng)
    {
        var options = ModifierGeneratedCurseIds
            .Where(_cardLookup.ContainsKey)
            .ToList();
        if (options.Count == 0)
        {
            return null;
        }

        return options[rng.NextInt(options.Count)];
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
            for (var commonIndex = 0; commonIndex < 2; commonIndex++)
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

    private static bool HasRarity(CardRarity rarity, bool hasCommon, bool hasUncommon, bool hasRare) =>
        rarity switch
        {
            CardRarity.Common => hasCommon,
            CardRarity.Uncommon => hasUncommon,
            CardRarity.Rare => hasRare,
            _ => false
        };

    private sealed record CardCandidate(string Id, NeowCardMetadata Metadata);

    private sealed record PickedCard(int Index, CardCandidate Candidate)
    {
        public string Id => Candidate.Id;

        public NeowCardMetadata Metadata => Candidate.Metadata;
    }

    private sealed record PotionCandidate(string Id, NeowPotionMetadata Metadata);
}
