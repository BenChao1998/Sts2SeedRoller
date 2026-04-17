using System.Collections.Generic;

namespace SeedModel.Sts2;

public sealed record ShopPreview
{
    public IReadOnlyList<ShopCardEntry> ColoredCards { get; init; } = Array.Empty<ShopCardEntry>();

    public IReadOnlyList<ShopCardEntry> ColorlessCards { get; init; } = Array.Empty<ShopCardEntry>();

    public IReadOnlyList<ShopRelicEntry> Relics { get; init; } = Array.Empty<ShopRelicEntry>();

    public IReadOnlyList<ShopPotionEntry> Potions { get; init; } = Array.Empty<ShopPotionEntry>();

    public int DiscountedColoredSlot { get; init; } = -1;

    public string? AssumedNeowOptionId { get; init; }

    public IReadOnlyList<string> RouteRooms { get; init; } = Array.Empty<string>();
}

internal sealed record FirstShopRouteInfo(int ShopRow, int RoomsBeforeShop);

internal sealed record ShopPreviewRequest
{
    private static readonly HashSet<string> EmptyIds = new(StringComparer.OrdinalIgnoreCase);

    public static ShopPreviewRequest Full { get; } = new()
    {
        IncludeCards = true,
        IncludeRelics = true,
        IncludePotions = true
    };

    public bool IncludeCards { get; init; }

    public bool IncludeRelics { get; init; }

    public bool IncludePotions { get; init; }

    public IReadOnlySet<string> RequiredCardIds { get; init; } = EmptyIds;

    public IReadOnlySet<string> RequiredRelicIds { get; init; } = EmptyIds;

    public IReadOnlySet<string> RequiredPotionIds { get; init; } = EmptyIds;

    public bool RequiresRelicsPhase => IncludeRelics || IncludePotions;

    public bool RequiresPotionsPhase => IncludePotions;

    public bool IsFull => IncludeCards && IncludeRelics && IncludePotions;

    public bool CaptureRouteRooms => IsFull;
}

public interface IShopEntry
{
    string Id { get; }
}

public sealed record ShopCardEntry : IShopEntry
{
    public string Id { get; init; } = string.Empty;

    public int Price { get; init; }
}

public sealed record ShopRelicEntry : IShopEntry
{
    public string Id { get; init; } = string.Empty;

    public int Price { get; init; }
}

public sealed record ShopPotionEntry : IShopEntry
{
    public string Id { get; init; } = string.Empty;

    public int Price { get; init; }
}
