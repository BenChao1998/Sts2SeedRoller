using System.Collections.Generic;

namespace SeedModel.Neow;

internal sealed class NeowRelicGrabBag
{
    private readonly Dictionary<RelicRarity, List<string>> _deques;

    public NeowRelicGrabBag(Dictionary<RelicRarity, List<string>> deques)
    {
        _deques = deques;
    }

    public string? PullFromFront(RelicRarity rarity)
    {
        RelicRarity? current = rarity;
        while (current.HasValue)
        {
            if (_deques.TryGetValue(current.Value, out var deque) && deque.Count > 0)
            {
                var relicId = deque[0];
                deque.RemoveAt(0);
                return relicId;
            }

            current = current.Value switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => null
            };
        }

        return null;
    }
}
