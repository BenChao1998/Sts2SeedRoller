using SeedModel.Sts2.RunValidation;
using System.IO;

namespace SeedUi.ViewModels;

internal sealed class Sts2RunValidationResultViewModel
{
    public Sts2RunValidationResultViewModel(Sts2RunValidationResult result)
    {
        FilePath = result.FilePath;
        FileName = Path.GetFileName(result.FilePath);
        RunId = result.RunId.ToString();
        GameVersion = string.IsNullOrWhiteSpace(result.GameVersion) ? "未知" : result.GameVersion;
        ValidationDataVersion = string.IsNullOrWhiteSpace(result.ValidationDataVersion) ? "未知" : result.ValidationDataVersion;
        ValidationScope = result.ValidationScope == Sts2RunValidationScope.OpeningOptionsOnly
            ? "仅开始选项"
            : "完整验证";
        SeedText = result.SeedText;
        CharacterId = result.CharacterId;
        Ascension = result.Ascension.ToString();
        PlayerCount = result.PlayerCount.ToString();
        Floors = result.Floors.ToString();
        FinalRelicMatchText = result.ValidationScope == Sts2RunValidationScope.OpeningOptionsOnly
            ? "-"
            : result.FinalRelicMatch ? "匹配" : "不匹配";
        GeneratedSummary = result.SummaryText;
        GeneratedComparisons = result.GeneratedComparisons.ToString();
        GeneratedMatches = result.GeneratedMatches.ToString();
        GeneratedMismatches = result.GeneratedMismatches.ToString();
        CardMatches = result.CardMatches.ToString();
        RelicMatches = result.RelicMatches.ToString();
        ShopRelicMatches = result.ShopRelicMatches.ToString();
        ShopPotionMatches = result.ShopPotionMatches.ToString();
        FirstMismatchFloor = result.FirstMismatchFloor?.ToString() ?? "无";
        MarkdownReport = result.MarkdownReport;
        Rows = result.FloorsResults
            .Select(row => new Sts2RunValidationFloorRowViewModel(row))
            .ToArray();
        MismatchRows = Rows
            .Where(row => row.IsMismatch)
            .ToArray();
        VisibleRows = MismatchRows.Count > 0 ? MismatchRows : Rows;
        DetailTitle = MismatchRows.Count > 0 ? "不匹配明细" : "全部可比项目";
    }

    public string FilePath { get; }

    public string FileName { get; }

    public string RunId { get; }

    public string GameVersion { get; }

    public string ValidationDataVersion { get; }

    public string ValidationScope { get; }

    public string SeedText { get; }

    public string CharacterId { get; }

    public string Ascension { get; }

    public string PlayerCount { get; }

    public string Floors { get; }

    public string FinalRelicMatchText { get; }

    public string GeneratedSummary { get; }

    public string GeneratedComparisons { get; }

    public string GeneratedMatches { get; }

    public string GeneratedMismatches { get; }

    public string CardMatches { get; }

    public string RelicMatches { get; }

    public string ShopRelicMatches { get; }

    public string ShopPotionMatches { get; }

    public string FirstMismatchFloor { get; }

    public string MarkdownReport { get; }

    public IReadOnlyList<Sts2RunValidationFloorRowViewModel> Rows { get; }

    public IReadOnlyList<Sts2RunValidationFloorRowViewModel> MismatchRows { get; }

    public IReadOnlyList<Sts2RunValidationFloorRowViewModel> VisibleRows { get; }

    public string DetailTitle { get; }
}

internal sealed class Sts2RunValidationFloorRowViewModel
{
    public Sts2RunValidationFloorRowViewModel(Sts2RunValidationFloorResult result)
    {
        Floor = result.Floor;
        RoomType = result.RoomType;
        Category = result.Category;
        MatchText = result.MatchText;
        Expected = string.IsNullOrWhiteSpace(result.Expected) ? "-" : result.Expected;
        Generated = string.IsNullOrWhiteSpace(result.Generated) ? "-" : result.Generated;
        Notes = string.IsNullOrWhiteSpace(result.Notes) ? "-" : result.Notes;
        IsMismatch = result.IsMatch == false;
    }

    public int Floor { get; }

    public string RoomType { get; }

    public string Category { get; }

    public string MatchText { get; }

    public string Expected { get; }

    public string Generated { get; }

    public string Notes { get; }

    public bool IsMismatch { get; }
}
