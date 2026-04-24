using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal sealed class RollResultViewModel
{
    public RollResultViewModel(
        string seedString,
        uint seedValue,
        CharacterId character,
        string characterName,
        IEnumerable<NeowOptionResult> act1Options,
        Sts2SeedAnalysis? poolAnalysis,
        Sts2PoolFilter? poolFilter,
        Sts2RunPreview? ancientPreview,
        bool requiresAct2,
        bool requiresAct3,
        int ascensionLevel,
        ShopPreview? shopPreview = null,
        bool shopFilterMatched = false)
    {
        SeedString = seedString;
        SeedValue = seedValue;
        Character = character;
        CharacterName = characterName;
        RequiresAct2 = requiresAct2;
        RequiresAct3 = requiresAct3;
        AscensionLevel = ascensionLevel;
        var optionList = act1Options.ToList();
        RawOptions = optionList;
        Options = optionList.Select(o => new OptionDisplayViewModel(o)).ToList();
        var act1EventIds = ToIdSet(poolFilter?.Act1EventIds);
        var act2EventIds = ToIdSet(poolFilter?.Act2EventIds);
        var act3EventIds = ToIdSet(poolFilter?.Act3EventIds);
        var sharedRelicIds = ToIdSet(poolFilter?.SharedRelicIds);
        var playerRelicIds = ToIdSet(poolFilter?.PlayerRelicIds);
        PoolActs = poolAnalysis == null
            ? Array.Empty<PoolActViewModel>()
            : poolAnalysis.Acts.Select(act => new PoolActViewModel(act, GetRequiredEventIds(act.ActNumber, act1EventIds, act2EventIds, act3EventIds))).ToList();
        SharedRelicPools = poolAnalysis == null
            ? Array.Empty<PoolRelicGroupViewModel>()
            : poolAnalysis.SharedRelicPools.Select(group => new PoolRelicGroupViewModel(group, sharedRelicIds)).ToList();
        PlayerRelicPools = poolAnalysis == null
            ? Array.Empty<PoolRelicGroupViewModel>()
            : poolAnalysis.PlayerRelicPools.Select(group => new PoolRelicGroupViewModel(group, playerRelicIds)).ToList();
        AncientActs = ancientPreview == null
            ? Array.Empty<AncientActViewModel>()
            : ancientPreview.Acts.Select(act => new AncientActViewModel(act)).ToList();

        ShopPreview = shopPreview;
        ShopFilterMatched = shopFilterMatched;
        HasShopPreview = shopPreview != null;
        if (shopPreview != null)
        {
            var colored = shopPreview.ColoredCards;
            ShopCardEntries = colored.Select((c, i) => new ShopItemViewModel(c.Id, c.Price, IsDiscounted(shopPreview.DiscountedColoredSlot, i), MainWindowViewModel.GetCardDisplayName(c.Id))).ToList();
            ShopColorlessEntries = shopPreview.ColorlessCards.Select(c => new ShopItemViewModel(c.Id, c.Price, false, MainWindowViewModel.GetCardDisplayName(c.Id))).ToList();
            ShopRelicEntries = shopPreview.Relics.Select(r => new ShopItemViewModel(r.Id, r.Price, false, MainWindowViewModel.GetRelicDisplayName(r.Id))).ToList();
            ShopPotionEntries = shopPreview.Potions.Select(p => new ShopItemViewModel(p.Id, p.Price, false, MainWindowViewModel.GetPotionDisplayName(p.Id))).ToList();
        }
        else
        {
            ShopCardEntries = Array.Empty<ShopItemViewModel>();
            ShopColorlessEntries = Array.Empty<ShopItemViewModel>();
            ShopRelicEntries = Array.Empty<ShopItemViewModel>();
            ShopPotionEntries = Array.Empty<ShopItemViewModel>();
        }
    }

    private static bool IsDiscounted(int discountedSlot, int index) => discountedSlot == index && discountedSlot >= 0;

    public string SeedString { get; }

    public uint SeedValue { get; }

    public CharacterId Character { get; }

    public string CharacterName { get; }

    public IReadOnlyList<OptionDisplayViewModel> Options { get; }

    public IReadOnlyList<NeowOptionResult> RawOptions { get; }

    public string SeedText => SeedString;

    public IReadOnlyList<PoolActViewModel> PoolActs { get; }

    public IReadOnlyList<PoolRelicGroupViewModel> SharedRelicPools { get; }

    public IReadOnlyList<PoolRelicGroupViewModel> PlayerRelicPools { get; }

    public bool HasPoolAnalysis => PoolActs.Count > 0 || SharedRelicPools.Count > 0 || PlayerRelicPools.Count > 0;

    public IReadOnlyList<AncientActViewModel> AncientActs { get; }

    public bool HasAncientActs => AncientActs.Count > 0;

    public bool RequiresAct2 { get; }

    public bool RequiresAct3 { get; }

    public int AscensionLevel { get; }

    public bool RequiresAncientMatch => RequiresAct2 || RequiresAct3;

    public int Act1MatchCount => Options.Count;

    public bool HasAct2 => AncientActs.Any(a => a.ActNumber == 2);

    public bool HasAct3 => AncientActs.Any(a => a.ActNumber == 3);

    public string Act2AncientDisplay => AncientActs.FirstOrDefault(a => a.ActNumber == 2)?.AncientDisplay ?? "—";

    public string Act3AncientDisplay => AncientActs.FirstOrDefault(a => a.ActNumber == 3)?.AncientDisplay ?? "—";

    public bool MatchesRequiredActs => (!RequiresAct2 || HasAct2) && (!RequiresAct3 || HasAct3);

    public ShopPreview? ShopPreview { get; }
    public bool ShopFilterMatched { get; }
    public bool HasShopPreview { get; }
    public IReadOnlyList<ShopItemViewModel> ShopCardEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopColorlessEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopRelicEntries { get; }
    public IReadOnlyList<ShopItemViewModel> ShopPotionEntries { get; }

    internal sealed class PoolActViewModel
    {
        public PoolActViewModel(Sts2ActPoolPreview act, HashSet<string> requiredIds)
        {
            Title = $"第{act.ActNumber}幕事件池";
            Events = act.EventPool
                .Select(eventId => new PoolEventItemViewModel(
                    MainWindowViewModel.CreateSeedAnalysisEventDisplayItem(eventId),
                    requiredIds.Contains(eventId)))
                .ToList();
        }

        public string Title { get; }

        public IReadOnlyList<PoolEventItemViewModel> Events { get; }
    }

    internal sealed class PoolRelicGroupViewModel
    {
        public PoolRelicGroupViewModel(Sts2RelicPoolPreviewGroup group, HashSet<string> requiredIds)
        {
            Title = $"{FormatRarity(group.Rarity)} ({group.Relics.Count})";
            Relics = group.Relics
                .Select(relicId => new PoolRelicItemViewModel(
                    MainWindowViewModel.GetRelicDisplayName(relicId),
                    requiredIds.Contains(relicId)))
                .ToList();
        }

        public string Title { get; }

        public IReadOnlyList<PoolRelicItemViewModel> Relics { get; }
    }

    internal sealed class PoolEventItemViewModel
    {
        public PoolEventItemViewModel(MainWindowViewModel.SeedAnalysisDisplayItemViewModel item, bool isMatched)
        {
            Title = item.Title;
            Description = item.Description;
            Options = item.Options;
            IsMatched = isMatched;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<MainWindowViewModel.SeedAnalysisOptionDisplayItemViewModel> Options { get; }

        public bool IsMatched { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasOptions => Options.Count > 0;

        public bool HasTooltipContent => HasDescription || HasOptions;

        public bool HasDescriptionAndOptions => HasDescription && HasOptions;
    }

    internal sealed class PoolRelicItemViewModel
    {
        public PoolRelicItemViewModel(string displayName, bool isMatched)
        {
            DisplayName = displayName;
            IsMatched = isMatched;
        }

        public string DisplayName { get; }

        public bool IsMatched { get; }
    }

    internal sealed class AncientActViewModel
    {
        public AncientActViewModel(Sts2ActPreview preview)
        {
            ActNumber = preview.ActNumber;
            AncientId = preview.AncientId ?? string.Empty;
            AncientName = AncientDisplayCatalog.GetLocalizedName(AncientId, preview.AncientName ?? AncientId);
            AncientDisplay = AncientDisplayCatalog.GetDisplayText(AncientId, preview.AncientName ?? AncientId);
            Options = preview.AncientOptions.Select(option => new AncientOptionDisplayViewModel(option)).ToList();
        }

        public int ActNumber { get; }

        public string AncientId { get; }

        public string AncientName { get; }

        public string AncientDisplay { get; }

        public string ActTitle => string.Format(
            CultureInfo.InvariantCulture,
            "第{0}幕古神",
            ActNumber);

        public bool HasAncientId => !string.IsNullOrWhiteSpace(AncientId);

        public string AncientIdLabel => HasAncientId ? $"ID: {AncientId}" : string.Empty;

        public IReadOnlyList<AncientOptionDisplayViewModel> Options { get; }

        public bool HasOptions => Options.Count > 0;
    }

    internal sealed class AncientOptionDisplayViewModel
    {
        private static readonly Regex ColorMarkupPattern = new(@"\[/?(gold|blue|red|purple|orange|gray|green)\]", RegexOptions.Compiled);
        private static readonly Regex ConditionalPattern = new(@"\{[^}]*:cond:[^{}]*(?:\{[^}]*\}[^{}]*)*\|[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex VariablePattern = new(@"\{[^}]+\}", RegexOptions.Compiled);

        public AncientOptionDisplayViewModel(Sts2AncientOption option)
        {
            OptionId = option.OptionId;
            Title = StripMarkup(option.Title ?? option.OptionId);
            Description = StripMarkup(option.Description ?? string.Empty);
            Note = StripMarkup(option.Note ?? string.Empty);
        }

        public string OptionId { get; }

        public string Title { get; }

        public string Description { get; }

        public string Note { get; }

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        private static string StripMarkup(string text)
        {
            text = ColorMarkupPattern.Replace(text, string.Empty);
            text = ConditionalPattern.Replace(text, string.Empty);
            text = VariablePattern.Replace(text, string.Empty);
            return text;
        }
    }

    internal sealed class ShopItemViewModel
    {
        public ShopItemViewModel(string id, int price, bool isDiscounted, string displayName)
        {
            Id = id;
            Price = price;
            IsDiscounted = isDiscounted;
            DisplayName = displayName;
        }

        public string Id { get; }
        public int Price { get; }
        public bool IsDiscounted { get; }
        public string DisplayName { get; }
        public string PriceDisplay => Price.ToString();
        public string DiscountLabel => IsDiscounted ? " (折)" : string.Empty;
    }

    private static HashSet<string> ToIdSet(IReadOnlyList<string>? values)
    {
        return values == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetRequiredEventIds(
        int actNumber,
        HashSet<string> act1EventIds,
        HashSet<string> act2EventIds,
        HashSet<string> act3EventIds)
    {
        return actNumber switch
        {
            1 => act1EventIds,
            2 => act2EventIds,
            3 => act3EventIds,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
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
}
