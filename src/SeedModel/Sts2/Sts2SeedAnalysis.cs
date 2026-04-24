using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed class Sts2SeedAnalysisRequest
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public CharacterId Character { get; init; } = CharacterId.Ironclad;

    public IReadOnlyList<CharacterId>? UnlockedCharacters { get; init; }

    public int AscensionLevel { get; init; }

    public bool IncludeDarvSharedAncient { get; init; } = true;
}

public sealed class Sts2SeedAnalysis
{
    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public required IReadOnlyList<Sts2ActPoolPreview> Acts { get; init; }

    public required IReadOnlyList<Sts2RelicPoolPreviewGroup> SharedRelicPools { get; init; }

    public required IReadOnlyList<Sts2RelicPoolPreviewGroup> PlayerRelicPools { get; init; }
}

public sealed class Sts2ActPoolPreview
{
    public required int ActNumber { get; init; }

    public required string ActName { get; init; }

    public required IReadOnlyList<string> EventPool { get; init; }

    public required IReadOnlyList<string> MonsterPool { get; init; }

    public required IReadOnlyList<string> ElitePool { get; init; }
}

public sealed class Sts2RelicPoolPreviewGroup
{
    public required string Rarity { get; init; }

    public required IReadOnlyList<string> Relics { get; init; }
}
