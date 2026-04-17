using System;
using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record NeowRelicMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rarity")]
    public required string Rarity { get; init; }

    [JsonIgnore]
    public RelicRarity ParsedRarity =>
        Enum.TryParse(Rarity, ignoreCase: true, out RelicRarity rarity)
            ? rarity
            : RelicRarity.Common;

    [JsonIgnore]
    public int MerchantCost => ParsedRarity switch
    {
        RelicRarity.Common => 175,
        RelicRarity.Uncommon => 225,
        RelicRarity.Rare => 275,
        RelicRarity.Shop => 200,
        RelicRarity.Ancient => 999,
        _ => 200
    };
}
