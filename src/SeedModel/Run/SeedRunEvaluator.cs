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
        var ancientMatched = true;

        Sts2RunPreview? actPreview = null;
        if (_ancientPreviewer != null && (context.IncludeAct2 || context.IncludeAct3))
        {
            var request = new Sts2RunRequest
            {
                SeedValue = context.RunSeed,
                SeedText = context.SeedText,
                Character = context.Character,
                PlayerCount = context.PlayerCount,
                AscensionLevel = context.AscensionLevel,
                IncludeAct2 = context.IncludeAct2,
                IncludeAct3 = context.IncludeAct3
            };

            actPreview = _ancientPreviewer.Preview(request);
        }

        if (filter.AncientFilter.HasCriteria)
        {
            ancientMatched = filter.AncientFilter.Matches(actPreview);
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
        }

        return new SeedRunMatch
        {
            NeowOptions = neowOptions,
            NeowMatches = neowMatches,
            NeowFilterMatched = neowMatched,
            AncientFilterMatched = ancientMatched,
            IsFinalMatch = neowMatched && ancientMatched && shopMatched,
            Sts2Preview = actPreview,
            ShopFilterMatched = shopMatched,
            ShopPreview = shopPreview
        };
    }
}
