using System;
using System.Numerics;

namespace SeedModel.Rng;

/// <summary>
/// Bit-exact replica of the game's <c>MegaCrit.Sts2.Core.Random.MegaRandom</c>
/// (v0.107.1): xoshiro256** PRNG, seeded via splitmix64.
/// The game switched from System.Random to this generator; every random stream
/// in the game (up_front, per-event RNG, rewards, shops, relic bags, ...) draws
/// from it, so all offline simulations must use the same generator to match the
/// game's seeded outcomes.
/// </summary>
public sealed class MegaRandom
{
    private const double IncrDouble = 1.1102230246251565E-16;

    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public MegaRandom(ulong seed)
    {
        Reinitialise(seed);
    }

    private static ulong Splitmix64(ref ulong x)
    {
        var z = (x += 11400714819323198485UL);
        z = (z ^ (z >> 30)) * 13787848793156543929UL;
        z = (z ^ (z >> 27)) * 10723151780598845931UL;
        return z ^ (z >> 31);
    }

    private void Reinitialise(ulong seed)
    {
        _s0 = Splitmix64(ref seed);
        _s1 = Splitmix64(ref seed);
        _s2 = Splitmix64(ref seed);
        _s3 = Splitmix64(ref seed);
    }

    private ulong NextULongInner()
    {
        var s = _s0;
        var s2 = _s1;
        var s3 = _s2;
        var s4 = _s3;
        var result = BitOperations.RotateLeft(s2 * 5, 7) * 9;
        var num = s2 << 17;
        s3 ^= s;
        s4 ^= s2;
        s2 ^= s3;
        s ^= s4;
        s3 ^= num;
        s4 = BitOperations.RotateLeft(s4, 45);
        _s0 = s;
        _s1 = s2;
        _s2 = s3;
        _s3 = s4;
        return result;
    }

    /// <summary>Uniform double in [0, 1), 53-bit precision.</summary>
    public double NextDouble()
    {
        return (NextULongInner() >> 11) * IncrDouble;
    }

    /// <summary>Equal to the game's MegaRandom.NextBool: top-bit test.</summary>
    public bool NextBool()
    {
        return (NextULongInner() & 0x8000000000000000UL) != 0;
    }

    /// <summary>Integer in [0, maxValue) using a single double draw (game NextInner semantics).</summary>
    public int Next(int maxValue)
    {
        if (maxValue < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be > 0");
        }

        return (int)(NextDouble() * maxValue);
    }

    /// <summary>Integer in [minValue, maxValue); long-range branch uses a single double draw.</summary>
    public int Next(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be > minValue");
        }

        var range = (long)maxValue - minValue;
        return range <= int.MaxValue
            ? (int)(NextDouble() * range) + minValue
            : (int)(NextDouble() * range) + minValue;
    }

    /// <summary>
    /// One raw state advancement, mirroring the game's Rng.FastForwardCounter,
    /// which advances with a single MegaRandom.NextInt() draw.
    /// </summary>
    public int NextIntRaw()
    {
        return (int)(NextULongInner() >> 33);
    }
}