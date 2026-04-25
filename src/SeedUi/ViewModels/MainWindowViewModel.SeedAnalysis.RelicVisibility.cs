using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private string _seedAnalysisRelicVisibilitySummary = "Probability preview is generated together with seed analysis.";

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
                    $"Act {act.ActNumber}: treasure {FormatChanceList(act.TreasureCounts)} / elite {FormatChanceList(act.EliteCounts)} / shop {FormatChanceList(act.ShopCounts)} / ancient {act.AncientVisitChance:P0}")
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
                .Select((sample, index) => $"Sample {index + 1}: {FormatRelicSample(sample)}")
                .ToList();

            SeedAnalysisRelicVisibilityProfiles.Add(new SeedAnalysisRelicVisibilityProfileViewModel(
                profile.Title,
                profile.Description,
                actSummaries,
                earlyRelics,
                seenRelics,
                samples));
        }

        SeedAnalysisRelicVisibilitySummary =
            $"Probability preview uses {analysis.Samples} Monte Carlo samples per route profile. Early window = first {analysis.EarlyWindow} relic opportunities.";
    }

    private void ClearSeedAnalysisRelicVisibility()
    {
        SeedAnalysisRelicVisibilityProfiles.Clear();
        SeedAnalysisRelicVisibilitySummary = "Probability preview is generated together with seed analysis.";
    }

    private static string FormatChanceList(IReadOnlyList<Sts2WeightedIntChance> entries)
    {
        return string.Join(", ", entries.Select(entry => $"{entry.Value} ({entry.Weight:P0})"));
    }

    private static string FormatRelicSample(IReadOnlyList<string> relicIds)
    {
        return relicIds.Count == 0
            ? "none in early window"
            : string.Join(" / ", relicIds.Select(GetRelicDisplayName));
    }

    private static string FormatRelicVisibilityRelicLine(Sts2RelicVisibilityRankedRelic item)
    {
        return $"{GetRelicDisplayName(item.RelicId)} | Seen {item.SeenProbability:P1} | Non-shop {item.NonShopSeenProbability:P1} | Shop {item.ShopSeenProbability:P1} | Early {item.EarlyProbability:P1} | Avg {item.AverageFirstOpportunity:F2} | {FormatRelicVisibilitySource(item.MostCommonSource)}";
    }

    private static string FormatRelicVisibilitySource(Sts2RelicVisibilitySource source)
    {
        return source switch
        {
            Sts2RelicVisibilitySource.Treasure => "Treasure",
            Sts2RelicVisibilitySource.Elite => "Elite",
            Sts2RelicVisibilitySource.Shop => "Shop",
            Sts2RelicVisibilitySource.AncientAct2 => "Ancient Act2",
            Sts2RelicVisibilitySource.AncientAct3 => "Ancient Act3",
            _ => source.ToString()
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
