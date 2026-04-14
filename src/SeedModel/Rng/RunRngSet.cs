using System;
using System.Collections.Generic;

namespace SeedModel.Rng;

/// <summary>
/// Minimal analogue of the game's RunRngSet so we can reproduce named RNG streams.
/// </summary>
public sealed class RunRngSet
{
    private readonly Dictionary<string, GameRng> _rngs = new(StringComparer.OrdinalIgnoreCase);

    public RunRngSet(uint seed)
    {
        Seed = seed;
    }

    public uint Seed { get; }

    public GameRng UpFront => GetOrCreate("up_front");

    public GameRng Shuffle => GetOrCreate("shuffle");

    public GameRng UnknownMapPoint => GetOrCreate("unknown_map_point");

    public GameRng Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return GetOrCreate(name);
    }

    private GameRng GetOrCreate(string name)
    {
        if (!_rngs.TryGetValue(name, out var rng))
        {
            rng = new GameRng(Seed, name);
            _rngs[name] = rng;
        }

        return rng;
    }
}
