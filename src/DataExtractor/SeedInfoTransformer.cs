using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeedModel.Neow;

namespace DataExtractor;

internal static class SeedInfoTransformer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static NeowOptionDataset ReadNeowData(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Seed info file not found.", path);
        }

        using var stream = File.OpenRead(path);
        var raw = JsonSerializer.Deserialize<SeedInfoFile>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"Failed to parse seed info file at {path}.");

        if (raw.Options.Count == 0)
        {
            throw new InvalidDataException("Seed info file did not contain any options.");
        }

        var dataset = new NeowOptionDataset
        {
            Options = new List<NeowOptionMetadata>(raw.Options.Count),
            Cards = new List<CardInfo>(raw.Cards.Count),
            Potions = new List<PotionInfo>(raw.Potions.Count)
        };

        foreach (var option in raw.Options)
        {
            dataset.Options.Add(new NeowOptionMetadata
            {
                Id = option.RelicId,
                RelicId = option.RelicId,
                Kind = NormalizeKind(option.Kind),
                Title = option.Title?.Trim(),
                Description = option.Description?.Trim(),
                Note = option.Note?.Trim()
            });
        }

        foreach (var card in raw.Cards)
        {
            dataset.Cards.Add(new CardInfo
            {
                Id = card.Id,
                Name = card.Name?.Trim() ?? card.Id
            });
        }

        foreach (var potion in raw.Potions)
        {
            dataset.Potions.Add(new PotionInfo
            {
                Id = potion.Id,
                Name = potion.Name?.Trim() ?? potion.Id
            });
        }

        return dataset;
    }

    private static NeowOptionKind NormalizeKind(string? kind)
    {
        if (string.Equals(kind, "Curse", StringComparison.OrdinalIgnoreCase))
        {
            return NeowOptionKind.Negative;
        }

        return NeowOptionKind.Positive;
    }

    private sealed record SeedInfoFile
    {
        [JsonPropertyName("Options")]
        public List<SeedInfoOption> Options { get; init; } = [];

        [JsonPropertyName("Cards")]
        public List<SeedInfoEntry> Cards { get; init; } = [];

        [JsonPropertyName("Potions")]
        public List<SeedInfoEntry> Potions { get; init; } = [];
    }

    private sealed record SeedInfoOption
    {
        [JsonPropertyName("RelicId")]
        public required string RelicId { get; init; }

        [JsonPropertyName("Kind")]
        public required string Kind { get; init; }

        [JsonPropertyName("Title")]
        public string? Title { get; init; }

        [JsonPropertyName("Description")]
        public string? Description { get; init; }

        [JsonPropertyName("Note")]
        public string? Note { get; init; }
    }

    private sealed record SeedInfoEntry
    {
        [JsonPropertyName("Id")]
        public required string Id { get; init; }

        [JsonPropertyName("Name")]
        public string? Name { get; init; }
    }
}
