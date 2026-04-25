using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed class Sts2RelicVisibilityRequest
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public CharacterId Character { get; init; } = CharacterId.Ironclad;

    public IReadOnlyList<CharacterId>? UnlockedCharacters { get; init; }

    public int AscensionLevel { get; init; }

    public int PlayerCount { get; init; } = 1;

    public int Samples { get; init; } = 8_000;

    public int EarlyWindow { get; init; } = 5;

    public Sts2AncientAvailability? AncientAvailability { get; init; }

    public bool IncludeDarvSharedAncient { get; init; } = true;

    internal Sts2AncientAvailability ResolveAncientAvailability()
    {
        return AncientAvailability ?? Sts2AncientAvailability.FromLegacyDarvFlag(IncludeDarvSharedAncient);
    }
}

public sealed class Sts2RelicVisibilityAnalysis
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public required CharacterId Character { get; init; }

    public required int PlayerCount { get; init; }

    public required int Samples { get; init; }

    public required int EarlyWindow { get; init; }

    public required int SharedBagSize { get; init; }

    public required int PlayerBagSize { get; init; }

    public required int Act3OnlyGateTrackedRelics { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityAncientAct> AncientActs { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityProfileResult> Profiles { get; init; }
}

public sealed class Sts2RelicVisibilityAncientAct
{
    public required int ActNumber { get; init; }

    public required string AncientId { get; init; }

    public required string AncientName { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityAncientOption> Options { get; init; }
}

public sealed class Sts2RelicVisibilityAncientOption
{
    public required string RelicId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Note { get; init; }
}

public sealed class Sts2RelicVisibilityProfileResult
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityActSummary> Acts { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityRankedRelic> EarlyRelics { get; init; }

    public required IReadOnlyList<Sts2RelicVisibilityRankedRelic> SeenRelics { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> EarlySamples { get; init; }
}

public sealed class Sts2RelicVisibilityActSummary
{
    public required int ActNumber { get; init; }

    public required IReadOnlyList<Sts2WeightedIntChance> TreasureCounts { get; init; }

    public required IReadOnlyList<Sts2WeightedIntChance> EliteCounts { get; init; }

    public required IReadOnlyList<Sts2WeightedIntChance> ShopCounts { get; init; }

    public required double AncientVisitChance { get; init; }
}

public sealed class Sts2WeightedIntChance
{
    public required int Value { get; init; }

    public required double Weight { get; init; }
}

public sealed class Sts2RelicVisibilityRankedRelic
{
    public required string RelicId { get; init; }

    public required double EarlyProbability { get; init; }

    public required double SeenProbability { get; init; }

    public required double NonShopSeenProbability { get; init; }

    public required double ShopSeenProbability { get; init; }

    public required double AverageFirstOpportunity { get; init; }

    public required Sts2RelicVisibilitySource MostCommonSource { get; init; }
}

public enum Sts2RelicVisibilitySource
{
    Treasure,
    Elite,
    Shop,
    AncientAct2,
    AncientAct3
}
