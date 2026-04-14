using System;

namespace SeedModel.Events;

public static class SeedEventParser
{
    public static bool TryParse(string? value, out SeedEventType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            type = SeedEventType.Act1Neow;
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "act1":
            case "act1-neow":
            case "neow":
            case "neow-act1":
                type = SeedEventType.Act1Neow;
                return true;
            default:
                if (Enum.TryParse<SeedEventType>(value, ignoreCase: true, out var parsed))
                {
                    type = parsed;
                    return true;
                }
                break;
        }

        type = SeedEventType.Act1Neow;
        return false;
    }
}
