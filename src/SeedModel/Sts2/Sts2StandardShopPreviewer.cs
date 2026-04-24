using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Collections;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Sts2.Generation;

namespace SeedModel.Sts2;

internal sealed class Sts2StandardShopPreviewer
{
    private readonly NeowOptionDataset _dataset;
    private readonly Sts2WorldData _world;

    internal Sts2StandardShopPreviewer(NeowOptionDataset dataset, Sts2WorldData world)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public ShopPreview PreviewFirstShop(
        SeedRunEvaluationContext context,
        IReadOnlyList<NeowOptionResult> neowOptions)
    {
        return PreviewFirstShop(context, neowOptions, ShopPreviewRequest.Full);
    }

    internal ShopPreview PreviewFirstShop(
        SeedRunEvaluationContext context,
        IReadOnlyList<NeowOptionResult> neowOptions,
        ShopPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(neowOptions);
        ArgumentNullException.ThrowIfNull(request);

        var playerSeed = unchecked((uint)GameRng.GetDeterministicHashCode(context.SeedText) + (uint)Math.Max(1, context.PlayerCount));
        var runRng = new RunRngSet(context.RunSeed);

        var state = new StandardShopState(
            _dataset,
            _world,
            context.Character,
            context.PlayerCount,
            context.AscensionLevel,
            runRng,
            new GameRng(playerSeed, "rewards"),
            new GameRng(playerSeed, "shops"));

        ApplyStandardNeowRule(state, neowOptions);

        var actOne = _world.ResolveActOne(context.RunSeed)
            ?? _world.Acts.FirstOrDefault(act => act.ActNumber == 1)
            ?? _world.Acts[0];
        var map = StandardActMapState.Create(actOne, context.PlayerCount > 1, context.AscensionLevel, runRng.Get("act_1_map"));
        var route = map.GetShortestRouteToFirstVisibleShop();
        if (route.Count == 0)
        {
            return BuildShopPreview(state, request);
        }

        foreach (var point in route.Skip(1))
        {
            var roomType = ResolveRoomType(state, point);
            if (request.CaptureRouteRooms)
            {
                state.RouteRooms.Add(FormatRouteRoom(point.PointType, roomType, point.Coord));
            }
            state.History.Add(new RouteHistoryEntry(point.PointType, roomType));

            if (roomType == StandardRoomType.Shop)
            {
                return BuildShopPreview(state, request);
            }

            SimulateBeforeShopRoom(state, roomType);
        }

        return BuildShopPreview(state, request);
    }

    internal static FirstShopRouteInfo? GetFirstShopRouteInfo(Sts2WorldData world, SeedRunEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(context);

        var runRng = new RunRngSet(context.RunSeed);
        var actOne = world.ResolveActOne(context.RunSeed)
            ?? world.Acts.FirstOrDefault(act => act.ActNumber == 1)
            ?? world.Acts[0];
        var map = StandardActMapState.Create(actOne, context.PlayerCount > 1, context.AscensionLevel, runRng.Get("act_1_map"));
        var route = map.GetShortestRouteToFirstVisibleShop();
        if (route.Count == 0)
        {
            return null;
        }

        var shopPoint = route[^1];
        return new FirstShopRouteInfo(shopPoint.Coord.Row, Math.Max(0, route.Count - 2));
    }

    private static void ApplyStandardNeowRule(StandardShopState state, IReadOnlyList<NeowOptionResult> neowOptions)
    {
        var selected = neowOptions.FirstOrDefault();
        if (selected == null)
        {
            return;
        }

        state.AssumedNeowOptionId = selected.RelicId;
        if (!string.IsNullOrWhiteSpace(selected.RelicId))
        {
            state.PlayerRelicBag.Remove(selected.RelicId);
            state.OwnedRelics.Add(selected.RelicId);
        }

        foreach (var detail in selected.Details)
        {
            if (!string.IsNullOrWhiteSpace(detail.ModelId) &&
                state.Dataset.RelicMetadataMap.ContainsKey(detail.ModelId))
            {
                state.PlayerRelicBag.Remove(detail.ModelId);
                state.OwnedRelics.Add(detail.ModelId);
            }
        }

        ReplayImmediatePickupEffects(state, selected.RelicId);
    }

    private static void ReplayImmediatePickupEffects(StandardShopState state, string? relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return;
        }

        switch (relicId.ToUpperInvariant())
        {
            case NeowOptionIds.ArcaneScroll:
                ReplayCardReward(state, GetCharacterCardPool(state), CardRarityOddsType.Uniform, 1, metadata => metadata.ParsedRarity == CardRarity.Rare, simulateUpgradeRoll: false);
                break;

            case NeowOptionIds.HeftyTablet:
                ReplayCardReward(state, GetCharacterCardPool(state), CardRarityOddsType.Uniform, 3, metadata => metadata.ParsedRarity == CardRarity.Rare, simulateUpgradeRoll: false);
                break;

            case NeowOptionIds.LeadPaperweight:
                ReplayCardReward(state, state.Dataset.ColorlessCardPoolList, CardRarityOddsType.RegularEncounter, 2);
                break;

            case NeowOptionIds.MassiveScroll:
                ReplayCardReward(state, GetMassiveScrollPool(state), CardRarityOddsType.RegularEncounter, 3, metadata => metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly);
                break;

            case NeowOptionIds.LostCoffer:
                ReplayCardReward(state, GetCharacterCardPool(state), CardRarityOddsType.RegularEncounter, 3);
                RollPotionReward(state);
                break;

            case NeowOptionIds.ScrollBoxes:
                ReplayScrollBoxes(state);
                break;

            case NeowOptionIds.LeafyPoultice:
            case NeowOptionIds.PhialHolster:
                // These consume non-rewards RNG channels in the live game and do not
                // affect the first-shop rewards/shops streams tracked here.
                break;
        }
    }

    private static IReadOnlyList<string> GetCharacterCardPool(StandardShopState state)
    {
        return state.Dataset.CharacterCardPoolMap.TryGetValue(state.Character, out var pool)
            ? pool
            : Array.Empty<string>();
    }

    private static IReadOnlyList<string> GetMassiveScrollPool(StandardShopState state)
    {
        var merged = new HashSet<string>(state.Dataset.ColorlessCardPoolList, StringComparer.OrdinalIgnoreCase);
        if (state.Dataset.CharacterCardPoolMap.TryGetValue(state.Character, out var characterPool))
        {
            foreach (var cardId in characterPool)
            {
                merged.Add(cardId);
            }
        }

        return merged.ToList();
    }

    private static void ReplayCardReward(
        StandardShopState state,
        IEnumerable<string> pool,
        CardRarityOddsType oddsType,
        int count,
        Func<NeowCardMetadata, bool>? predicate = null,
        bool simulateUpgradeRoll = true)
    {
        var candidates = pool
            .Where(cardId => state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsCardAllowedForReward(metadata, state.PlayerCount) &&
                             (predicate == null || predicate(metadata)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0 || count <= 0)
        {
            return;
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var available = candidates.Where(cardId => !selected.Contains(cardId)).ToList();
            if (available.Count == 0)
            {
                break;
            }

            var cardId = oddsType == CardRarityOddsType.Uniform
                ? PickUniformRewardCard(state, available)
                : PickWeightedRewardCard(state, available, oddsType);

            if (string.IsNullOrWhiteSpace(cardId))
            {
                break;
            }

            selected.Add(cardId);
            if (simulateUpgradeRoll)
            {
                state.RewardsRng.NextDouble();
            }
        }
    }

    private static string? PickUniformRewardCard(StandardShopState state, IReadOnlyList<string> available)
    {
        var filtered = available
            .Where(cardId => state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             metadata.ParsedRarity is not CardRarity.Basic and not CardRarity.Ancient)
            .ToList();

        if (filtered.Count == 0)
        {
            filtered = available.ToList();
        }

        return state.RewardsRng.NextItem(filtered);
    }

    private static string? PickWeightedRewardCard(StandardShopState state, IReadOnlyList<string> available, CardRarityOddsType oddsType)
    {
        var rarity = state.CardRarityOdds.Roll(oddsType);
        var allowedRarities = available
            .Select(cardId => state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) ? metadata.ParsedRarity : CardRarity.None)
            .Where(cardRarity => cardRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .ToHashSet();

        while (!allowedRarities.Contains(rarity) && rarity != CardRarity.None)
        {
            rarity = GetNextHighestRarity(rarity);
        }

        var candidates = rarity == CardRarity.None
            ? available.ToList()
            : available
                .Where(cardId => state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 metadata.ParsedRarity == rarity)
                .ToList();

        if (candidates.Count == 0)
        {
            candidates = available.ToList();
        }

        return state.RewardsRng.NextItem(candidates);
    }

    private static void ReplayScrollBoxes(StandardShopState state)
    {
        if (!state.Dataset.CharacterCardPoolMap.TryGetValue(state.Character, out var pool) || pool.Count == 0)
        {
            return;
        }

        var commons = new List<string>();
        var uncommons = new List<string>();
        foreach (var cardId in pool)
        {
            if (!state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) ||
                !IsCardAllowedForReward(metadata, state.PlayerCount))
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

        if (commons.Count < 4 || uncommons.Count < 2)
        {
            return;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var bundle = 0; bundle < 2; bundle++)
        {
            if (state.Character == CharacterId.Defect && state.RewardsRng.NextInt(100) < 1)
            {
                continue;
            }

            for (var i = 0; i < 2; i++)
            {
                var availableCommons = commons.Where(cardId => !used.Contains(cardId)).ToList();
                if (availableCommons.Count == 0)
                {
                    break;
                }

                var cardId = state.RewardsRng.NextItem(availableCommons);
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    used.Add(cardId);
                }
            }

            var availableUncommons = uncommons.Where(cardId => !used.Contains(cardId)).ToList();
            var uncommonId = state.RewardsRng.NextItem(availableUncommons);
            if (!string.IsNullOrWhiteSpace(uncommonId))
            {
                used.Add(uncommonId);
            }
        }
    }

    private static bool IsCardAllowedForReward(NeowCardMetadata metadata, int playerCount)
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

    private static string FormatRouteRoom(StandardMapPointType pointType, StandardRoomType roomType, StandardMapCoord coord)
    {
        return $"{coord.Row}:{coord.Col}:{pointType}->{roomType}";
    }

    private static StandardRoomType ResolveRoomType(StandardShopState state, StandardMapPoint point)
    {
        return point.PointType switch
        {
            StandardMapPointType.Unknown => state.UnknownOdds.Roll(BuildUnknownBlacklist(state, point.Children)),
            StandardMapPointType.Shop => StandardRoomType.Shop,
            StandardMapPointType.Treasure => StandardRoomType.Treasure,
            StandardMapPointType.RestSite => StandardRoomType.RestSite,
            StandardMapPointType.Monster => StandardRoomType.Monster,
            StandardMapPointType.Elite => StandardRoomType.Elite,
            StandardMapPointType.Boss => StandardRoomType.Boss,
            StandardMapPointType.Ancient => StandardRoomType.Event,
            _ => StandardRoomType.Unassigned
        };
    }

    private static HashSet<StandardRoomType> BuildUnknownBlacklist(StandardShopState state, IReadOnlyCollection<StandardMapPoint> nextPoints)
    {
        var blacklist = new HashSet<StandardRoomType>();
        if ((state.History.Count > 0 && state.History[^1].RoomType == StandardRoomType.Shop) ||
            (nextPoints.Count > 0 && nextPoints.All(p => p.PointType == StandardMapPointType.Shop)))
        {
            blacklist.Add(StandardRoomType.Shop);
        }

        return blacklist;
    }

    private static void SimulateBeforeShopRoom(StandardShopState state, StandardRoomType roomType)
    {
        switch (roomType)
        {
            case StandardRoomType.Monster:
                SimulateCombatRewards(state, CardRarityOddsType.RegularEncounter, roomType, includeRelicReward: false);
                break;
            case StandardRoomType.Elite:
                SimulateCombatRewards(state, CardRarityOddsType.EliteEncounter, roomType, includeRelicReward: true);
                break;
            case StandardRoomType.Treasure:
            case StandardRoomType.Event:
            case StandardRoomType.RestSite:
            case StandardRoomType.Unassigned:
            case StandardRoomType.Boss:
            case StandardRoomType.Shop:
            default:
                break;
        }
    }

    private static void SimulateCombatRewards(
        StandardShopState state,
        CardRarityOddsType rarityOddsType,
        StandardRoomType roomType,
        bool includeRelicReward)
    {
        // Gold rewards populate before potion/card rewards and consume the rewards RNG.
        _ = roomType == StandardRoomType.Elite
            ? state.RewardsRng.NextInt(25, 36)
            : state.RewardsRng.NextInt(10, 21);

        if (state.PotionOdds.Roll(roomType))
        {
            RollPotionReward(state);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rarityOddsType);
        }

        if (includeRelicReward)
        {
            var rarity = RollRelicRarity(state.RewardsRng);
            state.PlayerRelicBag.PullFromFront(rarity);
        }
    }

    private static void RollPotionReward(StandardShopState state)
    {
        var rarity = RollPotionRarity(state.RewardsRng);
        var candidates = GetAvailableItems(state.GetPotionPool(rarity), excluded: null);
        if (candidates.Count == 0)
        {
            candidates = GetAvailableItems(state.GetPotionPool(), excluded: null);
        }

        state.RewardsRng.NextItem(candidates);
    }

    private static void RollCombatRewardCard(StandardShopState state, CardRarityOddsType oddsType)
    {
        var pool = state.GetCombatRewardPool();
        if (pool.Length == 0)
        {
            return;
        }

        var selected = state.CurrentRewardCards;
        var rarity = state.CardRarityOdds.Roll(oddsType);
        var candidates = GetAvailableItems(state.GetCombatRewardPool(rarity), selected);

        while (candidates.Count == 0)
        {
            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return;
            }

            candidates = GetAvailableItems(state.GetCombatRewardPool(rarity), selected);
        }

        var cardId = state.RewardsRng.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        selected.Add(cardId);
        state.RewardsRng.NextDouble();
        if (selected.Count >= 3)
        {
            selected.Clear();
        }
    }

    private static List<string> GetAvailableItems(
        string[] pool,
        ISet<string>? excluded)
    {
        if (pool.Length == 0)
        {
            return new List<string>(0);
        }

        if (excluded == null || excluded.Count == 0)
        {
            return pool.ToList();
        }

        var result = new List<string>(pool.Length);
        foreach (var item in pool)
        {
            if (!excluded.Contains(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static string? PickAvailableItem(GameRng rng, string[] pool, ISet<string> excluded)
    {
        if (pool.Length == 0)
        {
            return null;
        }

        if (excluded.Count == 0)
        {
            return rng.NextItem(pool);
        }

        var candidates = GetAvailableItems(pool, excluded);
        return candidates.Count == 0 ? null : rng.NextItem(candidates);
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

    private ShopPreview BuildShopPreview(StandardShopState state, ShopPreviewRequest request)
    {
        var matchState = new ShopPreviewMatchState(request);
        var discountedSlot = state.ShopsRng.NextInt(5);
        var coloredCards = GenerateColoredCards(state, discountedSlot, request.IncludeCards, matchState);
        var colorlessCards = GenerateColorlessCards(state, request.IncludeCards, matchState);
        if (request.IncludeCards && !matchState.CardsMatched)
        {
            return new ShopPreview
            {
                ColoredCards = coloredCards,
                ColorlessCards = colorlessCards,
                DiscountedColoredSlot = discountedSlot,
                AssumedNeowOptionId = state.AssumedNeowOptionId,
                RouteRooms = state.RouteRooms.ToList()
            };
        }

        var relics = request.RequiresRelicsPhase
            ? GenerateRelics(state, request.IncludeRelics, matchState)
            : Array.Empty<ShopRelicEntry>();
        if (request.IncludeRelics && !matchState.RelicsMatched)
        {
            return new ShopPreview
            {
                ColoredCards = coloredCards,
                ColorlessCards = colorlessCards,
                Relics = relics,
                DiscountedColoredSlot = discountedSlot,
                AssumedNeowOptionId = state.AssumedNeowOptionId,
                RouteRooms = state.RouteRooms.ToList()
            };
        }

        var potions = request.RequiresPotionsPhase
            ? GenerateMerchantPotions(state, request.IncludePotions, matchState)
            : Array.Empty<ShopPotionEntry>();

        return new ShopPreview
        {
            ColoredCards = coloredCards,
            ColorlessCards = colorlessCards,
            Relics = relics,
            Potions = potions,
            DiscountedColoredSlot = discountedSlot,
            AssumedNeowOptionId = state.AssumedNeowOptionId,
            RouteRooms = request.CaptureRouteRooms ? state.RouteRooms.ToList() : Array.Empty<string>()
        };
    }

    private IReadOnlyList<ShopCardEntry> GenerateColoredCards(StandardShopState state, int discountedSlot, bool captureResults, ShopPreviewMatchState matchState)
    {
        List<ShopCardEntry>? result = captureResults ? new List<ShopCardEntry>(5) : null;
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 5; i++)
        {
            var cardType = StandardShopState.ColoredCardTypes[i];
            var rarity = state.CardRarityOdds.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop);
            var cardId = PickMerchantCard(state, selected, cardType, rarity);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            selected.Add(cardId);
            matchState.NoteCard(cardId);
            state.RewardsRng.NextFloat();
            var finalPrice = RollCardPrice(state, cardId, discounted: false, colorless: false);
            if (i == discountedSlot)
            {
                finalPrice = RollCardPrice(state, cardId, discounted: true, colorless: false);
            }
            if (captureResults)
            {
                result!.Add(new ShopCardEntry { Id = cardId, Price = finalPrice });
            }
        }

        return result != null ? result : Array.Empty<ShopCardEntry>();
    }

    private IReadOnlyList<ShopCardEntry> GenerateColorlessCards(StandardShopState state, bool captureResults, ShopPreviewMatchState matchState)
    {
        List<ShopCardEntry>? result = captureResults ? new List<ShopCardEntry>(2) : null;
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = state.GetColorlessMerchantPool();

        foreach (var rarity in StandardShopState.ColorlessCardRarities)
        {
            var candidates = GetAvailableItems(state.GetColorlessMerchantPool(rarity), selected);

            if (candidates.Count == 0)
            {
                candidates = GetAvailableItems(pool, selected);
            }

            var cardId = state.ShopsRng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            selected.Add(cardId);
            matchState.NoteCard(cardId);
            state.RewardsRng.NextFloat();
            if (captureResults)
            {
                result!.Add(new ShopCardEntry
                {
                    Id = cardId,
                    Price = RollCardPrice(state, cardId, discounted: false, colorless: true)
                });
            }
            else
            {
                _ = RollCardPrice(state, cardId, discounted: false, colorless: true);
            }
        }

        return result != null ? result : Array.Empty<ShopCardEntry>();
    }

    private string? PickMerchantCard(
        StandardShopState state,
        ISet<string> selected,
        CardType targetType,
        CardRarity targetRarity)
    {
        var rarity = targetRarity;
        while (true)
        {
            var cardId = PickAvailableItem(state.ShopsRng, state.GetMerchantCardPool(targetType, rarity), selected);
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                return cardId;
            }

            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                break;
            }
        }

        return PickAvailableItem(state.ShopsRng, state.GetMerchantCardPool(targetType), selected);
    }

    private static int RollCardPrice(StandardShopState state, string cardId, bool discounted, bool colorless)
    {
        var rarity = state.Dataset.CardMetadataMap.TryGetValue(cardId, out var metadata)
            ? metadata.ParsedRarity
            : CardRarity.Common;

        var basePrice = rarity switch
        {
            CardRarity.Uncommon => 75,
            CardRarity.Rare => 150,
            _ => 50
        };

        if (colorless)
        {
            basePrice = (int)Math.Round(basePrice * 1.15f);
        }

        var rolled = (int)Math.Round(basePrice * (0.95f + state.ShopsRng.NextFloat() * 0.10f));
        if (discounted)
        {
            rolled /= 2;
        }

        return rolled;
    }

    private IReadOnlyList<ShopRelicEntry> GenerateRelics(StandardShopState state, bool captureResults, ShopPreviewMatchState matchState)
    {
        List<ShopRelicEntry>? result = captureResults ? new List<ShopRelicEntry>(3) : null;
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 2; i++)
        {
            var rarity = RollRelicRarity(state.RewardsRng);
            var relicId = state.PlayerRelicBag.PullFromBack(rarity, selected, StandardShopState.MerchantRelicBlacklist);
            if (string.IsNullOrWhiteSpace(relicId))
            {
                continue;
            }

            selected.Add(relicId);
            matchState.NoteRelic(relicId);
            var price = RollRelicPrice(state, relicId);
            if (captureResults)
            {
                result!.Add(new ShopRelicEntry
                {
                    Id = relicId,
                    Price = price
                });
            }
        }

        var shopRelicId = state.PlayerRelicBag.PullFromBack(RelicRarity.Shop, selected, StandardShopState.MerchantRelicBlacklist);
        if (!string.IsNullOrWhiteSpace(shopRelicId))
        {
            selected.Add(shopRelicId);
            matchState.NoteRelic(shopRelicId);
            var price = RollRelicPrice(state, shopRelicId);
            if (captureResults)
            {
                result!.Add(new ShopRelicEntry
                {
                    Id = shopRelicId,
                    Price = price
                });
            }
        }

        return result != null ? result : Array.Empty<ShopRelicEntry>();
    }

    private static int RollRelicPrice(StandardShopState state, string relicId)
    {
        var basePrice = state.Dataset.RelicMetadataMap.TryGetValue(relicId, out var metadata)
            ? metadata.MerchantCost
            : 200;
        return (int)Math.Round(basePrice * (0.85f + state.ShopsRng.NextFloat() * 0.30f));
    }

    private IReadOnlyList<ShopPotionEntry> GenerateMerchantPotions(StandardShopState state, bool captureResults, ShopPreviewMatchState matchState)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = state.GetPotionPool();
        var rolledPotions = new List<(string Id, PotionRarity Rarity)>(3);

        for (var i = 0; i < 3; i++)
        {
            var rarity = RollPotionRarity(state.ShopsRng);
            var candidates = GetAvailableItems(state.GetPotionPool(rarity), selected);

            if (candidates.Count == 0)
            {
                candidates = GetAvailableItems(pool, selected);
            }

            var potionId = state.ShopsRng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(potionId))
            {
                continue;
            }

            selected.Add(potionId);
            matchState.NotePotion(potionId);
            rolledPotions.Add((potionId, rarity));
        }

        List<ShopPotionEntry>? result = captureResults ? new List<ShopPotionEntry>(rolledPotions.Count) : null;
        foreach (var (potionId, rarity) in rolledPotions)
        {
            var basePrice = rarity switch
            {
                PotionRarity.Uncommon => 75,
                PotionRarity.Rare => 100,
                _ => 50
            };

            var price = (int)Math.Round(basePrice * (0.95f + state.ShopsRng.NextFloat() * 0.10f));
            if (captureResults)
            {
                result!.Add(new ShopPotionEntry
                {
                    Id = potionId,
                    Price = price
                });
            }
        }

        return result != null ? result : Array.Empty<ShopPotionEntry>();
    }

    private sealed class ShopPreviewMatchState
    {
        private readonly IReadOnlySet<string> _requiredCards;
        private readonly IReadOnlySet<string> _requiredRelics;
        private readonly IReadOnlySet<string> _requiredPotions;
        private readonly HashSet<string> _matchedCards;
        private readonly HashSet<string> _matchedRelics;
        private readonly HashSet<string> _matchedPotions;

        public ShopPreviewMatchState(ShopPreviewRequest request)
        {
            _requiredCards = request.RequiredCardIds;
            _requiredRelics = request.RequiredRelicIds;
            _requiredPotions = request.RequiredPotionIds;
            _matchedCards = _requiredCards.Count > 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : EmptyMatchedSet;
            _matchedRelics = _requiredRelics.Count > 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : EmptyMatchedSet;
            _matchedPotions = _requiredPotions.Count > 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : EmptyMatchedSet;
        }

        private static HashSet<string> EmptyMatchedSet { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CardsMatched => _requiredCards.Count == 0 || _matchedCards.Count >= _requiredCards.Count;

        public bool RelicsMatched => _requiredRelics.Count == 0 || _matchedRelics.Count >= _requiredRelics.Count;

        public bool PotionsMatched => _requiredPotions.Count == 0 || _matchedPotions.Count >= _requiredPotions.Count;

        public void NoteCard(string id)
        {
            if (_requiredCards.Contains(id))
            {
                _matchedCards.Add(id);
            }
        }

        public void NoteRelic(string id)
        {
            if (_requiredRelics.Contains(id))
            {
                _matchedRelics.Add(id);
            }
        }

        public void NotePotion(string id)
        {
            if (_requiredPotions.Contains(id))
            {
                _matchedPotions.Add(id);
            }
        }
    }

    private sealed class StandardShopState
    {
        internal static readonly CardType[] ColoredCardTypes =
        [
            CardType.Attack,
            CardType.Attack,
            CardType.Skill,
            CardType.Skill,
            CardType.Power
        ];

        internal static readonly CardRarity[] ColorlessCardRarities =
        [
            CardRarity.Uncommon,
            CardRarity.Rare
        ];

        internal static readonly HashSet<string> MerchantRelicBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "THE_COURIER",
            "OLD_COIN",
            "AMETHYST_AUBERGINE",
            "BOWLER_HAT",
            "LUCKY_FYSH"
        };

        internal StandardShopState(
            NeowOptionDataset dataset,
            Sts2WorldData world,
            CharacterId character,
            int playerCount,
            int ascensionLevel,
            RunRngSet runRng,
            GameRng rewardsRng,
            GameRng shopsRng)
        {
            Dataset = dataset;
            World = world;
            Character = character;
            PlayerCount = playerCount;
            AscensionLevel = ascensionLevel;
            RewardsRng = rewardsRng;
            ShopsRng = shopsRng;
            UnknownOdds = new StandardUnknownMapPointOdds(runRng.UnknownMapPoint);
            CardRarityOdds = new StandardCardRarityOdds(rewardsRng, ascensionLevel);
            PotionOdds = new StandardPotionRewardOdds(rewardsRng);
            PlayerRelicBag = StandardRelicGrabBag.CreatePlayerBag(world.RelicPools, character, playerCount, runRng.UpFront);

            var characterPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var cards)
                ? cards
                : Array.Empty<string>();

            _merchantCardPool = characterPool
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForPlayer(metadata, playerCount) &&
                                 metadata.ParsedRarity is not CardRarity.Basic and not CardRarity.Ancient and not CardRarity.Event)
                .ToArray();
            _merchantCardPoolByType = BuildCardTypeBuckets(_merchantCardPool, dataset.CardMetadataMap);
            _merchantCardPoolByTypeAndRarity = BuildCardTypeRarityBuckets(_merchantCardPool, dataset.CardMetadataMap);

            _combatRewardPool = characterPool
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForPlayer(metadata, playerCount) &&
                                 metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                .ToArray();
            _combatRewardPoolByRarity = BuildCardRarityBuckets(_combatRewardPool, dataset.CardMetadataMap);

            _colorlessMerchantPool = dataset.ColorlessCardPoolList
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowedForPlayer(metadata, playerCount))
                .ToArray();
            _colorlessMerchantPoolByRarity = BuildCardRarityBuckets(_colorlessMerchantPool, dataset.CardMetadataMap);

            _potionPool = dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions)
                ? characterPotions.Concat(dataset.SharedPotionPoolList).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : dataset.SharedPotionPoolList.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _potionPoolByRarity = BuildPotionRarityBuckets(_potionPool, dataset.PotionMetadataMap);
        }

        public NeowOptionDataset Dataset { get; }

        public Sts2WorldData World { get; }

        public CharacterId Character { get; }

        public int PlayerCount { get; }

        public int AscensionLevel { get; }

        public GameRng RewardsRng { get; }

        public GameRng ShopsRng { get; }

        public StandardUnknownMapPointOdds UnknownOdds { get; }

        public StandardCardRarityOdds CardRarityOdds { get; }

        public StandardPotionRewardOdds PotionOdds { get; }

        public StandardRelicGrabBag PlayerRelicBag { get; }

        public HashSet<string> OwnedRelics { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RouteHistoryEntry> History { get; } = new();

        public List<string> RouteRooms { get; } = new();

        public HashSet<string> CurrentRewardCards { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? AssumedNeowOptionId { get; set; }

        private readonly string[] _merchantCardPool;
        private readonly Dictionary<CardType, string[]> _merchantCardPoolByType;
        private readonly Dictionary<(CardType Type, CardRarity Rarity), string[]> _merchantCardPoolByTypeAndRarity;
        private readonly string[] _combatRewardPool;
        private readonly Dictionary<CardRarity, string[]> _combatRewardPoolByRarity;
        private readonly string[] _colorlessMerchantPool;
        private readonly Dictionary<CardRarity, string[]> _colorlessMerchantPoolByRarity;
        private readonly string[] _potionPool;
        private readonly Dictionary<PotionRarity, string[]> _potionPoolByRarity;

        public string[] GetMerchantCardPool() => _merchantCardPool;

        public string[] GetMerchantCardPool(CardType type)
        {
            return _merchantCardPoolByType.TryGetValue(type, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetMerchantCardPool(CardType type, CardRarity rarity)
        {
            return _merchantCardPoolByTypeAndRarity.TryGetValue((type, rarity), out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetCombatRewardPool() => _combatRewardPool;

        public string[] GetCombatRewardPool(CardRarity rarity)
        {
            return _combatRewardPoolByRarity.TryGetValue(rarity, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetColorlessMerchantPool() => _colorlessMerchantPool;

        public string[] GetColorlessMerchantPool(CardRarity rarity)
        {
            return _colorlessMerchantPoolByRarity.TryGetValue(rarity, out var pool)
                ? pool
                : Array.Empty<string>();
        }

        public string[] GetPotionPool() => _potionPool;

        public string[] GetPotionPool(PotionRarity rarity)
        {
            return _potionPoolByRarity.TryGetValue(rarity, out var pool)
                ? pool
                : Array.Empty<string>();
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

        private static Dictionary<CardType, string[]> BuildCardTypeBuckets(
            IEnumerable<string> pool,
            IReadOnlyDictionary<string, NeowCardMetadata> metadataMap)
        {
            return pool
                .Where(cardId => metadataMap.ContainsKey(cardId))
                .GroupBy(cardId => metadataMap[cardId].ParsedType)
                .ToDictionary(group => group.Key, group => group.ToArray());
        }

        private static Dictionary<(CardType Type, CardRarity Rarity), string[]> BuildCardTypeRarityBuckets(
            IEnumerable<string> pool,
            IReadOnlyDictionary<string, NeowCardMetadata> metadataMap)
        {
            return pool
                .Where(cardId => metadataMap.ContainsKey(cardId))
                .GroupBy(cardId =>
                {
                    var metadata = metadataMap[cardId];
                    return (metadata.ParsedType, metadata.ParsedRarity);
                })
                .ToDictionary(group => group.Key, group => group.ToArray());
        }

        private static Dictionary<CardRarity, string[]> BuildCardRarityBuckets(
            IEnumerable<string> pool,
            IReadOnlyDictionary<string, NeowCardMetadata> metadataMap)
        {
            return pool
                .Where(cardId => metadataMap.ContainsKey(cardId))
                .GroupBy(cardId => metadataMap[cardId].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());
        }

        private static Dictionary<PotionRarity, string[]> BuildPotionRarityBuckets(
            IEnumerable<string> pool,
            IReadOnlyDictionary<string, NeowPotionMetadata> metadataMap)
        {
            return pool
                .Where(id => metadataMap.ContainsKey(id))
                .GroupBy(id => metadataMap[id].ParsedRarity)
                .ToDictionary(group => group.Key, group => group.ToArray());
        }
    }

    private sealed class StandardCardRarityOdds
    {
        private readonly GameRng _rng;
        private readonly bool _scarcityActive;

        public StandardCardRarityOdds(GameRng rng, int ascensionLevel)
        {
            _rng = rng;
            _scarcityActive = ascensionLevel >= 7;
        }

        public float CurrentValue { get; private set; } = -0.05f;

        public CardRarity Roll(CardRarityOddsType oddsType)
        {
            var rarity = RollWithoutChangingFutureOdds(oddsType, CurrentValue);
            if (rarity == CardRarity.Rare)
            {
                CurrentValue = -0.05f;
            }
            else
            {
                CurrentValue = Math.Min(CurrentValue + (_scarcityActive ? 0.005f : 0.01f), 0.4f);
            }

            return rarity;
        }

        public CardRarity RollWithoutChangingFutureOdds(CardRarityOddsType oddsType)
        {
            return RollWithoutChangingFutureOdds(oddsType, CurrentValue);
        }

        private CardRarity RollWithoutChangingFutureOdds(CardRarityOddsType oddsType, float offset)
        {
            var roll = _rng.NextFloat();
            var rareOdds = GetBaseOdds(oddsType, CardRarity.Rare) + offset;
            if (roll < rareOdds)
            {
                return CardRarity.Rare;
            }

            if (roll < GetBaseOdds(oddsType, CardRarity.Uncommon) + rareOdds)
            {
                return CardRarity.Uncommon;
            }

            return CardRarity.Common;
        }

        private float GetBaseOdds(CardRarityOddsType oddsType, CardRarity rarity)
        {
            return oddsType switch
            {
                CardRarityOddsType.EliteEncounter => rarity switch
                {
                    CardRarity.Common => _scarcityActive ? 0.549f : 0.5f,
                    CardRarity.Uncommon => 0.4f,
                    CardRarity.Rare => _scarcityActive ? 0.05f : 0.1f,
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
                    CardRarity.Common => _scarcityActive ? 0.585f : 0.54f,
                    CardRarity.Uncommon => 0.37f,
                    CardRarity.Rare => _scarcityActive ? 0.045f : 0.09f,
                    _ => 0f
                },
                _ => rarity switch
                {
                    CardRarity.Common => _scarcityActive ? 0.615f : 0.6f,
                    CardRarity.Uncommon => 0.37f,
                    CardRarity.Rare => _scarcityActive ? 0.0149f : 0.03f,
                    _ => 0f
                }
            };
        }
    }

    private sealed class StandardPotionRewardOdds
    {
        private readonly GameRng _rng;

        public StandardPotionRewardOdds(GameRng rng)
        {
            _rng = rng;
        }

        public float CurrentValue { get; private set; } = 0.4f;

        public bool Roll(StandardRoomType roomType)
        {
            var current = CurrentValue;
            var roll = _rng.NextFloat();
            if (roll < current)
            {
                CurrentValue -= 0.1f;
            }
            else
            {
                CurrentValue += 0.1f;
            }

            var eliteBonus = roomType == StandardRoomType.Elite ? 0.125f : 0f;
            return roll < current + eliteBonus;
        }
    }

    private sealed class StandardUnknownMapPointOdds
    {
        private readonly GameRng _rng;
        private readonly Dictionary<StandardRoomType, float> _baseOdds = new()
        {
            [StandardRoomType.Monster] = 0.1f,
            [StandardRoomType.Elite] = -1f,
            [StandardRoomType.Treasure] = 0.02f,
            [StandardRoomType.Shop] = 0.03f
        };

        private readonly Dictionary<StandardRoomType, float> _currentOdds = new()
        {
            [StandardRoomType.Monster] = 0.1f,
            [StandardRoomType.Elite] = -1f,
            [StandardRoomType.Treasure] = 0.02f,
            [StandardRoomType.Shop] = 0.03f
        };

        public StandardUnknownMapPointOdds(GameRng rng)
        {
            _rng = rng;
        }

        public StandardRoomType Roll(IReadOnlySet<StandardRoomType> blacklist)
        {
            var allowed = _currentOdds.Keys
                .Append(StandardRoomType.Event)
                .Where(roomType => !blacklist.Contains(roomType))
                .ToList();

            var selected = allowed.Contains(StandardRoomType.Event)
                ? StandardRoomType.Event
                : allowed.OrderBy(room => room).FirstOrDefault(StandardRoomType.Monster);

            var roll = _rng.NextFloat();
            var cumulative = 0f;
            foreach (var (roomType, odds) in _currentOdds)
            {
                if (!allowed.Contains(roomType) || odds < 0f)
                {
                    continue;
                }

                cumulative += odds;
                if (roll <= cumulative)
                {
                    selected = roomType;
                    break;
                }
            }

            foreach (var (roomType, baseOdds) in _baseOdds)
            {
                if (roomType == selected)
                {
                    _currentOdds[roomType] = baseOdds;
                }
                else if (allowed.Contains(roomType))
                {
                    _currentOdds[roomType] += baseOdds;
                }
            }

            return selected;
        }
    }

    private sealed class StandardRelicGrabBag
    {
        private readonly Dictionary<RelicRarity, List<string>> _deques;

        private StandardRelicGrabBag(Dictionary<RelicRarity, List<string>> deques)
        {
            _deques = deques;
        }

        public static StandardRelicGrabBag CreatePlayerBag(
            Sts2WorldData.RelicPoolInfo pools,
            CharacterId character,
            int playerCount,
            GameRng rng)
        {
            ShuffleBuckets(BuildBuckets(pools.SharedSequence, pools.RarityMap), rng);
            // Live-save replay shows one additional up_front sample between the
            // shared grab bag shuffle and the player's combined grab bag shuffle.
            rng.FastForward(rng.Counter + 1);

            Dictionary<RelicRarity, List<string>>? playerBuckets = null;
            var players = Math.Max(1, playerCount);
            for (var i = 0; i < players; i++)
            {
                var relics = new List<string>(pools.SharedSequence.Count + pools.GetSequenceFor(character).Count);
                relics.AddRange(pools.SharedSequence);
                relics.AddRange(pools.GetSequenceFor(character));
                playerBuckets = BuildBuckets(relics, pools.RarityMap);
                ShuffleBuckets(playerBuckets, rng);
            }

            return new StandardRelicGrabBag(playerBuckets ?? new Dictionary<RelicRarity, List<string>>());
        }

        private static Dictionary<RelicRarity, List<string>> BuildBuckets(
            IEnumerable<string> relics,
            IReadOnlyDictionary<string, string> rarityMap)
        {
            var byRarity = new Dictionary<RelicRarity, List<string>>();
            foreach (var relicId in relics)
            {
                if (!rarityMap.TryGetValue(relicId, out var rarityText) ||
                    !Enum.TryParse<RelicRarity>(rarityText, ignoreCase: true, out var rarity))
                {
                    continue;
                }

                if (rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
                {
                    continue;
                }

                if (!byRarity.TryGetValue(rarity, out var list))
                {
                    list = new List<string>();
                    byRarity[rarity] = list;
                }

                list.Add(relicId);
            }

            return byRarity;
        }

        private static void ShuffleBuckets(Dictionary<RelicRarity, List<string>> byRarity, GameRng rng)
        {
            foreach (var list in byRarity.Values)
            {
                list.UnstableShuffle(rng);
            }
        }

        public void Remove(string relicId)
        {
            foreach (var list in _deques.Values)
            {
                list.RemoveAll(id => string.Equals(id, relicId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public string? PullFromFront(
            RelicRarity rarity,
            IReadOnlySet<string>? selected = null,
            IReadOnlySet<string>? extraBlacklist = null)
        {
            return PullCore(rarity, selected, extraBlacklist, fromBack: false);
        }

        public string? PullFromBack(
            RelicRarity rarity,
            IReadOnlySet<string>? selected = null,
            IReadOnlySet<string>? extraBlacklist = null)
        {
            return PullCore(rarity, selected, extraBlacklist, fromBack: true);
        }

        private string? PullCore(
            RelicRarity rarity,
            IReadOnlySet<string>? selected,
            IReadOnlySet<string>? extraBlacklist,
            bool fromBack)
        {
            RelicRarity? current = rarity;
            while (true)
            {
                if (current is RelicRarity currentRarity &&
                    _deques.TryGetValue(currentRarity, out var deque) &&
                    deque.Count > 0 &&
                    HasAvailableRelic(deque, selected, extraBlacklist))
                {
                    if (fromBack)
                    {
                        for (var i = deque.Count - 1; i >= 0; i--)
                        {
                            if (IsBlocked(deque[i], selected, extraBlacklist))
                            {
                                continue;
                            }

                            var relicId = deque[i];
                            deque.RemoveAt(i);
                            return relicId;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < deque.Count; i++)
                        {
                            if (IsBlocked(deque[i], selected, extraBlacklist))
                            {
                                continue;
                            }

                            var relicId = deque[i];
                            deque.RemoveAt(i);
                            return relicId;
                        }
                    }
                }

                current = current switch
                {
                    RelicRarity.Shop => RelicRarity.Common,
                    RelicRarity.Common => RelicRarity.Uncommon,
                    RelicRarity.Uncommon => RelicRarity.Rare,
                    _ => (RelicRarity?)null
                };

                if (current is null)
                {
                    return null;
                }
            }
        }

        private static bool HasAvailableRelic(
            IEnumerable<string> relicIds,
            IReadOnlySet<string>? selected,
            IReadOnlySet<string>? extraBlacklist)
        {
            return relicIds.Any(relicId => !IsBlocked(relicId, selected, extraBlacklist));
        }

        private static bool IsBlocked(
            string relicId,
            IReadOnlySet<string>? selected,
            IReadOnlySet<string>? extraBlacklist)
        {
            if (selected != null && selected.Contains(relicId))
            {
                return true;
            }

            return extraBlacklist != null && extraBlacklist.Contains(relicId);
        }
    }

    private sealed class StandardActMapState
    {
        private const int MapWidth = 7;
        private readonly StandardMapPoint?[,] _grid;
        private readonly GameRng _rng;
        private readonly MapPointTypeCountsState _pointTypeCounts;
        private readonly int _mapLength;
        private readonly HashSet<StandardMapPointType> _lowerRestrictions =
        [
            StandardMapPointType.RestSite,
            StandardMapPointType.Elite
        ];
        private readonly HashSet<StandardMapPointType> _upperRestrictions =
        [
            StandardMapPointType.RestSite
        ];
        private readonly HashSet<StandardMapPointType> _parentRestrictions =
        [
            StandardMapPointType.Elite,
            StandardMapPointType.RestSite,
            StandardMapPointType.Treasure,
            StandardMapPointType.Shop
        ];
        private readonly HashSet<StandardMapPointType> _childRestrictions =
        [
            StandardMapPointType.Elite,
            StandardMapPointType.RestSite,
            StandardMapPointType.Treasure,
            StandardMapPointType.Shop
        ];
        private readonly HashSet<StandardMapPointType> _siblingRestrictions =
        [
            StandardMapPointType.RestSite,
            StandardMapPointType.Monster,
            StandardMapPointType.Unknown,
            StandardMapPointType.Elite,
            StandardMapPointType.Shop
        ];

        private StandardActMapState(
            Sts2WorldData.Sts2ActBlueprint act,
            bool isMultiplayer,
            int ascensionLevel,
            GameRng rng)
        {
            _rng = rng;
            _mapLength = act.BaseRooms - (isMultiplayer ? 1 : 0) + 1;
            _grid = new StandardMapPoint[MapWidth, _mapLength];
            _pointTypeCounts = MapPointTypeCountsState.ForAct(act.ActNumber, ascensionLevel, rng);
            StartingMapPoint = new StandardMapPoint(MapWidth / 2, 0) { PointType = StandardMapPointType.Ancient };
            BossMapPoint = new StandardMapPoint(MapWidth / 2, _mapLength) { PointType = StandardMapPointType.Boss };

            GenerateMap();
            AssignPointTypes();
            PruneAndRepair();
        }

        public StandardMapPoint StartingMapPoint { get; }

        public StandardMapPoint BossMapPoint { get; }

        public HashSet<StandardMapPoint> StartMapPoints { get; } = new();

        public static StandardActMapState Create(
            Sts2WorldData.Sts2ActBlueprint act,
            bool isMultiplayer,
            int ascensionLevel,
            GameRng rng)
        {
            return new StandardActMapState(act, isMultiplayer, ascensionLevel, rng);
        }

        public IReadOnlyList<StandardMapPoint> GetShortestRouteToFirstVisibleShop()
        {
            var shopPoints = GetAllMapPoints()
                .Where(point => point.PointType == StandardMapPointType.Shop)
                .OrderBy(point => point.Coord.Row)
                .ThenBy(point => point.Coord.Col)
                .ToList();
            if (shopPoints.Count == 0)
            {
                return new List<StandardMapPoint>();
            }

            var firstVisibleRow = shopPoints[0].Coord.Row;
            shopPoints = shopPoints
                .Where(point => point.Coord.Row == firstVisibleRow)
                .ToList();

            List<StandardMapPoint>? bestPath = null;
            foreach (var shop in shopPoints)
            {
                var path = FindShortestPath(shop);
                if (path.Count == 0)
                {
                    continue;
                }

                if (bestPath == null || ComparePaths(path, bestPath) < 0)
                {
                    bestPath = path;
                }
            }

            return bestPath ?? new List<StandardMapPoint>();
        }

        private static int ComparePaths(IReadOnlyList<StandardMapPoint> left, IReadOnlyList<StandardMapPoint> right)
        {
            var byLength = left.Count.CompareTo(right.Count);
            if (byLength != 0)
            {
                return byLength;
            }

            var leftUnknowns = left.Count(point => point.PointType == StandardMapPointType.Unknown);
            var rightUnknowns = right.Count(point => point.PointType == StandardMapPointType.Unknown);
            var byUnknowns = leftUnknowns.CompareTo(rightUnknowns);
            if (byUnknowns != 0)
            {
                return byUnknowns;
            }

            for (var i = 0; i < left.Count; i++)
            {
                var byRow = left[i].Coord.Row.CompareTo(right[i].Coord.Row);
                if (byRow != 0)
                {
                    return byRow;
                }

                var byCol = left[i].Coord.Col.CompareTo(right[i].Coord.Col);
                if (byCol != 0)
                {
                    return byCol;
                }
            }

            return 0;
        }

        private List<StandardMapPoint> FindShortestPath(StandardMapPoint target)
        {
            var queue = new Queue<StandardMapPoint>();
            var parents = new Dictionary<StandardMapPoint, StandardMapPoint>();
            queue.Enqueue(StartingMapPoint);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (ReferenceEquals(current, target))
                {
                    break;
                }

                foreach (var child in current.Children.OrderBy(point => point.Coord.Row).ThenBy(point => point.Coord.Col))
                {
                    if (ReferenceEquals(child, StartingMapPoint) || parents.ContainsKey(child))
                    {
                        continue;
                    }

                    parents[child] = current;
                    queue.Enqueue(child);
                }
            }

            if (!ReferenceEquals(target, StartingMapPoint) && !parents.ContainsKey(target))
            {
                return [];
            }

            var path = new List<StandardMapPoint>();
            var cursor = target;
            path.Add(cursor);
            while (!ReferenceEquals(cursor, StartingMapPoint))
            {
                cursor = parents[cursor];
                path.Add(cursor);
            }

            path.Reverse();
            return path;
        }

        private void GenerateMap()
        {
            for (var i = 0; i < MapWidth; i++)
            {
                var point = GetOrCreatePoint(_rng.NextInt(0, MapWidth), 1);
                if (i == 1)
                {
                    while (StartMapPoints.Contains(point))
                    {
                        point = GetOrCreatePoint(_rng.NextInt(0, MapWidth), 1);
                    }
                }

                StartMapPoints.Add(point);
                PathGenerate(point);
            }

            ForEachInRow(GetRowCount() - 1, point => point.AddChild(BossMapPoint));
            ForEachInRow(1, point => StartingMapPoint.AddChild(point));
        }

        private void PathGenerate(StandardMapPoint startingPoint)
        {
            var current = startingPoint;
            while (current.Coord.Row < _mapLength - 1)
            {
                var coord = GenerateNextCoord(current);
                var next = GetOrCreatePoint(coord.Col, coord.Row);
                current.AddChild(next);
                current = next;
            }
        }

        private StandardMapCoord GenerateNextCoord(StandardMapPoint current)
        {
            var minCol = Math.Max(0, current.Coord.Col - 1);
            var maxCol = Math.Min(current.Coord.Col + 1, MapWidth - 1);
            var candidates = new List<int> { -1, 0, 1 };
            StableShuffle(candidates);

            foreach (var delta in candidates)
            {
                var targetCol = delta switch
                {
                    -1 => minCol,
                    0 => current.Coord.Col,
                    1 => maxCol,
                    _ => current.Coord.Col
                };

                if (!HasInvalidCrossover(current, targetCol))
                {
                    return new StandardMapCoord(targetCol, current.Coord.Row + 1);
                }
            }

            throw new InvalidOperationException($"Cannot find next map coord for seed {_rng.Seed}.");
        }

        private bool HasInvalidCrossover(StandardMapPoint current, int targetCol)
        {
            var delta = targetCol - current.Coord.Col;
            if (delta == 0 || delta == MapWidth)
            {
                return false;
            }

            var point = _grid[targetCol, current.Coord.Row];
            if (point == null)
            {
                return false;
            }

            return point.Children.Any(child => child.Coord.Col - point.Coord.Col == -delta);
        }

        private void AssignPointTypes()
        {
            ForEachInRow(GetRowCount() - 1, point =>
            {
                point.PointType = StandardMapPointType.RestSite;
                point.CanBeModified = false;
            });

            ForEachInRow(GetRowCount() - 7, point =>
            {
                point.PointType = StandardMapPointType.Treasure;
                point.CanBeModified = false;
            });

            ForEachInRow(1, point =>
            {
                point.PointType = StandardMapPointType.Monster;
                point.CanBeModified = false;
            });

            var toAssign = new List<StandardMapPointType>();
            toAssign.AddRange(Enumerable.Repeat(StandardMapPointType.RestSite, _pointTypeCounts.NumOfRests));
            toAssign.AddRange(Enumerable.Repeat(StandardMapPointType.Shop, _pointTypeCounts.NumOfShops));
            toAssign.AddRange(Enumerable.Repeat(StandardMapPointType.Elite, _pointTypeCounts.NumOfElites));
            toAssign.AddRange(Enumerable.Repeat(StandardMapPointType.Unknown, _pointTypeCounts.NumOfUnknowns));

            var queue = new Queue<StandardMapPointType>(toAssign);
            AssignRemainingTypes(queue);

            foreach (var point in GetAllMapPoints().Where(point => point.PointType == StandardMapPointType.Unassigned))
            {
                point.PointType = StandardMapPointType.Monster;
            }
        }

        private void AssignRemainingTypes(Queue<StandardMapPointType> queue)
        {
            for (var pass = 0; pass < 3 && queue.Count > 0; pass++)
            {
                var unassigned = GetAllMapPoints()
                    .Where(point => point.PointType == StandardMapPointType.Unassigned)
                    .ToList();
                StableShuffle(unassigned);

                foreach (var point in unassigned)
                {
                    if (queue.Count == 0)
                    {
                        break;
                    }

                    point.PointType = GetNextValidPointType(queue, point);
                }
            }
        }

        private StandardMapPointType GetNextValidPointType(Queue<StandardMapPointType> queue, StandardMapPoint point)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var next = queue.Dequeue();
                if (_pointTypeCounts.PointTypesThatIgnoreRules.Contains(next) || IsValidPointType(next, point))
                {
                    return next;
                }

                queue.Enqueue(next);
            }

            return StandardMapPointType.Unassigned;
        }

        private bool IsValidPointType(StandardMapPointType pointType, StandardMapPoint point)
        {
            return IsValidForUpper(pointType, point) &&
                   IsValidForLower(pointType, point) &&
                   IsValidWithParents(pointType, point) &&
                   IsValidWithChildren(pointType, point) &&
                   IsValidWithSiblings(pointType, point);
        }

        private bool IsValidForUpper(StandardMapPointType pointType, StandardMapPoint point)
        {
            return point.Coord.Row < _mapLength - 3 || !_upperRestrictions.Contains(pointType);
        }

        private bool IsValidForLower(StandardMapPointType pointType, StandardMapPoint point)
        {
            return point.Coord.Row >= 6 || !_lowerRestrictions.Contains(pointType);
        }

        private bool IsValidWithParents(StandardMapPointType pointType, StandardMapPoint point)
        {
            return !point.Parents.Any(parent => _parentRestrictions.Contains(parent.PointType) && parent.PointType == pointType);
        }

        private bool IsValidWithChildren(StandardMapPointType pointType, StandardMapPoint point)
        {
            return !point.Children.Any(child => _childRestrictions.Contains(child.PointType) && child.PointType == pointType);
        }

        private bool IsValidWithSiblings(StandardMapPointType pointType, StandardMapPoint point)
        {
            if (!_siblingRestrictions.Contains(pointType))
            {
                return true;
            }

            return GetSiblings(point).All(other => other.PointType != pointType);
        }

        private void PruneAndRepair()
        {
            for (var i = 0; i < 3; i++)
            {
                PruneDuplicateSegments();
                if (!RepairPrunedPointTypes())
                {
                    break;
                }
            }
        }

        private bool RepairPrunedPointTypes()
        {
            var changed = false;
            changed |= RepairPointType(StandardMapPointType.Shop, _pointTypeCounts.NumOfShops);
            changed |= RepairPointType(StandardMapPointType.Elite, _pointTypeCounts.NumOfElites);
            changed |= RepairPointType(StandardMapPointType.RestSite, _pointTypeCounts.NumOfRests);
            changed |= RepairPointType(StandardMapPointType.Unknown, _pointTypeCounts.NumOfUnknowns);
            return changed;
        }

        private bool RepairPointType(StandardMapPointType type, int targetCount)
        {
            var currentCount = GetAllMapPoints().Count(point => point.PointType == type);
            var needed = targetCount - currentCount;
            if (needed <= 0)
            {
                return false;
            }

            var changed = false;
            var monsters = GetAllMapPoints()
                .Where(point => point.PointType == StandardMapPointType.Monster && point.CanBeModified)
                .ToList();
            StableShuffle(monsters);

            foreach (var point in monsters)
            {
                if (needed == 0)
                {
                    break;
                }

                if (IsValidPointType(type, point))
                {
                    point.PointType = type;
                    needed--;
                    changed = true;
                }
            }

            return changed;
        }

        private void PruneDuplicateSegments()
        {
            var matchingSegments = FindMatchingSegments();
            var safety = 0;
            while (PrunePaths(matchingSegments))
            {
                safety++;
                if (safety > 50)
                {
                    throw new InvalidOperationException("Unable to prune matching map segments.");
                }

                matchingSegments = FindMatchingSegments();
            }
        }

        private List<List<StandardMapPoint[]>> FindMatchingSegments()
        {
            var allPaths = FindAllPaths(StartingMapPoint);
            var segments = new SortedDictionary<string, List<StandardMapPoint[]>>(StringComparer.Ordinal);
            foreach (var path in allPaths)
            {
                AddSegments(path, segments);
            }

            return segments.Values.Where(segmentList => segmentList.Count > 1).ToList();
        }

        private static List<List<StandardMapPoint>> FindAllPaths(StandardMapPoint current)
        {
            if (current.PointType == StandardMapPointType.Boss)
            {
                return [[current]];
            }

            var result = new List<List<StandardMapPoint>>();
            foreach (var child in current.Children)
            {
                foreach (var path in FindAllPaths(child))
                {
                    var combined = new List<StandardMapPoint>(path.Count + 1) { current };
                    combined.AddRange(path);
                    result.Add(combined);
                }
            }

            return result;
        }

        private static void AddSegments(
            IReadOnlyList<StandardMapPoint> path,
            IDictionary<string, List<StandardMapPoint[]>> segments)
        {
            for (var i = 0; i < path.Count - 1; i++)
            {
                if (!(path[i].Children.Count > 1 || path[i].Coord.Row == 0))
                {
                    continue;
                }

                for (var length = 2; length < path.Count - i; length++)
                {
                    var end = path[i + length];
                    if (end.Parents.Count < 2)
                    {
                        continue;
                    }

                    var segment = path.Skip(i).Take(length + 1).ToArray();
                    var key = GenerateSegmentKey(segment);
                    if (!segments.TryGetValue(key, out var list))
                    {
                        list = new List<StandardMapPoint[]>();
                        segments[key] = list;
                    }

                    if (!list.Any(existing => Overlaps(existing, segment)))
                    {
                        list.Add(segment);
                    }
                }
            }
        }

        private static string GenerateSegmentKey(IReadOnlyList<StandardMapPoint> segment)
        {
            var start = segment[0];
            var end = segment[^1];
            var prefix = start.Coord.Row == 0
                ? $"{start.Coord.Row}-{end.Coord.Col},{end.Coord.Row}-"
                : $"{start.Coord.Col},{start.Coord.Row}-{end.Coord.Col},{end.Coord.Row}-";
            return prefix + string.Join(",", segment.Select(point => (int)point.PointType));
        }

        private static bool Overlaps(IReadOnlyList<StandardMapPoint> left, IReadOnlyList<StandardMapPoint> right)
        {
            if (left.Count < 3 || right.Count < 3)
            {
                return false;
            }

            for (var i = 1; i <= left.Count - 2; i++)
            {
                if (ReferenceEquals(left[i], right[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PrunePaths(List<List<StandardMapPoint[]>> matchingSegments)
        {
            foreach (var segmentGroup in matchingSegments)
            {
                segmentGroup.UnstableShuffle(_rng);
                if (PruneAllButLast(segmentGroup) != 0)
                {
                    return true;
                }

                if (BreakRelationshipInAnySegment(segmentGroup))
                {
                    return true;
                }
            }

            return false;
        }

        private int PruneAllButLast(IReadOnlyList<StandardMapPoint[]> matches)
        {
            var pruned = 0;
            foreach (var match in matches)
            {
                if (pruned == matches.Count - 1)
                {
                    return pruned;
                }

                if (PruneSegment(match))
                {
                    pruned++;
                }
            }

            return pruned;
        }

        private bool PruneSegment(IReadOnlyList<StandardMapPoint> segment)
        {
            var result = false;
            for (var i = 0; i < segment.Count - 1; i++)
            {
                var point = segment[i];
                if (!IsInMap(point))
                {
                    return true;
                }

                if (point.Children.Count > 1 ||
                    point.Parents.Count > 1 ||
                    point.Parents.Any(parent => parent.Children.Count == 1 && !IsRemoved(parent)))
                {
                    continue;
                }

                var remainder = segment.Skip(i).ToArray();
                if (!remainder.Any(node => node.Children.Count > 1 && node.Parents.Count == 1))
                {
                    if (segment[^1].Parents.Count == 1)
                    {
                        return false;
                    }

                    if (!point.Children.Where(child => !segment.Contains(child)).Any(child => child.Parents.Count == 1))
                    {
                        RemovePoint(point);
                        result = true;
                    }
                }
            }

            return result;
        }

        private static bool BreakRelationshipInAnySegment(IEnumerable<StandardMapPoint[]> matches)
        {
            foreach (var segment in matches)
            {
                for (var i = 0; i < segment.Length - 1; i++)
                {
                    var point = segment[i];
                    if (point.Children.Count >= 2)
                    {
                        var child = segment[i + 1];
                        if (child.Parents.Count != 1)
                        {
                            point.RemoveChild(child);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsInMap(StandardMapPoint point)
        {
            return _grid[point.Coord.Col, point.Coord.Row] != null || point.PointType is StandardMapPointType.Ancient or StandardMapPointType.Boss;
        }

        private bool IsRemoved(StandardMapPoint point)
        {
            return _grid[point.Coord.Col, point.Coord.Row] == null;
        }

        private void RemovePoint(StandardMapPoint point)
        {
            _grid[point.Coord.Col, point.Coord.Row] = null;
            StartMapPoints.Remove(point);
            foreach (var child in point.Children.ToList())
            {
                point.RemoveChild(child);
            }

            foreach (var parent in point.Parents.ToList())
            {
                parent.RemoveChild(point);
            }
        }

        private StandardMapPoint GetOrCreatePoint(int col, int row)
        {
            var point = _grid[col, row];
            if (point != null)
            {
                return point;
            }

            point = new StandardMapPoint(col, row);
            _grid[col, row] = point;
            return point;
        }

        private IEnumerable<StandardMapPoint> GetAllMapPoints()
        {
            for (var col = 0; col < _grid.GetLength(0); col++)
            {
                for (var row = 0; row < _grid.GetLength(1); row++)
                {
                    var point = _grid[col, row];
                    if (point != null)
                    {
                        yield return point;
                    }
                }
            }
        }

        private IEnumerable<StandardMapPoint> GetPointsInRow(int row)
        {
            if (row < 0 || row >= _grid.GetLength(1))
            {
                yield break;
            }

            for (var col = 0; col < _grid.GetLength(0); col++)
            {
                var point = _grid[col, row];
                if (point != null)
                {
                    yield return point;
                }
            }
        }

        private static IEnumerable<StandardMapPoint> GetSiblings(StandardMapPoint point)
        {
            return point.Parents
                .SelectMany(parent => parent.Children)
                .Where(other => !ReferenceEquals(other, point))
                .Distinct();
        }

        private int GetRowCount()
        {
            return _grid.GetLength(1);
        }

        private void ForEachInRow(int rowIndex, Action<StandardMapPoint> processor)
        {
            foreach (var point in GetPointsInRow(rowIndex))
            {
                processor(point);
            }
        }

        private void StableShuffle<T>(List<T> list) where T : IComparable<T>
        {
            list.Sort();
            list.UnstableShuffle(_rng);
        }
    }

    private sealed record RouteHistoryEntry(StandardMapPointType PointType, StandardRoomType RoomType);

    private sealed class MapPointTypeCountsState
    {
        private MapPointTypeCountsState(int numOfUnknowns, int numOfRests, int numOfElites, int numOfShops = 3)
        {
            NumOfUnknowns = numOfUnknowns;
            NumOfRests = numOfRests;
            NumOfElites = numOfElites;
            NumOfShops = numOfShops;
        }

        public int NumOfUnknowns { get; }

        public int NumOfRests { get; }

        public int NumOfElites { get; }

        public int NumOfShops { get; }

        public HashSet<StandardMapPointType> PointTypesThatIgnoreRules { get; } = [];

        public static MapPointTypeCountsState ForAct(int actNumber, int ascensionLevel, GameRng rng)
        {
            var elites = (int)Math.Round(5f * (ascensionLevel >= 1 ? 1.6f : 1f));
            return actNumber switch
            {
                1 => new MapPointTypeCountsState(
                    numOfUnknowns: rng.NextGaussianInt(12, 1, 10, 14),
                    numOfRests: rng.NextGaussianInt(7, 1, 6, 7),
                    numOfElites: elites),
                2 => new MapPointTypeCountsState(
                    numOfUnknowns: rng.NextGaussianInt(12, 1, 10, 14) - 1,
                    numOfRests: rng.NextGaussianInt(6, 1, 6, 7),
                    numOfElites: elites),
                _ => new MapPointTypeCountsState(
                    numOfUnknowns: rng.NextGaussianInt(12, 1, 10, 14) - 1,
                    numOfRests: rng.NextInt(5, 7),
                    numOfElites: elites)
            };
        }
    }

    private sealed class StandardMapPoint : IComparable<StandardMapPoint>
    {
        public StandardMapPoint(int col, int row)
        {
            Coord = new StandardMapCoord(col, row);
        }

        public StandardMapCoord Coord { get; set; }

        public StandardMapPointType PointType { get; set; }

        public bool CanBeModified { get; set; } = true;

        public HashSet<StandardMapPoint> Parents { get; } = [];

        public HashSet<StandardMapPoint> Children { get; } = [];

        public void AddChild(StandardMapPoint child)
        {
            Children.Add(child);
            child.Parents.Add(this);
        }

        public void RemoveChild(StandardMapPoint child)
        {
            Children.Remove(child);
            child.Parents.Remove(this);
        }

        public int CompareTo(StandardMapPoint? other)
        {
            return other == null
                ? 1
                : (Coord.Col, Coord.Row).CompareTo((other.Coord.Col, other.Coord.Row));
        }
    }

    private readonly record struct StandardMapCoord(int Col, int Row) : IComparable<StandardMapCoord>
    {
        public int CompareTo(StandardMapCoord other)
        {
            return (Col, Row).CompareTo((other.Col, other.Row));
        }
    }

    private enum StandardMapPointType
    {
        Unassigned,
        Unknown,
        Shop,
        Treasure,
        RestSite,
        Monster,
        Elite,
        Boss,
        Ancient
    }

    private enum StandardRoomType
    {
        Unassigned,
        Monster,
        Elite,
        Boss,
        Treasure,
        Shop,
        Event,
        RestSite
    }
}
