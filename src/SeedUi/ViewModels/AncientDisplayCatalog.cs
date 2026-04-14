using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SeedUi.ViewModels;

internal static class AncientDisplayCatalog
{
    internal sealed record AncientDisplayOption(
        string Id,
        string Name,
        string DisplayText,
        IReadOnlyList<int> Acts,
        IReadOnlyList<AncientRelicDisplayOption> RelicOptions);

    internal sealed record AncientRelicDisplayOption(
        string Id,
        string Title,
        string Description,
        string DisplayText);

    private sealed record AncientDefinition(string Id, string Name, int[] Acts, string[] RelicIds);

    private sealed record AncientOptionMetadata(string Id, string? Title, string? Description);

    private sealed record AncientOptionFileModel
    {
        public List<AncientOptionRecord> Options { get; init; } = new();
    }

    private sealed record AncientOptionRecord
    {
        public string? Id { get; init; }

        public string? Title { get; init; }

        public string? Description { get; init; }
    }

    private static readonly IReadOnlyDictionary<string, AncientDisplayOption> AncientLookup;
    private static readonly IReadOnlyDictionary<string, AncientRelicDisplayOption> RelicLookup;

    public static IReadOnlyList<AncientDisplayOption> AllowedForAct2 { get; }

    public static IReadOnlyList<AncientDisplayOption> AllowedForAct3 { get; }

    static AncientDisplayCatalog()
    {
        var metadata = LoadOptionMetadata();
        var definitions = BuildDefinitions();
        var ancients = new Dictionary<string, AncientDisplayOption>(StringComparer.OrdinalIgnoreCase);
        var relics = new Dictionary<string, AncientRelicDisplayOption>(StringComparer.OrdinalIgnoreCase);
        var act2 = new List<AncientDisplayOption>();
        var act3 = new List<AncientDisplayOption>();

        foreach (var definition in definitions)
        {
            var relicOptions = new List<AncientRelicDisplayOption>();
            foreach (var relicId in definition.RelicIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var option = CreateRelicOption(relicId, metadata) ?? CreateFallbackRelicOption(relicId);
                if (string.IsNullOrWhiteSpace(option.Id))
                {
                    continue;
                }

                var normalizedId = NormalizeOptionId(option.Id);
                option = option with { Id = normalizedId };
                if (relics.TryGetValue(normalizedId, out var existing))
                {
                    option = existing;
                }

                relicOptions.Add(option);
            }

            var displayText = BuildAncientDisplayText(definition.Id, definition.Name);
            var ancientOption = new AncientDisplayOption(
                NormalizeAncientId(definition.Id),
                definition.Name,
                displayText,
                definition.Acts,
                relicOptions.AsReadOnly());

            foreach (var relic in ancientOption.RelicOptions)
            {
                relics[relic.Id] = relic;
            }

            ancients[ancientOption.Id] = ancientOption;

            if (definition.Acts.Contains(2))
            {
                act2.Add(ancientOption);
            }

            if (definition.Acts.Contains(3))
            {
                act3.Add(ancientOption);
            }
        }

        AncientLookup = ancients;
        RelicLookup = relics;
        AllowedForAct2 = act2.AsReadOnly();
        AllowedForAct3 = act3.AsReadOnly();
    }

    public static string GetLocalizedName(string? ancientId, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(ancientId))
        {
            return "任意古神";
        }

        var normalized = NormalizeAncientId(ancientId);
        if (AncientLookup.TryGetValue(normalized, out var option) && !string.IsNullOrWhiteSpace(option.Name))
        {
            return option.Name;
        }

        return string.IsNullOrWhiteSpace(fallbackName) ? normalized : fallbackName;
    }

    public static string GetDisplayText(string? ancientId, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(ancientId))
        {
            return "任意古神";
        }

        var normalized = NormalizeAncientId(ancientId);
        if (AncientLookup.TryGetValue(normalized, out var option))
        {
            return option.DisplayText;
        }

        var name = string.IsNullOrWhiteSpace(fallbackName) ? normalized : fallbackName;
        return $"{name}（{normalized}）";
    }

    public static IReadOnlyList<AncientRelicDisplayOption> GetRelicOptions(string? ancientId)
    {
        if (string.IsNullOrWhiteSpace(ancientId))
        {
            return Array.Empty<AncientRelicDisplayOption>();
        }

        var normalized = NormalizeAncientId(ancientId);
        if (AncientLookup.TryGetValue(normalized, out var option))
        {
            return option.RelicOptions;
        }

        return Array.Empty<AncientRelicDisplayOption>();
    }

    public static AncientRelicDisplayOption? TryGetRelicOption(string? optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
        {
            return null;
        }

        var normalized = NormalizeOptionId(optionId);
        if (RelicLookup.TryGetValue(normalized, out var option))
        {
            return option;
        }

        return null;
    }

    public static string ResolveOptionDataPath()
    {
        var baseDirectory = Path.Combine(AppContext.BaseDirectory, "data", "ancients");
        var localized = Path.Combine(baseDirectory, "options.zhs.json");
        if (File.Exists(localized))
        {
            return localized;
        }

        return Path.Combine(baseDirectory, "options.json");
    }

    private static AncientRelicDisplayOption? CreateRelicOption(
        string optionId,
        IReadOnlyDictionary<string, AncientOptionMetadata> metadata)
    {
        if (string.IsNullOrWhiteSpace(optionId))
        {
            return null;
        }

        var normalized = NormalizeOptionId(optionId);
        if (!metadata.TryGetValue(normalized, out var record))
        {
            return new AncientRelicDisplayOption(
                normalized,
                normalized,
                string.Empty,
                normalized);
        }

        var title = string.IsNullOrWhiteSpace(record.Title) ? normalized : record.Title!;
        var description = record.Description ?? string.Empty;
        var displayText = string.IsNullOrWhiteSpace(title)
            ? normalized
            : $"{title}（{normalized}）";
        return new AncientRelicDisplayOption(normalized, title, description, displayText);
    }

    private static AncientRelicDisplayOption CreateFallbackRelicOption(string id)
    {
        var normalized = NormalizeOptionId(id);
        return new AncientRelicDisplayOption(normalized, normalized, string.Empty, normalized);
    }

    private static IReadOnlyDictionary<string, AncientOptionMetadata> LoadOptionMetadata()
    {
        var metadata = new Dictionary<string, AncientOptionMetadata>(StringComparer.OrdinalIgnoreCase);
        var path = ResolveOptionDataPath();
        if (!File.Exists(path))
        {
            return metadata;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var model = JsonSerializer.Deserialize<AncientOptionFileModel>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (model?.Options == null)
            {
                return metadata;
            }

            foreach (var record in model.Options)
            {
                if (string.IsNullOrWhiteSpace(record.Id))
                {
                    continue;
                }

                var normalized = NormalizeOptionId(record.Id);
                metadata[normalized] = new AncientOptionMetadata(normalized, record.Title, record.Description);
            }
        }
        catch
        {
            // 忽略解析错误，使用空白回退。
        }

        return metadata;
    }

    private static ReadOnlyCollection<AncientDisplayOption> AsReadOnly(this List<AncientDisplayOption> source) =>
        new(source);

    private static ReadOnlyCollection<AncientRelicDisplayOption> AsReadOnly(
        this List<AncientRelicDisplayOption> source) => new(source);

    private static AncientDefinition[] BuildDefinitions() =>
    [
        new("NEOW", "涅奥", new[] { 1 }, Array.Empty<string>()),
        new("DARV", "达弗", Array.Empty<int>(),
            new[]
            {
                "ASTROLABE",
                "BLACK_STAR",
                "CALLING_BELL",
                "EMPTY_CAGE",
                "PANDORAS_BOX",
                "RUNIC_PYRAMID",
                "SNECKO_EYE",
                "ECTOPLASM",
                "SOZU",
                "PHILOSOPHERS_STONE",
                "VELVET_CHOKER",
                "DUSTY_TOME"
            }),
        new("OROBAS", "欧洛巴斯", new[] { 2 },
            new[]
            {
                "ELECTRIC_SHRYMP",
                "GLASS_EYE",
                "SAND_CASTLE",
                "PRISMATIC_GEM",
                "SEA_GLASS",
                "ALCHEMICAL_COFFER",
                "DRIFTWOOD",
                "RADIANT_PEARL",
                "TOUCH_OF_OROBAS",
                "ARCHAIC_TOOTH"
            }),
        new("PAEL", "佩尔", new[] { 2 },
            new[]
            {
                "PAELS_FLESH",
                "PAELS_HORN",
                "PAELS_TEARS",
                "PAELS_WING",
                "PAELS_CLAW",
                "PAELS_TOOTH",
                "PAELS_GROWTH",
                "PAELS_EYE",
                "PAELS_BLOOD",
                "PAELS_LEGION"
            }),
        new("TEZCATARA", "特兹卡塔拉", new[] { 2 },
            new[]
            {
                "NUTRITIOUS_SOUP",
                "VERY_HOT_COCOA",
                "YUMMY_COOKIE",
                "BIIIG_HUG",
                "STORYBOOK",
                "SEAL_OF_GOLD",
                "TOASTY_MITTENS",
                "GOLDEN_COMPASS",
                "PUMPKIN_CANDLE",
                "TOY_BOX"
            }),
        new("NONUPEIPE", "诺奴佩普", new[] { 3 },
            new[]
            {
                "BLESSED_ANTLER",
                "BRILLIANT_SCARF",
                "DELICATE_FROND",
                "DIAMOND_DIADEM",
                "FUR_COAT",
                "GLITTER",
                "JEWELRY_BOX",
                "LOOMING_FRUIT",
                "SIGNET_RING",
                "BEAUTIFUL_BRACELET"
            }),
        new("TANX", "坦克斯", new[] { 3 },
            new[]
            {
                "CLAWS",
                "CROSSBOW",
                "IRON_CLUB",
                "MEAT_CLEAVER",
                "SAI",
                "SPIKED_GAUNTLETS",
                "TANXS_WHISTLE",
                "THROWING_AXE",
                "WAR_HAMMER",
                "TRI_BOOMERANG"
            }),
        new("VAKUU", "瓦库", new[] { 3 },
            new[]
            {
                "BLOOD_SOAKED_ROSE",
                "WHISPERING_EARRING",
                "FIDDLE",
                "PRESERVED_FOG",
                "SERE_TALON",
                "DISTINGUISHED_CAPE",
                "CHOICES_PARADOX",
                "MUSIC_BOX",
                "LORDS_PARASOL",
                "JEWELED_MASK"
            })
    ];

    private static string BuildAncientDisplayText(string id, string name)
    {
        var normalized = NormalizeAncientId(id);
        if (string.IsNullOrWhiteSpace(name))
        {
            return normalized;
        }

        return $"{name}（{normalized}）";
    }

    private static string NormalizeAncientId(string id) => id.Trim().ToUpperInvariant();

    private static string NormalizeOptionId(string id) => id.Trim().ToUpperInvariant();
}
