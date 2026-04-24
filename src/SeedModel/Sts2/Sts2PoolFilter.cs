using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Sts2;

public sealed record Sts2PoolFilter
{
    public static Sts2PoolFilter Empty { get; } = new();

    public IReadOnlyList<string> Act1EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act2EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act3EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SharedRelicIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlayerRelicIds { get; init; } = Array.Empty<string>();

    public bool HasCriteria =>
        Act1EventIds.Count > 0 ||
        Act2EventIds.Count > 0 ||
        Act3EventIds.Count > 0 ||
        SharedRelicIds.Count > 0 ||
        PlayerRelicIds.Count > 0;

    public bool Matches(Sts2SeedAnalysis? analysis)
    {
        if (!HasCriteria)
        {
            return true;
        }

        if (analysis == null)
        {
            return false;
        }

        return MatchesActEvents(analysis, 1, Act1EventIds) &&
               MatchesActEvents(analysis, 2, Act2EventIds) &&
               MatchesActEvents(analysis, 3, Act3EventIds) &&
               MatchesRelicPools(analysis.SharedRelicPools, SharedRelicIds) &&
               MatchesRelicPools(analysis.PlayerRelicPools, PlayerRelicIds);
    }

    private static bool MatchesActEvents(
        Sts2SeedAnalysis analysis,
        int actNumber,
        IReadOnlyList<string> requiredEventIds)
    {
        if (requiredEventIds.Count == 0)
        {
            return true;
        }

        var act = analysis.Acts.FirstOrDefault(item => item.ActNumber == actNumber);
        if (act == null)
        {
            return false;
        }

        return MatchesIds(requiredEventIds, act.EventPool.Take(act.PriorityEventCount));
    }

    private static bool MatchesRelicPools(
        IReadOnlyList<Sts2RelicPoolPreviewGroup> pools,
        IReadOnlyList<string> requiredRelicIds)
    {
        if (requiredRelicIds.Count == 0)
        {
            return true;
        }

        return MatchesIds(requiredRelicIds, pools.SelectMany(group => group.Relics.Take(group.PriorityCount)));
    }

    private static bool MatchesIds(
        IReadOnlyList<string> requiredIds,
        IEnumerable<string> availableIds)
    {
        var requiredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in requiredIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            requiredCounts[id] = requiredCounts.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        if (requiredCounts.Count == 0)
        {
            return true;
        }

        var availableCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in availableIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            availableCounts[id] = availableCounts.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        foreach (var requirement in requiredCounts)
        {
            if (!availableCounts.TryGetValue(requirement.Key, out var available) || available < requirement.Value)
            {
                return false;
            }
        }

        return true;
    }
}
