using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SeedModel.Sts2.Ancients;

internal sealed record AncientOptionMetadata
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }
}

internal sealed class AncientOptionCatalog
{
    private readonly Dictionary<string, AncientOptionMetadata> _options;

    private AncientOptionCatalog(IEnumerable<AncientOptionMetadata> options)
    {
        _options = options
            .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(option => option.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static AncientOptionCatalog Load(Stream stream)
    {
        var model = JsonSerializer.Deserialize<AncientOptionDataModel>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Failed to parse ancient option data.");

        var options = model.Options?.Select(record => new AncientOptionMetadata
        {
            Id = record.Id ?? throw new InvalidDataException("Option id is required."),
            Title = record.Title,
            Description = record.Description
        }) ?? Enumerable.Empty<AncientOptionMetadata>();

        return new AncientOptionCatalog(options);
    }

    public AncientOptionMetadata Get(string optionId)
    {
        if (_options.TryGetValue(optionId, out var metadata))
        {
            return metadata;
        }

        return new AncientOptionMetadata
        {
            Id = optionId,
            Title = optionId,
            Description = string.Empty
        };
    }

    public IReadOnlyCollection<string> OptionIds => _options.Keys;

    private sealed record AncientOptionDataModel
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public string? Locale { get; init; }

        public List<AncientOptionRecord>? Options { get; init; }
    }

    private sealed record AncientOptionRecord
    {
        public string? Id { get; init; }

        public string? Title { get; init; }

        public string? Description { get; init; }
    }

}
