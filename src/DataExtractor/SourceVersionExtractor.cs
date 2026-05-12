using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeedModel.Neow;

namespace DataExtractor;

internal sealed class SourceVersionExtractor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex ModelDbReferenceRegex = new(@"(?:ModelDb\.)?(?<kind>\w+)<(?<name>\w+)>\(", RegexOptions.Compiled);
    private static readonly Regex RelicOptionRegex = new(@"RelicOption<(?<name>\w+)>", RegexOptions.Compiled);
    private static readonly Regex BaseRoomsRegex = new(@"BaseNumberOfRooms\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex WeakRoomsRegex = new(@"NumberOfWeakEncounters\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex RoomTypeRegex = new(@"RoomType\s*=>\s*RoomType\.(?<value>\w+)", RegexOptions.Compiled);
    private static readonly Regex IsWeakRegex = new(@"IsWeak\s*=>\s*(?<value>true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CardBaseRegex = new(@":\s*base\s*\(\s*[^,]+,\s*CardType\.(?<type>\w+),\s*CardRarity\.(?<rarity>\w+),", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CardConstraintRegex = new(@"MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.(?<value>\w+)", RegexOptions.Compiled);
    private static readonly Regex CanGenerateInCombatRegex = new(@"CanBeGeneratedInCombat\s*=>\s*(?<value>true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PotionRarityRegex = new(@"Rarity\s*=>\s*PotionRarity\.(?<value>\w+)", RegexOptions.Compiled);
    private static readonly Regex RelicRarityRegex = new(@"Rarity\s*=>\s*RelicRarity\.(?<value>\w+)", RegexOptions.Compiled);
    private static readonly Regex VersionDirRegex = new(@"\b\d+\.\d+\.\d+\b", RegexOptions.Compiled);

    private readonly string _sourceRoot;
    private readonly string _outputRoot;
    private readonly string _version;
    private readonly string _modelsRoot;
    private readonly string _localizationRoot;
    private readonly Dictionary<string, string> _engRelics;
    private readonly Dictionary<string, string> _zhsRelics;
    private readonly Dictionary<string, string> _zhsCards;
    private readonly Dictionary<string, string> _zhsPotions;

    public SourceVersionExtractor(string sourceRoot, string outputRoot, string version)
    {
        _sourceRoot = sourceRoot;
        _outputRoot = outputRoot;
        _version = version;
        _modelsRoot = Path.Combine(_sourceRoot, "src", "Core", "Models");
        _localizationRoot = Path.Combine(_sourceRoot, "localization");
        _engRelics = LoadLocalization(Path.Combine(_localizationRoot, "eng"), "relics.json");
        _zhsRelics = LoadLocalization(Path.Combine(_localizationRoot, "zhs"), "relics.json");
        _zhsCards = LoadLocalization(Path.Combine(_localizationRoot, "zhs"), "cards.json");
        _zhsPotions = LoadLocalization(Path.Combine(_localizationRoot, "zhs"), "potions.json");
    }

    public void Extract()
    {
        Directory.CreateDirectory(_outputRoot);
        WriteJson(Path.Combine(_outputRoot, "sts2", "acts.json"), BuildActsData());
        WriteJson(Path.Combine(_outputRoot, "ancients", "options.json"), BuildAncientOptionCatalog(_engRelics, "eng"));
        WriteJson(Path.Combine(_outputRoot, "ancients", "options.zhs.json"), BuildAncientOptionCatalog(_zhsRelics, "zhs"));
        WriteJson(Path.Combine(_outputRoot, "neow", "options.json"), BuildNeowDataset());
        CopyLocalizationFiles();
    }

    private void CopyLocalizationFiles()
    {
        var targetRoot = Path.Combine(_outputRoot, "sts2", "localization", "zhs");
        Directory.CreateDirectory(targetRoot);

        foreach (var fileName in new[] { "acts.json", "cards.json", "encounters.json", "events.json", "relics.json" })
        {
            File.Copy(
                Path.Combine(_localizationRoot, "zhs", fileName),
                Path.Combine(targetRoot, fileName),
                overwrite: true);
        }
    }

    private object BuildActsData()
    {
        var actOrder = new[] { "Underdocks", "Overgrowth", "Hive", "Glory" };
        var actNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Underdocks"] = 1,
            ["Overgrowth"] = 1,
            ["Hive"] = 2,
            ["Glory"] = 3
        };

        var acts = new List<object>();
        var encounterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var actName in actOrder)
        {
            var text = File.ReadAllText(Path.Combine(_modelsRoot, "Acts", $"{actName}.cs"));
            var ancients = ExtractModelDbNames(text, "AncientEvent", "AllAncients", ExtractionScope.Expression)
                .Select(static value => value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var events = ExtractModelDbNames(text, "Event", "AllEvents", ExtractionScope.Expression).ToList();
            var encounters = ExtractModelDbNames(text, "Encounter", "GenerateAllEncounters", ExtractionScope.Block).ToList();
            foreach (var encounterId in encounters)
            {
                encounterIds.Add(encounterId);
            }

            acts.Add(new
            {
                name = actName,
                number = actNumbers[actName],
                baseRooms = ParseInt(text, BaseRoomsRegex),
                weakRooms = ParseInt(text, WeakRoomsRegex),
                events,
                encounters,
                ancients
            });
        }

        var modelDbText = File.ReadAllText(Path.Combine(_modelsRoot, "ModelDb.cs"));
        var encounterMap = encounterIds
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static id => id, BuildEncounterMetadata, StringComparer.OrdinalIgnoreCase);

        return new
        {
            acts,
            encounters = encounterMap,
            sharedEvents = ExtractModelDbNames(
                    modelDbText,
                    "Event",
                    "public static IEnumerable<EventModel> AllSharedEvents",
                    ExtractionScope.Expression)
                .ToList(),
            sharedAncients = ExtractModelDbNames(
                    modelDbText,
                    "AncientEvent",
                    "public static IEnumerable<AncientEventModel> AllSharedAncients",
                    ExtractionScope.Expression)
                .Select(static value => value.ToUpperInvariant())
                .ToList(),
            relicPools = BuildRelicPoolData()
        };
    }

    private object BuildRelicPoolData()
    {
        var rarityMap = BuildRelicMetadata()
            .ToDictionary(item => item.Id, item => item.Rarity, StringComparer.OrdinalIgnoreCase);

        return new
        {
            sharedSequence = ParseRelicPool("SharedRelicPool"),
            characters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ironclad"] = ParseRelicPool("IroncladRelicPool"),
                ["Silent"] = ParseRelicPool("SilentRelicPool"),
                ["Defect"] = ParseRelicPool("DefectRelicPool"),
                ["Necrobinder"] = ParseRelicPool("NecrobinderRelicPool"),
                ["Regent"] = ParseRelicPool("RegentRelicPool")
            },
            rarities = rarityMap
        };
    }

    private object BuildEncounterMetadata(string encounterId)
    {
        var text = File.ReadAllText(Path.Combine(_modelsRoot, "Encounters", $"{encounterId}.cs"));
        return new
        {
            roomType = RoomTypeRegex.Match(text).Groups["value"].Value,
            isWeak = bool.TryParse(IsWeakRegex.Match(text).Groups["value"].Value, out var value) && value,
            tags = ExtractEnumValues(text, "EncounterTag")
        };
    }

    private object BuildAncientOptionCatalog(IReadOnlyDictionary<string, string> localization, string locale)
    {
        var relicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ancient in new[] { "Orobas", "Pael", "Tezcatara", "Tanx", "Vakuu", "Nonupeipe" })
        {
            var text = File.ReadAllText(Path.Combine(_modelsRoot, "Events", $"{ancient}.cs"));
            foreach (var relicClass in ExtractRelicOptionNames(text))
            {
                relicIds.Add(ToModelId(relicClass));
            }
        }

        foreach (var relicId in GetKnownAncientOptionIds())
        {
            relicIds.Add(relicId);
        }

        var options = relicIds
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(id => new
            {
                id,
                title = LookupLocalization(localization, id, ".title"),
                description = LookupLocalization(localization, id, ".eventDescription", ".description")
            })
            .ToList();

        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            locale,
            options
        };
    }

    private static IReadOnlyList<string> GetKnownAncientOptionIds() =>
    [
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
        "DUSTY_TOME",
        "ELECTRIC_SHRYMP",
        "GLASS_EYE",
        "SAND_CASTLE",
        "PRISMATIC_GEM",
        "SEA_GLASS",
        "ALCHEMICAL_COFFER",
        "DRIFTWOOD",
        "RADIANT_PEARL",
        "TOUCH_OF_OROBAS",
        "ARCHAIC_TOOTH",
        "PAELS_FLESH",
        "PAELS_HORN",
        "PAELS_TEARS",
        "PAELS_WING",
        "PAELS_CLAW",
        "PAELS_TOOTH",
        "PAELS_GROWTH",
        "PAELS_EYE",
        "PAELS_BLOOD",
        "PAELS_LEGION",
        "NUTRITIOUS_SOUP",
        "VERY_HOT_COCOA",
        "YUMMY_COOKIE",
        "BIIIG_HUG",
        "STORYBOOK",
        "SEAL_OF_GOLD",
        "TOASTY_MITTENS",
        "GOLDEN_COMPASS",
        "PUMPKIN_CANDLE",
        "TOY_BOX",
        "BLESSED_ANTLER",
        "BRILLIANT_SCARF",
        "DELICATE_FROND",
        "DIAMOND_DIADEM",
        "FUR_COAT",
        "GLITTER",
        "JEWELRY_BOX",
        "LOOMING_FRUIT",
        "SIGNET_RING",
        "BEAUTIFUL_BRACELET",
        "CLAWS",
        "CROSSBOW",
        "IRON_CLUB",
        "MEAT_CLEAVER",
        "SAI",
        "SPIKED_GAUNTLETS",
        "TANXS_WHISTLE",
        "THROWING_AXE",
        "WAR_HAMMER",
        "TRI_BOOMERANG",
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
    ];

    private NeowOptionDataset BuildNeowDataset()
    {
        return new NeowOptionDataset
        {
            Version = _version,
            Options = BuildNeowOptions(),
            Cards = BuildCards(),
            Potions = BuildPotions(),
            CardPools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ironclad"] = ParseCardPool("IroncladCardPool"),
                ["Silent"] = ParseCardPool("SilentCardPool"),
                ["Defect"] = ParseCardPool("DefectCardPool"),
                ["Necrobinder"] = ParseCardPool("NecrobinderCardPool"),
                ["Regent"] = ParseCardPool("RegentCardPool")
            },
            ColorlessCardPool = ParseCardPool("ColorlessCardPool"),
            PotionPools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ironclad"] = ParsePotionPool("IroncladPotionPool"),
                ["Silent"] = ParsePotionPool("SilentPotionPool"),
                ["Defect"] = ParsePotionPool("DefectPotionPool"),
                ["Necrobinder"] = ParsePotionPool("NecrobinderPotionPool"),
                ["Regent"] = ParsePotionPool("RegentPotionPool")
            },
            SharedPotionPool = ParsePotionPool("SharedPotionPool"),
            CardMetadata = BuildCardMetadata(),
            PotionMetadata = BuildPotionMetadata(),
            RelicMetadata = BuildRelicMetadata(),
            RelicPools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Shared"] = ParseRelicPool("SharedRelicPool"),
                ["Ironclad"] = ParseRelicPool("IroncladRelicPool"),
                ["Silent"] = ParseRelicPool("SilentRelicPool"),
                ["Defect"] = ParseRelicPool("DefectRelicPool"),
                ["Necrobinder"] = ParseRelicPool("NecrobinderRelicPool"),
                ["Regent"] = ParseRelicPool("RegentRelicPool")
            }
        };
    }

    private List<NeowOptionMetadata> BuildNeowOptions()
    {
        var text = File.ReadAllText(Path.Combine(_modelsRoot, "Events", "Neow.cs"));
        var positive = ExtractRelicOptionNames(text, "private IEnumerable<EventOption> PositiveOptions", ExtractionScope.Expression)
            .Select(className => CreateNeowOption(className, NeowOptionKind.Positive));
        var negative = ExtractRelicOptionNames(text, "private IEnumerable<EventOption> CurseOptions", ExtractionScope.Expression)
            .Select(className => CreateNeowOption(className, NeowOptionKind.Negative));
        var extras = new[]
        {
            ("LavaRock", NeowOptionKind.Positive),
            ("NeowsTalisman", NeowOptionKind.Positive),
            ("NutritiousOyster", NeowOptionKind.Positive),
            ("Pomander", NeowOptionKind.Positive),
            ("ScrollBoxes", NeowOptionKind.Negative),
            ("SmallCapsule", NeowOptionKind.Positive),
            ("StoneHumidifier", NeowOptionKind.Positive)
        }
        .Select(item => CreateNeowOption(item.Item1, item.Item2));

        return positive
            .Concat(negative)
            .Concat(extras)
            .DistinctBy(static option => option.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private NeowOptionMetadata CreateNeowOption(string className, NeowOptionKind kind)
    {
        var relicId = ToModelId(className);
        return new NeowOptionMetadata
        {
            Id = relicId,
            RelicId = relicId,
            Kind = kind,
            Title = LookupLocalization(_zhsRelics, relicId, ".title"),
            Description = LookupLocalization(_zhsRelics, relicId, ".eventDescription", ".description")
        };
    }

    private List<CardInfo> BuildCards() =>
        BuildTitleIndex(_zhsCards)
            .Select(item => new CardInfo { Id = item.Id, Name = item.Title })
            .ToList();

    private List<PotionInfo> BuildPotions() =>
        BuildTitleIndex(_zhsPotions)
            .Select(item => new PotionInfo { Id = item.Id, Name = item.Title })
            .ToList();

    private List<NeowCardMetadata> BuildCardMetadata()
    {
        var results = new List<NeowCardMetadata>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_modelsRoot, "Cards"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var baseMatch = CardBaseRegex.Match(text);
            if (!baseMatch.Success)
            {
                continue;
            }

            var constraint = CardConstraintRegex.Match(text).Groups["value"].Value;
            var canGenerateText = CanGenerateInCombatRegex.Match(text).Groups["value"].Value;
            var canGenerate = !bool.TryParse(canGenerateText, out var parsed) || parsed;

            results.Add(new NeowCardMetadata
            {
                Id = ToModelId(Path.GetFileNameWithoutExtension(path)),
                Rarity = baseMatch.Groups["rarity"].Value,
                Type = baseMatch.Groups["type"].Value,
                MultiplayerConstraint = string.IsNullOrWhiteSpace(constraint) ? nameof(CardMultiplayerConstraint.None) : constraint,
                CanBeGeneratedInCombat = canGenerate
            });
        }

        return results.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<NeowPotionMetadata> BuildPotionMetadata()
    {
        var results = new List<NeowPotionMetadata>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_modelsRoot, "Potions"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var rarity = PotionRarityRegex.Match(text).Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(rarity))
            {
                continue;
            }

            results.Add(new NeowPotionMetadata
            {
                Id = ToModelId(Path.GetFileNameWithoutExtension(path)),
                Rarity = rarity
            });
        }

        return results.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<NeowRelicMetadata> BuildRelicMetadata()
    {
        var results = new List<NeowRelicMetadata>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_modelsRoot, "Relics"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var rarity = RelicRarityRegex.Match(text).Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(rarity))
            {
                continue;
            }

            results.Add(new NeowRelicMetadata
            {
                Id = ToModelId(Path.GetFileNameWithoutExtension(path)),
                Rarity = rarity
            });
        }

        return results.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> ParseCardPool(string poolName) =>
        ExtractModelDbNames(File.ReadAllText(Path.Combine(_modelsRoot, "CardPools", $"{poolName}.cs")), "Card")
            .Select(ToModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<string> ParsePotionPool(string poolName) =>
        ExtractModelDbNames(File.ReadAllText(Path.Combine(_modelsRoot, "PotionPools", $"{poolName}.cs")), "Potion")
            .Select(ToModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<string> ParseRelicPool(string poolName) =>
        ExtractModelDbNames(File.ReadAllText(Path.Combine(_modelsRoot, "RelicPools", $"{poolName}.cs")), "Relic")
            .Select(ToModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<(string Id, string Title)> BuildTitleIndex(IReadOnlyDictionary<string, string> localization)
    {
        return localization
            .Where(entry => entry.Key.EndsWith(".title", StringComparison.Ordinal))
            .Select(entry => (Id: entry.Key[..^".title".Length], Title: entry.Value))
            .OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string LookupLocalization(IReadOnlyDictionary<string, string> localization, string modelId, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (localization.TryGetValue($"{modelId}{suffix}", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return modelId;
    }

    private static Dictionary<string, string> LoadLocalization(string directoryPath, string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(directoryPath, fileName));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static int ParseInt(string text, Regex regex)
    {
        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : 0;
    }

    private static IEnumerable<string> ExtractModelDbNames(string text, string kind, string? marker = null, ExtractionScope scope = ExtractionScope.FullText)
    {
        var scopedText = GetScopedText(text, marker, scope);
        foreach (Match match in ModelDbReferenceRegex.Matches(scopedText))
        {
            if (string.Equals(match.Groups["kind"].Value, kind, StringComparison.Ordinal))
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractRelicOptionNames(string text, string? marker = null, ExtractionScope scope = ExtractionScope.FullText)
    {
        var scopedText = GetScopedText(text, marker, scope);
        foreach (Match match in RelicOptionRegex.Matches(scopedText))
        {
            yield return match.Groups["name"].Value;
        }
    }

    private static string GetScopedText(string text, string? marker, ExtractionScope scope)
    {
        if (string.IsNullOrWhiteSpace(marker) || scope == ExtractionScope.FullText)
        {
            return text;
        }

        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return text;
        }

        if (scope == ExtractionScope.Expression)
        {
            var end = text.IndexOf(';', start);
            return end > start ? text[start..(end + 1)] : text[start..];
        }

        var openBrace = text.IndexOf('{', start);
        if (openBrace < 0)
        {
            return text[start..];
        }

        var depth = 0;
        for (var index = openBrace; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(index + 1)];
                }
            }
        }

        return text[start..];
    }

    private static List<string> ExtractEnumValues(string text, string enumTypeName)
    {
        var regex = new Regex($@"{Regex.Escape(enumTypeName)}\.(?<value>\w+)", RegexOptions.Compiled);
        return regex.Matches(text)
            .Select(match => match.Groups["value"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToModelId(string className)
    {
        var value = Regex.Replace(className, "([a-z0-9])([A-Z])", "$1_$2");
        value = Regex.Replace(value, "([A-Z])([A-Z][a-z])", "$1_$2");
        return value.ToUpperInvariant();
    }

    private void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, value, SerializerOptions);
    }

    public static string InferVersionFromSourcePath(string sourcePath)
    {
        foreach (var segment in sourcePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var match = VersionDirRegex.Match(segment);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return "0.105.1";
    }

    private enum ExtractionScope
    {
        FullText,
        Expression,
        Block
    }
}
