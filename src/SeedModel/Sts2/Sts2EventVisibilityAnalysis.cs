using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed class Sts2EventVisibilityRequest
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

public sealed class Sts2EventVisibilityAnalysis
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public required CharacterId Character { get; init; }

    public required int PlayerCount { get; init; }

    public required int Samples { get; init; }

    public required int EarlyWindow { get; init; }

    public required IReadOnlyList<Sts2EventVisibilityProfileResult> Profiles { get; init; }
}

public sealed class Sts2EventVisibilityProfileResult
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<Sts2EventVisibilityActSummary> Acts { get; init; }

    public required IReadOnlyList<Sts2EventVisibilityRankedEvent> EarlyEvents { get; init; }

    public required IReadOnlyList<Sts2EventVisibilityRankedEvent> SeenEvents { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> EarlySamples { get; init; }

    public bool IsComposite { get; init; }

    public bool IsRecommended { get; init; }
}

public sealed class Sts2EventVisibilityActSummary
{
    public required int ActNumber { get; init; }

    public required IReadOnlyList<Sts2WeightedIntChance> UnknownCounts { get; init; }

    public required IReadOnlyList<Sts2WeightedIntChance> EventCounts { get; init; }

    public required double AncientVisitChance { get; init; }
}

public sealed class Sts2EventVisibilityRankedEvent
{
    public required int ActNumber { get; init; }

    public required string EventId { get; init; }

    public required double EarlyProbability { get; init; }

    public required double SeenProbability { get; init; }

    public required double AverageFirstOpportunity { get; init; }

    public required Sts2EventVisibilitySource MostCommonSource { get; init; }

    public int RouteCount { get; init; } = 1;

    public int EarlyRouteSupportCount { get; init; } = 1;

    public int SeenRouteSupportCount { get; init; } = 1;

    public double MinEarlyProbability { get; init; }

    public double MaxEarlyProbability { get; init; }

    public double MinSeenProbability { get; init; }

    public double MaxSeenProbability { get; init; }
}

public enum Sts2EventVisibilitySource
{
    Unknown,
    AncientAct2,
    AncientAct3
}
