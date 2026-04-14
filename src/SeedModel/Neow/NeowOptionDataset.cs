using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SeedModel.Neow;

public sealed class NeowOptionDataset
{
    private IReadOnlyDictionary<string, NeowOptionMetadata>? _optionCache;
    private IReadOnlyDictionary<string, CardInfo>? _cardCache;
    private IReadOnlyDictionary<string, PotionInfo>? _potionCache;
    private IReadOnlyDictionary<CharacterId, IReadOnlyList<string>>? _cardPoolCache;
    private IReadOnlyList<string>? _colorlessCardPool;
    private IReadOnlyDictionary<CharacterId, IReadOnlyList<string>>? _potionPoolCache;
    private IReadOnlyList<string>? _sharedPotionPool;
    private IReadOnlyDictionary<string, NeowCardMetadata>? _cardMetadataCache;
    private IReadOnlyDictionary<string, NeowPotionMetadata>? _potionMetadataCache;

    [JsonPropertyName("options")]
    public required List<NeowOptionMetadata> Options { get; init; }

    [JsonPropertyName("cards")]
    public List<CardInfo> Cards { get; init; } = new();

    [JsonPropertyName("potions")]
    public List<PotionInfo> Potions { get; init; } = new();

    [JsonPropertyName("cardPools")]
    public Dictionary<string, List<string>> CardPools { get; init; } = new();

    [JsonPropertyName("colorlessCardPool")]
    public List<string> ColorlessCardPool { get; init; } = new();

    [JsonPropertyName("potionPools")]
    public Dictionary<string, List<string>> PotionPools { get; init; } = new();

    [JsonPropertyName("sharedPotionPool")]
    public List<string> SharedPotionPool { get; init; } = new();

    [JsonPropertyName("cardMetadata")]
    public List<NeowCardMetadata> CardMetadata { get; init; } = new();

    [JsonPropertyName("potionMetadata")]
    public List<NeowPotionMetadata> PotionMetadata { get; init; } = new();

    [JsonIgnore]
    public IReadOnlyDictionary<string, NeowOptionMetadata> OptionMap =>
        _optionCache ??= Options.ToDictionary(o => o.Id, o => o, StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public IReadOnlyDictionary<string, CardInfo> CardMap =>
        _cardCache ??= Cards.ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public IReadOnlyDictionary<string, PotionInfo> PotionMap =>
        _potionCache ??= Potions.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> CharacterCardPoolMap =>
        _cardPoolCache ??= BuildCardPoolMap();

    [JsonIgnore]
    public IReadOnlyList<string> ColorlessCardPoolList =>
        _colorlessCardPool ??= ColorlessCardPool.AsReadOnly();

    [JsonIgnore]
    public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> CharacterPotionPoolMap =>
        _potionPoolCache ??= BuildPotionPoolMap();

    [JsonIgnore]
    public IReadOnlyList<string> SharedPotionPoolList =>
        _sharedPotionPool ??= SharedPotionPool.AsReadOnly();

    [JsonIgnore]
    public IReadOnlyDictionary<string, NeowCardMetadata> CardMetadataMap =>
        _cardMetadataCache ??= BuildCardMetadataMap();

    [JsonIgnore]
    public IReadOnlyDictionary<string, NeowPotionMetadata> PotionMetadataMap =>
        _potionMetadataCache ??= BuildPotionMetadataMap();

    private IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> BuildCardPoolMap()
    {
        var result = new Dictionary<CharacterId, IReadOnlyList<string>>();
        foreach (var entry in CardPools)
        {
            if (!Enum.TryParse<CharacterId>(entry.Key, ignoreCase: true, out var character))
            {
                continue;
            }

            result[character] = entry.Value.AsReadOnly();
        }

        return result;
    }

    private IReadOnlyDictionary<string, NeowCardMetadata> BuildCardMetadataMap()
    {
        var result = new Dictionary<string, NeowCardMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadata in CardMetadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.Id))
            {
                continue;
            }

            result[metadata.Id] = metadata;
        }

        return result;
    }

    private IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> BuildPotionPoolMap()
    {
        var result = new Dictionary<CharacterId, IReadOnlyList<string>>();
        foreach (var entry in PotionPools)
        {
            if (!Enum.TryParse<CharacterId>(entry.Key, ignoreCase: true, out var character))
            {
                continue;
            }

            result[character] = entry.Value.AsReadOnly();
        }

        return result;
    }

    private IReadOnlyDictionary<string, NeowPotionMetadata> BuildPotionMetadataMap()
    {
        var result = new Dictionary<string, NeowPotionMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadata in PotionMetadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.Id))
            {
                continue;
            }

            result[metadata.Id] = metadata;
        }

        return result;
    }
}
