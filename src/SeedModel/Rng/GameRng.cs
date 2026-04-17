using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Rng;

/// <summary>
/// Deterministic RNG that mirrors MegaCrit.Sts2.Core.Random.Rng but without any
/// dependency on the official assemblies.
/// </summary>
public sealed class GameRng
{
    private readonly Random _random;

    public uint Seed { get; }

    public int Counter { get; private set; }

    public GameRng(uint seed, int counter = 0)
    {
        Seed = seed;
        _random = new Random(unchecked((int)seed));
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
            _random.Next();
        }
    }

    public bool NextBool()
    {
        Counter++;
        return _random.Next(2) == 0;
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }
        Counter++;
        return _random.Next(maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(minInclusive));
        }
        Counter++;
        return _random.Next(minInclusive, maxExclusive);
    }

    public double NextDouble()
    {
        Counter++;
        return _random.NextDouble();
    }

    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        int value;
        do
        {
            var d = 1.0 - _random.NextDouble();
            var angleSeed = 1.0 - _random.NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Sin(Math.PI * 2.0 * angleSeed);
            value = (int)Math.Round(mean + stdDev * normal);
        }
        while (value < min || value > max);

        return value;
    }

    public float NextFloat()
    {
        Counter++;
        return (float)_random.NextDouble();
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
