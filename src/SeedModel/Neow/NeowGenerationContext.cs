using System;

namespace SeedModel.Neow;

public sealed record NeowGenerationContext
{
    private const ulong DefaultPlayerNetId = 1;
    private const ulong NeowEventHash = 348630327; // hash("NEOW")

    public uint Seed { get; init; }

    public uint RunSeed { get; init; }

    public int PlayerCount { get; init; } = 1;

    public bool ScrollBoxesEligible { get; init; }

    public bool HasRunModifiers { get; init; }

    public CharacterId Character { get; init; } = CharacterId.Ironclad;

    public int AscensionLevel { get; init; }

    public static NeowGenerationContext Create(
        uint seed,
        int playerCount = 1,
        bool scrollBoxesEligible = true,
        bool hasRunModifiers = false,
        CharacterId character = CharacterId.Ironclad,
        int ascensionLevel = 0)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        }

        var neowSeed = unchecked((uint)((ulong)seed + DefaultPlayerNetId + NeowEventHash));

        return new NeowGenerationContext
        {
            RunSeed = seed,
            Seed = neowSeed,
            PlayerCount = playerCount,
            ScrollBoxesEligible = scrollBoxesEligible,
            HasRunModifiers = hasRunModifiers,
            Character = character,
            AscensionLevel = Math.Max(ascensionLevel, 0)
        };
    }
}
