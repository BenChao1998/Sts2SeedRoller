using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Neow;

public sealed class NeowOptionFilter
{
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    private NeowOptionFilter(
        NeowOptionKind? kind,
        IReadOnlyList<string> relicTerms,
        IReadOnlyList<string> relicIds,
        IReadOnlyList<string> cardIds,
        IReadOnlyList<string> potionIds,
        bool hasCriteria)
    {
        Kind = kind;
        RelicTerms = relicTerms;
        RelicIds = relicIds;
        CardIds = cardIds;
        PotionIds = potionIds;
        HasCriteria = hasCriteria;
    }

    public NeowOptionKind? Kind { get; }

    public bool HasCriteria { get; }

    private IReadOnlyList<string> RelicTerms { get; }

    private IReadOnlyList<string> RelicIds { get; }

    private IReadOnlyList<string> CardIds { get; }

    private IReadOnlyList<string> PotionIds { get; }

    public static NeowOptionFilter Create(
        NeowOptionKind? kind,
        IEnumerable<string>? relicTerms,
        IEnumerable<string>? relicIds,
        IEnumerable<string>? cardIds,
        IEnumerable<string>? potionIds)
    {
        var normalizedRelicTerms = NormalizeTerms(relicTerms);
        var normalizedRelicIds = NormalizeTerms(relicIds);
        var normalizedCardIds = NormalizeTerms(cardIds, deduplicate: false);
        var normalizedPotionIds = NormalizeTerms(potionIds, deduplicate: false);

        var hasCriteria =
            kind.HasValue ||
            normalizedRelicTerms.Count > 0 ||
            normalizedRelicIds.Count > 0 ||
            normalizedCardIds.Count > 0 ||
            normalizedPotionIds.Count > 0;

        return new NeowOptionFilter(
            kind,
            normalizedRelicTerms,
            normalizedRelicIds,
            normalizedCardIds,
            normalizedPotionIds,
            hasCriteria);
    }

    public bool Matches(NeowOptionResult option)
    {
        if (!HasCriteria)
        {
            return true;
        }

        if (Kind.HasValue && option.Kind != Kind.Value)
        {
            return false;
        }

        if (RelicIds.Count > 0 &&
            !MatchesRelicIds(option))
        {
            return false;
        }

        if (RelicTerms.Count > 0 &&
            !MatchesText(option))
        {
            return false;
        }

        if (CardIds.Count > 0 &&
            !MatchesDetailIds(option.Details, RewardDetailType.Card, CardIds))
        {
            return false;
        }

        if (PotionIds.Count > 0 &&
            !MatchesDetailIds(option.Details, RewardDetailType.Potion, PotionIds))
        {
            return false;
        }

        return true;
    }

    private bool MatchesText(NeowOptionResult option)
    {
        foreach (var term in RelicTerms)
        {
            if (Contains(option.RelicId, term) ||
                Contains(option.Title, term) ||
                Contains(option.Description ?? string.Empty, term) ||
                Contains(option.Note ?? string.Empty, term) ||
                option.Details.Any(detail =>
                    Contains(detail.Label, term) ||
                    Contains(detail.Value, term) ||
                    Contains(detail.ModelId, term)))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesRelicIds(NeowOptionResult option)
    {
        return MatchesModelIds(
            RelicIds,
            EnumerateRelicIds(option));
    }

    private static bool MatchesDetailIds(
        IReadOnlyList<RewardDetail> details,
        RewardDetailType type,
        IReadOnlyList<string> requiredIds)
    {
        return MatchesModelIds(
            requiredIds,
            details
                .Where(detail => detail.Type == type)
                .Select(detail => detail.ModelId));
    }

    private static IEnumerable<string> EnumerateRelicIds(NeowOptionResult option)
    {
        if (!string.IsNullOrWhiteSpace(option.RelicId))
        {
            yield return option.RelicId;
        }

        foreach (var detail in option.Details)
        {
            if (detail.Type == RewardDetailType.Relic &&
                !string.IsNullOrWhiteSpace(detail.ModelId))
            {
                yield return detail.ModelId;
            }
        }
    }

    private static bool MatchesModelIds(
        IReadOnlyList<string> requiredIds,
        IEnumerable<string?> availableIds)
    {
        if (requiredIds.Count == 0)
        {
            return true;
        }

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
        foreach (var availableId in availableIds)
        {
            if (string.IsNullOrWhiteSpace(availableId))
            {
                continue;
            }

            var key = availableId;
            availableCounts[key] = availableCounts.TryGetValue(key, out var count) ? count + 1 : 1;
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

    private static List<string> NormalizeTerms(IEnumerable<string>? terms, bool deduplicate = true)
    {
        if (terms == null)
        {
            return new List<string>();
        }

        var filtered = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim());

        var list = deduplicate
            ? filtered.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : filtered.ToList();

        return list.Count > 0 ? list : new List<string>();
    }

    private static bool Contains(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.IndexOf(term, Comparison) >= 0;
    }
}
