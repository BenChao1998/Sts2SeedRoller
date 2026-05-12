using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedModel.Run;

public sealed record SeedRunMatch
{
    public required IReadOnlyList<NeowOptionResult> NeowOptions { get; init; }

    public required IReadOnlyList<NeowOptionResult> NeowMatches { get; init; }

    public required bool NeowFilterMatched { get; init; }

    public bool AncientFilterMatched { get; init; }

    public bool IsFinalMatch { get; init; }

    public Sts2RunPreview? Sts2Preview { get; init; }

    public bool ShopFilterMatched { get; init; }

    public ShopPreview? ShopPreview { get; init; }

    public bool PoolFilterMatched { get; init; }

    public Sts2SeedAnalysis? PoolAnalysis { get; init; }

    public Sts2EventVisibilityAnalysis? EventVisibilityAnalysis { get; init; }

    public Sts2RelicVisibilityAnalysis? RelicVisibilityAnalysis { get; init; }

    public SeedRunDiagnostics? Diagnostics { get; init; }
}

public sealed record SeedRunDiagnostics
{
    public long TotalElapsedMilliseconds { get; init; }

    public long NeowElapsedMilliseconds { get; init; }

    public long AncientElapsedMilliseconds { get; init; }

    public long PoolAnalysisElapsedMilliseconds { get; init; }

    public long EventTargetedMatchElapsedMilliseconds { get; init; }

    public long RelicTargetedMatchElapsedMilliseconds { get; init; }

    public long ShopElapsedMilliseconds { get; init; }

    public long FinalEventAnalysisElapsedMilliseconds { get; init; }

    public long FinalRelicAnalysisElapsedMilliseconds { get; init; }
}
