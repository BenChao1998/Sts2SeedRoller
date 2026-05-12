using System.Collections.Generic;

using SeedModel.Neow;

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

    public string? ContextCharacterId { get; init; }

    public List<string> PreviewCardIds { get; init; } = new();

    public Sts2SeaGlassPreview? SeaGlassPreview { get; init; }
}

public sealed class Sts2SeaGlassPreview
{
    public required CharacterId TargetCharacter { get; init; }

    public required int Samples { get; init; }

    public required IReadOnlyList<Sts2SeaGlassPreviewCard> RankedCards { get; init; }
}

public sealed class Sts2SeaGlassPreviewCard
{
    public required string CardId { get; init; }

    public required int SeenCount { get; init; }

    public required double SeenProbability { get; init; }
}
