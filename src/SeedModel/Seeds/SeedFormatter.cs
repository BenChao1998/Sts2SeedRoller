using System;
using System.Security.Cryptography;

namespace SeedModel.Seeds;

public static class SeedFormatter
{
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int AlphabetLength = 32;
    public const int DefaultLength = 10;
    private const ulong Modulus = 1UL << 50;

    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "����д 10 λ������";
            return false;
        }

        var canonical = Canonicalize(raw);
        if (canonical.Length < DefaultLength)
        {
            canonical = canonical.PadRight(DefaultLength, '0');
        }
        else if (canonical.Length > DefaultLength)
        {
            canonical = canonical[..DefaultLength];
        }

        if (!IsValidSeed(canonical))
        {
            error = "����ֻ�ܰ��� 0-9 �ʹ�д��ĸ��ȥ�� I/O��";
            return false;
        }

        normalized = canonical;
        return true;
    }

    public static string Normalize(string raw)
    {
        if (!TryNormalize(raw, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(raw));
        }
        return normalized;
    }

    public static uint ToUIntSeed(string normalized)
    {
        return unchecked((uint)GetDeterministicHashCode(normalized));
    }

    public static string Advance(string seed, int delta)
    {
        if (delta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        if (delta == 0)
        {
            return seed;
        }

        var value = ToNumber(seed);
        var next = (value + (ulong)delta) % Modulus;
        return FromNumber(next);
    }

    public static string GenerateRandomSeed()
    {
        Span<char> buffer = stackalloc char[DefaultLength];
        for (var i = 0; i < DefaultLength; i++)
        {
            var index = RandomNumberGenerator.GetInt32(AlphabetLength);
            buffer[i] = Alphabet[index];
        }

        return new string(buffer);
    }

    private static string Canonicalize(string seed)
    {
        var canonical = seed.ToUpperInvariant()
            .Replace('O', '0')
            .Replace('I', '1')
            .Trim();
        return canonical;
    }

    private static bool IsValidSeed(string seed)
    {
        if (seed.Length != DefaultLength)
        {
            return false;
        }

        foreach (var ch in seed)
        {
            if (Alphabet.IndexOf(ch) < 0)
            {
                return false;
            }
        }
        return true;
    }

    private static ulong ToNumber(string seed)
    {
        ulong value = 0;
        foreach (var ch in seed)
        {
            var index = Alphabet.IndexOf(ch);
            if (index < 0)
            {
                throw new ArgumentException($"��Ч�����ַ�: {ch}", nameof(seed));
            }
            value = checked(value * (ulong)AlphabetLength + (ulong)index);
        }
        return value;
    }

    private static string FromNumber(ulong value)
    {
        var chars = new char[DefaultLength];
        for (var i = DefaultLength - 1; i >= 0; i--)
        {
            var digit = (int)(value % (ulong)AlphabetLength);
            value /= (ulong)AlphabetLength;
            chars[i] = Alphabet[digit];
        }
        return new string(chars);
    }

    private static int GetDeterministicHashCode(string str)
    {
        var hash1 = 352654597;
        var hash2 = hash1;
        for (var i = 0; i < str.Length; i += 2)
        {
            hash1 = ((hash1 << 5) + hash1) ^ str[i];
            if (i == str.Length - 1)
            {
                break;
            }
            hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
        }
        return hash1 + hash2 * 1566083941;
    }
}


