using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed class Sts2ExactRouteAnalysisRequest
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public CharacterId Character { get; init; } = CharacterId.Ironclad;

    public IReadOnlyList<CharacterId>? UnlockedCharacters { get; init; }

    public IReadOnlyList<CharacterId>? TeamCharacters { get; init; }

    public int AscensionLevel { get; init; }

    public int PlayerCount { get; init; } = 1;

    public ulong PlayerNetId { get; init; } = NeowGenerationContext.DefaultPlayerNetId;

    public Sts2AncientAvailability? AncientAvailability { get; init; }

    public bool IncludeDarvSharedAncient { get; init; } = true;

    public IReadOnlyList<Sts2ExactRouteEventTargetRequest> EventTargets { get; init; } = Array.Empty<Sts2ExactRouteEventTargetRequest>();

    public IReadOnlyList<Sts2ExactRouteRelicTargetRequest> RelicTargets { get; init; } = Array.Empty<Sts2ExactRouteRelicTargetRequest>();

    public Sts2ExactRouteShopStrategy ShopStrategy { get; init; } = Sts2ExactRouteShopStrategy.TargetRelicsOnly;

    public Sts2ExactRoutePreference Preference { get; init; } = Sts2ExactRoutePreference.None;

    public NeowOptionResult? Act1OpeningOption { get; init; }

    public int? TargetEventActNumber { get; init; }

    public string? TargetEventId { get; init; }

    public int? TargetRelicActNumber { get; init; }

    public string? TargetRelicId { get; init; }

    public int MaxResults { get; init; } = 3;

    public long MaxRouteChecks { get; init; } = 20_000;

    public IReadOnlyList<Sts2ExactRouteEventTargetRequest> GetResolvedEventTargets()
    {
        if (EventTargets.Count > 0)
        {
            return EventTargets;
        }

        return string.IsNullOrWhiteSpace(TargetEventId)
            ? Array.Empty<Sts2ExactRouteEventTargetRequest>()
            : [new Sts2ExactRouteEventTargetRequest(TargetEventActNumber, TargetEventId!)];
    }

    public IReadOnlyList<Sts2ExactRouteRelicTargetRequest> GetResolvedRelicTargets()
    {
        if (RelicTargets.Count > 0)
        {
            return RelicTargets;
        }

        return string.IsNullOrWhiteSpace(TargetRelicId)
            ? Array.Empty<Sts2ExactRouteRelicTargetRequest>()
            : [new Sts2ExactRouteRelicTargetRequest(TargetRelicActNumber, TargetRelicId!)];
    }

    internal Sts2AncientAvailability ResolveAncientAvailability()
    {
        return AncientAvailability ?? Sts2AncientAvailability.FromLegacyDarvFlag(IncludeDarvSharedAncient);
    }
}

public sealed record Sts2ExactRouteEventTargetRequest(int? ActNumber, string EventId);

public sealed record Sts2ExactRouteRelicTargetRequest(int? ActNumber, string RelicId);

public enum Sts2ExactRouteShopStrategy
{
    TargetRelicsOnly = 0,
    NoPurchase = 1,
    CardRemovalOnly = 2
}

public enum Sts2ExactRoutePreference
{
    None = 0,
    MostElites = 1,
    FewestElites = 2,
    MostRestSites = 3,
    MostQuestionMarks = 4
}

public sealed class Sts2ExactRouteAnalysis
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public required int CheckedRoutes { get; init; }

    public required int FoundRouteCount { get; init; }

    public required bool WasTruncated { get; init; }

    public required IReadOnlyList<Sts2ExactMapAct> MapActs { get; init; }

    public required IReadOnlyList<Sts2ExactRouteMatch> Matches { get; init; }
}

public sealed class Sts2ExactMapAct
{
    public required int ActNumber { get; init; }

    public required IReadOnlyList<Sts2ExactMapNode> Nodes { get; init; }

    public required IReadOnlyList<Sts2ExactMapLink> Links { get; init; }
}

public sealed class Sts2ExactMapNode
{
    public required int Row { get; init; }

    public required int Col { get; init; }

    public required string PointType { get; init; }
}

public sealed class Sts2ExactMapLink
{
    public required int FromRow { get; init; }

    public required int FromCol { get; init; }

    public required int ToRow { get; init; }

    public required int ToCol { get; init; }
}

public sealed class Sts2ExactRouteMatch
{
    public required IReadOnlyList<Sts2ExactRouteAct> Acts { get; init; }
}

public sealed class Sts2ExactRouteAct
{
    public required int ActNumber { get; init; }

    public string? AncientId { get; init; }

    public required IReadOnlyList<string> AncientRelics { get; init; }

    public required IReadOnlyList<Sts2ExactRouteStep> Steps { get; init; }

    public required IReadOnlyList<Sts2ExactRouteRoom> Rooms { get; init; }
}

public sealed class Sts2ExactRouteStep
{
    public required int FromRow { get; init; }

    public required int FromCol { get; init; }

    public required int ToRow { get; init; }

    public required int ToCol { get; init; }
}

public sealed class Sts2ExactRouteRoom
{
    public required int Row { get; init; }

    public required int Col { get; init; }

    public required string PointType { get; init; }

    public required string RoomType { get; init; }

    public string? EventId { get; init; }

    public required IReadOnlyList<string> RelicIds { get; init; }

    public IReadOnlyList<string> DisplayRelicIds { get; init; } = Array.Empty<string>();
}

internal sealed record Sts2GeneratedRouteNode(
    int Row,
    int Col,
    string PointType,
    IReadOnlyList<string> ChildPointTypes);

internal sealed record Sts2GeneratedActRoute(
    int ActNumber,
    IReadOnlyList<Sts2GeneratedRouteNode> Nodes);
