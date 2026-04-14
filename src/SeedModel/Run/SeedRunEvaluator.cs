using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedModel.Run;

public sealed class SeedRunEvaluator
{
    private readonly NeowGenerator _neowGenerator;
    private readonly Sts2RunPreviewer? _ancientPreviewer;

    public SeedRunEvaluator(NeowOptionDataset neowDataset, Sts2RunPreviewer? ancientPreviewer = null)
    {
        _neowGenerator = new NeowGenerator(neowDataset ?? throw new ArgumentNullException(nameof(neowDataset)));
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

        return new SeedRunMatch
        {
            NeowOptions = neowOptions,
            NeowMatches = neowMatches,
            NeowFilterMatched = neowMatched,
            AncientFilterMatched = ancientMatched,
            IsFinalMatch = neowMatched && ancientMatched,
            Sts2Preview = actPreview
        };
    }
}
