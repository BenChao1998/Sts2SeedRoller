using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Rng;

/// <summary>
/// Deterministic RNG that mirrors MegaCrit.Sts2.Core.Random.Rng without any
/// dependency on the official assemblies.
/// </summary>
/// <remarks>
/// The game (v0.107.1) draws every seeded stream from <c>MegaRandom</c>, an
/// xoshiro256** generator. Earlier game versions used System.Random. The active
/// engine is selected per loaded game version via <see cref="ConfigureEngine"/>;
/// simulations targeting v0.107.1+ use the xoshiro engine so results match the
/// live game, while older version datasets keep the System.Random engine.
/// </remarks>
public sealed class GameRng
{
    /// <summary>First game version verified to use the xoshiro256** MegaRandom engine.</summary>
    public static readonly Version XoshiroEngineSinceVersion = new(0, 107, 1);

    private static bool _useOfficialXoshiroEngine;

    private readonly Random _random;
    private readonly MegaRandom? _megaRandom;

    public uint Seed { get; }

    public int Counter { get; private set; }

    /// <summary>
    /// Whether the active engine is the game's xoshiro256** MegaRandom.
    /// </summary>
    public static bool UseOfficialXoshiroEngine => _useOfficialXoshiroEngine;

    /// <summary>
    /// Selects the RNG engine matching the given game version. v0.107.1 and
    /// later use the game's xoshiro256** generator; older versions use
    /// System.Random. Call this whenever a dataset/game version is selected.
    /// </summary>
    public static void ConfigureEngine(string? gameVersion)
    {
        _useOfficialXoshiroEngine = Version.TryParse(gameVersion, out var parsed) &&
                                    parsed >= XoshiroEngineSinceVersion;
    }

    /// <summary>
    /// Seed addend for player-scoped streams (per-player events, rewards, shops).
    /// The game seeds these with the player's <em>slot index</em> (0 for the
    /// solo player) rather than the net id; older versions used the net id.
    /// Returns the addend matching the active engine.
    /// </summary>
    public static uint PlayerStreamAddend(ulong playerNetId, ulong playerSlotIndex = 0)
    {
        return _useOfficialXoshiroEngine
            ? unchecked((uint)playerSlotIndex)
            : unchecked((uint)playerNetId);
    }

    public GameRng(uint seed, int counter = 0)
    {
        Seed = seed;
        if (_useOfficialXoshiroEngine)
        {
            _megaRandom = new MegaRandom(seed);
        }
        else
        {
            _random = new Random(unchecked((int)seed));
        }

        FastForward(counter);
    }

    public GameRng(uint seed, string salt)
        : this(seed + unchecked((uint)GetDeterministicHashCode(salt ?? throw new ArgumentNullException(nameof(salt)))))
    {
    }

    public void FastForward(int targetCounter)
    {
        if (targetCounter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCounter));
        }

        if (targetCounter < Counter)
        {
            throw new InvalidOperationException($"Cannot rewind RNG from {Counter} down to {targetCounter}.");
        }

        while (Counter < targetCounter)
        {
            Counter++;
            if (_megaRandom != null)
            {
                // Game's Rng.FastForwardCounter advances with one raw draw.
                _megaRandom.NextIntRaw();
            }
            else
            {
                _random.Next();
            }
        }
    }

    public bool NextBool()
    {
        Counter++;
        if (_megaRandom != null)
        {
            // Game's Rng.NextBool: MegaRandom.Next(2) == 0 (single double draw).
            return _megaRandom.Next(2) == 0;
        }

        return _random.Next(2) == 0;
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        Counter++;
        return _megaRandom != null
            ? _megaRandom.Next(maxExclusive)
            : _random.Next(maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive));
        }

        Counter++;
        return _megaRandom != null
            ? _megaRandom.Next(minInclusive, maxExclusive)
            : _random.Next(minInclusive, maxExclusive);
    }

    public double NextDouble()
    {
        Counter++;
        return _megaRandom != null
            ? _megaRandom.NextDouble()
            : _random.NextDouble();
    }

    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        int value;
        do
        {
            var d = 1.0 - NextDouble();
            var angleSeed = 1.0 - NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Sin(Math.PI * 2.0 * angleSeed);
            value = (int)Math.Round(mean + stdDev * normal);
        }
        while (value < min || value > max);

        return value;
    }

    public float NextFloat()
    {
        Counter++;
        return (float)NextDouble();
    }

    public T? NextItem<T>(IEnumerable<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var materialized = items as T[] ?? items.ToArray();
        if (materialized.Length == 0)
        {
            return default;
        }

        var index = NextInt(materialized.Length);
        return materialized[index];
    }

    public void Shuffle<T>(IList<T> list)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        for (var i = list.Count - 1; i > 0; i--)
        {
            var swapIndex = NextInt(i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }

    internal static int GetDeterministicHashCode(string text)
    {
        unchecked
        {
            var hash1 = 352654597;
            var hash2 = hash1;
            for (var i = 0; i < text.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];
                if (i == text.Length - 1)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
            }

            return hash1 + hash2 * 1566083941;
        }
    }
}