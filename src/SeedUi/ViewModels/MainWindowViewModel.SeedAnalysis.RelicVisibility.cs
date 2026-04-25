using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private string _seedAnalysisRelicVisibilitySummary = "遗物概率预览会随种子分析一并生成。";

    public ObservableCollection<SeedAnalysisRelicVisibilityProfileViewModel> SeedAnalysisRelicVisibilityProfiles { get; } = new();

    public string SeedAnalysisRelicVisibilitySummary
    {
        get => _seedAnalysisRelicVisibilitySummary;
        private set => SetProperty(ref _seedAnalysisRelicVisibilitySummary, value);
    }

    private void ApplySeedAnalysisRelicVisibility(Sts2RelicVisibilityAnalysis analysis)
    {
        SeedAnalysisRelicVisibilityProfiles.Clear();

        foreach (var profile in analysis.Profiles)
        {
            var actSummaries = profile.Acts
                .Select(act =>
                    $"第 {act.ActNumber} 幕：宝箱 {FormatChanceList(act.TreasureCounts)} / 精英 {FormatChanceList(act.EliteCounts)} / 商店 {FormatChanceList(act.ShopCounts)} / 古神 {act.AncientVisitChance:P0}")
                .ToList();

            var earlyRelics = profile.EarlyRelics
                .Take(12)
                .Select((item, index) => new SeedAnalysisRelicVisibilityItemViewModel(
                    index + 1,
                    FormatRelicVisibilityRelicLine(item)))
                .ToList();

            var seenRelics = profile.SeenRelics
                .Take(12)
                .Select((item, index) => new SeedAnalysisRelicVisibilityItemViewModel(
                    index + 1,
                    FormatRelicVisibilityRelicLine(item)))
                .ToList();

            var samples = profile.EarlySamples
                .Select((sample, index) => $"样本 {index + 1}：{FormatRelicSample(sample)}")
                .ToList();

            SeedAnalysisRelicVisibilityProfiles.Add(new SeedAnalysisRelicVisibilityProfileViewModel(
                TranslateRelicVisibilityProfileTitle(profile.Title),
                TranslateRelicVisibilityProfileDescription(profile.Description),
                actSummaries,
                earlyRelics,
                seenRelics,
                samples));
        }

        SeedAnalysisRelicVisibilitySummary =
            $"当前每条路线画像使用 {analysis.Samples} 次采样；前期窗口为前 {analysis.EarlyWindow} 次遗物机会。";
    }

    private void ClearSeedAnalysisRelicVisibility()
    {
        SeedAnalysisRelicVisibilityProfiles.Clear();
        SeedAnalysisRelicVisibilitySummary = "遗物概率预览会随种子分析一并生成。";
    }

    private static string FormatChanceList(IReadOnlyList<Sts2WeightedIntChance> entries)
    {
        return string.Join(", ", entries.Select(entry => $"{entry.Value} ({entry.Weight:P0})"));
    }

    private static string FormatRelicSample(IReadOnlyList<string> relicIds)
    {
        return relicIds.Count == 0
            ? "前期窗口内未出现遗物"
            : string.Join(" / ", relicIds.Select(GetRelicDisplayName));
    }

    private static string FormatRelicVisibilityRelicLine(Sts2RelicVisibilityRankedRelic item)
    {
        return $"{GetRelicDisplayName(item.RelicId)} | 出现 {item.SeenProbability:P1} | 非商店 {item.NonShopSeenProbability:P1} | 商店 {item.ShopSeenProbability:P1} | 前期 {item.EarlyProbability:P1} | 平均首次机会 {item.AverageFirstOpportunity:F2} | 最常来源 {FormatRelicVisibilitySource(item.MostCommonSource)}";
    }

    private static string FormatRelicVisibilitySource(Sts2RelicVisibilitySource source)
    {
        return source switch
        {
            Sts2RelicVisibilitySource.Treasure => "宝箱",
            Sts2RelicVisibilitySource.Elite => "精英",
            Sts2RelicVisibilitySource.Shop => "商店",
            Sts2RelicVisibilitySource.AncientAct2 => "第二幕古神",
            Sts2RelicVisibilitySource.AncientAct3 => "第三幕古神",
            _ => source.ToString()
        };
    }

    private static string TranslateRelicVisibilityProfileTitle(string title)
    {
        return title switch
        {
            "Balanced" => "均衡",
            "Aggressive" => "激进",
            "Shopper" => "商店优先",
            _ => title
        };
    }

    private static string TranslateRelicVisibilityProfileDescription(string description)
    {
        return description switch
        {
            "Balanced route: a moderate number of elites, a few shops, and a medium chance to visit ancients."
                => "均衡路线：精英数量适中，商店较少，遇到古神的概率中等。",
            "Aggressive route: earlier elites, fewer shops, and a slightly lower ancient chance."
                => "激进路线：更早打精英，商店更少，遇到古神的概率略低。",
            "Shop-heavy route: more shop visibility, fewer elites, and a slightly higher ancient chance."
                => "商店优先路线：更容易经过商店，精英更少，遇到古神的概率略高。",
            _ => description
        };
    }

    internal sealed class SeedAnalysisRelicVisibilityProfileViewModel
    {
        public SeedAnalysisRelicVisibilityProfileViewModel(
            string title,
            string description,
            IReadOnlyList<string> actSummaries,
            IReadOnlyList<SeedAnalysisRelicVisibilityItemViewModel> earlyRelics,
            IReadOnlyList<SeedAnalysisRelicVisibilityItemViewModel> seenRelics,
            IReadOnlyList<string> sampleLines)
        {
            Title = title;
            Description = description;
            ActSummaries = actSummaries;
            EarlyRelics = earlyRelics;
            SeenRelics = seenRelics;
            SampleLines = sampleLines;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<string> ActSummaries { get; }

        public IReadOnlyList<SeedAnalysisRelicVisibilityItemViewModel> EarlyRelics { get; }

        public IReadOnlyList<SeedAnalysisRelicVisibilityItemViewModel> SeenRelics { get; }

        public IReadOnlyList<string> SampleLines { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasSamples => SampleLines.Count > 0;
    }

    internal sealed class SeedAnalysisRelicVisibilityItemViewModel
    {
        public SeedAnalysisRelicVisibilityItemViewModel(int rank, string text)
        {
            Rank = rank;
            Text = text;
        }

        public int Rank { get; }

        public string Text { get; }
    }
}
