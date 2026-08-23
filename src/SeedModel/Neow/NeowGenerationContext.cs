using System;
using SeedModel.Rng;

namespace SeedModel.Neow;

public sealed record NeowGenerationContext
{
    public const ulong DefaultPlayerNetId = 1;

    public const ulong DefaultPlayerSlotIndex = 0;

    private const ulong NeowEventHash = 348630327; // hash("NEOW")

    public uint Seed { get; init; }

    public uint RunSeed { get; init; }

    public int PlayerCount { get; init; } = 1;

    public ulong PlayerNetId { get; init; } = DefaultPlayerNetId;

    public ulong PlayerSlotIndex { get; init; } = DefaultPlayerSlotIndex;

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
        int ascensionLevel = 0,
        ulong playerNetId = DefaultPlayerNetId,
        ulong playerSlotIndex = DefaultPlayerSlotIndex)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        }

        // Game v0.107.1 seeds the Neow event with
        //   Rng(runSeed + playerSlotIndex + hash("NEOW"))
        // where the solo player has slot index 0. Older versions used the
        // net id as the player addend; the active engine decides which.
        var playerAddend = GameRng.PlayerStreamAddend(playerNetId, playerSlotIndex);
        var neowSeed = unchecked((uint)((ulong)seed + playerAddend + NeowEventHash));

        return new NeowGenerationContext
        {
            RunSeed = seed,
            Seed = neowSeed,
            PlayerCount = playerCount,
            PlayerNetId = playerNetId,
            PlayerSlotIndex = playerSlotIndex,
            ScrollBoxesEligible = scrollBoxesEligible,
            HasRunModifiers = hasRunModifiers,
            Character = character,
            AscensionLevel = Math.Max(ascensionLevel, 0)
        };
    }
}
