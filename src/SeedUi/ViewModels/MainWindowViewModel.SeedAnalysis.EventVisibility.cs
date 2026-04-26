using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private string _seedAnalysisEventVisibilitySummary = "事件概率预览会随种子分析一并生成。";

    public ObservableCollection<SeedAnalysisEventVisibilityProfileViewModel> SeedAnalysisEventVisibilityProfiles { get; } = new();

    public string SeedAnalysisEventVisibilitySummary
    {
        get => _seedAnalysisEventVisibilitySummary;
        private set => SetProperty(ref _seedAnalysisEventVisibilitySummary, value);
    }

    private void ApplySeedAnalysisEventVisibility(Sts2EventVisibilityAnalysis analysis)
    {
        SeedAnalysisEventVisibilityProfiles.Clear();
        var routeProfileCount = analysis.Profiles.Count(profile => !profile.IsComposite);
        var recommendedProfile = analysis.Profiles.FirstOrDefault(profile => profile.IsRecommended);

        foreach (var profile in analysis.Profiles)
        {
            var itemLimit = profile.IsRecommended ? 16 : 12;
            var actSummaries = profile.Acts
                .Select(FormatEventActSummary)
                .ToList();

            var earlyEvents = profile.EarlyEvents
                .Take(itemLimit)
                .Select((item, index) => new SeedAnalysisEventVisibilityItemViewModel(
                    index + 1,
                    FormatEventVisibilityEventLine(item)))
                .ToList();

            var seenEvents = profile.SeenEvents
                .Take(itemLimit)
                .Select((item, index) => new SeedAnalysisEventVisibilityItemViewModel(
                    index + 1,
                    FormatEventVisibilityEventLine(item)))
                .ToList();

            var samples = profile.EarlySamples
                .Select((sample, index) => $"样本 {index + 1}：{FormatEventSample(sample)}")
                .ToList();

            SeedAnalysisEventVisibilityProfiles.Add(new SeedAnalysisEventVisibilityProfileViewModel(
                TranslateEventVisibilityProfileTitle(profile.Id, profile.Title),
                TranslateEventVisibilityProfileDescription(profile.Id, profile.Description),
                actSummaries,
                earlyEvents,
                seenEvents,
                samples,
                profile.IsRecommended));
        }

        SeedAnalysisEventVisibilitySummary =
            $"当前使用 {analysis.Samples} 次采样，已融合 {routeProfileCount} 条路线画像；优先看“{TranslateEventVisibilityProfileTitle(recommendedProfile?.Id ?? "consensus", recommendedProfile?.Title ?? "Consensus")}”。前期窗口按前 {analysis.EarlyWindow} 次事件机会统计，第二、第三幕古神按幕开场固定可见处理。";
    }

    private void ClearSeedAnalysisEventVisibility()
    {
        SeedAnalysisEventVisibilityProfiles.Clear();
        SeedAnalysisEventVisibilitySummary = "事件概率预览会随种子分析一并生成。";
    }

    private static string FormatEventActSummary(Sts2EventVisibilityActSummary act)
    {
        var unknownRooms = FormatWeightedCountBand(act.UnknownCounts);
        var events = FormatWeightedCountBand(act.EventCounts);
        var ancient = act.ActNumber == 1
            ? "无幕开场古神"
            : act.AncientVisitChance >= 0.999
                ? "幕开场古神固定可见"
                : $"幕开场古神约 {act.AncientVisitChance:P0}";

        return $"第 {act.ActNumber} 幕：通常会踩 {unknownRooms} 问号房，其中大约 {events} 会真正落成事件；{ancient}。";
    }

    private static string FormatWeightedCountBand(IReadOnlyList<Sts2WeightedIntChance> entries)
    {
        if (entries.Count == 0)
        {
            return "0 个";
        }

        var ordered = entries
            .OrderBy(entry => entry.Value)
            .ToList();
        var min = ordered[0].Value;
        var max = ordered[^1].Value;
        var totalWeight = ordered.Sum(entry => entry.Weight);
        var average = totalWeight <= 0
            ? ordered.Average(entry => entry.Value)
            : ordered.Sum(entry => entry.Value * entry.Weight) / totalWeight;
        var common = ordered
            .OrderByDescending(entry => entry.Weight)
            .ThenBy(entry => entry.Value)
            .First()
            .Value;

        if (min == max)
        {
            return $"{min} 个";
        }

        return $"{min}-{max} 个（常见 {common} 个，均值 {average:F1}）";
    }

    private static string FormatEventSample(IReadOnlyList<string> eventIds)
    {
        return eventIds.Count == 0
            ? "前期窗口内未出现事件"
            : string.Join(" / ", eventIds.Select(FormatEventVisibilityDisplayName));
    }

    private static string FormatEventVisibilityEventLine(Sts2EventVisibilityRankedEvent item)
    {
        if (item.RouteCount > 1)
        {
            return $"{FormatEventVisibilityDisplayName(item.EventId)} | 前期 {item.EarlyProbability:P1}（区间 {item.MinEarlyProbability:P0}-{item.MaxEarlyProbability:P0}，一致 {item.EarlyRouteSupportCount}/{item.RouteCount}） | 整局 {item.SeenProbability:P1}（区间 {item.MinSeenProbability:P0}-{item.MaxSeenProbability:P0}，一致 {item.SeenRouteSupportCount}/{item.RouteCount}） | 首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventVisibilitySource(item.MostCommonSource)}";
        }

        return $"{FormatEventVisibilityDisplayName(item.EventId)} | 前期 {item.EarlyProbability:P1} | 整局 {item.SeenProbability:P1} | 首次约第 {item.AverageFirstOpportunity:F1} 个事件位 | 多来自 {FormatEventVisibilitySource(item.MostCommonSource)}";
    }

    private static string FormatEventVisibilitySource(Sts2EventVisibilitySource source)
    {
        return source switch
        {
            Sts2EventVisibilitySource.Unknown => "问号房",
            Sts2EventVisibilitySource.AncientAct2 => "第二幕开场古神",
            Sts2EventVisibilitySource.AncientAct3 => "第三幕开场古神",
            _ => source.ToString()
        };
    }

    private static string FormatEventVisibilityDisplayName(string eventId)
    {
        if (IsAncientEventVisibilityId(eventId))
        {
            return AncientDisplayCatalog.GetDisplayText(eventId, eventId);
        }

        return FormatEventId(eventId, _staticSeedAnalysisEventLocalization);
    }

    private static bool IsAncientEventVisibilityId(string eventId)
    {
        return AncientDisplayCatalog.AllowedForAct2.Any(option => string.Equals(option.Id, eventId, StringComparison.OrdinalIgnoreCase)) ||
               AncientDisplayCatalog.AllowedForAct3.Any(option => string.Equals(option.Id, eventId, StringComparison.OrdinalIgnoreCase));
    }

    private static string TranslateEventVisibilityProfileTitle(string profileId, string fallbackTitle)
    {
        return profileId switch
        {
            "consensus" => "综合预测（推荐）",
            "balanced" => "均衡推进",
            "aggressive" => "前压打精英",
            "shopper" => "偏商店补强",
            "explorer" => "问号优先",
            _ => fallbackTitle
        };
    }

    private static string TranslateEventVisibilityProfileDescription(string profileId, string fallbackDescription)
    {
        return profileId switch
        {
            "consensus" => "把均衡推进、前压打精英、偏商店补强、问号优先四套路线揉成一份综合结果。只给种子时建议先看它：共同高概率事件会排得更靠前，路线分歧则通过概率区间直接展示。",
            "balanced" => "按普通推进估算：会踩一部分问号，不会强行绕路去堆精英或商店。",
            "aggressive" => "按前期更愿意打精英估算：问号会更少，前期事件可见率通常也会更低。",
            "shopper" => "按主动找商店补强估算：仍会踩问号，但会牺牲一部分事件量去换更稳定的进店。",
            "explorer" => "按更愿意绕路踩问号估算：精英更少、事件量更高，适合补充那些只会在事件偏多路线里冒头的候选。",
            _ => fallbackDescription
        };
    }

    internal sealed class SeedAnalysisEventVisibilityProfileViewModel
    {
        public SeedAnalysisEventVisibilityProfileViewModel(
            string title,
            string description,
            IReadOnlyList<string> actSummaries,
            IReadOnlyList<SeedAnalysisEventVisibilityItemViewModel> earlyEvents,
            IReadOnlyList<SeedAnalysisEventVisibilityItemViewModel> seenEvents,
            IReadOnlyList<string> sampleLines,
            bool isExpandedByDefault)
        {
            Title = title;
            Description = description;
            ActSummaries = actSummaries;
            EarlyEvents = earlyEvents;
            SeenEvents = seenEvents;
            SampleLines = sampleLines;
            IsExpandedByDefault = isExpandedByDefault;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<string> ActSummaries { get; }

        public IReadOnlyList<SeedAnalysisEventVisibilityItemViewModel> EarlyEvents { get; }

        public IReadOnlyList<SeedAnalysisEventVisibilityItemViewModel> SeenEvents { get; }

        public IReadOnlyList<string> SampleLines { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasSamples => SampleLines.Count > 0;

        public bool IsExpandedByDefault { get; }
    }

    internal sealed class SeedAnalysisEventVisibilityItemViewModel
    {
        public SeedAnalysisEventVisibilityItemViewModel(int rank, string text)
        {
            Rank = rank;
            Text = text;
        }

        public int Rank { get; }

        public string Text { get; }
    }
}
