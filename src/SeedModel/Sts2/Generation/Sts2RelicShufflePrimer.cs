using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2.Generation;

internal sealed class Sts2RelicShufflePrimer
{
    private const int PreviewLimitPerRarity = 12;
    private static readonly HashSet<string> TrackedPlayerRarities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common",
        "Uncommon",
        "Rare",
        "Shop"
    };

    private readonly Sts2WorldData.RelicPoolInfo _pools;

    internal Sts2RelicShufflePrimer(Sts2WorldData world)
    {
        _pools = world?.RelicPools ?? throw new ArgumentNullException(nameof(world));
    }

    public void Prime(
        GameRng rng,
        CharacterId character,
        int playerCount,
        Sts2AncientAvailability? availability = null)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        availability ??= Sts2AncientAvailability.Default;
        var sharedSequence = _pools.GetSharedSequence(availability);
        var combined = _pools.GetCombinedSequence(character, availability);

        // Save-backed replay shows the game shuffles the full shared bag first,
        // then immediately shuffles the player's tracked gameplay rarities
        // (Common/Uncommon/Rare/Shop) without an extra up_front sample between
        // the two steps.
        ShuffleBag(rng, sharedSequence, trackedOnly: false);
        var players = Math.Max(1, playerCount);
        for (var i = 0; i < players; i++)
        {
            ShuffleBag(rng, combined, trackedOnly: true);
        }
    }

    public RelicPoolPreviewResult PrimeAndCapture(
        GameRng rng,
        CharacterId character,
        int playerCount,
        Sts2AncientAvailability? availability = null)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        availability ??= Sts2AncientAvailability.Default;
        var sharedSequence = _pools.GetSharedSequence(availability);
        var combined = _pools.GetCombinedSequence(character, availability);

        var sharedPools = ShuffleBagAndCapture(rng, sharedSequence, trackedOnly: false);
        var playerPools = Array.Empty<Sts2RelicPoolPreviewGroup>();
        var players = Math.Max(1, playerCount);
        for (var i = 0; i < players; i++)
        {
            playerPools = ShuffleBagAndCapture(rng, combined, trackedOnly: true).ToArray();
        }

        return new RelicPoolPreviewResult(sharedPools, playerPools);
    }
    private void ShuffleBag(GameRng rng, IReadOnlyList<string> sequence, bool trackedOnly)
    {
        var buckets = BuildBuckets(sequence, trackedOnly);
        foreach (var list in buckets.Values)
        {
            if (list.Count <= 1)
            {
                continue;
            }

            rng.Shuffle(list);
        }
    }

    private IReadOnlyList<Sts2RelicPoolPreviewGroup> ShuffleBagAndCapture(
        GameRng rng,
        IReadOnlyList<string> sequence,
        bool trackedOnly)
    {
        var buckets = BuildBuckets(sequence, trackedOnly);
        foreach (var list in buckets.Values)
        {
            if (list.Count <= 1)
            {
                continue;
            }

            rng.Shuffle(list);
        }

        return buckets
            .Select(entry =>
            {
                var totalCount = entry.Value.Count;
                var preview = entry.Value
                    .Take(PreviewLimitPerRarity)
                    .ToList();

                return new Sts2RelicPoolPreviewGroup
                {
                    Rarity = entry.Key,
                    PriorityCount = Math.Min(PreviewLimitPerRarity, totalCount),
                    TotalCount = totalCount,
                    Relics = preview
                };
            })
            .OrderBy(group => GetRarityPriority(group.Rarity))
            .ThenBy(group => group.Rarity, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, List<string>> BuildBuckets(IReadOnlyList<string> sequence, bool trackedOnly)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var relic in sequence)
        {
            if (string.IsNullOrWhiteSpace(relic))
            {
                continue;
            }

            if (!_pools.RarityMap.TryGetValue(relic, out var rarity))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rarity))
            {
                continue;
            }

            if (trackedOnly && !TrackedPlayerRarities.Contains(rarity))
            {
                continue;
            }

            if (!buckets.TryGetValue(rarity, out var list))
            {
                list = new List<string>();
                buckets[rarity] = list;
            }

            list.Add(relic);
        }

        return buckets;
    }

    private static int GetRarityPriority(string rarity)
    {
        return rarity switch
        {
            "Common" => 0,
            "Uncommon" => 1,
            "Rare" => 2,
            "Shop" => 3,
            _ => 10
        };
    }

    internal sealed record RelicPoolPreviewResult(
        IReadOnlyList<Sts2RelicPoolPreviewGroup> SharedPools,
        IReadOnlyList<Sts2RelicPoolPreviewGroup> PlayerPools);
}
