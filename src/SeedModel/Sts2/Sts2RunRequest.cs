using SeedModel.Neow;

namespace SeedModel.Sts2;

public sealed record Sts2RunRequest
{
    public required uint SeedValue { get; init; }

    public required string SeedText { get; init; }

    public required CharacterId Character { get; init; }

    public IReadOnlyList<CharacterId>? UnlockedCharacters { get; init; }

    public IReadOnlyList<CharacterId>? TeamCharacters { get; init; }

    public int AscensionLevel { get; init; }

    public int PlayerCount { get; init; } = 1;

    public ulong PlayerNetId { get; init; } = NeowGenerationContext.DefaultPlayerNetId;

    /// <summary>
    /// Player slot index used by the game (v0.107.1) when seeding per-player
    /// streams (events, rewards, shops). The solo player has slot index 0.
    /// </summary>
    public ulong PlayerSlotIndex { get; init; } = NeowGenerationContext.DefaultPlayerSlotIndex;

    public string? ActOneName { get; init; }

    public Sts2AncientAvailability? AncientAvailability { get; init; }

    public bool IncludeDarvSharedAncient { get; init; } = true;

    public bool IncludeAct2 { get; init; }

    public bool IncludeAct3 { get; init; }

    public int? SeaGlassPreviewSamples { get; init; }

    internal Sts2AncientAvailability ResolveAncientAvailability()
    {
        return AncientAvailability ?? Sts2AncientAvailability.FromLegacyDarvFlag(IncludeDarvSharedAncient);
    }
}
