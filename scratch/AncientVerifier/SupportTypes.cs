using System.Collections.Generic;

sealed record OfficialActDefinition(
    string Name,
    int Number,
    int BaseRooms,
    int WeakRooms,
    IReadOnlyList<string> Ancients,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Encounters);

sealed record EncounterMetadataRecord(
    string RoomType,
    bool IsWeak,
    IReadOnlyList<string> Tags);

sealed class CurrentEncounterData
{
    public string RoomType { get; init; } = string.Empty;

    public bool IsWeak { get; init; }

    public List<string> Tags { get; init; } = new();
}

sealed record TempActPoolState(
    IReadOnlyList<string> Monsters,
    IReadOnlyList<string> Elites,
    string AncientId);

sealed record CardSourceFeatures(
    string Type,
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> Keywords);

sealed record RebuiltAncientState(
    int RemovableCards,
    int GoopyEligible,
    int InstinctEligible,
    int SwiftEligible,
    bool HasEventPet);

sealed record EncounterSpec(
    string Id,
    EncounterMetadataRecord Metadata);

sealed record PhaseCounter(
    string Phase,
    int Before,
    int After,
    string? Value = null);

sealed record TracedActGeneration(
    int ActNumber,
    string BossId,
    string AncientId,
    IReadOnlyList<string> Monsters,
    IReadOnlyList<string> Elites,
    IReadOnlyList<PhaseCounter> Phases);
