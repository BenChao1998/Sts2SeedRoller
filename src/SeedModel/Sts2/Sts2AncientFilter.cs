using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Sts2;

public sealed record Sts2AncientFilter
{
    public static Sts2AncientFilter Disabled { get; } = new();

    public string? Act2AncientId { get; init; }

    public string? Act3AncientId { get; init; }

    public IReadOnlyList<string> Act2OptionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act3OptionIds { get; init; } = Array.Empty<string>();

    public bool HasAct2Criteria => !string.IsNullOrWhiteSpace(Act2AncientId) || Act2OptionIds.Count > 0;

    public bool HasAct3Criteria => !string.IsNullOrWhiteSpace(Act3AncientId) || Act3OptionIds.Count > 0;

    public bool HasCriteria => HasAct2Criteria || HasAct3Criteria;

    public bool Matches(Sts2RunPreview? preview)
    {
        if (!HasCriteria)
        {
            return true;
        }

        if (preview == null)
        {
            return false;
        }

        return MatchesAct(preview, 2, Act2AncientId, Act2OptionIds) &&
               MatchesAct(preview, 3, Act3AncientId, Act3OptionIds);
    }

    private static bool MatchesAct(
        Sts2RunPreview preview,
        int actNumber,
        string? expectedAncientId,
        IReadOnlyList<string> requiredOptionIds)
    {
        var hasAncientCriterion = !string.IsNullOrWhiteSpace(expectedAncientId);
        var hasOptionCriterion = requiredOptionIds.Count > 0;

        if (!hasAncientCriterion && !hasOptionCriterion)
        {
            return true;
        }

        var act = preview.Acts.FirstOrDefault(a => a.ActNumber == actNumber);
        if (act == null)
        {
            return false;
        }

        if (hasAncientCriterion &&
            !string.Equals(expectedAncientId, act.AncientId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!hasOptionCriterion)
        {
            return true;
        }

        return act.AncientOptions.Any(option =>
            requiredOptionIds.Contains(option.OptionId, StringComparer.OrdinalIgnoreCase));
    }
}
