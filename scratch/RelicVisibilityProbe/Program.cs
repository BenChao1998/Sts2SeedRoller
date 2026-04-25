using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

Console.OutputEncoding = Encoding.UTF8;

var options = Options.Parse(args, Directory.GetCurrentDirectory());
var titles = LocalizationTable.Load(options.RelicLocalizationPath);
var relicRules = RelicRules.Load(options.SourceRootPath);
var ancientPreview = AncientPreview.Load(options);
string report;
if (!string.IsNullOrWhiteSpace(options.SavePath))
{
    var dataset = DatasetLoader.Load(options.NeowDataPath);
    var baseline = OfficialBagInitializer.Create(options.SeedText, options.Character, options.AscensionLevel);
    var snapshot = SaveSnapshot.Load(options.SavePath);
    var replay = SaveReplayAnalyzer.Analyze(options, snapshot, baseline, ancientPreview, relicRules, dataset);
    report = SaveReplayReportBuilder.Build(options, snapshot, replay, titles);
}
else
{
    var baseline = OfficialBagInitializer.Create(options.SeedText, options.Character, options.AscensionLevel);
    var profileResults = RouteProfile.All
        .Select(profile => MonteCarloSimulator.Run(options, profile, baseline, ancientPreview, relicRules))
        .ToList();

    report = ReportBuilder.Build(options, baseline, ancientPreview, relicRules, profileResults, titles);
}
Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
File.WriteAllText(options.OutputPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine(report);
Console.WriteLine();
Console.WriteLine($"报告已写入: {options.OutputPath}");

sealed class Options
{
    private Options(
        string seedText,
        uint seedValue,
        CharacterId character,
        int ascensionLevel,
        int samples,
        int earlyWindow,
        string optionDataPath,
        string actDataPath,
        string neowDataPath,
        string relicLocalizationPath,
        string sourceRootPath,
        string? savePath,
        string outputPath)
    {
        SeedText = seedText;
        SeedValue = seedValue;
        Character = character;
        AscensionLevel = ascensionLevel;
        Samples = samples;
        EarlyWindow = earlyWindow;
        OptionDataPath = optionDataPath;
        ActDataPath = actDataPath;
        NeowDataPath = neowDataPath;
        RelicLocalizationPath = relicLocalizationPath;
        SourceRootPath = sourceRootPath;
        SavePath = savePath;
        OutputPath = outputPath;
    }

    public string SeedText { get; }

    public uint SeedValue { get; }

    public CharacterId Character { get; }

    public int AscensionLevel { get; }

    public int Samples { get; }

    public int EarlyWindow { get; }

    public string OptionDataPath { get; }

    public string ActDataPath { get; }

    public string NeowDataPath { get; }

    public string RelicLocalizationPath { get; }

    public string SourceRootPath { get; }

    public string? SavePath { get; }

    public string OutputPath { get; }

    public static Options Parse(string[] args, string workingDirectory)
    {
        string? seedRaw = null;
        string? savePath = null;
        var character = CharacterId.Silent;
        var ascensionLevel = 0;
        var samples = 8000;
        var earlyWindow = 5;
        var optionDataPath = Path.Combine(workingDirectory, "data", "0.103.2", "ancients", "options.zhs.json");
        var actDataPath = Path.Combine(workingDirectory, "data", "0.103.2", "sts2", "acts.json");
        var neowDataPath = Path.Combine(workingDirectory, "data", "0.103.2", "neow", "options.json");
        var relicLocalizationPath = Path.Combine(workingDirectory, "data", "0.103.2", "sts2", "localization", "zhs", "relics.json");
        var sourceRootPath = Path.Combine(workingDirectory, "Slay the Spire 2 版本0.103.2源码（游戏源码，读取用）");
        string? outputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed" when i + 1 < args.Length:
                    seedRaw = args[++i];
                    break;
                case "--character" when i + 1 < args.Length:
                    if (!CharacterIdExtensions.TryParse(args[++i], out character))
                    {
                        throw new InvalidOperationException($"Unknown character: {args[i]}");
                    }

                    break;
                case "--ascension" when i + 1 < args.Length:
                    ascensionLevel = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--samples" when i + 1 < args.Length:
                    samples = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--early-window" when i + 1 < args.Length:
                    earlyWindow = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--ancient-data" when i + 1 < args.Length:
                    optionDataPath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--acts" when i + 1 < args.Length:
                    actDataPath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--neow-data" when i + 1 < args.Length:
                    neowDataPath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--relic-loc" when i + 1 < args.Length:
                    relicLocalizationPath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--source-root" when i + 1 < args.Length:
                    sourceRootPath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--save" when i + 1 < args.Length:
                    savePath = ResolvePath(workingDirectory, args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    outputPath = ResolvePath(workingDirectory, args[++i]);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(savePath) &&
            TryReadSaveMetadata(savePath, out var saveSeedText, out var saveCharacter, out var saveAscension))
        {
            seedRaw ??= saveSeedText;
            character = saveCharacter;
            ascensionLevel = saveAscension;
        }

        if (string.IsNullOrWhiteSpace(seedRaw))
        {
            throw new InvalidOperationException("Please provide --seed or --save.");
        }

        if (!TryParseSeed(seedRaw, out var seedText, out var seedValue))
        {
            throw new InvalidOperationException($"Invalid seed: {seedRaw}");
        }

        outputPath ??= Path.Combine(
            workingDirectory,
            "artifacts",
            string.IsNullOrWhiteSpace(savePath)
                ? $"relic_visibility_probe_{seedText}_{character.ToString().ToLowerInvariant()}.md"
                : $"relic_visibility_save_replay_{Path.GetFileNameWithoutExtension(savePath)}.md");

        return new Options(
            seedText,
            seedValue,
            character,
            ascensionLevel,
            samples,
            earlyWindow,
            optionDataPath,
            actDataPath,
            neowDataPath,
            relicLocalizationPath,
            sourceRootPath,
            savePath,
            outputPath);
    }

    private static bool TryParseSeed(string raw, out string seedText, out uint seedValue)
    {
        seedText = string.Empty;
        seedValue = 0;

        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seedValue))
            {
                seedText = seedValue.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out seedValue))
        {
            seedText = seedValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (SeedFormatter.TryNormalize(raw, out var normalized, out _))
        {
            seedText = normalized;
            seedValue = SeedFormatter.ToUIntSeed(normalized);
            return true;
        }

        return false;
    }

    private static string ResolvePath(string workingDirectory, string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(workingDirectory, path));
    }

    private static bool TryReadSaveMetadata(
        string savePath,
        out string seedText,
        out CharacterId character,
        out int ascensionLevel)
    {
        seedText = string.Empty;
        character = CharacterId.Silent;
        ascensionLevel = 0;

        if (!File.Exists(savePath))
        {
            return false;
        }

        var root = JsonNode.Parse(File.ReadAllText(savePath));
        if (root is null)
        {
            return false;
        }

        seedText = root["seed"]?.GetValue<string>() ?? string.Empty;
        ascensionLevel = root["ascension"]?.GetValue<int>() ?? 0;
        var characterText =
            root["players"]?[0]?["character_id"]?.GetValue<string>() ??
            root["players"]?[0]?["character"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(seedText))
        {
            return false;
        }

        var normalizedCharacter = characterText ?? string.Empty;
        var lastDot = normalizedCharacter.LastIndexOf('.');
        if (lastDot >= 0)
        {
            normalizedCharacter = normalizedCharacter[(lastDot + 1)..];
        }

        if (CharacterIdExtensions.TryParse(normalizedCharacter, out character))
        {
            return true;
        }

        switch (normalizedCharacter.ToUpperInvariant())
        {
            case "SILENT":
                character = CharacterId.Silent;
                return true;
            case "IRONCLAD":
                character = CharacterId.Ironclad;
                return true;
            case "DEFECT":
                character = CharacterId.Defect;
                return true;
            case "NECROBINDER":
                character = CharacterId.Necrobinder;
                return true;
            case "REGENT":
                character = CharacterId.Regent;
                return true;
            default:
                return false;
        }
    }
}

sealed class AncientPreview
{
    private AncientPreview(
        IReadOnlyDictionary<int, AncientActResult> acts)
    {
        Acts = acts;
    }

    public IReadOnlyDictionary<int, AncientActResult> Acts { get; }

    public static AncientPreview Load(Options options)
    {
        var previewer = Sts2RunPreviewer.CreateFromDataFiles(options.OptionDataPath, options.ActDataPath);
        var preview = previewer.Preview(new Sts2RunRequest
        {
            SeedValue = options.SeedValue,
            SeedText = options.SeedText,
            Character = options.Character,
            UnlockedCharacters =
            [
                CharacterId.Ironclad,
                CharacterId.Silent,
                CharacterId.Defect,
                CharacterId.Necrobinder,
                CharacterId.Regent
            ],
            AscensionLevel = options.AscensionLevel,
            PlayerCount = 1,
            IncludeDarvSharedAncient = true,
            IncludeAct2 = true,
            IncludeAct3 = true
        });

        var acts = new Dictionary<int, AncientActResult>();
        foreach (var act in preview.Acts)
        {
            var relics = act.AncientOptions
                .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
                .Select(option => new AncientRelicOption(
                    option.RelicId!,
                    option.Title ?? option.RelicId!,
                    option.Description,
                    option.Note))
                .ToList();

            acts[act.ActNumber] = new AncientActResult(
                act.ActNumber,
                act.AncientId ?? string.Empty,
                act.AncientName ?? act.AncientId ?? string.Empty,
                relics);
        }

        return new AncientPreview(acts);
    }
}

sealed record AncientActResult(
    int ActNumber,
    string AncientId,
    string AncientName,
    IReadOnlyList<AncientRelicOption> Options);

sealed record AncientRelicOption(
    string RelicId,
    string Title,
    string? Description,
    string? Note);

enum BagRarity
{
    None,
    Common,
    Uncommon,
    Rare,
    Shop
}

enum OpportunityKind
{
    Treasure,
    Elite,
    Shop,
    Ancient
}

enum FirstSourceKind
{
    Treasure,
    Elite,
    Shop,
    AncientAct2,
    AncientAct3
}

sealed class RouteProfile
{
    private RouteProfile(
        string id,
        string title,
        string description,
        IReadOnlyList<ActRouteProfile> acts,
        double regularPotionChance,
        double elitePotionChance)
    {
        Id = id;
        Title = title;
        Description = description;
        Acts = acts;
        RegularPotionChance = regularPotionChance;
        ElitePotionChance = elitePotionChance;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<ActRouteProfile> Acts { get; }

    public double RegularPotionChance { get; }

    public double ElitePotionChance { get; }

    public static IReadOnlyList<RouteProfile> All { get; } =
    [
        new RouteProfile(
            "balanced",
            "Balanced",
            "平衡路线：默认会进少量商店、打中等数量精英，古神有中等概率看到。",
            [
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.25), (1, 0.60), (2, 0.15)),
                    ancientVisitChance: 0.00,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.25), (1, 0.55), (2, 0.20)),
                    slotVariants:
                    [
                        [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                        [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                        [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.55), (3, 0.20)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                    ancientVisitChance: 0.68,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.50), (2, 0.35)),
                    slotVariants:
                    [
                        [OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite],
                        [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.35), (1, 0.50), (2, 0.15)),
                    ancientVisitChance: 0.62,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                    slotVariants:
                    [
                        [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                    ])
            ],
            regularPotionChance: 0.38,
            elitePotionChance: 0.58),
        new RouteProfile(
            "aggressive",
            "Aggressive",
            "多精英路线：更偏向提早打精英，商店更少，古神概率略低。",
            [
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.50), (3, 0.15)),
                    eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                    ancientVisitChance: 0.00,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.15), (1, 0.45), (2, 0.40)),
                    slotVariants:
                    [
                        [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                        [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                        [OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.35), (2, 0.45), (3, 0.20)),
                    eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.40), (1, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                    ancientVisitChance: 0.60,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.40), (2, 0.50)),
                    slotVariants:
                    [
                        [OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                        [OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop],
                        [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                    eliteCounts: WeightedIntDistribution.Of((2, 0.45), (3, 0.35), (1, 0.20)),
                    shopCounts: WeightedIntDistribution.Of((0, 0.45), (1, 0.45), (2, 0.10)),
                    ancientVisitChance: 0.56,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.10), (1, 0.35), (2, 0.55)),
                    slotVariants:
                    [
                        [OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                        [OpportunityKind.Ancient, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure],
                        [OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure]
                    ])
            ],
            regularPotionChance: 0.35,
            elitePotionChance: 0.60),
        new RouteProfile(
            "shopper",
            "Shopper",
            "多商店路线：更容易看到商店陈列 relic，精英数量更保守，古神概率略高。",
            [
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((2, 0.40), (3, 0.45), (1, 0.15)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                    ancientVisitChance: 0.00,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.30), (1, 0.55), (2, 0.15)),
                    slotVariants:
                    [
                        [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                        [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop],
                        [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Treasure, OpportunityKind.Elite, OpportunityKind.Shop]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.25), (2, 0.50), (3, 0.25)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (3, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                    ancientVisitChance: 0.74,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                    slotVariants:
                    [
                        [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                    ]),
                new ActRouteProfile(
                    treasureCounts: WeightedIntDistribution.Of((1, 0.30), (2, 0.45), (3, 0.25)),
                    eliteCounts: WeightedIntDistribution.Of((1, 0.50), (2, 0.35), (3, 0.15)),
                    shopCounts: WeightedIntDistribution.Of((1, 0.45), (2, 0.40), (0, 0.15)),
                    ancientVisitChance: 0.70,
                    betweenOpportunityCombats: WeightedIntDistribution.Of((0, 0.20), (1, 0.55), (2, 0.25)),
                    slotVariants:
                    [
                        [OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Shop, OpportunityKind.Ancient, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure],
                        [OpportunityKind.Shop, OpportunityKind.Treasure, OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure]
                    ])
            ],
            regularPotionChance: 0.40,
            elitePotionChance: 0.55)
    ];
}

sealed class ActRouteProfile
{
    public ActRouteProfile(
        WeightedIntDistribution treasureCounts,
        WeightedIntDistribution eliteCounts,
        WeightedIntDistribution shopCounts,
        double ancientVisitChance,
        WeightedIntDistribution betweenOpportunityCombats,
        IReadOnlyList<OpportunityKind[]> slotVariants)
    {
        TreasureCounts = treasureCounts;
        EliteCounts = eliteCounts;
        ShopCounts = shopCounts;
        AncientVisitChance = ancientVisitChance;
        BetweenOpportunityCombats = betweenOpportunityCombats;
        SlotVariants = slotVariants;
    }

    public WeightedIntDistribution TreasureCounts { get; }

    public WeightedIntDistribution EliteCounts { get; }

    public WeightedIntDistribution ShopCounts { get; }

    public double AncientVisitChance { get; }

    public WeightedIntDistribution BetweenOpportunityCombats { get; }

    public IReadOnlyList<OpportunityKind[]> SlotVariants { get; }
}

sealed class WeightedIntDistribution
{
    private WeightedIntDistribution(IReadOnlyList<WeightedIntOption> options)
    {
        Options = options;
        TotalWeight = options.Sum(option => option.Weight);
    }

    public IReadOnlyList<WeightedIntOption> Options { get; }

    public double TotalWeight { get; }

    public static WeightedIntDistribution Of(params (int Value, double Weight)[] options)
    {
        return new WeightedIntDistribution(options
            .Select(option => new WeightedIntOption(option.Value, option.Weight))
            .ToList());
    }

    public int Sample(GameRng rng)
    {
        var roll = rng.NextDouble() * TotalWeight;
        foreach (var option in Options)
        {
            roll -= option.Weight;
            if (roll <= 0)
            {
                return option.Value;
            }
        }

        return Options[^1].Value;
    }

    public string Describe()
    {
        return string.Join(", ", Options.Select(option => $"{option.Value} ({option.Weight:P0})"));
    }
}

sealed record WeightedIntOption(int Value, double Weight);

sealed class MonteCarloSimulator
{
    private static readonly HashSet<string> ShopBlockedRelics =
    [
        "AMETHYST_AUBERGINE",
        "BOWLER_HAT",
        "LUCKY_FYSH",
        "OLD_COIN",
        "THE_COURIER"
    ];

    public static ProfileResult Run(
        Options options,
        RouteProfile profile,
        BaselineState baseline,
        AncientPreview ancientPreview,
        RelicRules relicRules)
    {
        var routeRng = new GameRng(options.SeedValue, $"relic_visibility_{profile.Id}");
        var stats = new Dictionary<string, AppearanceStats>(StringComparer.OrdinalIgnoreCase);
        var earliestSeenSamples = new List<IReadOnlyList<string>>(capacity: 3);

        for (var sample = 0; sample < options.Samples; sample++)
        {
            var state = baseline.Clone();
            var sampleSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sampleFirstSource = new Dictionary<string, FirstSourceKind>(StringComparer.OrdinalIgnoreCase);
            var earliestThisSample = new List<string>();

            var opportunities = BuildOpportunities(routeRng, profile);
            var currentAct = 0;
            foreach (var opportunity in opportunities)
            {
                if (currentAct != opportunity.ActNumber)
                {
                    currentAct = opportunity.ActNumber;
                }
                else
                {
                    var actProfile = profile.Acts[opportunity.ActNumber - 1];
                    var regularCombats = actProfile.BetweenOpportunityCombats.Sample(routeRng);
                    for (var i = 0; i < regularCombats; i++)
                    {
                        ConsumeRegularCombat(state.RewardsRng, profile.RegularPotionChance);
                    }
                }

                var shownRelics = opportunity.Kind switch
                {
                    OpportunityKind.Treasure => ShowTreasure(state, opportunity.ActNumber, relicRules),
                    OpportunityKind.Elite => ShowElite(state, opportunity.ActNumber, relicRules, profile.ElitePotionChance),
                    OpportunityKind.Shop => ShowShop(state, opportunity.ActNumber, relicRules),
                    OpportunityKind.Ancient => ShowAncient(ancientPreview, opportunity.ActNumber),
                    _ => Array.Empty<ShownRelic>()
                };

                foreach (var shown in shownRelics)
                {
                    if (!sampleSeen.Add(shown.RelicId))
                    {
                        continue;
                    }

                    sampleFirstSeen[shown.RelicId] = opportunity.GlobalIndex;
                    sampleFirstSource[shown.RelicId] = shown.Source;
                    if (opportunity.GlobalIndex <= options.EarlyWindow)
                    {
                        earliestThisSample.Add(shown.RelicId);
                    }
                }
            }

            if (earliestSeenSamples.Count < 3)
            {
                earliestSeenSamples.Add(earliestThisSample);
            }

            foreach (var (relicId, firstIndex) in sampleFirstSeen)
            {
                if (!stats.TryGetValue(relicId, out var relicStats))
                {
                    relicStats = new AppearanceStats();
                    stats[relicId] = relicStats;
                }

                relicStats.SeenCount++;
                relicStats.FirstOpportunityTotal += firstIndex;
                if (firstIndex <= options.EarlyWindow)
                {
                    relicStats.EarlyCount++;
                }

                relicStats.FirstSourceCounts[sampleFirstSource[relicId]] =
                    relicStats.FirstSourceCounts.GetValueOrDefault(sampleFirstSource[relicId]) + 1;
            }
        }

        return new ProfileResult(profile, stats, earliestSeenSamples);
    }

    private static List<Opportunity> BuildOpportunities(GameRng rng, RouteProfile profile)
    {
        var result = new List<Opportunity>();
        var globalIndex = 0;
        for (var actNumber = 1; actNumber <= profile.Acts.Count; actNumber++)
        {
            var actProfile = profile.Acts[actNumber - 1];
            var remaining = new Dictionary<OpportunityKind, int>
            {
                [OpportunityKind.Treasure] = actProfile.TreasureCounts.Sample(rng),
                [OpportunityKind.Elite] = actProfile.EliteCounts.Sample(rng),
                [OpportunityKind.Shop] = actProfile.ShopCounts.Sample(rng),
                [OpportunityKind.Ancient] = actNumber == 1
                    ? 0
                    : (rng.NextDouble() < actProfile.AncientVisitChance ? 1 : 0)
            };

            var slotVariant = actProfile.SlotVariants[rng.NextInt(actProfile.SlotVariants.Count)];
            foreach (var kind in slotVariant)
            {
                if (!remaining.TryGetValue(kind, out var count) || count <= 0)
                {
                    continue;
                }

                remaining[kind] = count - 1;
                globalIndex++;
                result.Add(new Opportunity(actNumber, kind, globalIndex));
            }

            foreach (var kind in new[] { OpportunityKind.Ancient, OpportunityKind.Shop, OpportunityKind.Elite, OpportunityKind.Treasure })
            {
                while (remaining.GetValueOrDefault(kind) > 0)
                {
                    remaining[kind]--;
                    globalIndex++;
                    result.Add(new Opportunity(actNumber, kind, globalIndex));
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<ShownRelic> ShowTreasure(BaselineState state, int actNumber, RelicRules rules)
    {
        var rarity = RollRelicRarity(state.TreasureRng);
        var relic = state.SharedBag.PullFromFront(rarity, relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.PlayerBag.Remove(relic);
        return [new ShownRelic(relic, FirstSourceKind.Treasure)];
    }

    private static IReadOnlyList<ShownRelic> ShowElite(
        BaselineState state,
        int actNumber,
        RelicRules rules,
        double elitePotionChance)
    {
        ConsumeEliteCombatPreRelic(state.RewardsRng, elitePotionChance);
        var rarity = RollRelicRarity(state.RewardsRng);
        var relic = state.PlayerBag.PullFromFront(rarity, relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.SharedBag.Remove(relic);
        return [new ShownRelic(relic, actNumber >= 3 ? FirstSourceKind.Elite : FirstSourceKind.Elite)];
    }

    private static IReadOnlyList<ShownRelic> ShowShop(
        BaselineState state,
        int actNumber,
        RelicRules rules)
    {
        var shown = new List<ShownRelic>(3);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rarity in new[] { RollRelicRarity(state.RewardsRng), RollRelicRarity(state.RewardsRng), BagRarity.Shop })
        {
            var relic = state.PlayerBag.PullFromBack(
                rarity,
                selected,
                ShopBlockedRelics,
                relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
            if (relic == null)
            {
                continue;
            }

            selected.Add(relic);
            state.SharedBag.Remove(relic);
            shown.Add(new ShownRelic(relic, FirstSourceKind.Shop));
        }

        return shown;
    }

    private static IReadOnlyList<ShownRelic> ShowAncient(AncientPreview preview, int actNumber)
    {
        if (!preview.Acts.TryGetValue(actNumber, out var act))
        {
            return Array.Empty<ShownRelic>();
        }

        var source = actNumber == 2 ? FirstSourceKind.AncientAct2 : FirstSourceKind.AncientAct3;
        return act.Options
            .Select(option => new ShownRelic(option.RelicId, source))
            .ToList();
    }

    private static void ConsumeRegularCombat(GameRng rewardsRng, double potionChance)
    {
        _ = rewardsRng.NextInt(10, 21);
        MaybeConsumePotion(rewardsRng, potionChance);
        ConsumeCardRewardBurns(rewardsRng);
    }

    private static void ConsumeEliteCombatPreRelic(GameRng rewardsRng, double potionChance)
    {
        _ = rewardsRng.NextInt(25, 36);
        MaybeConsumePotion(rewardsRng, potionChance);
        ConsumeCardRewardBurns(rewardsRng);
    }

    private static void MaybeConsumePotion(GameRng rewardsRng, double chance)
    {
        if (rewardsRng.NextDouble() > chance)
        {
            return;
        }

        _ = rewardsRng.NextFloat();
        _ = rewardsRng.NextInt(1);
    }

    private static void ConsumeCardRewardBurns(GameRng rewardsRng)
    {
        for (var i = 0; i < 3; i++)
        {
            _ = rewardsRng.NextFloat();
            _ = rewardsRng.NextInt(1);
            _ = rewardsRng.NextDouble();
        }
    }

    private static BagRarity RollRelicRarity(GameRng rng)
    {
        var value = rng.NextFloat();
        if (value < 0.5f)
        {
            return BagRarity.Common;
        }

        if (value < 0.83f)
        {
            return BagRarity.Uncommon;
        }

        return BagRarity.Rare;
    }
}

sealed record Opportunity(int ActNumber, OpportunityKind Kind, int GlobalIndex);

sealed record ShownRelic(string RelicId, FirstSourceKind Source);

sealed class AppearanceStats
{
    public int SeenCount { get; set; }

    public int EarlyCount { get; set; }

    public double FirstOpportunityTotal { get; set; }

    public Dictionary<FirstSourceKind, int> FirstSourceCounts { get; } = new();
}

sealed class ProfileResult
{
    public ProfileResult(
        RouteProfile profile,
        IReadOnlyDictionary<string, AppearanceStats> stats,
        IReadOnlyList<IReadOnlyList<string>> earlySamples)
    {
        Profile = profile;
        Stats = stats;
        EarlySamples = earlySamples;
    }

    public RouteProfile Profile { get; }

    public IReadOnlyDictionary<string, AppearanceStats> Stats { get; }

    public IReadOnlyList<IReadOnlyList<string>> EarlySamples { get; }
}

sealed class ReportBuilder
{
    public static string Build(
        Options options,
        BaselineState baseline,
        AncientPreview ancientPreview,
        RelicRules relicRules,
        IReadOnlyList<ProfileResult> profileResults,
        LocalizationTable titles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Relic Visibility Probe");
        sb.AppendLine();
        sb.AppendLine($"- Seed: `{options.SeedText}` (`0x{options.SeedValue:X8}`)");
        sb.AppendLine($"- Character: `{options.Character}`");
        sb.AppendLine($"- Samples per profile: `{options.Samples}`");
        sb.AppendLine($"- Early window: first `{options.EarlyWindow}` relic opportunities");
        sb.AppendLine($"- Bag init: `official DLL RelicGrabBag.Populate`");
        sb.AppendLine($"- Shared bag size: `{baseline.SharedBag.TotalCount}`");
        sb.AppendLine($"- Player bag size: `{baseline.PlayerBag.TotalCount}`");
        sb.AppendLine($"- Act3-only gate tracked relics: `{relicRules.BeforeAct3TreasureChestRelics.Count}`");
        sb.AppendLine();

        sb.AppendLine("## 脚本边界");
        sb.AppendLine();
        sb.AppendLine("- 这是“概率版曝光预测”，不是整局掉落顺序的精确还原。");
        sb.AppendLine("- 当前只统计 4 类玩家最容易感知到的 relic 曝光：宝箱、精英、商店陈列、Act2/Act3 古神选项。");
        sb.AppendLine("- 初始 relic bag 使用官方 DLL 生成。");
        sb.AppendLine("- 精英/商店会消费 `Rewards` RNG，但这里仍然是路线画像下的 Monte Carlo，不是完整地图与奖励链还原。");
        sb.AppendLine();

        sb.AppendLine("## 第二幕 / 第三幕古神");
        sb.AppendLine();
        foreach (var actNumber in ancientPreview.Acts.Keys.OrderBy(number => number))
        {
            var act = ancientPreview.Acts[actNumber];
            sb.AppendLine($"### Act {act.ActNumber}: {act.AncientName} (`{act.AncientId}`)");
            sb.AppendLine();
            foreach (var option in act.Options)
            {
                sb.AppendLine($"- {FormatRelic(option.RelicId, titles)}");
                if (!string.IsNullOrWhiteSpace(option.Note))
                {
                    sb.AppendLine($"  Note: {option.Note}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 路线画像结果");
        sb.AppendLine();
        foreach (var result in profileResults)
        {
            AppendProfile(sb, options, result, titles);
        }

        return sb.ToString();
    }

    private static void AppendProfile(
        StringBuilder sb,
        Options options,
        ProfileResult result,
        LocalizationTable titles)
    {
        sb.AppendLine($"### {result.Profile.Title}");
        sb.AppendLine();
        sb.AppendLine(result.Profile.Description);
        sb.AppendLine();
        for (var i = 0; i < result.Profile.Acts.Count; i++)
        {
            var act = result.Profile.Acts[i];
            sb.AppendLine($"- Act {i + 1}: treasure `{act.TreasureCounts.Describe()}` / elite `{act.EliteCounts.Describe()}` / shop `{act.ShopCounts.Describe()}` / ancient chance `{act.AncientVisitChance:P0}`");
        }
        sb.AppendLine();

        var ranked = result.Stats
            .Select(pair => ToRankedRelic(pair.Key, pair.Value, options.Samples))
            .OrderByDescending(item => item.EarlyProbability)
            .ThenByDescending(item => item.SeenProbability)
            .ThenBy(item => item.AverageFirstOpportunity)
            .ThenBy(item => item.RelicId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sb.AppendLine("#### 更容易先碰到什么 relic");
        sb.AppendLine();
        sb.AppendLine("| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");
        foreach (var (item, index) in ranked.Take(15).Select((item, index) => (item, index)))
        {
            sb.AppendLine($"| {index + 1} | {FormatRelic(item.RelicId, titles)} | {item.EarlyProbability:P1} | {item.SeenProbability:P1} | {item.AverageFirstOpportunity:F2} | {FormatSource(item.MostCommonSource)} |");
        }
        sb.AppendLine();

        sb.AppendLine("#### 这局大概率会出现哪些 relic");
        sb.AppendLine();
        sb.AppendLine("| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");
        foreach (var (item, index) in ranked
                     .OrderByDescending(item => item.SeenProbability)
                     .ThenByDescending(item => item.EarlyProbability)
                     .ThenBy(item => item.AverageFirstOpportunity)
                     .ThenBy(item => item.RelicId, StringComparer.OrdinalIgnoreCase)
                     .Take(20)
                     .Select((item, index) => (item, index)))
        {
            sb.AppendLine($"| {index + 1} | {FormatRelic(item.RelicId, titles)} | {item.SeenProbability:P1} | {item.EarlyProbability:P1} | {item.AverageFirstOpportunity:F2} | {FormatSource(item.MostCommonSource)} |");
        }
        sb.AppendLine();

        if (result.EarlySamples.Count > 0)
        {
            sb.AppendLine("#### 早期样本示例");
            sb.AppendLine();
            foreach (var (sample, index) in result.EarlySamples.Select((sample, index) => (sample, index)))
            {
                var rendered = sample.Count == 0
                    ? "（这一轮前期没有记录到 relic 曝光）"
                    : string.Join(" / ", sample.Take(8).Select(relicId => FormatRelic(relicId, titles)));
                sb.AppendLine($"- Sample {index + 1}: {rendered}");
            }
            sb.AppendLine();
        }
    }

    private static RankedRelic ToRankedRelic(string relicId, AppearanceStats stats, int samples)
    {
        var mostCommonSource = stats.FirstSourceCounts.Count == 0
            ? FirstSourceKind.Treasure
            : stats.FirstSourceCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First()
                .Key;

        return new RankedRelic(
            relicId,
            stats.EarlyCount / (double)samples,
            stats.SeenCount / (double)samples,
            stats.SeenCount == 0 ? 999 : stats.FirstOpportunityTotal / stats.SeenCount,
            mostCommonSource);
    }

    private static string FormatRelic(string relicId, LocalizationTable titles)
    {
        var title = titles.GetRelicTitle(relicId);
        return string.IsNullOrWhiteSpace(title)
            ? $"`{relicId}`"
            : $"{title} (`{relicId}`)";
    }

    private static string FormatSource(FirstSourceKind source)
    {
        return source switch
        {
            FirstSourceKind.Treasure => "Treasure",
            FirstSourceKind.Elite => "Elite",
            FirstSourceKind.Shop => "Shop",
            FirstSourceKind.AncientAct2 => "Ancient Act2",
            FirstSourceKind.AncientAct3 => "Ancient Act3",
            _ => source.ToString()
        };
    }
}

sealed record RankedRelic(
    string RelicId,
    double EarlyProbability,
    double SeenProbability,
    double AverageFirstOpportunity,
    FirstSourceKind MostCommonSource);

static class DatasetLoader
{
    public static NeowOptionDataset Load(string path)
    {
        var dataset = JsonSerializer.Deserialize<NeowOptionDataset>(File.ReadAllText(path));
        return dataset ?? throw new InvalidOperationException($"Failed to load Neow dataset: {path}");
    }
}

enum SaveNodeKind
{
    RegularCombat,
    Treasure,
    Elite,
    Shop,
    Ancient,
    Other
}

sealed record SaveNode(
    int ActNumber,
    int NodeIndex,
    SaveNodeKind Kind,
    string Label,
    IReadOnlyList<string> ActualOfferedRelics,
    string? ActualPickedRelic,
    IReadOnlyList<string> ActualCardChoiceIds,
    bool HasPotionRewardChoice);

sealed class SaveSnapshot
{
    private SaveSnapshot(
        string path,
        string seedText,
        CharacterId character,
        int ascensionLevel,
        string? startingRelicId,
        IReadOnlyList<SaveNode> nodes)
    {
        Path = path;
        SeedText = seedText;
        Character = character;
        AscensionLevel = ascensionLevel;
        StartingRelicId = startingRelicId;
        Nodes = nodes;
    }

    public string Path { get; }

    public string SeedText { get; }

    public CharacterId Character { get; }

    public int AscensionLevel { get; }

    public string? StartingRelicId { get; }

    public IReadOnlyList<SaveNode> Nodes { get; }

    public static SaveSnapshot Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Failed to parse save file: {path}");

        var seedText = root["seed"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Save file missing seed: {path}");
        var character = ParseCharacter(
            root["players"]?[0]?["character_id"]?.GetValue<string>() ??
            root["players"]?[0]?["character"]?.GetValue<string>());
        var ascensionLevel = root["ascension"]?.GetValue<int>() ?? 0;
        var history = root["map_point_history"]?.AsArray()
            ?? throw new InvalidOperationException($"Save file missing map_point_history: {path}");

        string? startingRelicId = null;
        var nodes = new List<SaveNode>();
        for (var actIndex = 0; actIndex < history.Count; actIndex++)
        {
            var actHistory = history[actIndex]?.AsArray() ?? [];
            var actNumber = actIndex + 1;
            for (var nodeIndex = 0; nodeIndex < actHistory.Count; nodeIndex++)
            {
                var node = actHistory[nodeIndex];
                if (node is null)
                {
                    continue;
                }

                var mapPointType = node["map_point_type"]?.GetValue<string>() ?? string.Empty;
                var roomTypes = node["rooms"]?.AsArray()?
                    .Select(room => room?["room_type"]?.GetValue<string>() ?? string.Empty)
                    .ToList()
                    ?? [];
                var roomModelId = node["rooms"]?[0]?["model_id"]?.GetValue<string>() ?? string.Empty;
                var stats = node["player_stats"]?[0];
                var relicChoices = ExtractRelicChoices(stats?["relic_choices"]?.AsArray());
                var actualPickedRelic = relicChoices.FirstOrDefault(choice => choice.WasPicked)?.RelicId;
                var actualCardChoiceIds = ExtractCardChoiceIds(stats?["card_choices"]?.AsArray());
                var hasPotionRewardChoice = (stats?["potion_choices"]?.AsArray()?.Count ?? 0) > 0;

                if (actNumber == 1 &&
                    string.Equals(mapPointType, "ancient", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(roomModelId, "EVENT.NEOW", StringComparison.OrdinalIgnoreCase))
                {
                    startingRelicId = actualPickedRelic;
                    continue;
                }

                var kind = DetermineKind(actNumber, mapPointType, roomTypes);

                IReadOnlyList<string> actualOfferedRelics = Array.Empty<string>();
                switch (kind)
                {
                    case SaveNodeKind.Shop:
                    case SaveNodeKind.Treasure:
                    case SaveNodeKind.Elite:
                        actualOfferedRelics = relicChoices.Select(choice => choice.RelicId).ToList();
                        break;
                    case SaveNodeKind.Ancient:
                        actualOfferedRelics = ExtractAncientChoices(stats?["ancient_choice"]?.AsArray())
                            .Select(choice => choice.RelicId)
                            .ToList();
                        actualPickedRelic = ExtractAncientChoices(stats?["ancient_choice"]?.AsArray())
                            .FirstOrDefault(choice => choice.WasPicked)
                            ?.RelicId;
                        break;
                }

                nodes.Add(new SaveNode(
                    actNumber,
                    nodeIndex + 1,
                    kind,
                    $"Act {actNumber} Node {nodeIndex + 1} {kind}",
                    actualOfferedRelics,
                    actualPickedRelic,
                    actualCardChoiceIds,
                    hasPotionRewardChoice));
            }
        }

        return new SaveSnapshot(path, seedText, character, ascensionLevel, startingRelicId, nodes);
    }

    private static SaveNodeKind DetermineKind(int actNumber, string mapPointType, IReadOnlyList<string> roomTypes)
    {
        if (actNumber > 1 && string.Equals(mapPointType, "ancient", StringComparison.OrdinalIgnoreCase))
        {
            return SaveNodeKind.Ancient;
        }

        if (roomTypes.Any(roomType => string.Equals(roomType, "elite", StringComparison.OrdinalIgnoreCase)))
        {
            return SaveNodeKind.Elite;
        }

        if (roomTypes.Any(roomType => string.Equals(roomType, "monster", StringComparison.OrdinalIgnoreCase)))
        {
            return SaveNodeKind.RegularCombat;
        }

        if (roomTypes.Any(roomType => string.Equals(roomType, "treasure", StringComparison.OrdinalIgnoreCase)))
        {
            return SaveNodeKind.Treasure;
        }

        if (roomTypes.Any(roomType => string.Equals(roomType, "shop", StringComparison.OrdinalIgnoreCase)))
        {
            return SaveNodeKind.Shop;
        }

        return SaveNodeKind.Other;
    }

    private static CharacterId ParseCharacter(string? raw)
    {
        var normalized = raw ?? string.Empty;
        var lastDot = normalized.LastIndexOf('.');
        if (lastDot >= 0)
        {
            normalized = normalized[(lastDot + 1)..];
        }

        if (CharacterIdExtensions.TryParse(normalized, out var character))
        {
            return character;
        }

        return normalized.ToUpperInvariant() switch
        {
            "SILENT" => CharacterId.Silent,
            "IRONCLAD" => CharacterId.Ironclad,
            "DEFECT" => CharacterId.Defect,
            "NECROBINDER" => CharacterId.Necrobinder,
            "REGENT" => CharacterId.Regent,
            _ => throw new InvalidOperationException($"Unknown save character: {raw}")
        };
    }

    private static IReadOnlyList<SaveRelicChoice> ExtractRelicChoices(JsonArray? array)
    {
        return array?
            .Select(item => new SaveRelicChoice(
                NormalizeRelicId(item?["choice"]?.GetValue<string>()),
                item?["was_picked"]?.GetValue<bool>() == true))
            .Where(choice => !string.IsNullOrWhiteSpace(choice.RelicId))
            .ToList()
            ?? [];
    }

    private static IReadOnlyList<SaveRelicChoice> ExtractAncientChoices(JsonArray? array)
    {
        return array?
            .Select(item => new SaveRelicChoice(
                NormalizeRelicId(item?["TextKey"]?.GetValue<string>()),
                item?["was_chosen"]?.GetValue<bool>() == true))
            .Where(choice => !string.IsNullOrWhiteSpace(choice.RelicId))
            .ToList()
            ?? [];
    }

    private static IReadOnlyList<string> ExtractCardChoiceIds(JsonArray? array)
    {
        return array?
            .Select(item => NormalizeRelicId(item?["card"]?["id"]?.GetValue<string>()))
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
            .ToList()
            ?? [];
    }

    private static string NormalizeRelicId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return (lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed).ToUpperInvariant();
    }

    private sealed record SaveRelicChoice(string RelicId, bool WasPicked);
}

sealed record SaveReplayNodeResult(
    SaveNode Node,
    IReadOnlyList<string> PredictedRelics,
    int MatchCount,
    int TotalCount);

sealed class SaveReplayResult
{
    public required IReadOnlyList<SaveReplayNodeResult> Nodes { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public int PickupMatches => Nodes
        .Where(node => node.Node.Kind is SaveNodeKind.Treasure or SaveNodeKind.Elite or SaveNodeKind.Ancient)
        .Sum(node => node.MatchCount);

    public int PickupTotal => Nodes
        .Where(node => node.Node.Kind is SaveNodeKind.Treasure or SaveNodeKind.Elite or SaveNodeKind.Ancient)
        .Sum(node => node.TotalCount);

    public int ShopMatches => Nodes
        .Where(node => node.Node.Kind == SaveNodeKind.Shop)
        .Sum(node => node.MatchCount);

    public int ShopTotal => Nodes
        .Where(node => node.Node.Kind == SaveNodeKind.Shop)
        .Sum(node => node.TotalCount);

    public int CombinedMatches => Nodes.Sum(node => node.MatchCount);

    public int CombinedTotal => Nodes.Sum(node => node.TotalCount);
}

sealed class RewardSimulationModel
{
    private RewardSimulationModel(
        IReadOnlyDictionary<string, NeowCardMetadata> cardMetadataMap,
        IReadOnlyList<string> cardPool,
        IReadOnlyDictionary<CardRarity, string[]> cardPoolByRarity,
        IReadOnlyList<string> potionPool,
        IReadOnlyDictionary<PotionRarity, string[]> potionPoolByRarity)
    {
        CardMetadataMap = cardMetadataMap;
        CardPool = cardPool;
        CardPoolByRarity = cardPoolByRarity;
        PotionPool = potionPool;
        PotionPoolByRarity = potionPoolByRarity;
    }

    public IReadOnlyDictionary<string, NeowCardMetadata> CardMetadataMap { get; }

    public IReadOnlyList<string> CardPool { get; }

    public IReadOnlyDictionary<CardRarity, string[]> CardPoolByRarity { get; }

    public IReadOnlyList<string> PotionPool { get; }

    public IReadOnlyDictionary<PotionRarity, string[]> PotionPoolByRarity { get; }

    public static RewardSimulationModel Create(NeowOptionDataset dataset, CharacterId character, int playerCount)
    {
        var cardPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
            ? characterCards
                .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                                 IsCardAllowed(metadata, playerCount))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        var cardPoolByRarity = cardPool
            .Where(cardId => dataset.CardMetadataMap.ContainsKey(cardId))
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var potionPool = new List<string>(dataset.SharedPotionPoolList);
        if (dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions))
        {
            potionPool.AddRange(characterPotions);
        }

        potionPool = potionPool
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var potionPoolByRarity = potionPool
            .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return new RewardSimulationModel(
            dataset.CardMetadataMap,
            cardPool,
            cardPoolByRarity,
            potionPool,
            potionPoolByRarity);
    }

    private static bool IsCardAllowed(NeowCardMetadata metadata, int playerCount)
    {
        if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
        {
            return false;
        }

        if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
        {
            return false;
        }

        return metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
    }
}

static class SaveReplayAnalyzer
{
    private static readonly HashSet<string> ShopBlockedRelics =
    [
        "AMETHYST_AUBERGINE",
        "BOWLER_HAT",
        "LUCKY_FYSH",
        "OLD_COIN",
        "THE_COURIER"
    ];

    public static SaveReplayResult Analyze(
        Options options,
        SaveSnapshot snapshot,
        BaselineState baseline,
        AncientPreview ancientPreview,
        RelicRules relicRules,
        NeowOptionDataset dataset)
    {
        var state = baseline.Clone();
        var rewardModel = RewardSimulationModel.Create(dataset, snapshot.Character, state.PlayerCount);
        var warnings = new List<string>();
        ApplyStartingRelicEffects(state, rewardModel, snapshot.StartingRelicId, warnings);

        var results = new List<SaveReplayNodeResult>();
        foreach (var node in snapshot.Nodes)
        {
            switch (node.Kind)
            {
                case SaveNodeKind.RegularCombat:
                    AdvanceActualRegularCombat(state, rewardModel, node.ActualCardChoiceIds, node.HasPotionRewardChoice);
                    break;
                case SaveNodeKind.Treasure:
                {
                    var predicted = ShowTreasure(state.Clone(), node.ActNumber, relicRules).Select(item => item.RelicId).ToList();
                    results.Add(new SaveReplayNodeResult(
                        node,
                        predicted,
                        MatchPicked(node.ActualPickedRelic, predicted),
                        string.IsNullOrWhiteSpace(node.ActualPickedRelic) ? 0 : 1));
                    AdvanceActualTreasure(state, node.ActualPickedRelic);
                    break;
                }
                case SaveNodeKind.Elite:
                {
                    var predicted = ShowElite(state.Clone(), node.ActNumber, relicRules, rewardModel).Select(item => item.RelicId).ToList();
                    results.Add(new SaveReplayNodeResult(
                        node,
                        predicted,
                        MatchPicked(node.ActualPickedRelic, predicted),
                        string.IsNullOrWhiteSpace(node.ActualPickedRelic) ? 0 : 1));
                    AdvanceActualElite(state, rewardModel, node.ActualPickedRelic, node.ActualCardChoiceIds, node.HasPotionRewardChoice);
                    break;
                }
                case SaveNodeKind.Shop:
                {
                    var predicted = ShowShop(state.Clone(), node.ActNumber, relicRules).Select(item => item.RelicId).ToList();
                    results.Add(new SaveReplayNodeResult(
                        node,
                        predicted,
                        CountOverlap(node.ActualOfferedRelics, predicted),
                        node.ActualOfferedRelics.Count));
                    AdvanceActualShop(state, node.ActualOfferedRelics);
                    break;
                }
                case SaveNodeKind.Ancient:
                {
                    var predicted = ShowAncient(ancientPreview, node.ActNumber).Select(item => item.RelicId).ToList();
                    results.Add(new SaveReplayNodeResult(
                        node,
                        predicted,
                        MatchPicked(node.ActualPickedRelic, predicted),
                        string.IsNullOrWhiteSpace(node.ActualPickedRelic) ? 0 : 1));
                    AdvanceActualAncient(state, node.ActualPickedRelic);
                    break;
                }
                case SaveNodeKind.Other:
                    if (!string.IsNullOrWhiteSpace(node.ActualPickedRelic))
                    {
                        state.SharedBag.Remove(node.ActualPickedRelic);
                        state.PlayerBag.Remove(node.ActualPickedRelic);
                    }
                    break;
            }
        }

        return new SaveReplayResult
        {
            Nodes = results,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<ShownRelic> ShowTreasure(BaselineState state, int actNumber, RelicRules rules)
    {
        var rarity = RollRelicRarity(state.TreasureRng);
        var relic = state.SharedBag.PullFromFront(rarity, relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.PlayerBag.Remove(relic);
        return [new ShownRelic(relic, FirstSourceKind.Treasure)];
    }

    private static IReadOnlyList<ShownRelic> ShowElite(
        BaselineState state,
        int actNumber,
        RelicRules rules,
        RewardSimulationModel rewardModel)
    {
        ConsumeEliteCombatPreRelic(state, rewardModel);
        var rarity = RollRelicRarity(state.RewardsRng);
        var relic = state.PlayerBag.PullFromFront(rarity, relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
        if (relic == null)
        {
            return Array.Empty<ShownRelic>();
        }

        state.SharedBag.Remove(relic);
        return [new ShownRelic(relic, FirstSourceKind.Elite)];
    }

    private static IReadOnlyList<ShownRelic> ShowShop(BaselineState state, int actNumber, RelicRules rules)
    {
        ConsumeShopPreRelicRewards(state);
        var shown = new List<ShownRelic>(3);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rarity in new[] { RollRelicRarity(state.RewardsRng), RollRelicRarity(state.RewardsRng), BagRarity.Shop })
        {
            var relic = state.PlayerBag.PullFromBack(
                rarity,
                selected,
                ShopBlockedRelics,
                relicId => rules.IsAllowed(relicId, actNumber, state.PlayerCount));
            if (relic == null)
            {
                continue;
            }

            selected.Add(relic);
            state.SharedBag.Remove(relic);
            shown.Add(new ShownRelic(relic, FirstSourceKind.Shop));
        }

        return shown;
    }

    private static void ConsumeShopPreRelicRewards(BaselineState state)
    {
        for (var i = 0; i < 5; i++)
        {
            _ = state.RewardsRng.NextFloat();
            _ = state.RewardsRng.NextFloat();
        }

        for (var i = 0; i < 2; i++)
        {
            _ = state.RewardsRng.NextFloat();
        }
    }

    private static IReadOnlyList<ShownRelic> ShowAncient(AncientPreview preview, int actNumber)
    {
        if (!preview.Acts.TryGetValue(actNumber, out var act))
        {
            return Array.Empty<ShownRelic>();
        }

        var source = actNumber == 2 ? FirstSourceKind.AncientAct2 : FirstSourceKind.AncientAct3;
        return act.Options
            .Select(option => new ShownRelic(option.RelicId, source))
            .ToList();
    }

    private static void ApplyStartingRelicEffects(
        BaselineState state,
        RewardSimulationModel rewardModel,
        string? relicId,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return;
        }

        state.SharedBag.Remove(relicId);
        state.PlayerBag.Remove(relicId);

        switch (relicId.ToUpperInvariant())
        {
            case "LEAFY_POULTICE":
            case "PHIAL_HOLSTER":
                return;
            case "HEFTY_TABLET":
                ReplayUniformCardReward(
                    state,
                    rewardModel,
                    rewardModel.CardPool,
                    count: 3,
                    predicate: metadata => metadata.ParsedRarity == CardRarity.Rare,
                    simulateUpgradeRoll: false);
                return;
            default:
                warnings.Add($"鏈疄鐜拌捣濮嬮仐鐗╃殑鍗冲埢濂栧姳鍥炴斁: {relicId}");
                return;
        }
    }

    private static void ConsumeRegularCombat(BaselineState state, RewardSimulationModel rewardModel)
    {
        _ = state.RewardsRng.NextInt(10, 21);
        if (RollPotionRewardChance(state, isElite: false))
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.RegularEncounter);
        }
    }

    private static void ConsumeEliteCombatPreRelic(BaselineState state, RewardSimulationModel rewardModel)
    {
        _ = state.RewardsRng.NextInt(25, 36);
        if (RollPotionRewardChance(state, isElite: true))
        {
            RollPotionReward(state, rewardModel);
        }

        for (var i = 0; i < 3; i++)
        {
            RollCombatRewardCard(state, rewardModel, CardRarityOddsType.EliteEncounter);
        }
    }

    private static bool RollPotionRewardChance(BaselineState state, bool isElite)
    {
        var current = state.PotionChance;
        var roll = state.RewardsRng.NextFloat();
        if (roll < current)
        {
            state.PotionChance -= 0.1f;
        }
        else
        {
            state.PotionChance += 0.1f;
        }

        return roll < current + (isElite ? 0.125f : 0f);
    }

    private static void RollPotionReward(BaselineState state, RewardSimulationModel rewardModel)
    {
        var rarity = RollPotionRarity(state.RewardsRng);
        if (!rewardModel.PotionPoolByRarity.TryGetValue(rarity, out var pool) || pool.Length == 0)
        {
            pool = rewardModel.PotionPool.ToArray();
        }

        if (pool.Length > 0)
        {
            _ = state.RewardsRng.NextItem(pool);
        }
    }

    private static void RollCombatRewardCard(BaselineState state, RewardSimulationModel rewardModel, CardRarityOddsType oddsType)
    {
        if (rewardModel.CardPool.Count == 0)
        {
            return;
        }

        var rarity = RollCardRarity(state, oddsType);
        var candidates = GetAvailableCards(rewardModel, rarity, state.CurrentRewardCards);
        while (candidates.Count == 0)
        {
            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return;
            }

            candidates = GetAvailableCards(rewardModel, rarity, state.CurrentRewardCards);
        }

        var cardId = state.RewardsRng.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        state.CurrentRewardCards.Add(cardId);
        _ = state.RewardsRng.NextDouble();
        if (state.CurrentRewardCards.Count >= 3)
        {
            state.CurrentRewardCards.Clear();
        }
    }

    private static void ReplayUniformCardReward(
        BaselineState state,
        RewardSimulationModel rewardModel,
        IReadOnlyList<string> pool,
        int count,
        Func<NeowCardMetadata, bool> predicate,
        bool simulateUpgradeRoll)
    {
        var candidates = pool
            .Where(cardId => rewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata) && predicate(metadata))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var available = candidates.Where(cardId => !selected.Contains(cardId)).ToList();
            if (available.Count == 0)
            {
                break;
            }

            var cardId = state.RewardsRng.NextItem(available);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                break;
            }

            selected.Add(cardId);
            if (simulateUpgradeRoll)
            {
                _ = state.RewardsRng.NextDouble();
            }
        }
    }

    private static CardRarity RollCardRarity(BaselineState state, CardRarityOddsType oddsType)
    {
        var roll = state.RewardsRng.NextFloat();
        var rareOdds = GetBaseCardOdds(oddsType, CardRarity.Rare, state.AscensionLevel) + state.CardRareOffset;
        CardRarity rarity;
        if (roll < rareOdds)
        {
            rarity = CardRarity.Rare;
        }
        else if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon, state.AscensionLevel) + rareOdds)
        {
            rarity = CardRarity.Uncommon;
        }
        else
        {
            rarity = CardRarity.Common;
        }

        if (rarity == CardRarity.Rare)
        {
            state.CardRareOffset = -0.05f;
        }
        else
        {
            state.CardRareOffset = Math.Min(
                state.CardRareOffset + (state.AscensionLevel >= 7 ? 0.005f : 0.01f),
                0.4f);
        }

        return rarity;
    }

    private static float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity, int ascensionLevel)
    {
        var scarcityActive = ascensionLevel >= 7;
        return oddsType switch
        {
            CardRarityOddsType.EliteEncounter => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
                CardRarity.Uncommon => 0.4f,
                CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
                _ => 0f
            },
            CardRarityOddsType.BossEncounter => rarity == CardRarity.Rare ? 1f : 0f,
            CardRarityOddsType.Shop => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.585f : 0.54f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.045f : 0.09f,
                _ => 0f
            },
            _ => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
                _ => 0f
            }
        };
    }

    private static List<string> GetAvailableCards(
        RewardSimulationModel rewardModel,
        CardRarity rarity,
        ISet<string> excluded)
    {
        if (!rewardModel.CardPoolByRarity.TryGetValue(rarity, out var pool))
        {
            return [];
        }

        return pool.Where(cardId => !excluded.Contains(cardId)).ToList();
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            _ => CardRarity.None
        };

    private static PotionRarity RollPotionRarity(GameRng rng)
    {
        var roll = rng.NextFloat();
        if (roll <= 0.1f)
        {
            return PotionRarity.Rare;
        }

        if (roll <= 0.35f)
        {
            return PotionRarity.Uncommon;
        }

        return PotionRarity.Common;
    }

    private static BagRarity RollRelicRarity(GameRng rng)
    {
        var value = rng.NextFloat();
        if (value < 0.5f)
        {
            return BagRarity.Common;
        }

        if (value < 0.83f)
        {
            return BagRarity.Uncommon;
        }

        return BagRarity.Rare;
    }

    private static int MatchPicked(string? actualPickedRelic, IReadOnlyCollection<string> predicted)
    {
        return !string.IsNullOrWhiteSpace(actualPickedRelic) &&
               predicted.Contains(actualPickedRelic, StringComparer.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    private static void AdvanceActualTreasure(BaselineState state, string? actualPickedRelic)
    {
        _ = RollRelicRarity(state.TreasureRng);
        RemoveActualRelics(state, string.IsNullOrWhiteSpace(actualPickedRelic) ? [] : [actualPickedRelic]);
    }

    private static void AdvanceActualRegularCombat(
        BaselineState state,
        RewardSimulationModel rewardModel,
        IReadOnlyList<string> actualCardChoiceIds,
        bool hasPotionRewardChoice)
    {
        AdvanceActualCombatRewards(state, rewardModel, actualCardChoiceIds, hasPotionRewardChoice, isElite: false);
    }

    private static void AdvanceActualElite(
        BaselineState state,
        RewardSimulationModel rewardModel,
        string? actualPickedRelic,
        IReadOnlyList<string> actualCardChoiceIds,
        bool hasPotionRewardChoice)
    {
        AdvanceActualCombatRewards(state, rewardModel, actualCardChoiceIds, hasPotionRewardChoice, isElite: true);
        _ = RollRelicRarity(state.RewardsRng);
        RemoveActualRelics(state, string.IsNullOrWhiteSpace(actualPickedRelic) ? [] : [actualPickedRelic]);
    }

    private static void AdvanceActualShop(BaselineState state, IReadOnlyList<string> actualOfferedRelics)
    {
        ConsumeShopPreRelicRewards(state);
        _ = RollRelicRarity(state.RewardsRng);
        _ = RollRelicRarity(state.RewardsRng);
        RemoveActualRelics(state, actualOfferedRelics);
    }

    private static void AdvanceActualAncient(BaselineState state, string? actualPickedRelic)
    {
        RemoveActualRelics(state, string.IsNullOrWhiteSpace(actualPickedRelic) ? [] : [actualPickedRelic]);
    }

    private static void RemoveActualRelics(BaselineState state, IReadOnlyList<string> relicIds)
    {
        foreach (var relicId in relicIds)
        {
            if (string.IsNullOrWhiteSpace(relicId))
            {
                continue;
            }

            state.SharedBag.Remove(relicId);
            state.PlayerBag.Remove(relicId);
        }
    }

    private static void AdvanceActualCombatRewards(
        BaselineState state,
        RewardSimulationModel rewardModel,
        IReadOnlyList<string> actualCardChoiceIds,
        bool hasPotionRewardChoice,
        bool isElite)
    {
        _ = isElite
            ? state.RewardsRng.NextInt(25, 36)
            : state.RewardsRng.NextInt(10, 21);

        var currentPotionChance = state.PotionChance;
        var potionRoll = state.RewardsRng.NextFloat();
        if (potionRoll < currentPotionChance)
        {
            state.PotionChance -= 0.1f;
        }
        else
        {
            state.PotionChance += 0.1f;
        }

        if (hasPotionRewardChoice)
        {
            RollPotionReward(state, rewardModel);
        }

        foreach (var cardId in actualCardChoiceIds.Take(3))
        {
            _ = state.RewardsRng.NextFloat();
            ApplyActualCardRarity(state, rewardModel, cardId);
            _ = state.RewardsRng.NextInt(1);
            _ = state.RewardsRng.NextDouble();
        }
    }

    private static void ApplyActualCardRarity(BaselineState state, RewardSimulationModel rewardModel, string? cardId)
    {
        var rarity = !string.IsNullOrWhiteSpace(cardId) &&
                     rewardModel.CardMetadataMap.TryGetValue(cardId, out var metadata)
            ? metadata.ParsedRarity
            : CardRarity.Common;

        if (rarity == CardRarity.Rare)
        {
            state.CardRareOffset = -0.05f;
        }
        else
        {
            state.CardRareOffset = Math.Min(
                state.CardRareOffset + (state.AscensionLevel >= 7 ? 0.005f : 0.01f),
                0.4f);
        }
    }

    private static int CountOverlap(IReadOnlyList<string> actual, IReadOnlyList<string> predicted)
    {
        var remaining = predicted.ToList();
        var matches = 0;
        foreach (var relicId in actual)
        {
            var index = remaining.FindIndex(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            remaining.RemoveAt(index);
            matches++;
        }

        return matches;
    }
}

static class SaveReplayReportBuilder
{
    public static string Build(
        Options options,
        SaveSnapshot snapshot,
        SaveReplayResult replay,
        LocalizationTable titles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Save Replay Relic Check");
        sb.AppendLine();
        sb.AppendLine($"- Save: `{snapshot.Path}`");
        sb.AppendLine($"- Seed: `{snapshot.SeedText}`");
        sb.AppendLine($"- Character: `{snapshot.Character}`");
        sb.AppendLine($"- Ascension: `{snapshot.AscensionLevel}`");
        sb.AppendLine($"- Start relic: {FormatRelic(snapshot.StartingRelicId, titles)}");
        sb.AppendLine();
        sb.AppendLine("## 姹囨€?");
        sb.AppendLine();
        sb.AppendLine($"- 闈炲晢搴楀疄寰楅仐鐗? `{replay.PickupMatches}/{replay.PickupTotal}` (`{ToPercent(replay.PickupMatches, replay.PickupTotal)}`)");
        sb.AppendLine($"- 鍟嗗簵闄堝垪閬楃墿妲戒綅: `{replay.ShopMatches}/{replay.ShopTotal}` (`{ToPercent(replay.ShopMatches, replay.ShopTotal)}`)");
        sb.AppendLine($"- 缁煎悎鍛戒腑: `{replay.CombinedMatches}/{replay.CombinedTotal}` (`{ToPercent(replay.CombinedMatches, replay.CombinedTotal)}`)");
        sb.AppendLine();

        if (replay.Warnings.Count > 0)
        {
            sb.AppendLine("## 娉ㄦ剰");
            sb.AppendLine();
            foreach (var warning in replay.Warnings)
            {
                sb.AppendLine($"- {warning}");
            }
            sb.AppendLine();
        }

        var mismatches = replay.Nodes.Where(node => node.MatchCount < node.TotalCount).ToList();
        sb.AppendLine("## 宸紓鑺傜偣");
        sb.AppendLine();
        if (mismatches.Count == 0)
        {
            sb.AppendLine("- 鏈彂鐜伴仐鐗╁樊寮傝妭鐐广€?");
            sb.AppendLine();
            return sb.ToString();
        }

        foreach (var node in mismatches)
        {
            sb.AppendLine($"### {node.Node.Label}");
            sb.AppendLine();
            var actual = node.Node.Kind == SaveNodeKind.Shop
                ? node.Node.ActualOfferedRelics
                : string.IsNullOrWhiteSpace(node.Node.ActualPickedRelic) ? [] : [node.Node.ActualPickedRelic];
            sb.AppendLine($"- Actual: {FormatRelicList(actual, titles)}");
            sb.AppendLine($"- Predicted: {FormatRelicList(node.PredictedRelics, titles)}");
            sb.AppendLine($"- Match: `{node.MatchCount}/{node.TotalCount}`");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ToPercent(int numerator, int denominator)
    {
        return denominator <= 0 ? "n/a" : $"{numerator / (double)denominator:P1}";
    }

    private static string FormatRelicList(IReadOnlyList<string> relicIds, LocalizationTable titles)
    {
        return relicIds.Count == 0
            ? "(none)"
            : string.Join(" / ", relicIds.Select(relicId => FormatRelic(relicId, titles)));
    }

    private static string FormatRelic(string? relicId, LocalizationTable titles)
    {
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return "(none)";
        }

        var title = titles.GetRelicTitle(relicId);
        return string.IsNullOrWhiteSpace(title)
            ? $"`{relicId}`"
            : $"{title} (`{relicId}`)";
    }
}

sealed class BaselineState
{
    private BaselineState(
        string seedText,
        CharacterId character,
        int playerCount,
        int ascensionLevel,
        uint runSeed,
        uint playerSeed,
        RelicBag sharedBag,
        RelicBag playerBag,
        GameRng treasureRng,
        GameRng rewardsRng,
        float cardRareOffset,
        float potionChance,
        HashSet<string> currentRewardCards)
    {
        SeedText = seedText;
        Character = character;
        PlayerCount = playerCount;
        AscensionLevel = ascensionLevel;
        RunSeed = runSeed;
        PlayerSeed = playerSeed;
        SharedBag = sharedBag;
        PlayerBag = playerBag;
        TreasureRng = treasureRng;
        RewardsRng = rewardsRng;
        CardRareOffset = cardRareOffset;
        PotionChance = potionChance;
        CurrentRewardCards = currentRewardCards;
    }

    public string SeedText { get; }

    public CharacterId Character { get; }

    public int PlayerCount { get; }

    public int AscensionLevel { get; }

    public uint RunSeed { get; }

    public uint PlayerSeed { get; }

    public RelicBag SharedBag { get; }

    public RelicBag PlayerBag { get; }

    public GameRng TreasureRng { get; }

    public GameRng RewardsRng { get; }

    public float CardRareOffset { get; set; }

    public float PotionChance { get; set; }

    public HashSet<string> CurrentRewardCards { get; }

    public BaselineState Clone()
    {
        return new BaselineState(
            SeedText,
            Character,
            PlayerCount,
            AscensionLevel,
            RunSeed,
            PlayerSeed,
            SharedBag.Clone(),
            PlayerBag.Clone(),
            new GameRng(TreasureRng.Seed, TreasureRng.Counter),
            new GameRng(RewardsRng.Seed, RewardsRng.Counter),
            CardRareOffset,
            PotionChance,
            new HashSet<string>(CurrentRewardCards, StringComparer.OrdinalIgnoreCase));
    }

    public static BaselineState Create(
        string seedText,
        CharacterId character,
        int playerCount,
        int ascensionLevel,
        RelicBag sharedBag,
        RelicBag playerBag)
    {
        var runSeed = SeedFormatter.ToUIntSeed(seedText);
        var playerSeed = unchecked(runSeed + 1u);
        return new BaselineState(
            seedText,
            character,
            playerCount,
            ascensionLevel,
            runSeed,
            playerSeed,
            sharedBag,
            playerBag,
            new GameRng(runSeed, "treasure_room_relics"),
            new GameRng(playerSeed, "rewards"),
            -0.05f,
            0.4f,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}

static class OfficialBagInitializer
{
    public static BaselineState Create(string seedText, CharacterId character, int ascensionLevel)
    {
        MegaCrit.Sts2.Core.Models.ModelDb.Init();

        var runSeed = SeedFormatter.ToUIntSeed(seedText);
        var upFront = new MegaCrit.Sts2.Core.Random.Rng(runSeed, "up_front");
        var unlock = MegaCrit.Sts2.Core.Unlocks.UnlockState.all;

        var sharedPool = MegaCrit.Sts2.Core.Models.ModelDb
            .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool>()
            .GetUnlockedRelics(unlock)
            .ToList();
        var characterPool = GetCharacterRelics(character, unlock).ToList();

        var sharedGrabBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag(refreshAllowed: true);
        sharedGrabBag.Populate(sharedPool, upFront);

        var playerGrabBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag(refreshAllowed: true);
        playerGrabBag.Populate(sharedPool.Concat(characterPool), upFront);

        return BaselineState.Create(
            seedText,
            character,
            playerCount: 1,
            ascensionLevel,
            Convert(sharedGrabBag.ToSerializable()),
            Convert(playerGrabBag.ToSerializable()));
    }

    private static IEnumerable<MegaCrit.Sts2.Core.Models.RelicModel> GetCharacterRelics(
        CharacterId character,
        MegaCrit.Sts2.Core.Unlocks.UnlockState unlock)
    {
        return character switch
        {
            CharacterId.Ironclad => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.IroncladRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Silent => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SilentRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Defect => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.DefectRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Necrobinder => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Regent => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.RegentRelicPool>()
                .GetUnlockedRelics(unlock),
            _ => Array.Empty<MegaCrit.Sts2.Core.Models.RelicModel>()
        };
    }

    private static RelicBag Convert(MegaCrit.Sts2.Core.Saves.Runs.SerializableRelicGrabBag bag)
    {
        var buckets = new Dictionary<BagRarity, List<string>>();
        foreach (var (officialRarity, ids) in bag.RelicIdLists)
        {
            if (!Enum.TryParse<BagRarity>(officialRarity.ToString(), ignoreCase: true, out var rarity))
            {
                continue;
            }

            buckets[rarity] = ids
                .Select(id => id.Entry)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        return new RelicBag(buckets);
    }
}

sealed class RelicBag
{
    private readonly Dictionary<BagRarity, List<string>> _deques;

    public RelicBag(Dictionary<BagRarity, List<string>> deques)
    {
        _deques = deques;
    }

    public int TotalCount => _deques.Values.Sum(list => list.Count);

    public RelicBag Clone()
    {
        var clone = _deques.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToList());
        return new RelicBag(clone);
    }

    public string? PullFromFront(BagRarity rarity, Func<string, bool>? isAllowed = null)
    {
        if (isAllowed != null)
        {
            RemoveDisallowed(isAllowed);
        }

        BagRarity? current = rarity;
        while (current is BagRarity currentRarity)
        {
            if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
            {
                var relic = list[0];
                list.RemoveAt(0);
                return relic;
            }

            current = current switch
            {
                BagRarity.Shop => BagRarity.Common,
                BagRarity.Common => BagRarity.Uncommon,
                BagRarity.Uncommon => BagRarity.Rare,
                _ => null
            };
        }

        return null;
    }

    public string? PullFromBack(
        BagRarity rarity,
        IReadOnlySet<string>? selected = null,
        IReadOnlySet<string>? extraBlacklist = null,
        Func<string, bool>? isAllowed = null)
    {
        if (isAllowed != null)
        {
            RemoveDisallowed(isAllowed);
        }

        BagRarity? current = rarity;
        while (current is BagRarity currentRarity)
        {
            if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (selected != null && selected.Contains(list[i]))
                    {
                        continue;
                    }

                    if (extraBlacklist != null && extraBlacklist.Contains(list[i]))
                    {
                        continue;
                    }

                    var relic = list[i];
                    list.RemoveAt(i);
                    return relic;
                }
            }

            current = current switch
            {
                BagRarity.Shop => BagRarity.Common,
                BagRarity.Common => BagRarity.Uncommon,
                BagRarity.Uncommon => BagRarity.Rare,
                _ => null
            };
        }

        return null;
    }

    public void Remove(string relicId)
    {
        foreach (var list in _deques.Values)
        {
            list.RemoveAll(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RemoveDisallowed(Func<string, bool> isAllowed)
    {
        foreach (var list in _deques.Values)
        {
            list.RemoveAll(item => !isAllowed(item));
        }
    }
}

sealed class RelicRules
{
    private RelicRules(
        IReadOnlySet<string> beforeAct3TreasureChestRelics,
        IReadOnlySet<string> singlePlayerOnlyRelics,
        IReadOnlySet<string> multiplayerOnlyRelics)
    {
        BeforeAct3TreasureChestRelics = beforeAct3TreasureChestRelics;
        SinglePlayerOnlyRelics = singlePlayerOnlyRelics;
        MultiplayerOnlyRelics = multiplayerOnlyRelics;
    }

    public IReadOnlySet<string> BeforeAct3TreasureChestRelics { get; }

    public IReadOnlySet<string> SinglePlayerOnlyRelics { get; }

    public IReadOnlySet<string> MultiplayerOnlyRelics { get; }

    public bool IsAllowed(string relicId, int actNumber, int playerCount)
    {
        if (BeforeAct3TreasureChestRelics.Contains(relicId) && actNumber >= 3)
        {
            return false;
        }

        if (playerCount == 1 && MultiplayerOnlyRelics.Contains(relicId))
        {
            return false;
        }

        if (playerCount > 1 && SinglePlayerOnlyRelics.Contains(relicId))
        {
            return false;
        }

        return true;
    }

    public static RelicRules Load(string sourceRootPath)
    {
        var relicDir = Path.Combine(sourceRootPath, "src", "Core", "Models", "Relics");
        var beforeAct3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var singlePlayerOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var multiplayerOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(relicDir))
        {
            return new RelicRules(beforeAct3, singlePlayerOnly, multiplayerOnly);
        }

        foreach (var path in Directory.EnumerateFiles(relicDir, "*.cs"))
        {
            var text = File.ReadAllText(path);
            var relicId = NormalizeSourceRelicId(Path.GetFileNameWithoutExtension(path));
            if (text.Contains("IsBeforeAct3TreasureChest(runState)", StringComparison.Ordinal))
            {
                beforeAct3.Add(relicId);
            }

            if (text.Contains("runState.Players.Count == 1", StringComparison.Ordinal))
            {
                singlePlayerOnly.Add(relicId);
            }

            if (text.Contains("runState.Players.Count > 1", StringComparison.Ordinal))
            {
                multiplayerOnly.Add(relicId);
            }
        }

        return new RelicRules(beforeAct3, singlePlayerOnly, multiplayerOnly);
    }

    private static string NormalizeSourceRelicId(string typeName)
    {
        var snake = System.Text.RegularExpressions.Regex.Replace(typeName, "([A-Z]+)([A-Z][a-z])", "$1_$2");
        snake = System.Text.RegularExpressions.Regex.Replace(snake, "([a-z0-9])([A-Z])", "$1_$2");
        return snake.ToUpperInvariant();
    }
}

sealed class LocalizationTable
{
    private LocalizationTable(IReadOnlyDictionary<string, string> relicTitles)
    {
        _relicTitles = relicTitles;
    }

    private readonly IReadOnlyDictionary<string, string> _relicTitles;

    public string? GetRelicTitle(string relicId)
    {
        return _relicTitles.TryGetValue(relicId, out var title)
            ? title
            : null;
    }

    public static LocalizationTable Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LocalizationTable(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relicId = property.Name[..^".title".Length];
            titles[relicId] = property.Value.GetString() ?? relicId;
        }

        return new LocalizationTable(titles);
    }
}
