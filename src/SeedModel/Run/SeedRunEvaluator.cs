using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Sts2;

namespace SeedModel.Run;

public sealed class SeedRunEvaluator
{
    private readonly NeowGenerator _neowGenerator;
    private readonly Sts2RunPreviewer? _ancientPreviewer;
    private readonly NeowOptionDataset _neowDataset;

    public SeedRunEvaluator(NeowOptionDataset neowDataset, Sts2RunPreviewer? ancientPreviewer = null)
    {
        _neowDataset = neowDataset ?? throw new ArgumentNullException(nameof(neowDataset));
        _neowGenerator = new NeowGenerator(neowDataset);
        _ancientPreviewer = ancientPreviewer;
    }

    public SeedRunMatch Evaluate(SeedRunEvaluationContext context, SeedRunFilter filter)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        var neowContext = NeowGenerationContext.Create(
            context.RunSeed,
            playerCount: context.PlayerCount,
            scrollBoxesEligible: context.ScrollBoxesEligible,
            hasRunModifiers: context.HasRunModifiers,
            character: context.Character,
            ascensionLevel: context.AscensionLevel);

        var neowOptions = _neowGenerator.Generate(neowContext);
        var neowMatches = filter.NeowFilter.HasCriteria
            ? neowOptions.Where(filter.NeowFilter.Matches).ToList()
            : neowOptions.ToList();

        var neowMatched = neowMatches.Count > 0;
        if (!neowMatched)
        {
            return CreateMatch(
                neowOptions,
                neowMatches,
                neowMatched: false,
                ancientMatched: true,
                poolMatched: true,
                shopMatched: true);
        }

        var ancientMatched = true;

        Sts2RunPreview? actPreview = null;
        if (filter.AncientFilter.HasCriteria)
        {
            if (_ancientPreviewer == null)
            {
                ancientMatched = false;
            }
            else
            {
                actPreview = PreviewActs(context);
                ancientMatched = filter.AncientFilter.Matches(actPreview);
            }

            if (!ancientMatched)
            {
                return CreateMatch(
                    neowOptions,
                    neowMatches,
                    neowMatched: true,
                    ancientMatched: false,
                    poolMatched: true,
                    shopMatched: true);
            }
        }

        var poolMatched = true;
        Sts2SeedAnalysis? poolAnalysis = null;
        Sts2EventVisibilityAnalysis? eventVisibilityAnalysis = null;
        Sts2RelicVisibilityAnalysis? relicVisibilityAnalysis = null;
        if (filter.PoolFilter.HasCriteria)
        {
            if (_ancientPreviewer == null)
            {
                poolMatched = false;
            }
            else
            {
                var actOnlyPoolFilter = filter.PoolFilter with
                {
                    Act1EventIds = Array.Empty<string>(),
                    Act2EventIds = Array.Empty<string>(),
                    Act3EventIds = Array.Empty<string>(),
                    HighProbabilityEventIds = Array.Empty<string>(),
                    HighProbabilityRelicIds = Array.Empty<string>()
                };
                if (actOnlyPoolFilter.HasCriteria)
                {
                    poolAnalysis = _ancientPreviewer.AnalyzePools(new Sts2SeedAnalysisRequest
                    {
                        SeedText = context.SeedText,
                        SeedValue = context.RunSeed,
                        Character = context.Character,
                        UnlockedCharacters = context.UnlockedCharacters,
                        AscensionLevel = context.AscensionLevel,
                        AncientAvailability = context.ResolveAncientAvailability(),
                        IncludeDarvSharedAncient = context.IncludeDarvSharedAncient
                    });

                    poolMatched = actOnlyPoolFilter.Matches(poolAnalysis, relicVisibility: null);
                }

                if (poolMatched && filter.PoolFilter.HighProbabilityEventIds.Count > 0)
                {
                    eventVisibilityAnalysis = _ancientPreviewer.AnalyzeEventVisibility(_neowDataset, new Sts2EventVisibilityRequest
                    {
                        SeedText = context.SeedText,
                        SeedValue = context.RunSeed,
                        Character = context.Character,
                        UnlockedCharacters = context.UnlockedCharacters,
                        AscensionLevel = context.AscensionLevel,
                        PlayerCount = context.PlayerCount,
                        AncientAvailability = context.ResolveAncientAvailability(),
                        IncludeDarvSharedAncient = context.IncludeDarvSharedAncient
                    });

                    poolMatched = filter.PoolFilter.Matches(
                        poolAnalysis,
                        relicVisibility: null,
                        eventVisibilityAnalysis);
                }

                if (poolMatched && filter.PoolFilter.HighProbabilityRelicIds.Count > 0)
                {
                    poolMatched = _ancientPreviewer.MatchesHighProbabilityRelics(
                        _neowDataset,
                        new Sts2RelicVisibilityRequest
                        {
                            SeedText = context.SeedText,
                            SeedValue = context.RunSeed,
                            Character = context.Character,
                            UnlockedCharacters = context.UnlockedCharacters,
                            AscensionLevel = context.AscensionLevel,
                            PlayerCount = context.PlayerCount,
                            AncientAvailability = context.ResolveAncientAvailability(),
                            IncludeDarvSharedAncient = context.IncludeDarvSharedAncient
                        },
                        filter.PoolFilter);
                }
            }

            if (!poolMatched)
            {
                return CreateMatch(
                    neowOptions,
                    neowMatches,
                    neowMatched: true,
                    ancientMatched: true,
                    poolMatched: false,
                    shopMatched: true,
                    actPreview: actPreview,
                    poolAnalysis: poolAnalysis,
                    eventVisibilityAnalysis: eventVisibilityAnalysis);
            }
        }

        var shopMatched = true;
        ShopPreview? shopPreview = null;

        if (filter.ShopFilter.HasCriteria)
        {
            if (filter.ShopFilter.HasRouteCriteria)
            {
                if (_ancientPreviewer == null)
                {
                    shopMatched = false;
                }
                else
                {
                    var routeInfo = _ancientPreviewer.GetFirstShopRouteInfo(context);
                    shopMatched = filter.ShopFilter.MatchesRoute(routeInfo);
                }
            }

            if (shopMatched && filter.ShopFilter.HasInventoryCriteria)
            {
                if (_ancientPreviewer != null)
                {
                    var previewRequest = filter.ShopFilter.BuildPreviewRequest();
                    var filteredPreview = _ancientPreviewer.PreviewFirstShop(_neowDataset, context, neowOptions, previewRequest);
                    shopMatched = filter.ShopFilter.Matches(filteredPreview);

                    if (shopMatched)
                    {
                        shopPreview = previewRequest.IsFull
                            ? filteredPreview
                            : _ancientPreviewer.PreviewFirstShop(_neowDataset, context, neowOptions);
                    }
                }
                else
                {
                    // Fallback to the original direct shop simulation when act data is unavailable.
                    var netId = context.PlayerCount;
                    var baseSeed = unchecked((uint)GameRng.GetDeterministicHashCode(context.SeedText) + (uint)netId);
                    var rewardsHash = (uint)GameRng.GetDeterministicHashCode("rewards");
                    var shopsHash = (uint)GameRng.GetDeterministicHashCode("shops");
                    var rewardsSeed = unchecked(baseSeed + rewardsHash);
                    var shopsSeed = unchecked(baseSeed + shopsHash);

                    var shopSim = new Sts2ShopSimulator(_neowDataset, rewardsSeed, shopsSeed);
                    shopPreview = shopSim.Preview(context.Character);
                    shopMatched = filter.ShopFilter.Matches(shopPreview);
                }
            }

            if (!shopMatched)
            {
                return CreateMatch(
                    neowOptions,
                    neowMatches,
                    neowMatched: true,
                    ancientMatched: true,
                    poolMatched: true,
                    shopMatched: false,
                    actPreview: actPreview,
                    poolAnalysis: poolAnalysis,
                    shopPreview: shopPreview);
            }
        }

        if (_ancientPreviewer != null &&
            actPreview == null &&
            (context.IncludeAct2 || context.IncludeAct3))
        {
            actPreview = PreviewActs(context);
        }

        if (_ancientPreviewer != null &&
            filter.PoolFilter.HighProbabilityEventIds.Count > 0 &&
            eventVisibilityAnalysis == null)
        {
            eventVisibilityAnalysis = _ancientPreviewer.AnalyzeEventVisibility(_neowDataset, new Sts2EventVisibilityRequest
            {
                SeedText = context.SeedText,
                SeedValue = context.RunSeed,
                Character = context.Character,
                UnlockedCharacters = context.UnlockedCharacters,
                AscensionLevel = context.AscensionLevel,
                PlayerCount = context.PlayerCount,
                AncientAvailability = context.ResolveAncientAvailability(),
                IncludeDarvSharedAncient = context.IncludeDarvSharedAncient
            });
        }

        if (_ancientPreviewer != null &&
            filter.PoolFilter.HighProbabilityRelicIds.Count > 0)
        {
            relicVisibilityAnalysis = _ancientPreviewer.AnalyzeRelicVisibility(_neowDataset, new Sts2RelicVisibilityRequest
            {
                SeedText = context.SeedText,
                SeedValue = context.RunSeed,
                Character = context.Character,
                UnlockedCharacters = context.UnlockedCharacters,
                AscensionLevel = context.AscensionLevel,
                PlayerCount = context.PlayerCount,
                AncientAvailability = context.ResolveAncientAvailability(),
                IncludeDarvSharedAncient = context.IncludeDarvSharedAncient
            });
        }

        if (!filter.PoolFilter.Matches(poolAnalysis, relicVisibilityAnalysis, eventVisibilityAnalysis))
        {
            return CreateMatch(
                neowOptions,
                neowMatches,
                neowMatched: true,
                ancientMatched: true,
                poolMatched: false,
                shopMatched: true,
                actPreview: actPreview,
                shopPreview: shopPreview,
                poolAnalysis: poolAnalysis,
                eventVisibilityAnalysis: eventVisibilityAnalysis,
                relicVisibilityAnalysis: relicVisibilityAnalysis);
        }

        return CreateMatch(
            neowOptions,
            neowMatches,
            neowMatched: true,
            ancientMatched: true,
            poolMatched: true,
            shopMatched: true,
            actPreview: actPreview,
            shopPreview: shopPreview,
            poolAnalysis: poolAnalysis,
            eventVisibilityAnalysis: eventVisibilityAnalysis,
            relicVisibilityAnalysis: relicVisibilityAnalysis);
    }

    private Sts2RunPreview? PreviewActs(SeedRunEvaluationContext context)
    {
        if (_ancientPreviewer == null)
        {
            return null;
        }

        var request = new Sts2RunRequest
        {
            SeedValue = context.RunSeed,
            SeedText = context.SeedText,
            Character = context.Character,
            UnlockedCharacters = context.UnlockedCharacters,
            PlayerCount = context.PlayerCount,
            AscensionLevel = context.AscensionLevel,
            AncientAvailability = context.ResolveAncientAvailability(),
            IncludeDarvSharedAncient = context.IncludeDarvSharedAncient,
            IncludeAct2 = context.IncludeAct2,
            IncludeAct3 = context.IncludeAct3
        };

        return _ancientPreviewer.Preview(request);
    }

    private static SeedRunMatch CreateMatch(
        IReadOnlyList<NeowOptionResult> neowOptions,
        IReadOnlyList<NeowOptionResult> neowMatches,
        bool neowMatched,
        bool ancientMatched,
        bool poolMatched,
        bool shopMatched,
        Sts2RunPreview? actPreview = null,
        ShopPreview? shopPreview = null,
        Sts2SeedAnalysis? poolAnalysis = null,
        Sts2EventVisibilityAnalysis? eventVisibilityAnalysis = null,
        Sts2RelicVisibilityAnalysis? relicVisibilityAnalysis = null)
    {
        return new SeedRunMatch
        {
            NeowOptions = neowOptions,
            NeowMatches = neowMatches,
            NeowFilterMatched = neowMatched,
            AncientFilterMatched = ancientMatched,
            IsFinalMatch = neowMatched && ancientMatched && poolMatched && shopMatched,
            Sts2Preview = actPreview,
            ShopFilterMatched = shopMatched,
            ShopPreview = shopPreview,
            PoolFilterMatched = poolMatched,
            PoolAnalysis = poolAnalysis,
            EventVisibilityAnalysis = eventVisibilityAnalysis,
            RelicVisibilityAnalysis = relicVisibilityAnalysis
        };
    }
}
