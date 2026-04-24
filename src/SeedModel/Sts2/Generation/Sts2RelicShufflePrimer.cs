using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2.Generation;

internal sealed class Sts2RelicShufflePrimer
{
    private static readonly string[] TrackedRarities = ["Common", "Uncommon", "Rare", "Shop"];

    private readonly Sts2WorldData.RelicPoolInfo _pools;

    internal Sts2RelicShufflePrimer(Sts2WorldData world)
    {
        _pools = world?.RelicPools ?? throw new ArgumentNullException(nameof(world));
    }

    public void Prime(GameRng rng, CharacterId character, int playerCount)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        ShuffleBag(rng, _pools.SharedSequence);

        var combined = CombineSequences(_pools.SharedSequence, _pools.GetSequenceFor(character));
        var players = Math.Max(1, playerCount);
        for (var i = 0; i < players; i++)
        {
            ShuffleBag(rng, combined);
        }
    }

    public RelicPoolPreviewResult PrimeAndCapture(GameRng rng, CharacterId character, int playerCount)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        var sharedPools = ShuffleBagAndCapture(rng, _pools.SharedSequence);
        var combined = CombineSequences(_pools.SharedSequence, _pools.GetSequenceFor(character));
        var playerPools = Array.Empty<Sts2RelicPoolPreviewGroup>();
        var players = Math.Max(1, playerCount);
        for (var i = 0; i < players; i++)
        {
            playerPools = ShuffleBagAndCapture(rng, combined).ToArray();
        }

        return new RelicPoolPreviewResult(sharedPools, playerPools);
    }

    private IReadOnlyList<string> CombineSequences(
        IReadOnlyList<string> shared,
        IReadOnlyList<string> character)
    {
        if (ReferenceEquals(shared, character))
        {
            return shared;
        }

        var combined = new List<string>(shared.Count + character.Count);
        combined.AddRange(shared);
        combined.AddRange(character);
        return combined;
    }

    private void ShuffleBag(GameRng rng, IReadOnlyList<string> sequence)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var relic in sequence)
        {
            if (!_pools.RarityMap.TryGetValue(relic, out var rarity))
            {
                continue;
            }

            if (!TrackedRarities.Contains(rarity, StringComparer.OrdinalIgnoreCase))
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

        foreach (var list in buckets.Values)
        {
            if (list.Count <= 1)
            {
                continue;
            }

            rng.Shuffle(list);
        }
    }

    private IReadOnlyList<Sts2RelicPoolPreviewGroup> ShuffleBagAndCapture(GameRng rng, IReadOnlyList<string> sequence)
    {
        var buckets = BuildBuckets(sequence);
        foreach (var list in buckets.Values)
        {
            if (list.Count <= 1)
            {
                continue;
            }

            rng.Shuffle(list);
        }

        return TrackedRarities
            .Select(rarity => new Sts2RelicPoolPreviewGroup
            {
                Rarity = rarity,
                Relics = buckets.TryGetValue(rarity, out var list)
                    ? list.ToList()
                    : Array.Empty<string>()
            })
            .ToList();
    }

    private Dictionary<string, List<string>> BuildBuckets(IReadOnlyList<string> sequence)
    {
        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var relic in sequence)
        {
            if (!_pools.RarityMap.TryGetValue(relic, out var rarity))
            {
                continue;
            }

            if (!TrackedRarities.Contains(rarity, StringComparer.OrdinalIgnoreCase))
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

    internal sealed record RelicPoolPreviewResult(
        IReadOnlyList<Sts2RelicPoolPreviewGroup> SharedPools,
        IReadOnlyList<Sts2RelicPoolPreviewGroup> PlayerPools);
}
