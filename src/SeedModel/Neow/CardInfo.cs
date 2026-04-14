using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record CardInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}
