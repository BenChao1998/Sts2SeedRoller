using System.Globalization;
using System.Text.Json;

namespace SeedModel.Sts2.RunValidation;

public sealed class Sts2RunValidationResult
{
    public required string FilePath { get; init; }

    public required int RunId { get; init; }

    public required string GameVersion { get; init; }

    public required string ValidationDataVersion { get; init; }

    public required Sts2RunValidationScope ValidationScope { get; init; }

    public required string SeedText { get; init; }

    public required string CharacterId { get; init; }

    public required int Ascension { get; init; }

    public required int PlayerCount { get; init; }

    public required int Floors { get; init; }

    public required bool FinalRelicMatch { get; init; }

    public required int GeneratedComparisons { get; init; }

    public required int GeneratedMatches { get; init; }

    public required int GeneratedMismatches { get; init; }

    public required int CardMatches { get; init; }

    public required int RelicMatches { get; init; }

    public required int ShopRelicMatches { get; init; }

    public required int ShopPotionMatches { get; init; }

    public required int? FirstMismatchFloor { get; init; }

    public required string MarkdownReport { get; init; }

    public required IReadOnlyList<Sts2RunValidationFloorResult> FloorsResults { get; init; }

    public double MatchRate => GeneratedComparisons <= 0 ? 0 : (double)GeneratedMatches / GeneratedComparisons;

    public string MatchRateText => GeneratedComparisons <= 0
        ? "0.0%"
        : MatchRate.ToString("P1", CultureInfo.InvariantCulture);

    public string SummaryText => $"{GeneratedMatches}/{GeneratedComparisons} ({MatchRateText})";
}

public sealed class Sts2RunValidationOptions
{
    public string? DataVersionOverride { get; init; }

    public Sts2RunValidationScope Scope { get; init; } = Sts2RunValidationScope.FullRun;
}

public enum Sts2RunValidationScope
{
    FullRun,
    OpeningOptionsOnly
}

public static class Sts2RunValidationCategories
{
    public const string OpeningOption = "开始选项";
}

public sealed class Sts2RunValidationFloorResult
{
    public required int Floor { get; init; }

    public required string RoomType { get; init; }

    public required string Category { get; init; }

    public required bool? IsMatch { get; init; }

    public required string Expected { get; init; }

    public required string Generated { get; init; }

    public required string Notes { get; init; }

    public string MatchText => IsMatch.HasValue ? (IsMatch.Value ? "匹配" : "不匹配") : "仅记录";
}

public static class Sts2RunValidationService
{
    public static Sts2RunValidationResult ValidateFile(string runFilePath, string? workspaceRoot = null)
        => ValidateFile(runFilePath, workspaceRoot, options: null);

    public static Sts2RunValidationResult ValidateFile(
        string runFilePath,
        string? workspaceRoot,
        Sts2RunValidationOptions? options)
    {
        if (string.IsNullOrWhiteSpace(runFilePath))
        {
            throw new ArgumentException("Run file path is required.", nameof(runFilePath));
        }

        if (!File.Exists(runFilePath))
        {
            throw new FileNotFoundException("Run file not found.", runFilePath);
        }

        return InWorkspace(workspaceRoot, () =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runFilePath));
            var replay = Sts2LogReplayParser.Parse(document.RootElement);
            var validationOptions = options ?? new Sts2RunValidationOptions();
            var validationDataVersion = ResolveValidationDataVersion(replay.GameVersion, validationOptions.DataVersionOverride);
            var validationReplay = replay with { GameVersion = validationDataVersion };
            var replayResult = validationOptions.Scope == Sts2RunValidationScope.OpeningOptionsOnly
                ? new ReplayResult(Array.Empty<string>(), Array.Empty<ReplayTraceEntry>(), Array.Empty<string>())
                : LogDrivenReplay.Run(validationReplay);
            var comparisons = validationOptions.Scope == Sts2RunValidationScope.OpeningOptionsOnly
                ? OpeningOptionComparer.Compare(validationReplay)
                : GeneratedRunComparer.Compare(
                    validationReplay,
                    preCombatReplayMode: PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots);
            var markdown = ReplayReportFormatter.Format(validationReplay, replayResult, comparisons);
            var comparable = comparisons
                .Where(item => item.IsMatch.HasValue)
                .ToArray();
            var firstMismatch = comparable.FirstOrDefault(item => item.IsMatch == false);

            return new Sts2RunValidationResult
            {
                FilePath = runFilePath,
                RunId = replay.RunId,
                GameVersion = replay.GameVersion,
                ValidationDataVersion = validationDataVersion,
                ValidationScope = validationOptions.Scope,
                SeedText = replay.SeedText,
                CharacterId = replay.CharacterId,
                Ascension = replay.Ascension,
                PlayerCount = replay.PlayerCount,
                Floors = replay.Floors.Count,
                FinalRelicMatch = replayResult.Mismatches.Count == 0,
                GeneratedComparisons = comparable.Length,
                GeneratedMatches = comparable.Count(item => item.IsMatch == true),
                GeneratedMismatches = comparable.Count(item => item.IsMatch == false),
                CardMatches = comparisons.Count(item => item.CardMatch == true),
                RelicMatches = comparisons.Count(item => item.RelicMatch == true),
                ShopRelicMatches = comparisons.Count(item => item.ShopRelicMatch == true),
                ShopPotionMatches = comparisons.Count(item => item.ShopPotionMatch == true),
                FirstMismatchFloor = firstMismatch?.Floor,
                MarkdownReport = markdown,
                FloorsResults = comparisons.SelectMany(ToFloorResults).ToArray()
            };
        });
    }

    private static string ResolveValidationDataVersion(string? replayGameVersion, string? dataVersionOverride)
    {
        var requested = string.IsNullOrWhiteSpace(dataVersionOverride)
            ? ExactRewardStateBridge.ResolveRunValidationDataVersion(replayGameVersion)
            : dataVersionOverride.Trim();

        var neowDataPath = Path.Combine("data", requested, "neow", "options.json");
        if (!File.Exists(neowDataPath))
        {
            throw new FileNotFoundException($"Run validation data version '{requested}' is not available. Expected file: {neowDataPath}", neowDataPath);
        }

        return requested;
    }

    private static T InWorkspace<T>(string? workspaceRoot, Func<T> action)
    {
        var oldCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            if (!string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
            {
                Environment.CurrentDirectory = workspaceRoot;
            }

            return action();
        }
        finally
        {
            Environment.CurrentDirectory = oldCurrentDirectory;
        }
    }

    private static IEnumerable<Sts2RunValidationFloorResult> ToFloorResults(GeneratedFloorComparison comparison)
    {
        if (comparison.Notes?.Contains("开始选项", StringComparison.OrdinalIgnoreCase) == true)
        {
            yield return CreateFloorResult(
                comparison,
                Sts2RunValidationCategories.OpeningOption,
                comparison.RelicMatch,
                comparison.ExpectedRelics,
                comparison.GeneratedRelics);
            yield break;
        }

        if (comparison.CardMatch.HasValue)
        {
            yield return CreateFloorResult(
                comparison,
                "卡牌",
                comparison.CardMatch,
                comparison.ExpectedCards,
                comparison.GeneratedCards);
        }

        if (comparison.RelicMatch.HasValue)
        {
            yield return CreateFloorResult(
                comparison,
                "遗物",
                comparison.RelicMatch,
                comparison.ExpectedRelics,
                comparison.GeneratedRelics);
        }

        if (comparison.ShopRelicMatch.HasValue)
        {
            yield return CreateFloorResult(
                comparison,
                "商店遗物",
                comparison.ShopRelicMatch,
                comparison.ExpectedShopRelics,
                comparison.GeneratedShopRelics);
        }

        if (comparison.ShopPotionMatch.HasValue)
        {
            yield return CreateFloorResult(
                comparison,
                "商店药水",
                comparison.ShopPotionMatch,
                comparison.ExpectedShopPotions,
                comparison.GeneratedShopPotions);
        }

        if (!comparison.CardMatch.HasValue &&
            !comparison.RelicMatch.HasValue &&
            !comparison.ShopRelicMatch.HasValue &&
            !comparison.ShopPotionMatch.HasValue)
        {
            yield return CreateFloorResult(
                comparison,
                "记录",
                null,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }

    private static Sts2RunValidationFloorResult CreateFloorResult(
        GeneratedFloorComparison comparison,
        string category,
        bool? match,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> generated)
    {
        return new Sts2RunValidationFloorResult
        {
            Floor = comparison.Floor,
            RoomType = comparison.RoomType,
            Category = category,
            IsMatch = match,
            Expected = Join(expected),
            Generated = Join(generated),
            Notes = comparison.Notes ?? string.Empty
        };
    }

    private static string Join(IEnumerable<string> values)
    {
        var materialized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return materialized.Length == 0 ? "" : string.Join(", ", materialized);
    }
}
