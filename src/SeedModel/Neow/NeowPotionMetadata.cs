using System;
using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record NeowPotionMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rarity")]
    public required string Rarity { get; init; }

    [JsonIgnore]
    public PotionRarity ParsedRarity =>
        Enum.TryParse(Rarity, ignoreCase: true, out PotionRarity rarity)
            ? rarity
            : PotionRarity.None;
}

