using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Sts2;

public sealed record Sts2PoolFilter
{
    public const double DefaultHighProbabilitySeenThreshold = 0.50;

    public static Sts2PoolFilter Empty { get; } = new();

    public IReadOnlyList<string> Act1EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act2EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act3EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HighProbabilityRelicIds { get; init; } = Array.Empty<string>();

    public double HighProbabilitySeenThreshold { get; init; } = DefaultHighProbabilitySeenThreshold;

    public double? HighProbabilityNonShopThreshold { get; init; }

    public double? HighProbabilityShopThreshold { get; init; }

    public double? HighProbabilityEarlyThreshold { get; init; }

    public double? HighProbabilityAverageFirstOpportunityMax { get; init; }

    public Sts2RelicVisibilitySource? HighProbabilityMostCommonSource { get; init; }

    public bool HasCriteria =>
        Act1EventIds.Count > 0 ||
        Act2EventIds.Count > 0 ||
        Act3EventIds.Count > 0 ||
        HighProbabilityRelicIds.Count > 0;

    public bool Matches(Sts2SeedAnalysis? analysis, Sts2RelicVisibilityAnalysis? relicVisibility)
    {
        if (!HasCriteria)
        {
            return true;
        }

        if (!MatchesActEvents(analysis, 1, Act1EventIds) ||
            !MatchesActEvents(analysis, 2, Act2EventIds) ||
            !MatchesActEvents(analysis, 3, Act3EventIds))
        {
            return false;
        }

        return MatchesHighProbabilityRelics(relicVisibility, this);
    }

    private static bool MatchesActEvents(
        Sts2SeedAnalysis? analysis,
        int actNumber,
        IReadOnlyList<string> requiredEventIds)
    {
        if (requiredEventIds.Count == 0)
        {
            return true;
        }

        if (analysis == null)
        {
            return false;
        }

        var act = analysis.Acts.FirstOrDefault(item => item.ActNumber == actNumber);
        if (act == null)
        {
            return false;
        }

        return MatchesIds(requiredEventIds, act.EventPool.Take(act.PriorityEventCount));
    }

    private static bool MatchesHighProbabilityRelics(
        Sts2RelicVisibilityAnalysis? analysis,
        Sts2PoolFilter filter)
    {
        var requiredRelicIds = filter.HighProbabilityRelicIds;
        if (requiredRelicIds.Count == 0)
        {
            return true;
        }

        if (analysis == null)
        {
            return false;
        }

        var availableIds = analysis.Profiles
            .SelectMany(profile => profile.SeenRelics)
            .Where(filter.MatchesHighProbabilityRelic)
            .Select(relic => relic.RelicId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return MatchesIds(requiredRelicIds, availableIds);
    }

    internal bool MatchesHighProbabilityRelic(Sts2RelicVisibilityRankedRelic relic)
    {
        ArgumentNullException.ThrowIfNull(relic);

        if (relic.SeenProbability < HighProbabilitySeenThreshold)
        {
            return false;
        }

        if (HighProbabilityNonShopThreshold.HasValue &&
            relic.NonShopSeenProbability < HighProbabilityNonShopThreshold.Value)
        {
            return false;
        }

        if (HighProbabilityShopThreshold.HasValue &&
            relic.ShopSeenProbability < HighProbabilityShopThreshold.Value)
        {
            return false;
        }

        if (HighProbabilityEarlyThreshold.HasValue &&
            relic.EarlyProbability < HighProbabilityEarlyThreshold.Value)
        {
            return false;
        }

        if (HighProbabilityAverageFirstOpportunityMax.HasValue &&
            relic.AverageFirstOpportunity > HighProbabilityAverageFirstOpportunityMax.Value)
        {
            return false;
        }

        if (HighProbabilityMostCommonSource.HasValue &&
            relic.MostCommonSource != HighProbabilityMostCommonSource.Value)
        {
            return false;
        }

        return true;
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
