using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SeedModel.Neow;
using SeedModel.Seeds;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private static readonly IReadOnlyDictionary<string, string> EmptyLocalizationTable = new Dictionary<string, string>();
    private static readonly Regex EventDescriptionKeyRegex = new(@"\.pages\.(?<page>[^.]+)\.description$", RegexOptions.Compiled);
    private static readonly Regex EventOptionTitleKeyRegex = new(@"\.pages\.(?<page>[^.]+)\.options\.(?<option>[^.]+)\.title$", RegexOptions.Compiled);
    private static readonly Regex MarkupRegex = new(@"\[(?:/?[A-Za-z]+(?: [^\]]*)?)\]", RegexOptions.Compiled);
    private static readonly Regex LocalizationRefRegex = new(@"@\{[^}]+\}", RegexOptions.Compiled);
    private static readonly Regex AncientConditionalRegex = new(@"\{[^}]*:cond:[^{}]*(?:\{[^}]*\}[^{}]*)*\|[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex AncientVariableRegex = new(@"\{[^}]+\}", RegexOptions.Compiled);

    private RelayCommand? _analyzeSeedCommand;
    private bool _hasSeedAnalysisResult;
    private string _seedAnalysisSummary = "输入种子后点击分析。";
    private string _seedAnalysisSeedValueText = string.Empty;
    private IReadOnlyDictionary<string, string> _seedAnalysisActLocalization = EmptyLocalizationTable;
    private IReadOnlyDictionary<string, string> _seedAnalysisEventLocalization = EmptyLocalizationTable;
    private IReadOnlyDictionary<string, string> _seedAnalysisEncounterLocalization = EmptyLocalizationTable;
    private static IReadOnlyDictionary<string, string> _staticSeedAnalysisActLocalization = EmptyLocalizationTable;
    private static IReadOnlyDictionary<string, string> _staticSeedAnalysisEventLocalization = EmptyLocalizationTable;
    private static IReadOnlyDictionary<string, string> _staticSeedAnalysisEncounterLocalization = EmptyLocalizationTable;

    public ObservableCollection<SeedAnalysisActViewModel> SeedAnalysisActs { get; } = new();

    public ObservableCollection<SeedAnalysisOpeningActViewModel> SeedAnalysisOpeningActs { get; } = new();

    public ObservableCollection<SeedAnalysisRelicGroupViewModel> SeedAnalysisSharedRelicPools { get; } = new();

    public ObservableCollection<SeedAnalysisRelicGroupViewModel> SeedAnalysisPlayerRelicPools { get; } = new();

    public RelayCommand AnalyzeSeedCommand => _analyzeSeedCommand ??= new RelayCommand(AnalyzeSeed);

    public bool HasSeedAnalysisResult
    {
        get => _hasSeedAnalysisResult;
        private set => SetProperty(ref _hasSeedAnalysisResult, value);
    }

    public string SeedAnalysisSummary
    {
        get => _seedAnalysisSummary;
        private set => SetProperty(ref _seedAnalysisSummary, value);
    }

    public string SeedAnalysisSeedValueText
    {
        get => _seedAnalysisSeedValueText;
        private set => SetProperty(ref _seedAnalysisSeedValueText, value);
    }

    private void AnalyzeSeed()
    {
        if (_ancientPreviewer == null)
        {
            SeedAnalysisSummary = "当前未加载种子分析所需的 StS2 数据。";
            LogWarn(SeedAnalysisSummary);
            return;
        }

        if (!SeedFormatter.TryNormalize(SeedText, out var normalizedSeed, out var error))
        {
            SeedAnalysisSummary = error;
            LogWarn($"种子分析失败：{error}");
            return;
        }

        try
        {
            var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
            var analysis = _ancientPreviewer.AnalyzePools(new Sts2SeedAnalysisRequest
            {
                SeedText = normalizedSeed,
                SeedValue = seedValue,
                Character = SelectedCharacter,
                UnlockedCharacters = GetConfiguredUnlockedCharacters(),
                AscensionLevel = SelectedAscensionLevel,
                IncludeDarvSharedAncient = DefaultIncludeDarvSharedAncient
            });

            var openingActs = BuildSeedAnalysisOpeningActs(normalizedSeed, seedValue);
            ApplySeedAnalysis(analysis);
            ApplySeedAnalysisOpenings(openingActs);
            SeedAnalysisSummary = $"已分析种子 {normalizedSeed}，角色：{GetCharacterDisplayName(SelectedCharacter)}，进阶等级：{SelectedAscensionLevel}";
            SeedAnalysisSeedValueText = $"uint seed: {analysis.SeedValue}";
            StatusMessage = "种子分析完成。";
            LogInfo($"种子分析完成：{normalizedSeed}");
        }
        catch (Exception ex)
        {
            ClearSeedAnalysisResults();
            SeedAnalysisSummary = $"种子分析失败：{ex.Message}";
            SeedAnalysisSeedValueText = string.Empty;
            StatusMessage = SeedAnalysisSummary;
            LogError(SeedAnalysisSummary);
        }
    }

    private void ApplySeedAnalysis(Sts2SeedAnalysis analysis)
    {
        ClearSeedAnalysisResults();

        foreach (var act in analysis.Acts)
        {
            SeedAnalysisActs.Add(new SeedAnalysisActViewModel(
                $"第{act.ActNumber}幕 · {FormatActName(act.ActName)}",
                act.EventPool.Select(CreateEventDisplayItem).ToList(),
                act.MonsterPool.Select(FormatEncounterId).ToList(),
                act.ElitePool.Select(FormatEncounterId).ToList()));
        }

        foreach (var rarityGroup in analysis.SharedRelicPools)
        {
            SeedAnalysisSharedRelicPools.Add(new SeedAnalysisRelicGroupViewModel(
                $"{FormatRarity(rarityGroup.Rarity)} ({rarityGroup.Relics.Count})",
                rarityGroup.Relics.Select(FormatRelicId).ToList()));
        }

        foreach (var rarityGroup in analysis.PlayerRelicPools)
        {
            SeedAnalysisPlayerRelicPools.Add(new SeedAnalysisRelicGroupViewModel(
                $"{FormatRarity(rarityGroup.Rarity)} ({rarityGroup.Relics.Count})",
                rarityGroup.Relics.Select(FormatRelicId).ToList()));
        }

        HasSeedAnalysisResult = true;
    }

    private void ApplySeedAnalysisOpenings(IReadOnlyList<SeedAnalysisOpeningActViewModel> openingActs)
    {
        SeedAnalysisOpeningActs.Clear();
        foreach (var openingAct in openingActs)
        {
            SeedAnalysisOpeningActs.Add(openingAct);
        }
    }

    private void ClearSeedAnalysisResults()
    {
        SeedAnalysisActs.Clear();
        SeedAnalysisOpeningActs.Clear();
        SeedAnalysisSharedRelicPools.Clear();
        SeedAnalysisPlayerRelicPools.Clear();
        HasSeedAnalysisResult = false;
    }

    private void ResetSeedAnalysis()
    {
        SeedAnalysisSummary = "输入种子后点击分析。";
        SeedAnalysisSeedValueText = string.Empty;
        ClearSeedAnalysisResults();
    }

    private void ReloadSeedAnalysisLocalization()
    {
        var version = SelectedGameVersion.Id;
        _seedAnalysisActLocalization = LoadSts2LocalizationTable(version, "acts.json", "章节");
        _seedAnalysisEventLocalization = LoadSts2LocalizationTable(version, "events.json", "事件");
        _seedAnalysisEncounterLocalization = LoadSts2LocalizationTable(version, "encounters.json", "遭遇");
        _staticSeedAnalysisActLocalization = _seedAnalysisActLocalization;
        _staticSeedAnalysisEventLocalization = _seedAnalysisEventLocalization;
        _staticSeedAnalysisEncounterLocalization = _seedAnalysisEncounterLocalization;
    }

    internal static SeedAnalysisDisplayItemViewModel CreateSeedAnalysisEventDisplayItem(string eventId)
    {
        var preferredPage = GetPreferredEventPage(eventId, _staticSeedAnalysisEventLocalization);
        return new SeedAnalysisDisplayItemViewModel(
            FormatEventId(eventId, _staticSeedAnalysisEventLocalization),
            GetEventDescription(eventId, preferredPage, _staticSeedAnalysisEventLocalization),
            GetEventOptions(eventId, preferredPage, _staticSeedAnalysisEventLocalization));
    }

    internal static string GetSeedAnalysisEncounterDisplayName(string encounterId)
    {
        return TryGetLocalizedTitle(_staticSeedAnalysisEncounterLocalization, encounterId, out var localized)
            ? localized
            : FormatGenericId(encounterId
                .Replace("Normal", string.Empty, StringComparison.Ordinal)
                .Replace("Weak", " 前置", StringComparison.Ordinal)
                .Replace("Elite", " 精英", StringComparison.Ordinal)
                .Replace("Boss", " Boss", StringComparison.Ordinal));
    }

    private static string FormatRarity(string rarity)
    {
        return rarity switch
        {
            "Common" => "普通",
            "Uncommon" => "非凡",
            "Rare" => "稀有",
            "Shop" => "商店",
            _ => rarity
        };
    }

    private IReadOnlyList<SeedAnalysisOpeningActViewModel> BuildSeedAnalysisOpeningActs(string seedText, uint seedValue)
    {
        var openingActs = new List<SeedAnalysisOpeningActViewModel>();

        var act1Options = BuildAct1OpeningOptions(seedValue);
        if (act1Options.Count > 0)
        {
            openingActs.Add(new SeedAnalysisOpeningActViewModel(
                "第一幕开局选项",
                AncientDisplayCatalog.GetLocalizedName("NEOW", "Neow"),
                AncientDisplayCatalog.GetDisplayText("NEOW", "Neow"),
                act1Options));
        }

        var preview = _ancientPreviewer?.Preview(new Sts2RunRequest
        {
            SeedValue = seedValue,
            SeedText = seedText,
            Character = SelectedCharacter,
            UnlockedCharacters = GetConfiguredUnlockedCharacters(),
            AscensionLevel = SelectedAscensionLevel,
            PlayerCount = 1,
            IncludeDarvSharedAncient = DefaultIncludeDarvSharedAncient,
            IncludeAct2 = true,
            IncludeAct3 = true
        });

        if (preview != null)
        {
            foreach (var act in preview.Acts.OrderBy(act => act.ActNumber))
            {
                var fallbackName = act.AncientName ?? act.AncientId ?? string.Empty;
                openingActs.Add(new SeedAnalysisOpeningActViewModel(
                    $"第{act.ActNumber}幕开局选项",
                    AncientDisplayCatalog.GetLocalizedName(act.AncientId, fallbackName),
                    AncientDisplayCatalog.GetDisplayText(act.AncientId, fallbackName),
                    act.AncientOptions
                        .Select(option => new SeedAnalysisOpeningOptionViewModel(
                            SanitizeAncientText(option.Title ?? option.OptionId),
                            SanitizeAncientText(option.Description ?? string.Empty),
                            SanitizeAncientText(option.Note ?? string.Empty),
                            Array.Empty<string>()))
                        .ToList()));
            }
        }

        return openingActs;
    }

    private IReadOnlyList<SeedAnalysisOpeningOptionViewModel> BuildAct1OpeningOptions(uint seedValue)
    {
        var dataset = EnsureSeedAnalysisDataset();
        if (dataset == null)
        {
            return Array.Empty<SeedAnalysisOpeningOptionViewModel>();
        }

        var generator = new NeowGenerator(dataset);
        var options = generator.Generate(NeowGenerationContext.Create(
            seed: seedValue,
            playerCount: 1,
            scrollBoxesEligible: true,
            hasRunModifiers: false,
            character: SelectedCharacter,
            ascensionLevel: SelectedAscensionLevel));

        return options
            .Select(option => new SeedAnalysisOpeningOptionViewModel(
                SanitizeLocalizationText(option.Title ?? option.RelicId),
                SanitizeLocalizationText(option.Description ?? string.Empty),
                FormatNeowOptionNote(option),
                option.Details.Select(FormatRewardDetail).Where(detail => !string.IsNullOrWhiteSpace(detail)).ToList()))
            .ToList();
    }

    private NeowOptionDataset? EnsureSeedAnalysisDataset()
    {
        if (_dataset != null)
        {
            return _dataset;
        }

        try
        {
            var version = SelectedGameVersion.Id;
            var neowPath = Path.Combine(AppContext.BaseDirectory, "data", version, "neow", "options.json");
            if (!File.Exists(neowPath))
            {
                neowPath = Path.Combine(AppContext.BaseDirectory, "data", "0.99.1", "neow", "options.json");
            }

            _dataset = LoadDatasetInternal(neowPath);
            BuildCatalogs(_dataset);
            _rollCommand?.RaiseCanExecuteChanged();
            return _dataset;
        }
        catch (Exception ex)
        {
            LogWarn($"加载第一幕开局选项数据失败：{ex.Message}");
            return null;
        }
    }

    private static string FormatNeowOptionNote(NeowOptionResult option)
    {
        return option.Pool switch
        {
            "Positive" => "正面池",
            "Negative" => "负面池",
            _ => option.Pool
        };
    }

    private static string FormatRewardDetail(RewardDetail detail)
    {
        var label = SanitizeLocalizationText(detail.Label);
        var value = SanitizeLocalizationText(detail.Value);
        if (string.IsNullOrWhiteSpace(label))
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return label;
        }

        return $"{label}：{value}";
    }

    private static string SanitizeAncientText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = SanitizeLocalizationText(value);
        text = AncientConditionalRegex.Replace(text, string.Empty);
        text = AncientVariableRegex.Replace(text, string.Empty);
        return text.Trim();
    }

    private static string FormatRelicId(string relicId)
    {
        return GetRelicDisplayName(relicId);
    }

    private string FormatActName(string actName)
    {
        return FormatActName(actName, _seedAnalysisActLocalization);
    }

    private string FormatEventId(string eventId)
    {
        return FormatEventId(eventId, _seedAnalysisEventLocalization);
    }

    private SeedAnalysisDisplayItemViewModel CreateEventDisplayItem(string eventId)
    {
        var preferredPage = GetPreferredEventPage(eventId, _seedAnalysisEventLocalization);

        return new SeedAnalysisDisplayItemViewModel(
            FormatEventId(eventId, _seedAnalysisEventLocalization),
            GetEventDescription(eventId, preferredPage, _seedAnalysisEventLocalization),
            GetEventOptions(eventId, preferredPage, _seedAnalysisEventLocalization));
    }

    private static string GetPreferredEventPage(string eventId, IReadOnlyDictionary<string, string> localizationTable)
    {
        if (localizationTable.Count == 0)
        {
            return string.Empty;
        }

        var tokenPrefix = $"{ToLocalizationToken(eventId)}.";
        return localizationTable.Keys
            .Where(key => key.StartsWith(tokenPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(key => key.EndsWith(".description", StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetEventDescriptionPriority)
            .Select(GetEventPage)
            .FirstOrDefault(page => !string.IsNullOrWhiteSpace(page))
            ?? string.Empty;
    }

    private static string GetEventDescription(
        string eventId,
        string preferredPage,
        IReadOnlyDictionary<string, string> localizationTable)
    {
        if (localizationTable.Count == 0)
        {
            return string.Empty;
        }

        foreach (var page in GetCandidateEventPages(preferredPage))
        {
            var key = $"{ToLocalizationToken(eventId)}.pages.{page}.description";
            if (localizationTable.TryGetValue(key, out var description) &&
                !string.IsNullOrWhiteSpace(description))
            {
                return SanitizeLocalizationText(description);
            }
        }

        var tokenPrefix = $"{ToLocalizationToken(eventId)}.";
        var bestKey = localizationTable.Keys
            .Where(key => key.StartsWith(tokenPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(key => key.EndsWith(".description", StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetEventDescriptionPriority)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(bestKey) ||
            !localizationTable.TryGetValue(bestKey, out var fallbackDescription) ||
            string.IsNullOrWhiteSpace(fallbackDescription))
        {
            return string.Empty;
        }

        return SanitizeLocalizationText(fallbackDescription);
    }

    private static IReadOnlyList<SeedAnalysisOptionDisplayItemViewModel> GetEventOptions(
        string eventId,
        string preferredPage,
        IReadOnlyDictionary<string, string> localizationTable)
    {
        if (localizationTable.Count == 0)
        {
            return Array.Empty<SeedAnalysisOptionDisplayItemViewModel>();
        }

        var token = ToLocalizationToken(eventId);
        foreach (var page in GetCandidateEventPages(preferredPage))
        {
            var pagePrefix = $"{token}.pages.{page}.options.";
            var options = localizationTable.Keys
                .Where(key => key.StartsWith(pagePrefix, StringComparison.OrdinalIgnoreCase))
                .Where(key => key.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Select(titleKey => CreateEventOptionDisplayItem(titleKey, localizationTable))
                .Where(option => option is not null)
                .Cast<SeedAnalysisOptionDisplayItemViewModel>()
                .ToList();

            if (options.Count > 0)
            {
                return options;
            }
        }

        return Array.Empty<SeedAnalysisOptionDisplayItemViewModel>();
    }

    private static SeedAnalysisOptionDisplayItemViewModel? CreateEventOptionDisplayItem(
        string titleKey,
        IReadOnlyDictionary<string, string> localizationTable)
    {
        if (!localizationTable.TryGetValue(titleKey, out var title) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var descriptionKey = titleKey[..^".title".Length] + ".description";
        localizationTable.TryGetValue(descriptionKey, out var description);

        return new SeedAnalysisOptionDisplayItemViewModel(
            SanitizeLocalizationText(title),
            SanitizeLocalizationText(description ?? string.Empty));
    }

    private static IEnumerable<string> GetCandidateEventPages(string preferredPage)
    {
        if (!string.IsNullOrWhiteSpace(preferredPage))
        {
            yield return preferredPage;
        }

        yield return "INITIAL";
        yield return "INTRO";
        yield return "ALL";
    }

    private static string GetEventPage(string key)
    {
        var descriptionMatch = EventDescriptionKeyRegex.Match(key);
        if (descriptionMatch.Success)
        {
            return descriptionMatch.Groups["page"].Value;
        }

        var optionMatch = EventOptionTitleKeyRegex.Match(key);
        return optionMatch.Success
            ? optionMatch.Groups["page"].Value
            : string.Empty;
    }

    private static int GetEventDescriptionPriority(string key)
    {
        var match = EventDescriptionKeyRegex.Match(key);
        if (!match.Success)
        {
            return 100;
        }

        return match.Groups["page"].Value.ToUpperInvariant() switch
        {
            "INITIAL" => 0,
            "INTRO" => 1,
            "ALL" => 2,
            _ => 10
        };
    }

    private static string SanitizeLocalizationText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = LocalizationRefRegex.Replace(value, string.Empty);
        text = MarkupRegex.Replace(text, string.Empty);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("NL", "\n", StringComparison.Ordinal);

        var lines = text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatEncounterId(string encounterId)
    {
        return GetSeedAnalysisEncounterDisplayName(encounterId);
    }

    private static string FormatActName(string actName, IReadOnlyDictionary<string, string> localizationTable)
    {
        return TryGetLocalizedTitle(localizationTable, actName, out var localized)
            ? localized
            : FormatGenericId(actName);
    }

    private static string FormatEventId(string eventId, IReadOnlyDictionary<string, string> localizationTable)
    {
        return TryGetLocalizedTitle(localizationTable, eventId, out var localized)
            ? localized
            : FormatGenericId(eventId);
    }

    private static bool TryGetLocalizedTitle(IReadOnlyDictionary<string, string> table, string rawId, out string title)
    {
        if (table.Count > 0 &&
            table.TryGetValue($"{ToLocalizationToken(rawId)}.title", out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
        {
            title = localized;
            return true;
        }

        title = string.Empty;
        return false;
    }

    private static string ToLocalizationToken(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawId.Length * 2);
        for (var i = 0; i < rawId.Length; i++)
        {
            var current = rawId[i];
            if (current is '_' or ' ' or '-')
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                continue;
            }

            if (i > 0)
            {
                var previous = rawId[i - 1];
                var next = i + 1 < rawId.Length ? rawId[i + 1] : '\0';
                var shouldInsertUnderscore =
                    (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous))) ||
                    (char.IsUpper(current) && char.IsUpper(previous) && next != '\0' && char.IsLower(next)) ||
                    (char.IsDigit(current) && char.IsLetter(previous)) ||
                    (char.IsLetter(current) && char.IsDigit(previous));

                if (shouldInsertUnderscore && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString();
    }

    private static string FormatGenericId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0)
            {
                var previous = value[i - 1];
                if ((char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous))) ||
                    (char.IsDigit(current) && char.IsLetter(previous)))
                {
                    builder.Append(' ');
                }
            }

            builder.Append(current == '_' ? ' ' : current);
        }

        return builder.ToString().Trim();
    }

    internal sealed class SeedAnalysisActViewModel
    {
        public SeedAnalysisActViewModel(
            string title,
            IReadOnlyList<SeedAnalysisDisplayItemViewModel> events,
            IReadOnlyList<string> monsters,
            IReadOnlyList<string> elites)
        {
            Title = title;
            Events = events;
            Monsters = monsters;
            Elites = elites;
        }

        public string Title { get; }

        public IReadOnlyList<SeedAnalysisDisplayItemViewModel> Events { get; }

        public IReadOnlyList<string> Monsters { get; }

        public IReadOnlyList<string> Elites { get; }
    }

    internal sealed class SeedAnalysisOpeningActViewModel
    {
        public SeedAnalysisOpeningActViewModel(
            string title,
            string ancientName,
            string ancientDisplay,
            IReadOnlyList<SeedAnalysisOpeningOptionViewModel> options)
        {
            Title = title;
            AncientName = ancientName;
            AncientDisplay = ancientDisplay;
            Options = options;
        }

        public string Title { get; }

        public string AncientName { get; }

        public string AncientDisplay { get; }

        public IReadOnlyList<SeedAnalysisOpeningOptionViewModel> Options { get; }

        public bool HasAncientDisplay => !string.IsNullOrWhiteSpace(AncientDisplay);
    }

    internal sealed class SeedAnalysisOpeningOptionViewModel
    {
        public SeedAnalysisOpeningOptionViewModel(
            string title,
            string description,
            string note,
            IReadOnlyList<string> details)
        {
            Title = title;
            Description = description;
            Note = note;
            Details = details;
        }

        public string Title { get; }

        public string Description { get; }

        public string Note { get; }

        public IReadOnlyList<string> Details { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        public bool HasDetails => Details.Count > 0;
    }

    internal sealed class SeedAnalysisDisplayItemViewModel
    {
        public SeedAnalysisDisplayItemViewModel(
            string title,
            string description,
            IReadOnlyList<SeedAnalysisOptionDisplayItemViewModel> options)
        {
            Title = title;
            Description = description;
            Options = options;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<SeedAnalysisOptionDisplayItemViewModel> Options { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasOptions => Options.Count > 0;

        public bool HasTooltipContent => HasDescription || HasOptions;

        public bool HasDescriptionAndOptions => HasDescription && HasOptions;
    }

    internal sealed class SeedAnalysisOptionDisplayItemViewModel
    {
        public SeedAnalysisOptionDisplayItemViewModel(string title, string description)
        {
            Title = title;
            Description = description;
        }

        public string Title { get; }

        public string Description { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }

    internal sealed class SeedAnalysisRelicGroupViewModel
    {
        public SeedAnalysisRelicGroupViewModel(string title, IReadOnlyList<string> relics)
        {
            Title = title;
            Relics = relics;
        }

        public string Title { get; }

        public IReadOnlyList<string> Relics { get; }
    }
}
