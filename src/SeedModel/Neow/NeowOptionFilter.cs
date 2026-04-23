using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Neow;

public sealed class NeowOptionFilter
{
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private readonly IReadOnlyDictionary<string, int> _requiredRelicCounts;
    private readonly IReadOnlyDictionary<string, int> _requiredCardCounts;
    private readonly IReadOnlyDictionary<string, int> _requiredPotionCounts;

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
        _requiredRelicCounts = BuildRequiredCounts(relicIds);
        _requiredCardCounts = BuildRequiredCounts(cardIds);
        _requiredPotionCounts = BuildRequiredCounts(potionIds);
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

        if (RelicIds.Count > 0 && !MatchesRelicIds(option))
        {
            return false;
        }

        if (RelicTerms.Count > 0 && !MatchesText(option))
        {
            return false;
        }

        if (CardIds.Count > 0 &&
            !MatchesDetailIds(option, RewardDetailType.Card, _requiredCardCounts, NeowDetailHint.Card))
        {
            return false;
        }

        if (PotionIds.Count > 0 &&
            !MatchesDetailIds(option, RewardDetailType.Potion, _requiredPotionCounts, NeowDetailHint.Potion))
        {
            return false;
        }

        return true;
    }

    private bool MatchesText(NeowOptionResult option)
    {
        for (var i = 0; i < RelicTerms.Count; i++)
        {
            var term = RelicTerms[i];
            if (Contains(option.RelicId, term) ||
                Contains(option.Title, term) ||
                Contains(option.Description ?? string.Empty, term) ||
                Contains(option.Note ?? string.Empty, term))
            {
                return true;
            }
        }

        if (option.DetailHint == NeowDetailHint.None)
        {
            return false;
        }

        var details = option.Details;
        for (var i = 0; i < RelicTerms.Count; i++)
        {
            var term = RelicTerms[i];
            for (var detailIndex = 0; detailIndex < details.Count; detailIndex++)
            {
                var detail = details[detailIndex];
                if (Contains(detail.Label, term) ||
                    Contains(detail.Value, term) ||
                    Contains(detail.ModelId, term))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool MatchesRelicIds(NeowOptionResult option)
    {
        if (_requiredRelicCounts.Count == 0)
        {
            return true;
        }

        if ((option.DetailHint & NeowDetailHint.Relic) == 0)
        {
            return MatchesPrimaryIdOnly(_requiredRelicCounts, option.RelicId);
        }

        return MatchesModelIds(_requiredRelicCounts, option.RelicId, option.Details, RewardDetailType.Relic);
    }

    private bool MatchesDetailIds(
        NeowOptionResult option,
        RewardDetailType type,
        IReadOnlyDictionary<string, int> requiredCounts,
        NeowDetailHint requiredHint)
    {
        if (requiredCounts.Count == 0)
        {
            return true;
        }

        if ((option.DetailHint & requiredHint) == 0)
        {
            return false;
        }

        return MatchesModelIds(requiredCounts, primaryId: null, option.Details, type);
    }

    private static bool MatchesModelIds(
        IReadOnlyDictionary<string, int> requiredCounts,
        string? primaryId,
        IReadOnlyList<RewardDetail> details,
        RewardDetailType detailType)
    {
        if (requiredCounts.Count == 0)
        {
            return true;
        }

        var availableCounts = new Dictionary<string, int>(Comparer);
        if (!string.IsNullOrWhiteSpace(primaryId))
        {
            availableCounts[primaryId] = 1;
        }

        for (var i = 0; i < details.Count; i++)
        {
            var detail = details[i];
            if (detail.Type != detailType || string.IsNullOrWhiteSpace(detail.ModelId))
            {
                continue;
            }

            var key = detail.ModelId;
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

    private static bool MatchesPrimaryIdOnly(IReadOnlyDictionary<string, int> requiredCounts, string? primaryId)
    {
        foreach (var requirement in requiredCounts)
        {
            var available = !string.IsNullOrWhiteSpace(primaryId) && Comparer.Equals(primaryId, requirement.Key) ? 1 : 0;
            if (available < requirement.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, int> BuildRequiredCounts(IReadOnlyList<string> ids)
    {
        var result = new Dictionary<string, int>(Comparer);
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result[id] = result.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        return result;
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
