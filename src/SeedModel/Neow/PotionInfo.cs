using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record PotionInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
