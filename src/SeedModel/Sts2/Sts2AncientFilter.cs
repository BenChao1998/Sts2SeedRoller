using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed record Sts2AncientFilter
{
    public static Sts2AncientFilter Disabled { get; } = new();

    public string? Act2AncientId { get; init; }

    public string? Act3AncientId { get; init; }

    public IReadOnlyList<string> Act2OptionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act3OptionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act2SeaGlassOtherCharacterIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Act2SeaGlassCardIds { get; init; } = Array.Empty<string>();

    public double? Act2SeaGlassCardSeenThreshold { get; init; }

    public int SeaGlassPreviewSamples { get; init; } = 1000;

    public bool HasAct2Criteria => !string.IsNullOrWhiteSpace(Act2AncientId) || Act2OptionIds.Count > 0 || Act2SeaGlassOtherCharacterIds.Count > 0 || Act2SeaGlassCardIds.Count > 0;

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

        return MatchesAct(preview, 2, Act2AncientId, Act2OptionIds, Act2SeaGlassOtherCharacterIds, Act2SeaGlassCardIds, Act2SeaGlassCardSeenThreshold) &&
               MatchesAct(preview, 3, Act3AncientId, Act3OptionIds);
    }

    private static bool MatchesAct(
        Sts2RunPreview preview,
        int actNumber,
        string? expectedAncientId,
        IReadOnlyList<string> requiredOptionIds,
        IReadOnlyList<string>? requiredSeaGlassCharacterIds = null,
        IReadOnlyList<string>? requiredSeaGlassCardIds = null,
        double? requiredSeaGlassCardSeenThreshold = null)
    {
        var hasAncientCriterion = !string.IsNullOrWhiteSpace(expectedAncientId);
        var hasOptionCriterion = requiredOptionIds.Count > 0;
        var hasSeaGlassCharacterCriterion = requiredSeaGlassCharacterIds != null && requiredSeaGlassCharacterIds.Count > 0;
        var hasSeaGlassCardCriterion = requiredSeaGlassCardIds != null && requiredSeaGlassCardIds.Count > 0;

        if (!hasAncientCriterion && !hasOptionCriterion && !hasSeaGlassCharacterCriterion && !hasSeaGlassCardCriterion)
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
            if (!hasSeaGlassCharacterCriterion && !hasSeaGlassCardCriterion)
            {
                return true;
            }

            return MatchesSeaGlass(act, requiredSeaGlassCharacterIds, requiredSeaGlassCardIds, requiredSeaGlassCardSeenThreshold);
        }

        var optionMatched = act.AncientOptions.Any(option =>
            requiredOptionIds.Contains(option.OptionId, StringComparer.OrdinalIgnoreCase));
        if (!optionMatched)
        {
            return false;
        }

        if (!hasSeaGlassCharacterCriterion && !hasSeaGlassCardCriterion)
        {
            return true;
        }

        return MatchesSeaGlass(act, requiredSeaGlassCharacterIds, requiredSeaGlassCardIds, requiredSeaGlassCardSeenThreshold);
    }

    private static bool MatchesSeaGlass(
        Sts2ActPreview act,
        IReadOnlyList<string>? requiredSeaGlassCharacterIds,
        IReadOnlyList<string>? requiredSeaGlassCardIds,
        double? requiredSeaGlassCardSeenThreshold)
    {
        return act.AncientOptions.Any(option =>
            string.Equals(option.OptionId, "SEA_GLASS", StringComparison.OrdinalIgnoreCase) &&
            MatchesSeaGlassOption(option, requiredSeaGlassCharacterIds, requiredSeaGlassCardIds, requiredSeaGlassCardSeenThreshold));
    }

    private static bool MatchesSeaGlassOption(
        Sts2AncientOption option,
        IReadOnlyList<string>? requiredSeaGlassCharacterIds,
        IReadOnlyList<string>? requiredSeaGlassCardIds,
        double? requiredSeaGlassCardSeenThreshold)
    {
        if (requiredSeaGlassCharacterIds != null && requiredSeaGlassCharacterIds.Count > 0)
        {
            var characterId = option.ContextCharacterId ?? ParseSeaGlassCharacterId(option.Note);
            if (!requiredSeaGlassCharacterIds.Contains(characterId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (requiredSeaGlassCardIds != null && requiredSeaGlassCardIds.Count > 0)
        {
            foreach (var cardId in requiredSeaGlassCardIds)
            {
                if (requiredSeaGlassCardSeenThreshold.HasValue && option.SeaGlassPreview != null)
                {
                    var rankedCard = option.SeaGlassPreview.RankedCards.FirstOrDefault(card =>
                        string.Equals(card.CardId, cardId, StringComparison.OrdinalIgnoreCase));
                    if (rankedCard == null || rankedCard.SeenProbability < requiredSeaGlassCardSeenThreshold.Value)
                    {
                        return false;
                    }
                }
                else if (!option.PreviewCardIds.Contains(cardId, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string? ParseSeaGlassCharacterId(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        const string prefix = "Character:";
        var value = note.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? note[prefix.Length..].Trim()
            : note.Trim();

        return value switch
        {
            "Ironclad" => CharacterId.Ironclad.ToString(),
            "Silent Huntress" => CharacterId.Silent.ToString(),
            "Defect" => CharacterId.Defect.ToString(),
            "Necrobinder" => CharacterId.Necrobinder.ToString(),
            "Regent" => CharacterId.Regent.ToString(),
            _ => null
        };
    }
}
