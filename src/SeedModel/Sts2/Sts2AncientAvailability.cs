using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Sts2;

public sealed record Sts2AncientAvailability
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> EmptyActAncients =
        new Dictionary<int, IReadOnlyList<string>>();

    public static Sts2AncientAvailability Default { get; } = new();

    public IReadOnlyList<string> DisabledSharedAncientIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<int, IReadOnlyList<string>> DisabledActAncientIds { get; init; } = EmptyActAncients;

    public bool IsSharedAncientAvailable(string ancientId)
    {
        return !ContainsAncientId(DisabledSharedAncientIds, ancientId);
    }

    public bool IsActAncientAvailable(int actNumber, string ancientId)
    {
        return !DisabledActAncientIds.TryGetValue(actNumber, out var disabledAncients) ||
               !ContainsAncientId(disabledAncients, ancientId);
    }

    public static Sts2AncientAvailability FromLegacyDarvFlag(bool includeDarvSharedAncient)
    {
        return includeDarvSharedAncient
            ? Default
            : new Sts2AncientAvailability
            {
                DisabledSharedAncientIds = ["DARV"]
            };
    }

    public static Sts2AncientAvailability FromRevealedEpochIds(IEnumerable<string>? revealedEpochIds)
    {
        if (revealedEpochIds == null)
        {
            return Default;
        }

        var revealed = revealedEpochIds
            .Where(epochId => !string.IsNullOrWhiteSpace(epochId))
            .Select(epochId => epochId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var disabledSharedAncients = new List<string>();
        var disabledActAncients = new Dictionary<int, IReadOnlyList<string>>();

        // Official unlock checks currently gate shared DARV and Act 2 OROBAS.
        // Centralizing them here keeps the formal program aligned across roll,
        // seed analysis, and archive replay instead of relying on a single bool.
        if (!revealed.Contains("DARV_EPOCH"))
        {
            disabledSharedAncients.Add("DARV");
        }

        if (!revealed.Contains("OROBAS_EPOCH"))
        {
            disabledActAncients[2] = ["OROBAS"];
        }

        if (disabledSharedAncients.Count == 0 && disabledActAncients.Count == 0)
        {
            return Default;
        }

        return new Sts2AncientAvailability
        {
            DisabledSharedAncientIds = disabledSharedAncients,
            DisabledActAncientIds = disabledActAncients
        };
    }

    private static bool ContainsAncientId(IReadOnlyList<string> source, string ancientId)
    {
        return source.Any(id => string.Equals(id, ancientId, StringComparison.OrdinalIgnoreCase));
    }
}
