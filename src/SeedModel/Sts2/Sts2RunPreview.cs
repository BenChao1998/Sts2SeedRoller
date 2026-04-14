using System.Collections.Generic;

namespace SeedModel.Sts2;

public sealed class Sts2RunPreview
{
    public required uint Seed { get; init; }

    public required string SeedText { get; init; }

    public List<Sts2ActPreview> Acts { get; } = new();
}

public sealed class Sts2ActPreview
{
    public required int ActNumber { get; init; }

    public string? AncientId { get; init; }

    public string? AncientName { get; init; }

    public List<Sts2AncientOption> AncientOptions { get; } = new();
}

public sealed class Sts2AncientOption
{
    public required string OptionId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? RelicId { get; init; }

    public bool WasChosen { get; init; }

    public string? Note { get; init; }
}
