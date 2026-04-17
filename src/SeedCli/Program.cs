using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SeedCli;
using SeedModel.Events;
using SeedModel.Neow;
using SeedModel.Run;
using SeedModel.Sts2;
using SeedModel.Seeds;

var arguments = CliArguments.Parse(args);

var eventValue = arguments.Get("--event");
if (!SeedEventParser.TryParse(eventValue, out var selectedEvent))
{
    Console.Error.WriteLine($"Unknown event id: {eventValue}");
    return 1;
}

var eventMetadata = SeedEventRegistry.Get(selectedEvent);
if (!eventMetadata.IsImplemented)
{
    Console.Error.WriteLine($"Event \"{eventMetadata.DisplayName}\" is not implemented yet.");
    return 1;
}

var dataPath = ResolveDataPath(arguments.Get("--data"), eventMetadata.DefaultDataPath);
var seedValue = arguments.Get("--seed") ?? "0";
var playerValue = arguments.Get("--players") ?? "1";
var countValue = arguments.Get("--count") ?? "1";
var stepValue = arguments.Get("--seed-step") ?? "1";
var characterValue = arguments.Get("--character") ?? "ironclad";
var relicIdsValue = arguments.Get("--filter-relic");
var relicTermsValue = arguments.Get("--filter-relic-term");
var cardIdsValue = arguments.Get("--filter-card");
var potionIdsValue = arguments.Get("--filter-potion");
var includeAct2 = ParseBool(arguments.Get("--include-act2"));
var includeAct3 = ParseBool(arguments.Get("--include-act3"));
if (ParseBool(arguments.Get("--include-all-acts")))
{
    includeAct2 = true;
    includeAct3 = true;
}

if (!File.Exists(dataPath))
{
    Console.Error.WriteLine($"Data file not found at {dataPath}. Run the extractor first.");
    return 1;
}

var ancientDataPath = ResolveAncientDataPath(arguments.Get("--ancient-data"));
var actDataPath = ResolveActDataPath(arguments.Get("--sts2-act-data"));

if (!TryParseSeed(seedValue, out var seed, out var normalizedSeedText, out var canIncrementSeedText))
{
    Console.Error.WriteLine($"Invalid seed value: {seedValue}");
    return 1;
}

if (!int.TryParse(playerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerCount) || playerCount <= 0)
{
    Console.Error.WriteLine($"Invalid player count: {playerValue}");
    return 1;
}

if (!int.TryParse(countValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rollCount) || rollCount <= 0)
{
    Console.Error.WriteLine($"Invalid roll count: {countValue}");
    return 1;
}

if (!uint.TryParse(stepValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seedStep))
{
    Console.Error.WriteLine($"Invalid seed step: {stepValue}");
    return 1;
}

if (!CharacterIdExtensions.TryParse(characterValue, out var character))
{
    Console.Error.WriteLine($"Unknown character id: {characterValue}");
    return 1;
}

var relicIds = ParseList(relicIdsValue);
var relicTerms = ParseList(relicTermsValue);
var cardIds = ParseList(cardIdsValue);
var potionIds = ParseList(potionIdsValue);
var filter = NeowOptionFilter.Create(null, relicTerms, relicIds, cardIds, potionIds);
var runFilter = new SeedRunFilter
{
    NeowFilter = filter
};

try
{
    if (selectedEvent != SeedEventType.Act1Neow)
    {
        Console.Error.WriteLine($"Event {selectedEvent} is not supported by the CLI yet.");
        return 1;
    }

    var generatorFactory = new NeowEventGeneratorFactory();
    var dataset = generatorFactory.LoadDataset(dataPath);
    Sts2RunPreviewer? ancientPreviewer = null;
    if (includeAct2 || includeAct3)
    {
        try
        {
            ancientPreviewer = Sts2RunPreviewer.CreateFromDataFiles(ancientDataPath, actDataPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to load Act 2/3 data: {ex.Message}");
            ancientPreviewer = null;
        }
    }

    var evaluator = new SeedRunEvaluator(dataset, ancientPreviewer);
    var seedsScanned = 0;
    var seedsDisplayed = 0;
    var neowOptionsDisplayed = 0;
    for (var i = 0; i < rollCount; i++)
    {
        seedsScanned++;
        var offset = (ulong)i * seedStep;
        var currentSeed = unchecked((uint)((ulong)seed + offset));
        var seedText = canIncrementSeedText
            ? currentSeed.ToString(CultureInfo.InvariantCulture)
            : normalizedSeedText;
        var runContext = new SeedRunEvaluationContext
        {
            RunSeed = currentSeed,
            SeedText = seedText,
            Character = character,
            PlayerCount = playerCount,
            ScrollBoxesEligible = true,
            AscensionLevel = 0,
            IncludeAct2 = includeAct2,
            IncludeAct3 = includeAct3
        };

        var runMatch = evaluator.Evaluate(runContext, runFilter);
        if (!runMatch.IsFinalMatch)
        {
            continue;
        }

        var displayOptions = filter.HasCriteria ? runMatch.NeowMatches : runMatch.NeowOptions;

        seedsDisplayed++;
        neowOptionsDisplayed += displayOptions.Count;

        Console.WriteLine($"=== Roll {seedsDisplayed}/{rollCount} ===");
        Console.WriteLine($"Seed: {seedText} (0x{currentSeed:X8})  Character: {character}  Players: {playerCount}");
        Console.WriteLine(new string('-', 80));
        foreach (var option in displayOptions)
        {
            Console.WriteLine($"[{option.Pool}] {option.Title} ({option.RelicId})");
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                Console.WriteLine($"    {option.Description}");
            }
            if (!string.IsNullOrWhiteSpace(option.Note))
            {
                Console.WriteLine($"    Note: {option.Note}");
            }

            if (option.Details.Count > 0)
            {
                foreach (var detail in option.Details)
                {
                    var modelFragment = string.IsNullOrWhiteSpace(detail.ModelId) ? string.Empty : $" [{detail.ModelId}]";
                    Console.WriteLine($"    - {detail.Label}: {detail.Value}{modelFragment}");
                }
            }

            Console.WriteLine();
        }

        if (runMatch.Sts2Preview?.Acts.Count > 0)
        {
            foreach (var act in runMatch.Sts2Preview.Acts)
            {
                Console.WriteLine($"    [Act {act.ActNumber}] {act.AncientName} ({act.AncientId})");
                foreach (var option in act.AncientOptions)
                {
                    var optionTitle = option.Title ?? option.OptionId;
                    Console.WriteLine($"        - {optionTitle} ({option.OptionId})");
                    if (!string.IsNullOrWhiteSpace(option.Description))
                    {
                        Console.WriteLine($"            {option.Description}");
                    }

                    if (!string.IsNullOrWhiteSpace(option.Note))
                    {
                        Console.WriteLine($"            Note: {option.Note}");
                    }
                }

                Console.WriteLine();
            }
        }

        if (i + 1 < rollCount)
        {
            Console.WriteLine();
        }
    }

    Console.WriteLine($"Scanned seeds: {seedsScanned}, Displayed seeds: {seedsDisplayed}, Act1 options: {neowOptionsDisplayed}");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to generate Neow options: {ex.Message}");
    return 1;
}

static bool TryParseSeed(string seedText, out uint seed, out string normalizedSeedText, out bool isNumericInput)
{
    normalizedSeedText = string.Empty;
    isNumericInput = false;
    if (seedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        if (uint.TryParse(seedText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed))
        {
            normalizedSeedText = seed.ToString(CultureInfo.InvariantCulture);
            isNumericInput = true;
            return true;
        }
        return false;
    }

    if (uint.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
    {
        normalizedSeedText = seed.ToString(CultureInfo.InvariantCulture);
        isNumericInput = true;
        return true;
    }

    if (SeedFormatter.TryNormalize(seedText, out var normalized, out _))
    {
        normalizedSeedText = normalized;
        seed = SeedFormatter.ToUIntSeed(normalized);
        return true;
    }

    seed = 0;
    return false;
}

static IReadOnlyList<string> ParseList(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return Array.Empty<string>();
    }

    return value
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(v => v.Trim())
        .Where(v => v.Length > 0)
        .ToList();
}

static string ResolveDataPath(string? overrideValue, string fallback)
{
    var raw = string.IsNullOrWhiteSpace(overrideValue) ? fallback : overrideValue;
    if (string.IsNullOrWhiteSpace(raw))
    {
        return string.Empty;
    }

    var normalized = raw.Replace('/', Path.DirectorySeparatorChar);
    return Path.IsPathRooted(normalized)
        ? normalized
        : Path.Combine(AppContext.BaseDirectory, normalized);
}

static string ResolveAncientDataPath(string? overrideValue)
{
    var baseDirectory = Path.Combine(AppContext.BaseDirectory, "data", "0.99.1", "ancients");
    var localized = Path.Combine(baseDirectory, "options.zhs.json");
    var defaultPath = File.Exists(localized)
        ? localized
        : Path.Combine(baseDirectory, "options.json");
    return ResolveDataPath(overrideValue, defaultPath);
}

static string ResolveActDataPath(string? overrideValue)
{
    var defaultPath = Path.Combine(AppContext.BaseDirectory, "data", "0.99.1", "sts2", "acts.json");
    return ResolveDataPath(overrideValue, defaultPath);
}

static bool ParseBool(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("y", StringComparison.OrdinalIgnoreCase);
}
