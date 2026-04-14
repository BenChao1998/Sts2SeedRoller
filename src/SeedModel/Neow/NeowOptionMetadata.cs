using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed record NeowOptionMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("relic_id")]
    public required string RelicId { get; init; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NeowOptionKind Kind { get; init; } = NeowOptionKind.Positive;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
