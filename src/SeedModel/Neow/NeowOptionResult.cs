namespace SeedModel.Neow;

public sealed record NeowOptionResult
{
    public required string Id { get; init; }

    public required string RelicId { get; init; }

    public required string Pool { get; init; }

    public required NeowOptionKind Kind { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Note { get; init; }

    public IReadOnlyList<RewardDetail> Details { get; init; } = Array.Empty<RewardDetail>();

    internal NeowDetailHint DetailHint { get; init; }
}
