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
        IReadOnlyList<NeowDerivedBindingFilter> derivedBindingFilters,
        bool hasCriteria)
    {
        Kind = kind;
        RelicTerms = relicTerms;
        RelicIds = relicIds;
        CardIds = cardIds;
        PotionIds = potionIds;
        DerivedBindingFilters = derivedBindingFilters;
        HasCriteria = hasCriteria;
    }

    public NeowOptionKind? Kind { get; }

    public bool HasCriteria { get; }

    private IReadOnlyList<string> RelicTerms { get; }

    private IReadOnlyList<string> RelicIds { get; }

    private IReadOnlyList<string> CardIds { get; }

    private IReadOnlyList<string> PotionIds { get; }

    private IReadOnlyList<NeowDerivedBindingFilter> DerivedBindingFilters { get; }

    public static NeowOptionFilter Create(
        NeowOptionKind? kind,
        IEnumerable<string>? relicTerms,
        IEnumerable<string>? relicIds,
        IEnumerable<string>? cardIds,
        IEnumerable<string>? potionIds,
        IEnumerable<NeowDerivedBindingFilter>? derivedBindingFilters = null)
    {
        var normalizedRelicTerms = NormalizeTerms(relicTerms);
        var normalizedRelicIds = NormalizeTerms(relicIds);
        var normalizedCardIds = NormalizeTerms(cardIds, deduplicate: false);
        var normalizedPotionIds = NormalizeTerms(potionIds, deduplicate: false);
        var normalizedDerivedBindingFilters = NormalizeDerivedBindingFilters(derivedBindingFilters);

        var hasCriteria =
            kind.HasValue ||
            normalizedRelicTerms.Count > 0 ||
            normalizedRelicIds.Count > 0 ||
            normalizedCardIds.Count > 0 ||
            normalizedPotionIds.Count > 0 ||
            normalizedDerivedBindingFilters.Count > 0;

        return new NeowOptionFilter(
            kind,
            normalizedRelicTerms,
            normalizedRelicIds,
            normalizedCardIds,
            normalizedPotionIds,
            normalizedDerivedBindingFilters,
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

        if (DerivedBindingFilters.Count > 0 &&
            !DerivedBindingFilters.All(filter => MatchesDerivedBinding(option, filter)))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesDerivedBinding(NeowOptionResult option, NeowDerivedBindingFilter filter)
    {
        if (!string.Equals(option.RelicId, filter.PrimarySourceRelicId, Comparison))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.SecondarySourceRelicId) &&
            !option.Details.Any(detail =>
                detail.Type == RewardDetailType.Relic &&
                string.Equals(detail.ModelId, filter.SecondarySourceRelicId, Comparison)))
        {
            return false;
        }

        var sourcePath = filter.GetSourcePath();
        var scopedDetails = option.Details
            .Where(detail => string.Equals(detail.SourcePath, sourcePath, Comparison))
            .ToList();

        if (filter.RelicIds.Count > 0 &&
            !MatchesDetailIds(scopedDetails, RewardDetailType.Relic, filter.RelicIds))
        {
            return false;
        }

        if (filter.CardIds.Count > 0 &&
            !MatchesDerivedCardIds(option, filter, scopedDetails))
        {
            return false;
        }

        if (filter.PotionIds.Count > 0 &&
            !MatchesDetailIds(scopedDetails, RewardDetailType.Potion, filter.PotionIds))
        {
            return false;
        }

        return filter.HasCriteria;
    }

    private static bool MatchesDerivedCardIds(
        NeowOptionResult option,
        NeowDerivedBindingFilter filter,
        IReadOnlyList<RewardDetail> scopedDetails)
    {
        if (IsKaleidoscopeSource(filter) &&
            filter.CardIds.Count > 1)
        {
            return MatchesKaleidoscopeCardsInDistinctBundles(scopedDetails, filter.CardIds);
        }

        return MatchesDetailIds(scopedDetails, RewardDetailType.Card, filter.CardIds);
    }

    private static bool IsKaleidoscopeSource(NeowDerivedBindingFilter filter)
    {
        if (string.Equals(filter.PrimarySourceRelicId, NeowOptionIds.Kaleidoscope, Comparison))
        {
            return true;
        }

        return string.Equals(filter.SecondarySourceRelicId, NeowOptionIds.Kaleidoscope, Comparison);
    }

    private static bool MatchesKaleidoscopeCardsInDistinctBundles(
        IReadOnlyList<RewardDetail> scopedDetails,
        IReadOnlyList<string> requiredCardIds)
    {
        var requiredOccurrences = requiredCardIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        if (requiredOccurrences.Count == 0)
        {
            return true;
        }

        var bundleLookup = scopedDetails
            .Where(detail => detail.Type == RewardDetailType.Card &&
                             !string.IsNullOrWhiteSpace(detail.ModelId))
            .Select(detail => new
            {
                BundleKey = TryGetKaleidoscopeBundleKey(detail.Label),
                ModelId = detail.ModelId!
            })
            .Where(entry => entry.BundleKey is not null)
            .GroupBy(entry => entry.BundleKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        if (bundleLookup.Count == 0 || requiredOccurrences.Count > bundleLookup.Count)
        {
            return false;
        }

        return TryMatchRequiredCardsToDistinctBundles(
            requiredOccurrences,
            0,
            bundleLookup,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryMatchRequiredCardsToDistinctBundles(
        IReadOnlyList<string> requiredOccurrences,
        int index,
        IReadOnlyDictionary<string, HashSet<string>> bundleLookup,
        HashSet<string> usedBundles)
    {
        if (index >= requiredOccurrences.Count)
        {
            return true;
        }

        var requiredCardId = requiredOccurrences[index];
        foreach (var bundle in bundleLookup)
        {
            if (usedBundles.Contains(bundle.Key) ||
                !bundle.Value.Contains(requiredCardId))
            {
                continue;
            }

            usedBundles.Add(bundle.Key);
            if (TryMatchRequiredCardsToDistinctBundles(requiredOccurrences, index + 1, bundleLookup, usedBundles))
            {
                return true;
            }

            usedBundles.Remove(bundle.Key);
        }

        return false;
    }

    private static string? TryGetKaleidoscopeBundleKey(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        const string prefix = "卡牌包选项";
        if (!label.StartsWith(prefix, Comparison))
        {
            return null;
        }

        var suffix = label[prefix.Length..];
        var separatorIndex = suffix.IndexOf('-');
        if (separatorIndex <= 0)
        {
            return null;
        }

        return suffix[..separatorIndex].Trim();
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

    private static List<NeowDerivedBindingFilter> NormalizeDerivedBindingFilters(IEnumerable<NeowDerivedBindingFilter>? filters)
    {
        if (filters == null)
        {
            return new List<NeowDerivedBindingFilter>();
        }

        return filters
            .Select(filter => filter.Normalize())
            .Where(filter => filter.HasCriteria)
            .ToList();
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

public sealed record NeowDerivedBindingFilter(
    string PrimarySourceRelicId,
    string? SecondarySourceRelicId,
    IReadOnlyList<string> RelicIds,
    IReadOnlyList<string> CardIds,
    IReadOnlyList<string> PotionIds)
{
    public bool HasCriteria =>
        !string.IsNullOrWhiteSpace(PrimarySourceRelicId) &&
        (!string.IsNullOrWhiteSpace(SecondarySourceRelicId) ||
         RelicIds.Count > 0 ||
         CardIds.Count > 0 ||
         PotionIds.Count > 0);

    public string GetSourcePath() =>
        string.IsNullOrWhiteSpace(SecondarySourceRelicId)
            ? PrimarySourceRelicId
            : $"{PrimarySourceRelicId}>{SecondarySourceRelicId}";

    public NeowDerivedBindingFilter Normalize()
    {
        return new NeowDerivedBindingFilter(
            NormalizeValue(PrimarySourceRelicId) ?? string.Empty,
            NormalizeValue(SecondarySourceRelicId),
            NormalizeList(RelicIds),
            NormalizeList(CardIds),
            NormalizeList(PotionIds));
    }

    private static string? NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
