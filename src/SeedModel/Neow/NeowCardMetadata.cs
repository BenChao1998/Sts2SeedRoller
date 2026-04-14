using System;
using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record NeowCardMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rarity")]
    public required string Rarity { get; init; }

    [JsonPropertyName("multiplayerConstraint")]
    public string MultiplayerConstraint { get; init; } = nameof(CardMultiplayerConstraint.None);

    [JsonIgnore]
    public CardRarity ParsedRarity =>
        Enum.TryParse(Rarity, ignoreCase: true, out CardRarity rarity)
            ? rarity
            : CardRarity.None;

    [JsonIgnore]
    public CardMultiplayerConstraint ParsedConstraint =>
        Enum.TryParse(MultiplayerConstraint, ignoreCase: true, out CardMultiplayerConstraint constraint)
            ? constraint
            : CardMultiplayerConstraint.None;
}
