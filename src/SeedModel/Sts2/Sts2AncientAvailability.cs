using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed record Sts2AncientAvailability
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> EmptyActAncients =
        new Dictionary<int, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> EmptyActEvents =
        new Dictionary<int, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> EmptyCharacterRelics =
        new Dictionary<CharacterId, IReadOnlyList<string>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SharedRelicEpochs =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["RELIC1_EPOCH"] = ["UNSETTLING_LAMP", "INTIMIDATING_HELMET", "REPTILE_TRINKET"],
            ["RELIC2_EPOCH"] = ["BOOK_OF_FIVE_RINGS", "ICE_CREAM", "KUSARIGAMA"],
            ["RELIC3_EPOCH"] = ["VEXING_PUZZLEBOX", "RIPPLE_BASIN", "FESTIVE_POPPER"],
            ["RELIC4_EPOCH"] = ["MINIATURE_CANNON", "TUNGSTEN_ROD", "WHITE_STAR"],
            ["RELIC5_EPOCH"] = ["TINY_MAILBOX", "JOSS_PAPER", "BEATING_REMNANT"]
        };

    private static readonly IReadOnlyDictionary<CharacterId, IReadOnlyDictionary<string, IReadOnlyList<string>>> CharacterRelicEpochs =
        new Dictionary<CharacterId, IReadOnlyDictionary<string, IReadOnlyList<string>>>
        {
            [CharacterId.Ironclad] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["IRONCLAD3_EPOCH"] = ["RED_SKULL", "PAPER_PHROG", "RUINED_HELMET"],
                ["IRONCLAD6_EPOCH"] = ["SELF_FORMING_CLAY", "CHARONS_ASHES", "DEMON_TONGUE"]
            },
            [CharacterId.Silent] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["SILENT3_EPOCH"] = ["TOUGH_BANDAGES", "PAPER_KRANE", "TINGSHA"],
                ["SILENT6_EPOCH"] = ["TWISTED_FUNNEL", "SNECKO_SKULL", "HELICAL_DART"]
            },
            [CharacterId.Defect] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DEFECT3_EPOCH"] = ["DATA_DISK", "SYMBIOTIC_VIRUS", "METRONOME"],
                ["DEFECT6_EPOCH"] = ["GOLD_PLATED_CABLES", "EMOTION_CHIP", "POWER_CELL"]
            },
            [CharacterId.Necrobinder] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["NECROBINDER3_EPOCH"] = ["BONE_FLUTE", "FUNERARY_MASK", "BOOK_REPAIR_KNIFE"],
                ["NECROBINDER6_EPOCH"] = ["IVORY_TILE", "BIG_HAT", "BOOKMARK"]
            },
            [CharacterId.Regent] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["REGENT3_EPOCH"] = ["FENCING_MANUAL", "GALACTIC_DUST", "LUNAR_PASTRY"],
                ["REGENT6_EPOCH"] = ["REGALITE", "MINI_REGENT", "ORANGE_DOUGH"]
            }
        };

    public static Sts2AncientAvailability Default { get; } = new();

    public IReadOnlyList<string> DisabledSharedAncientIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<int, IReadOnlyList<string>> DisabledActAncientIds { get; init; } = EmptyActAncients;

    public IReadOnlyDictionary<int, IReadOnlyList<string>> DisabledActEventIds { get; init; } = EmptyActEvents;

    public IReadOnlyList<string> DisabledSharedRelicIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> DisabledCharacterRelicIds { get; init; } = EmptyCharacterRelics;

    public bool IsUnderdocksUnlocked { get; init; } = true;

    public bool HasDiscoveredUnderdocks { get; init; } = true;

    public bool IsSharedAncientAvailable(string ancientId)
    {
        return !ContainsId(DisabledSharedAncientIds, ancientId);
    }

    public bool IsActAncientAvailable(int actNumber, string ancientId)
    {
        return !DisabledActAncientIds.TryGetValue(actNumber, out var disabledAncients) ||
               !ContainsId(disabledAncients, ancientId);
    }

    public bool IsActEventAvailable(int actNumber, string eventId)
    {
        return !DisabledActEventIds.TryGetValue(actNumber, out var disabledEvents) ||
               !ContainsId(disabledEvents, eventId);
    }

    public bool IsSharedRelicAvailable(string relicId)
    {
        return !ContainsId(DisabledSharedRelicIds, relicId);
    }

    public bool IsCharacterRelicAvailable(CharacterId character, string relicId)
    {
        return !DisabledCharacterRelicIds.TryGetValue(character, out var disabledRelics) ||
               !ContainsId(disabledRelics, relicId);
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
        return FromProgressState(revealedEpochIds, discoveredActIds: null);
    }

    public static Sts2AncientAvailability FromProgressState(
        IEnumerable<string>? revealedEpochIds,
        IEnumerable<string>? discoveredActIds)
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
        var disabledActEvents = new Dictionary<int, IReadOnlyList<string>>();
        var disabledSharedRelics = new List<string>();
        var disabledCharacterRelics = new Dictionary<CharacterId, IReadOnlyList<string>>();
        var discoveredActs = discoveredActIds?
            .Where(actId => !string.IsNullOrWhiteSpace(actId))
            .Select(actId => actId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (epochId, relicIds) in SharedRelicEpochs)
        {
            if (!revealed.Contains(epochId))
            {
                disabledSharedRelics.AddRange(relicIds);
            }
        }

        foreach (var (character, epochs) in CharacterRelicEpochs)
        {
            var disabledRelics = new List<string>();
            foreach (var (epochId, relicIds) in epochs)
            {
                if (!revealed.Contains(epochId))
                {
                    disabledRelics.AddRange(relicIds);
                }
            }

            if (disabledRelics.Count > 0)
            {
                disabledCharacterRelics[character] = disabledRelics;
            }
        }

        // Official unlock checks currently gate shared DARV and Act 2 OROBAS.
        // Centralizing them here keeps roll, pool analysis, and save replay
        // aligned with the same progress.save-driven content availability.
        if (!revealed.Contains("DARV_EPOCH"))
        {
            disabledSharedAncients.Add("DARV");
        }

        if (!revealed.Contains("OROBAS_EPOCH"))
        {
            disabledActAncients[2] = ["OROBAS"];
        }

        // Official ActModel.GenerateRooms removes these epoch-gated events
        // before the pool is shuffled, so they must be filtered here too.
        if (!revealed.Contains("EVENT1_EPOCH"))
        {
            disabledActEvents[1] = ["TRASH_HEAP"];
        }

        if (!revealed.Contains("EVENT2_EPOCH"))
        {
            disabledActEvents[3] = ["REFLECTIONS"];
        }

        if (!revealed.Contains("EVENT3_EPOCH"))
        {
            disabledActEvents[2] = ["COLORFUL_PHILOSOPHERS"];
        }

        var isUnderdocksUnlocked = revealed.Contains("UNDERDOCKS_EPOCH");
        var hasDiscoveredUnderdocks = discoveredActs?.Contains("ACT.UNDERDOCKS") ?? true;

        if (disabledSharedAncients.Count == 0 &&
            disabledActAncients.Count == 0 &&
            disabledActEvents.Count == 0 &&
            disabledSharedRelics.Count == 0 &&
            disabledCharacterRelics.Count == 0 &&
            isUnderdocksUnlocked &&
            hasDiscoveredUnderdocks)
        {
            return Default;
        }

        return new Sts2AncientAvailability
        {
            DisabledSharedAncientIds = disabledSharedAncients,
            DisabledActAncientIds = disabledActAncients,
            DisabledActEventIds = disabledActEvents,
            DisabledSharedRelicIds = disabledSharedRelics,
            DisabledCharacterRelicIds = disabledCharacterRelics,
            IsUnderdocksUnlocked = isUnderdocksUnlocked,
            HasDiscoveredUnderdocks = hasDiscoveredUnderdocks
        };
    }

    private static bool ContainsId(IReadOnlyList<string> source, string id)
    {
        return source.Any(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
    }
}
