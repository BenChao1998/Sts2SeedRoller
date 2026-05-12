using System;
using System.Collections.Generic;

namespace SeedModel.Neow;

internal static class NeowCardMetadataNormalizer
{
    private static readonly HashSet<string> CombatRewardExcludedCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "FEED",
        "NOT_YET",
        "ROYALTIES",
        "THE_HUNT"
    };

    private static readonly HashSet<string> MultiplayerOnlyCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEACON_OF_HOPE",
        "BELIEVE_IN_YOU",
        "COORDINATE",
        "DEMONIC_SHIELD",
        "ENERGY_SURGE",
        "FLANKING",
        "GANG_UP",
        "GLIMPSE_BEYOND",
        "HAMMER_TIME",
        "HUDDLE_UP",
        "IGNITION",
        "INTERCEPT",
        "KNOCKDOWN",
        "LARGESSE",
        "LEGION_OF_BONE",
        "LIFT",
        "MIMIC",
        "RALLY",
        "SNEAKY",
        "TAG_TEAM",
        "TANK"
    };

    private static readonly HashSet<string> SingleplayerOnlyCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "STRATAGEM",
        "WELL_LAID_PLANS"
    };

    public static NeowCardMetadata Normalize(NeowCardMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (CombatRewardExcludedCards.Contains(metadata.Id))
        {
            metadata = metadata with { CanBeGeneratedInCombat = false };
        }

        if (MultiplayerOnlyCards.Contains(metadata.Id))
        {
            return metadata with { MultiplayerConstraint = nameof(CardMultiplayerConstraint.MultiplayerOnly) };
        }

        if (SingleplayerOnlyCards.Contains(metadata.Id))
        {
            return metadata with { MultiplayerConstraint = nameof(CardMultiplayerConstraint.SingleplayerOnly) };
        }

        return metadata;
    }
}
