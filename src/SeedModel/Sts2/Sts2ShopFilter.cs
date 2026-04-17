using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Sts2;

public sealed record Sts2ShopFilter
{
    private ShopPreviewRequest? _previewRequest;

    public static Sts2ShopFilter Empty { get; } = new();

    public int? MaxFirstShopRow { get; init; }

    public IReadOnlyList<string> CardIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelicIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PotionIds { get; init; } = Array.Empty<string>();

    public bool HasRouteCriteria => MaxFirstShopRow is > 0;

    public bool HasCardCriteria => CardIds.Count > 0;

    public bool HasRelicCriteria => RelicIds.Count > 0;

    public bool HasPotionCriteria => PotionIds.Count > 0;

    public bool HasInventoryCriteria => HasCardCriteria || HasRelicCriteria || HasPotionCriteria;

    public bool HasCriteria => HasRouteCriteria || HasInventoryCriteria;

    internal ShopPreviewRequest BuildPreviewRequest()
    {
        return _previewRequest ??= new ShopPreviewRequest
        {
            IncludeCards = HasCardCriteria,
            IncludeRelics = HasRelicCriteria,
            IncludePotions = HasPotionCriteria,
            RequiredCardIds = new HashSet<string>(CardIds, StringComparer.OrdinalIgnoreCase),
            RequiredRelicIds = new HashSet<string>(RelicIds, StringComparer.OrdinalIgnoreCase),
            RequiredPotionIds = new HashSet<string>(PotionIds, StringComparer.OrdinalIgnoreCase)
        };
    }

    internal bool MatchesRoute(FirstShopRouteInfo? routeInfo)
    {
        if (!HasRouteCriteria)
        {
            return true;
        }

        return routeInfo != null && routeInfo.ShopRow <= MaxFirstShopRow;
    }

    public bool Matches(ShopPreview? preview)
    {
        if (!HasInventoryCriteria || preview == null)
        {
            return true;
        }

        if (HasCardCriteria)
        {
            foreach (var filterId in CardIds)
            {
                if (!ContainsCard(preview.ColoredCards, filterId) &&
                    !ContainsCard(preview.ColorlessCards, filterId))
                {
                    return false;
                }
            }
        }

        if (HasRelicCriteria)
        {
            foreach (var filterId in RelicIds)
            {
                if (!ContainsEntry(preview.Relics, filterId))
                {
                    return false;
                }
            }
        }

        if (HasPotionCriteria)
        {
            foreach (var filterId in PotionIds)
            {
                if (!ContainsEntry(preview.Potions, filterId))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ContainsCard(IReadOnlyList<ShopCardEntry> entries, string id)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsEntry<TEntry>(IReadOnlyList<TEntry> entries, string id)
        where TEntry : IShopEntry
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
