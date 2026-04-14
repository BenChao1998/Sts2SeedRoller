using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed record Sts2RunRequest
{
    public required uint SeedValue { get; init; }

    public required string SeedText { get; init; }

    public required CharacterId Character { get; init; }

    public int AscensionLevel { get; init; }

    public int PlayerCount { get; init; } = 1;

    public bool IncludeAct2 { get; init; }

    public bool IncludeAct3 { get; init; }
}
