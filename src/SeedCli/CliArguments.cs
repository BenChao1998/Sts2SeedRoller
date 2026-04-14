using System;
using System.Collections.Generic;

namespace SeedCli;

internal static class CliArguments
{
    public static IReadOnlyDictionary<string, string> Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key;
            string? value = null;
            var equalsIndex = current.IndexOf('=');
            if (equalsIndex > 2)
            {
                key = current[..equalsIndex];
                value = current[(equalsIndex + 1)..];
            }
            else
            {
                key = current;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
            }

            if (value != null)
            {
                map[key] = value;
            }
        }

        return map;
    }

    public static string? Get(this IReadOnlyDictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out var value) ? value : null;
    }
}
