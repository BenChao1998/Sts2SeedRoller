using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Collections;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Sts2.Generation;

namespace SeedModel.Sts2;

internal sealed class Sts2RelicVisibilityAnalyzer
{
    private static readonly HashSet<string> ShopBlockedRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "AMETHYST_AUBERGINE",
        "BOWLER_HAT",
        "LUCKY_FYSH",
        "OLD_COIN",
        "THE_COURIER"
    };

    private static readonly HashSet<string> BeforeAct3TreasureChestRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "AMETHYST_AUBERGINE",
        "BOOK_OF_FIVE_RINGS",
        "BOWLER_HAT",
        "DRAGON_FRUIT",
        "FROZEN_EGG",
        "GIRYA",
        "JUZU_BRACELET",
        "LASTING_CANDY",
        "LUCKY_FYSH",
        "MEAL_TICKET",
        "MOLTEN_EGG",
        "OLD_COIN",
        "PLANISPHERE",
        "SHOVEL",
        "TOXIC_EGG",
        "WHITE_BEAST_STATUE",
        "WHITE_STAR"
    };

    private static readonly HashSet<string> SinglePlayerOnlyRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "SILVER_CRUCIBLE",
        "WINGED_BOOTS"
    };

    private static readonly HashSet<string> MultiplayerOnlyRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "MASSIVE_SCROLL"
    };

    private readonly Sts2WorldData _world;

    internal Sts2RelicVisibilityAnalyzer(Sts2WorldData world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    internal Sts2RelicVisibilityAnalysis Analyze(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs,
        IReadOnlyDictionary<string, string> rarityMap)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ancientActs);
        ArgumentNullException.ThrowIfNull(rarityMap);

        if (request.Samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Samples must be positive.");
        }

        if (request.EarlyWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Early window must be positive.");
        }

        var playerCount = Math.Max(1, request.PlayerCount);
        var ancientAvailability = request.ResolveAncientAvailability();
        var baseline = BaselineState.Create(
            _world.RelicPools,
            rarityMap,
            request.SeedText,
            request.SeedValue,
            request.Character,
            playerCount,
            request.AscensionLevel,
            ancientAvailability);
        var rewardModel = RewardSimulationModel.Create(dataset, request.Character, playerCount);
        var ancientMap = ancientActs.ToDictionary(act => act.ActNumber, act => act, EqualityComparer<int>.Default);

        var profileResults = RouteProfile.All
            .Select(profile => RunProfile(request, profile, baseline, rewardModel, ancientMap))
            .ToList();

        return new Sts2RelicVisibilityAnalysis
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            Character = request.Character,
            PlayerCount = playerCount,
            Samples = request.Samples,
            EarlyWindow = request.EarlyWindow,
            SharedBagSize = baseline.SharedBag.TotalCount,
            PlayerBagSize = baseline.PlayerBag.TotalCount,
            Act3OnlyGateTrackedRelics = BeforeAct3TreasureChestRelics.Count,
            AncientActs = ancientActs.OrderBy(act => act.ActNumber).ToList(),
            Profiles = profileResults
        };
    }

    internal bool MatchesHighProbabilityRelics(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs,
        IReadOnlyDictionary<string, string> rarityMap,
        Sts2PoolFilter filter)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ancientActs);
        ArgumentNullException.ThrowIfNull(rarityMap);
        ArgumentNullException.ThrowIfNull(filter);

        var requiredRelicIds = filter.HighProbabilityRelicIds;
        var targetRelics = requiredRelicIds
            .Where(relicId => !string.IsNullOrWhiteSpace(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetRelics.Count == 0)
        {
            return true;
        }

        if (request.Samples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Samples must be positive.");
        }

        var playerCount = Math.Max(1, request.PlayerCount);
        var ancientAvailability = request.ResolveAncientAvailability();
        var baseline = BaselineState.Create(
            _world.RelicPools,
            rarityMap,
            request.SeedText,
            request.SeedValue,
            request.Character,
            playerCount,
            request.AscensionLevel,
            ancientAvailability);
        var rewardModel = RewardSimulationModel.Create(dataset, request.Character, playerCount);
        var ancientMap = ancientActs.ToDictionary(act => act.ActNumber, act => act, EqualityComparer<int>.Default);
        var matchedRelics = targetRelics.ToDictionary(relicId => relicId, _ => false, StringComparer.OrdinalIgnoreCase);

        foreach (var profile in RouteProfile.All)
        {
            var profileRelics = RunTargetedProfile(request, profile, baseline, rewardModel, ancientMap, targetRelics);
            foreach (var relicId in targetRelics)
            {
                if (matchedRelics[relicId])
                {
                    continue;
                }

                if (profileRelics.TryGetValue(relicId, out var relic) &&
                    filter.MatchesHighProbabilityRelic(relic))
                {
                    matchedRelics[relicId] = true;
                }
            }

            if (targetRelics.All(relicId => matchedRelics[relicId]))
            {
                return true;
            }
        }

        return targetRelics.All(relicId => matchedRelics[relicId]);
    }

    private Sts2RelicVisibilityProfileResult RunProfile(
        Sts2RelicVisibilityRequest request,
        RouteProfile profile,
        BaselineState baseline,
        RewardSimulationModel rewardModel,
        IReadOnlyDictionary<int, Sts2RelicVisibilityAncientAct> ancientActs)
    {
        var routeRng = new GameRng(request.SeedValue, $"relic_visibility_{profile.Id}");
        var stats = new Dictionary<string, AppearanceStats>(StringComparer.OrdinalIgnoreCase);
        var earlySamples = new List<IReadOnlyList<string>>(capacity: 3);

        for (var sample = 0; sample < request.Samples; sample++)
        {
            var state = baseline.Clone();
            var sampleSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSource = new Dictionary<string, Sts2RelicVisibilitySource>(StringComparer.OrdinalIgnoreCase);
            var sampleSourcePresence = new Dictionary<string, SourcePresence>(StringComparer.OrdinalIgnoreCase);
            var earliestThisSample = new List<string>();

            var opportunities = BuildOpportunities(routeRng, profile);
            var currentAct = 0;
            foreach (var opportunity in opportunities)
            {
                if (currentAct != opportunity.ActNumber)
                {
                    currentAct = opportunity.ActNumber;
                }
                else
                {
                    var actProfile = profile.Acts[opportunity.ActNumber - 1];
                    var regularCombats = actProfile.BetweenOpportunityCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        ConsumeRegularCombat(state, rewardModel);
                    }
                }

                var shownRelics = opportunity.Kind switch
                {
                    OpportunityKind.Treasure => ShowTreasure(state, opportunity.ActNumber),
                    OpportunityKind.Elite => ShowElite(state, opportunity.ActNumber, rewardModel),
                    OpportunityKind.Shop => ShowShop(state, opportunity.ActNumber),
                    OpportunityKind.Ancient => ShowAncient(ancientActs, opportunity.ActNumber),
                    _ => Array.Empty<ShownRelic>()
                };

                foreach (var shown in shownRelics)
                {
                    if (!sampleSourcePresence.TryGetValue(shown.RelicId, out var presence))
                    {
                        presence = new SourcePresence();
                        sampleSourcePresence[shown.RelicId] = presence;
                    }

                    if (shown.Source == Sts2RelicVisibilitySource.Shop)
                    {
                        presence.ShopSeen = true;
                    }
                    else
                    {
                        presence.NonShopSeen = true;
                    }

                    if (!sampleSeen.Add(shown.RelicId))
                    {
                        continue;
                    }

                    sampleFirstSeen[shown.RelicId] = opportunity.GlobalIndex;
                    sampleFirstSource[shown.RelicId] = shown.Source;
                    if (opportunity.GlobalIndex <= request.EarlyWindow)
                    {
                        earliestThisSample.Add(shown.RelicId);
                    }
                }
            }

            if (earlySamples.Count < 3)
            {
                earlySamples.Add(earliestThisSample);
            }

            foreach (var (relicId, firstIndex) in sampleFirstSeen)
            {
                if (!stats.TryGetValue(relicId, out var relicStats))
                {
                    relicStats = new AppearanceStats();
                    stats[relicId] = relicStats;
                }

                relicStats.SeenCount++;
                relicStats.FirstOpportunityTotal += firstIndex;
                if (firstIndex <= request.EarlyWindow)
                {
                    relicStats.EarlyCount++;
                }

                if (sampleSourcePresence.TryGetValue(relicId, out var presence))
                {
                    if (presence.NonShopSeen)
                    {
                        relicStats.NonShopSeenCount++;
                    }

                    if (presence.ShopSeen)
                    {
                        relicStats.ShopSeenCount++;
                    }
                }

                relicStats.FirstSourceCounts[sampleFirstSource[relicId]] =
                    relicStats.FirstSourceCounts.GetValueOrDefault(sampleFirstSource[relicId]) + 1;
            }
        }

        var ranked = stats
            .Select(pair => ToRankedRelic(pair.Key, pair.Value, request.Samples))
            .OrderByDescending(item => item.EarlyProbability)
            .ThenByDescending(item => item.SeenProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenBy(item => item.RelicId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seenRanked = ranked
            .OrderByDescending(item => item.SeenProbability)
            .ThenByDescending(item => item.EarlyProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenBy(item => item.RelicId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Sts2RelicVisibilityProfileResult
        {
            Id = profile.Id,
            Title = profile.Title,
            Description = profile.Description,
            Acts = profile.Acts
                .Select((act, index) => act.ToModel(index + 1))
                .ToList(),
            EarlyRelics = ranked,
            SeenRelics = seenRanked,
            EarlySamples = earlySamples
        };
    }

    private Dictionary<string, Sts2RelicVisibilityRankedRelic> RunTargetedProfile(
        Sts2RelicVisibilityRequest request,
        RouteProfile profile,
        BaselineState baseline,
        RewardSimulationModel rewardModel,
        IReadOnlyDictionary<int, Sts2RelicVisibilityAncientAct> ancientActs,
        IReadOnlyList<string> targetRelics)
    {
        var routeRng = new GameRng(request.SeedValue, $"relic_visibility_{profile.Id}");
        var targetSet = targetRelics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stats = targetRelics.ToDictionary(
            relicId => relicId,
            _ => new AppearanceStats(),
            StringComparer.OrdinalIgnoreCase);

        for (var sample = 0; sample < request.Samples; sample++)
        {
            var state = baseline.Clone();
            var sampleSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sampleSourcePresence = new Dictionary<string, SourcePresence>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSource = new Dictionary<string, Sts2RelicVisibilitySource>(StringComparer.OrdinalIgnoreCase);
            var opportunities = BuildOpportunities(routeRng, profile);
            var currentAct = 0;

            foreach (var opportunity in opportunities)
            {
                if (currentAct != opportunity.ActNumber)
                {
                    currentAct = opportunity.ActNumber;
                }
                else
                {
                    var actProfile = profile.Acts[opportunity.ActNumber - 1];
                    var regularCombats = actProfile.BetweenOpportunityCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        ConsumeRegularCombat(state, rewardModel);
                    }
                }

                var shownRelics = opportunity.Kind switch
                {
                    OpportunityKind.Treasure => ShowTreasure(state, opportunity.ActNumber),
                    OpportunityKind.Elite => ShowElite(state, opportunity.ActNumber, rewardModel),
                    OpportunityKind.Shop => ShowShop(state, opportunity.ActNumber),
                    OpportunityKind.Ancient => ShowAncient(ancientActs, opportunity.ActNumber),
                    _ => Array.Empty<ShownRelic>()
                };

                foreach (var shown in shownRelics)
                {
                    if (!targetSet.Contains(shown.RelicId))
                    {
                        continue;
                    }

                    if (!sampleSourcePresence.TryGetValue(shown.RelicId, out var presence))
                    {
                        presence = new SourcePresence();
                        sampleSourcePresence[shown.RelicId] = presence;
                    }

                    if (shown.Source == Sts2RelicVisibilitySource.Shop)
                    {
                        presence.ShopSeen = true;
                    }
                    else
                    {
                        presence.NonShopSeen = true;
                    }

                    if (!sampleSeen.Add(shown.RelicId))
                    {
                        continue;
                    }

                    sampleFirstSeen[shown.RelicId] = opportunity.GlobalIndex;
                    sampleFirstSource[shown.RelicId] = shown.Source;

                    if (sampleSeen.Count == targetSet.Count)
                    {
                        break;
                    }
                }

                if (sampleSeen.Count == targetSet.Count)
                {
                    break;
                }
            }

            foreach (var relicId in targetRelics)
            {
                var relicStats = stats[relicId];

                if (sampleFirstSeen.TryGetValue(relicId, out var firstIndex))
                {
                    relicStats.SeenCount++;
                    relicStats.FirstOpportunityTotal += firstIndex;
                    if (firstIndex <= request.EarlyWindow)
                    {
                        relicStats.EarlyCount++;
                    }

                    var firstSource = sampleFirstSource[relicId];
                    relicStats.FirstSourceCounts[firstSource] =
                        relicStats.FirstSourceCounts.TryGetValue(firstSource, out var count)
                            ? count + 1
                            : 1;
                }

                if (sampleSourcePresence.TryGetValue(relicId, out var sourcePresence))
                {
                    if (sourcePresence.NonShopSeen)
                    {
                        relicStats.NonShopSeenCount++;
                    }

                    if (sourcePresence.ShopSeen)
                    {
                        relicStats.ShopSeenCount++;
                    }
                }
            }
        }

        return targetRelics.ToDictionary(
            relicId => relicId,
            relicId => ToRankedRelic(relicId, stats[relicId], request.Samples),
            StringComparer.OrdinalIgnoreCase);
    }

    private List<Opportunity> BuildOpportunities(GameRng rng, RouteProfile profile)
    {
        var result = new List<Opportunity>();
        var globalIndex = 0;
        for (var actNumber = 1; actNumber <= profile.Acts.Count; actNumber++)
        {
            var actProfile = profile.Acts[actNumber - 1];
            var remaining = new Dictionary<OpportunityKind, int>
            {
                [OpportunityKind.Treasure] = actProfile.TreasureCounts.Sample(rng),
                [OpportunityKind.Elite] = actProfile.EliteCounts.Sample(rng),
                [OpportunityKind.Shop] = actProfile.ShopCounts.Sample(rng),
                [OpportunityKind.Ancient] = actNumber == 1
                    ? 0
                    : (rng.NextDouble() < actProfile.AncientVisitChance ? 1 : 0)
            };

            var slotVariant = actProfile.SlotVariants[rng.NextInt(actProfile.SlotVariants.Count)];
            foreach (var kind in slotVariant)
            {
                if (!remaining.TryGetValue(kind, out var count) || count <= 0)
                {
                    continue;
                }

                remaining[kind] = count - 1;
                globalIndex++;
                result.Add(new Opportunity(actNumber, kind, globalIndex));
            }

            foreach (var kind in new[] { OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure })
            {
                while (remaining.GetValueOrDefault(kind) > 0)
                {
                    remaining[kind]--;
                    globalIndex++;
                    result.Add(new Opportunity(actNumber, kind, globalIndex));
                }
            }
        }

        return result;
    }

    private IReadOnlyList<ShownRelic> ShowTreasure(BaselineState state, int actNumber)
    {
        var rarity = RollRelicRarity(state.TreasureRng);
        var relic = state.SharedBag.PullFromFront(rarity, relicId => IsRelicAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.PlayerBag.Remove(relic);
        return [new ShownRelic(relic, Sts2RelicVisibilitySource.Treasure)];
    }

    private IReadOnlyList<ShownRelic> ShowElite(BaselineState state, int actNumber, RewardSimulationModel rewardModel)
    {
        ConsumeEliteCombatPreRelic(state, rewardModel);
        var rarity = RollRelicRarity(state.RewardsRng);
        var relic = state.PlayerBag.PullFromFront(rarity, relicId => IsRelicAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.SharedBag.Remove(relic);
        return [new ShownRelic(relic, Sts2RelicVisibilitySource.Elite)];
    }

    private IReadOnlyList<ShownRelic> ShowShop(BaselineState state, int actNumber)
    {
        ConsumeShopPreRelicRewards(state);
        var shown = new List<ShownRelic>(3);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rarity in new[] { RollRelicRarity(state.RewardsRng), RollRelicRarity(state.RewardsRng), RelicRarity.Shop })
        {
            var relic = state.PlayerBag.PullFromBack(
                rarity,
                selected,
                ShopBlockedRelics,
                relicId => IsRelicAllowed(relicId, actNumber, state.PlayerCount));
            if (relic == null)
            {
                continue;
            }

            selected.Add(relic);
            state.SharedBag.Remove(relic);
            shown.Add(new ShownRelic(relic, Sts2RelicVisibilitySource.Shop));
        }

        return shown;
    }

    private static IReadOnlyList<ShownRelic> ShowAncient(
        IReadOnlyDictionary<int, Sts2RelicVisibilityAncientAct> ancientActs,
        int actNumber)
    {
        if (!ancientActs.TryGetValue(actNumber, out var act))
        {
            return Array.Empty<ShownRelic>();
        }

        var source = actNumber == 2
            ? Sts2RelicVisibilitySource.AncientAct2
            : Sts2RelicVisibilitySource.AncientAct3;

        return act.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
            .Select(option => new ShownRelic(option.RelicId, source))
            .ToList();
    }

    private static void ConsumeRegularCombat(BaselineState state, RewardSimulationModel rewardModel)
    {
        _ = state.RewardsRng.NextInt(10, 21);
        if (RollPotionRewardChance(state, isElite: false))
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.RegularEncounter);
        }
    }

    private static void ConsumeEliteCombatPreRelic(BaselineState state, RewardSimulationModel rewardModel)
    {
        _ = state.RewardsRng.NextInt(25, 36);
        if (RollPotionRewardChance(state, isElite: true))
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.EliteEncounter);
        }
    }

    private static void ConsumeShopPreRelicRewards(BaselineState state)
    {
        for (var i = 0; i < 5; i++)
        {
            _ = state.RewardsRng.NextFloat();
            _ = state.RewardsRng.NextFloat();
        }

        for (var i = 0; i < 2; i++)
        {
            _ = state.RewardsRng.NextFloat();
        }
    }

    private static bool RollPotionRewardChance(BaselineState state, bool isElite)
    {
        var current = state.PotionChance;
        var roll = state.RewardsRng.NextFloat();
        if (roll < current)
        {
            state.PotionChance -= 0.1f;
        }
        else
        {
            state.PotionChance += 0.1f;
        }

        var eliteBonus = isElite ? 0.125f : 0f;
        return roll < current + eliteBonus;
    }

    private static void RollPotionReward(BaselineState state, RewardSimulationModel rewardModel)
    {
        var rarity = RollPotionRarity(state.RewardsRng);
        if (!rewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) || pool.Length == 0)
        {
            pool = rewardModel.PotionPool.ToArray();
        }

        if (pool.Length > 0)
        {
            _ = state.RewardsRng.NextItem(pool);
        }
    }

    private static void RollCombatRewardCard(BaselineState state, RewardSimulationModel rewardModel, CardRarityOddsType oddsType)
    {
        if (rewardModel.CardPool.Count == 0)
        {
            return;
        }

        var rarity = RollCardRarity(state, oddsType);
        var candidates = GetAvailableCards(rewardModel, rarity, state.CurrentRewardCards);
        while (candidates.Count == 0)
        {
            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return;
            }

            candidates = GetAvailableCards(rewardModel, rarity, state.CurrentRewardCards);
        }

        var cardId = state.RewardsRng.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        state.CurrentRewardCards.Add(cardId);
        _ = state.RewardsRng.NextDouble();
        if (state.CurrentRewardCards.Count >= 3)
        {
            state.CurrentRewardCards.Clear();
        }
    }

    private static CardRarity RollCardRarity(BaselineState state, CardRarityOddsType oddsType)
    {
        var roll = state.RewardsRng.NextFloat();
        var rareOdds = GetBaseCardOdds(oddsType, CardRarity.Rare, state.AscensionLevel) + state.CardRareOffset;
        CardRarity rarity;
        if (roll < rareOdds)
        {
            rarity = CardRarity.Rare;
        }
        else if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon, state.AscensionLevel) + rareOdds)
        {
            rarity = CardRarity.Uncommon;
        }
        else
        {
            rarity = CardRarity.Common;
        }

        if (rarity == CardRarity.Rare)
        {
            state.CardRareOffset = -0.05f;
        }
        else
        {
            state.CardRareOffset = Math.Min(
                state.CardRareOffset + (state.AscensionLevel >= 7 ? 0.005f : 0.01f),
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
            CardRarityOddsType.BossEncounter => rarity switch
            {
                CardRarity.Common => 0f,
                CardRarity.Uncommon => 0f,
                CardRarity.Rare => 1f,
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

    private static List<string> GetAvailableCards(
        RewardSimulationModel rewardModel,
        CardRarity rarity,
        ISet<string> excluded)
    {
        if (!rewardModel.CardPoolByRarity.TryGetValue(rarity, out var pool))
        {
            return [];
        }

        return pool.Where(cardId => !excluded.Contains(cardId)).ToList();
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
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

    private static RelicRarity RollRelicRarity(GameRng rng)
    {
        var value = rng.NextFloat();
        if (value < 0.5f)
        {
            return RelicRarity.Common;
        }

        if (value < 0.83f)
        {
            return RelicRarity.Uncommon;
        }

        return RelicRarity.Rare;
    }

    private static bool IsRelicAllowed(string relicId, int actNumber, int playerCount)
    {
        if (BeforeAct3TreasureChestRelics.Contains(relicId) && actNumber >= 3)
        {
            return false;
        }

        if (playerCount <= 1 && MultiplayerOnlyRelics.Contains(relicId))
        {
            return false;
        }

        if (playerCount > 1 && SinglePlayerOnlyRelics.Contains(relicId))
        {
            return false;
        }

        return true;
    }

    private static Sts2RelicVisibilityRankedRelic ToRankedRelic(string relicId, AppearanceStats stats, int totalSamples)
    {
        var mostCommonSource = stats.FirstSourceCounts.Count == 0
            ? Sts2RelicVisibilitySource.Treasure
            : stats.FirstSourceCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First()
                .Key;

        return new Sts2RelicVisibilityRankedRelic
        {
            RelicId = relicId,
            EarlyProbability = (double)stats.EarlyCount / totalSamples,
            SeenProbability = (double)stats.SeenCount / totalSamples,
            NonShopSeenProbability = (double)stats.NonShopSeenCount / totalSamples,
            ShopSeenProbability = (double)stats.ShopSeenCount / totalSamples,
            AverageFirstOpportunity = stats.SeenCount == 0 ? double.PositiveInfinity : stats.FirstOpportunityTotal / stats.SeenCount,
            MostCommonSource = mostCommonSource
        };
    }

    private sealed class AppearanceStats
    {
        public int SeenCount { get; set; }

        public int EarlyCount { get; set; }

        public int NonShopSeenCount { get; set; }

        public int ShopSeenCount { get; set; }

        public double FirstOpportunityTotal { get; set; }

        public Dictionary<Sts2RelicVisibilitySource, int> FirstSourceCounts { get; } = new();
    }

    private sealed record Opportunity(int ActNumber, OpportunityKind Kind, int GlobalIndex);

    private sealed record ShownRelic(string RelicId, Sts2RelicVisibilitySource Source);

    private sealed class SourcePresence
    {
        public bool NonShopSeen { get; set; }

        public bool ShopSeen { get; set; }
    }

    private enum OpportunityKind
    {
        Treasure,
        Elite,
        Shop,
        Ancient
    }

    private sealed class RewardSimulationModel
    {
        private RewardSimulationModel(
            IReadOnlyList<string> cardPool,
            IReadOnlyDictionary<CardRarity, string[]> cardPoolByRarity,
            IReadOnlyList<string> potionPool,
            IReadOnlyDictionary<PotionRarity, string[]> potionPoolByRarity)
        {
            CardPool = cardPool;
            CardPoolByRarity = cardPoolByRarity;
            PotionPool = potionPool;
            PotionPoolByRarity = potionPoolByRarity;
        }

        public IReadOnlyList<string> CardPool { get; }

        public IReadOnlyDictionary<CardRarity, string[]> CardPoolByRarity { get; }

        public IReadOnlyList<string> PotionPool { get; }

        public IReadOnlyDictionary<PotionRarity, string[]> PotionPoolByRarity { get; }

        public static RewardSimulationModel Create(NeowOptionDataset dataset, CharacterId character, int playerCount)
        {
            var cardPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
                ? characterCards
                    .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                     IsCardAllowed(metadata, playerCount))
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

            potionPool = potionPool
                .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var potionPoolByRarity = potionPool
                .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());

            return new RewardSimulationModel(
                cardPool,
                cardPoolByRarity,
                potionPool,
                potionPoolByRarity);
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

            return metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
        }
    }

    private sealed class BaselineState
    {
        private const uint DefaultPlayerNetId = 1;

        private BaselineState(
            string seedText,
            CharacterId character,
            int playerCount,
            int ascensionLevel,
            uint runSeed,
            uint playerSeed,
            RelicBag sharedBag,
            RelicBag playerBag,
            GameRng treasureRng,
            GameRng rewardsRng,
            float cardRareOffset,
            float potionChance,
            HashSet<string> currentRewardCards)
        {
            SeedText = seedText;
            Character = character;
            PlayerCount = playerCount;
            AscensionLevel = ascensionLevel;
            RunSeed = runSeed;
            PlayerSeed = playerSeed;
            SharedBag = sharedBag;
            PlayerBag = playerBag;
            TreasureRng = treasureRng;
            RewardsRng = rewardsRng;
            CardRareOffset = cardRareOffset;
            PotionChance = potionChance;
            CurrentRewardCards = currentRewardCards;
        }

        public string SeedText { get; }

        public CharacterId Character { get; }

        public int PlayerCount { get; }

        public int AscensionLevel { get; }

        public uint RunSeed { get; }

        public uint PlayerSeed { get; }

        public RelicBag SharedBag { get; }

        public RelicBag PlayerBag { get; }

        public GameRng TreasureRng { get; }

        public GameRng RewardsRng { get; }

        public float CardRareOffset { get; set; }

        public float PotionChance { get; set; }

        public HashSet<string> CurrentRewardCards { get; }

        public BaselineState Clone()
        {
            return new BaselineState(
                SeedText,
                Character,
                PlayerCount,
                AscensionLevel,
                RunSeed,
                PlayerSeed,
                SharedBag.Clone(),
                PlayerBag.Clone(),
                new GameRng(TreasureRng.Seed, TreasureRng.Counter),
                new GameRng(RewardsRng.Seed, RewardsRng.Counter),
                CardRareOffset,
                PotionChance,
                new HashSet<string>(CurrentRewardCards, StringComparer.OrdinalIgnoreCase));
        }

        public static BaselineState Create(
            Sts2WorldData.RelicPoolInfo pools,
            IReadOnlyDictionary<string, string> rarityMap,
            string seedText,
            uint runSeed,
            CharacterId character,
            int playerCount,
            int ascensionLevel,
            Sts2AncientAvailability availability)
        {
            var upFrontRng = new GameRng(runSeed, "up_front");
            var sharedSequence = pools.GetSharedSequence(availability);
            var playerSequence = pools.GetCombinedSequence(character, availability);

            // Match the run creation order from the game: shuffle every unlocked
            // shared relic rarity first, then immediately shuffle only the
            // tracked gameplay rarities for the player's combined grab bag.
            var sharedBag = RelicBag.CreateFromSequence(sharedSequence, rarityMap, upFrontRng, trackedOnly: false);
            var playerBag = RelicBag.CreateFromSequence(playerSequence, rarityMap, upFrontRng, trackedOnly: true);

            var playerSeed = unchecked(runSeed + DefaultPlayerNetId);
            return new BaselineState(
                seedText,
                character,
                playerCount,
                ascensionLevel,
                runSeed,
                playerSeed,
                sharedBag,
                playerBag,
                new GameRng(runSeed, "treasure_room_relics"),
                new GameRng(playerSeed, "rewards"),
                -0.05f,
                0.4f,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class RelicBag
    {
        private readonly Dictionary<RelicRarity, List<string>> _deques;

        private RelicBag(Dictionary<RelicRarity, List<string>> deques)
        {
            _deques = deques;
        }

        public int TotalCount => _deques.Values.Sum(list => list.Count);

        public static RelicBag CreateFromSequence(
            IReadOnlyList<string> sequence,
            IReadOnlyDictionary<string, string> rarityMap,
            GameRng rng,
            bool trackedOnly)
        {
            var buckets = new Dictionary<RelicRarity, List<string>>();
            foreach (var relicId in sequence)
            {
                if (string.IsNullOrWhiteSpace(relicId) ||
                    !rarityMap.TryGetValue(relicId, out var rarityText) ||
                    !Enum.TryParse<RelicRarity>(rarityText, ignoreCase: true, out var rarity))
                {
                    continue;
                }

                if (trackedOnly &&
                    rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
                {
                    continue;
                }

                if (!buckets.TryGetValue(rarity, out var list))
                {
                    list = new List<string>();
                    buckets[rarity] = list;
                }

                list.Add(relicId);
            }

            foreach (var list in buckets.Values)
            {
                if (list.Count > 1)
                {
                    list.UnstableShuffle(rng);
                }
            }

            return new RelicBag(buckets);
        }

        public RelicBag Clone()
        {
            return new RelicBag(_deques.ToDictionary(entry => entry.Key, entry => entry.Value.ToList()));
        }

        public string? PullFromFront(RelicRarity rarity, Func<string, bool>? isAllowed = null)
        {
            if (isAllowed != null)
            {
                RemoveDisallowed(isAllowed);
            }

            RelicRarity? current = rarity;
            while (current is RelicRarity currentRarity)
            {
                if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
                {
                    var relic = list[0];
                    list.RemoveAt(0);
                    return relic;
                }

                current = current switch
                {
                    RelicRarity.Shop => RelicRarity.Common,
                    RelicRarity.Common => RelicRarity.Uncommon,
                    RelicRarity.Uncommon => RelicRarity.Rare,
                    _ => null
                };
            }

            return null;
        }

        public string? PullFromBack(
            RelicRarity rarity,
            IReadOnlySet<string>? selected = null,
            IReadOnlySet<string>? extraBlacklist = null,
            Func<string, bool>? isAllowed = null)
        {
            if (isAllowed != null)
            {
                RemoveDisallowed(isAllowed);
            }

            RelicRarity? current = rarity;
            while (current is RelicRarity currentRarity)
            {
                if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
                {
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        var relic = list[i];
                        if (selected != null && selected.Contains(relic))
                        {
                            continue;
                        }

                        if (extraBlacklist != null && extraBlacklist.Contains(relic))
                        {
                            continue;
                        }

                        list.RemoveAt(i);
                        return relic;
                    }
                }

                current = current switch
                {
                    RelicRarity.Shop => RelicRarity.Common,
                    RelicRarity.Common => RelicRarity.Uncommon,
                    RelicRarity.Uncommon => RelicRarity.Rare,
                    _ => null
                };
            }

            return null;
        }

        public void Remove(string relicId)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void RemoveDisallowed(Func<string, bool> isAllowed)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(item => !isAllowed(item));
            }
        }
    }

    private sealed class RouteProfile
    {
        private RouteProfile(
            string id,
            string title,
            string description,
            IReadOnlyList<ActRouteProfile> acts,
            double regularPotionChance,
            double elitePotionChance)
        {
            Id = id;
            Title = title;
            Description = description;
            Acts = acts;
            RegularPotionChance = regularPotionChance;
            ElitePotionChance = elitePotionChance;
        }

        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<ActRouteProfile> Acts { get; }

        public double RegularPotionChance { get; }

        public double ElitePotionChance { get; }

        public static IReadOnlyList<RouteProfile> All { get; } =
        [
            new RouteProfile(
                "balanced",
                "Balanced",
                "Balanced route: a moderate number of elites, a few shops, and a medium chance to visit ancients.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.25), (1, 0.60), (2, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.25), (1, 0.55), (2, 0.20)),
                        slotVariants:
                        [
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.55), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                        ancientVisitChance: 0.68,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.50), (2, 0.35)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.35), (1, 0.50), (2, 0.15)),
                        ancientVisitChance: 0.62,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.38,
                elitePotionChance: 0.58),
            new RouteProfile(
                "aggressive",
                "Aggressive",
                "Aggressive route: earlier elites, fewer shops, and a slightly lower ancient chance.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.45), (3, 0.20)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.60,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.40), (2, 0.50)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                            [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.35), (1, 0.20)),
                        shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                        ancientVisitChance: 0.56,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.35), (2, 0.55)),
                        slotVariants:
                        [
                            [OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.35,
                elitePotionChance: 0.60),
            new RouteProfile(
                "shopper",
                "Shopper",
                "Shop-heavy route: more shop visibility, fewer elites, and a slightly higher ancient chance.",
                [
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((2, 0.40), (3, 0.45), (1, 0.15)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.00,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                        slotVariants:
                        [
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.74,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ]),
                    new ActRouteProfile(
                        treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                        eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                        shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                        ancientVisitChance: 0.70,
                        betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                        slotVariants:
                        [
                            [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                            [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                        ])
                ],
                regularPotionChance: 0.40,
                elitePotionChance: 0.55)
        ];
    }

    private sealed class ActRouteProfile
    {
        public ActRouteProfile(
            WeightedIntDistribution treasureCounts,
            WeightedIntDistribution eliteCounts,
            WeightedIntDistribution shopCounts,
            double ancientVisitChance,
            WeightedIntDistribution betweenOpportunityCombats,
            IReadOnlyList<OpportunityKind[]> slotVariants)
        {
            TreasureCounts = treasureCounts;
            EliteCounts = eliteCounts;
            ShopCounts = shopCounts;
            AncientVisitChance = ancientVisitChance;
            BetweenOpportunityCombats = betweenOpportunityCombats;
            SlotVariants = slotVariants;
        }

        public WeightedIntDistribution TreasureCounts { get; }

        public WeightedIntDistribution EliteCounts { get; }

        public WeightedIntDistribution ShopCounts { get; }

        public double AncientVisitChance { get; }

        public WeightedIntDistribution BetweenOpportunityCombats { get; }

        public IReadOnlyList<OpportunityKind[]> SlotVariants { get; }

        public Sts2RelicVisibilityActSummary ToModel(int actNumber)
        {
            return new Sts2RelicVisibilityActSummary
            {
                ActNumber = actNumber,
                TreasureCounts = TreasureCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                EliteCounts = EliteCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                ShopCounts = ShopCounts.Options
                    .Select(option => new Sts2WeightedIntChance { Value = option.Value, Weight = option.Weight })
                    .ToList(),
                AncientVisitChance = AncientVisitChance
            };
        }
    }

    private sealed class WeightedIntDistribution
    {
        private WeightedIntDistribution(IReadOnlyList<WeightedIntOption> options)
        {
            Options = options;
            TotalWeight = options.Sum(option => option.Weight);
        }

        public IReadOnlyList<WeightedIntOption> Options { get; }

        public double TotalWeight { get; }

        public static WeightedIntDistribution Of(params (int Value, double Weight)[] options)
        {
            return new WeightedIntDistribution(options
                .Select(option => new WeightedIntOption(option.Value, option.Weight))
                .ToList());
        }

        public int Sample(GameRng rng)
        {
            var roll = rng.NextDouble() * TotalWeight;
            foreach (var option in Options)
            {
                roll -= option.Weight;
                if (roll <= 0)
                {
                    return option.Value;
                }
            }

            return Options[^1].Value;
        }
    }

    private sealed record WeightedIntOption(int Value, double Weight);
}
