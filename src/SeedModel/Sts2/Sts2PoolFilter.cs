using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SeedModel.Sts2;

public sealed record Sts2PoolFilter
{
    public const double DefaultHighProbabilitySeenThreshold = 0.50;
    public const double DefaultHighProbabilityEventSeenThreshold = 0.50;

    public static Sts2PoolFilter Empty { get; } = new();

    public IReadOnlyList<string> Act1EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act2EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act3EventIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HighProbabilityEventIds { get; init; } = Array.Empty<string>();

    public double HighProbabilityEventSeenThreshold { get; init; } = DefaultHighProbabilityEventSeenThreshold;

    public double? HighProbabilityEventEarlyThreshold { get; init; }

    public double? HighProbabilityEventAverageFirstOpportunityMax { get; init; }

    public Sts2EventVisibilitySource? HighProbabilityEventMostCommonSource { get; init; }

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
        HighProbabilityEventIds.Count > 0 ||
        HighProbabilityRelicIds.Count > 0;

    public IReadOnlyList<Sts2ActScopedEventId> GetHighProbabilityEventSelections()
    {
        var result = new List<Sts2ActScopedEventId>();
        AppendScopedEventIds(result, 1, Act1EventIds);
        AppendScopedEventIds(result, 2, Act2EventIds);
        AppendScopedEventIds(result, 3, Act3EventIds);
        return result;
    }

    public bool Matches(
        Sts2SeedAnalysis? analysis,
        Sts2RelicVisibilityAnalysis? relicVisibility,
        Sts2EventVisibilityAnalysis? eventVisibility = null)
    {
        if (!HasCriteria)
        {
            return true;
        }

        return MatchesHighProbabilityEvents(eventVisibility, this) &&
               MatchesHighProbabilityRelics(relicVisibility, this);
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

        return MatchesIds(
            requiredEventIds.Select(NormalizeIdentifier).ToList(),
            act.EventPool.Take(act.PriorityEventCount).Select(NormalizeIdentifier));
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

    private static bool MatchesHighProbabilityEvents(
        Sts2EventVisibilityAnalysis? analysis,
        Sts2PoolFilter filter)
    {
        var requiredEventSelections = filter.GetHighProbabilityEventSelections();
        if (requiredEventSelections.Count == 0)
        {
            return true;
        }

        if (analysis == null)
        {
            return false;
        }

        var availableKeys = analysis.Profiles
            .Where(profile => !profile.IsComposite)
            .SelectMany(profile => profile.SeenEvents)
            .Where(filter.MatchesHighProbabilityEvent)
            .Select(@event => BuildScopedEventKey(@event.ActNumber, NormalizeIdentifier(@event.EventId)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return MatchesIds(
            requiredEventSelections
                .Select(selection => BuildScopedEventKey(selection.ActNumber, NormalizeIdentifier(selection.EventId)))
                .ToList(),
            availableKeys);
    }

    public bool MatchesHighProbabilityEvent(Sts2EventVisibilityRankedEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.SeenProbability < HighProbabilityEventSeenThreshold)
        {
            return false;
        }

        if (HighProbabilityEventEarlyThreshold.HasValue &&
            @event.EarlyProbability < HighProbabilityEventEarlyThreshold.Value)
        {
            return false;
        }

        if (HighProbabilityEventAverageFirstOpportunityMax.HasValue &&
            @event.AverageFirstOpportunity > HighProbabilityEventAverageFirstOpportunityMax.Value)
        {
            return false;
        }

        if (HighProbabilityEventMostCommonSource.HasValue &&
            @event.MostCommonSource != HighProbabilityEventMostCommonSource.Value)
        {
            return false;
        }

        return true;
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

    private static string NormalizeIdentifier(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(id.Length * 2);
        char? previous = null;

        foreach (var ch in id)
        {
            if (ch is '_' or '-' || char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                previous = ch;
                continue;
            }

            if (char.IsUpper(ch) &&
                builder.Length > 0 &&
                builder[^1] != '_' &&
                previous.HasValue &&
                (char.IsLower(previous.Value) || char.IsDigit(previous.Value)))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(ch));
            previous = ch;
        }

        return builder.ToString().Trim('_');
    }

    private static void AppendScopedEventIds(
        ICollection<Sts2ActScopedEventId> target,
        int actNumber,
        IReadOnlyList<string> eventIds)
    {
        foreach (var eventId in eventIds)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                continue;
            }

            target.Add(new Sts2ActScopedEventId(actNumber, eventId.Trim()));
        }
    }

    private static string BuildScopedEventKey(int actNumber, string eventId)
    {
        return $"{actNumber}:{eventId}";
    }
}

public readonly record struct Sts2ActScopedEventId(int ActNumber, string EventId);



