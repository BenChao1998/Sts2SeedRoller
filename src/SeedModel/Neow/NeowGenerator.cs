using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Collections;
using SeedModel.Events;
using SeedModel.Rng;

namespace SeedModel.Neow;

public sealed class NeowGenerator : ISeedEventGenerator<NeowGenerationContext, IReadOnlyList<NeowOptionResult>>
{
    private const string PositivePoolName = "Positive";
    private const string NegativePoolName = "Negative";

    private static readonly string[] BasePositiveOptions =
    [
        NeowOptionIds.ArcaneScroll,
        NeowOptionIds.BoomingConch,
        NeowOptionIds.Pomander,
        NeowOptionIds.GoldenPearl,
        NeowOptionIds.LeadPaperweight,
        NeowOptionIds.NewLeaf,
        NeowOptionIds.NeowsTorment,
        NeowOptionIds.PreciseScissors,
        NeowOptionIds.LostCoffer
    ];

    private static readonly string[] BaseNegativeOptions =
    [
        NeowOptionIds.CursedPearl,
        NeowOptionIds.LargeCapsule,
        NeowOptionIds.LeafyPoultice,
        NeowOptionIds.PrecariousShears
    ];

    private readonly NeowOptionDataset _dataset;
    private readonly NeowRewardPreviewer _rewardPreviewer;

    public SeedEventType EventType => SeedEventType.Act1Neow;

    public NeowGenerator(NeowOptionDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _rewardPreviewer = new NeowRewardPreviewer(dataset);
    }

    public IReadOnlyList<NeowOptionResult> Generate(NeowGenerationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.HasRunModifiers)
        {
            throw new NotSupportedException("Modifier-based Neow options are not implemented yet.");
        }

        var rng = new GameRng(context.Seed);
        var negativePool = BuildNegativePool(context);
        var negativePick = rng.NextItem(negativePool)
            ?? throw new InvalidOperationException("Neow negative pool is empty.");

        var positivePool = BuildPositivePool(context, negativePick, rng);
        if (positivePool.Count < 2)
        {
            throw new InvalidOperationException("Neow positive pool does not contain enough entries.");
        }

        var positives = positivePool.UnstableShuffle(rng)
            .Take(2)
            .ToList();

        var results = positives.Select(id => ToResult(id, PositivePoolName, context)).ToList();
        results.Add(ToResult(negativePick, NegativePoolName, context));
        return results;
    }

    private List<string> BuildNegativePool(NeowGenerationContext context)
    {
        var pool = new List<string>(BaseNegativeOptions);
        if (context.ScrollBoxesEligible)
        {
            pool.Add(NeowOptionIds.ScrollBoxes);
        }

        if (context.PlayerCount == 1)
        {
            pool.Add(NeowOptionIds.SilverCrucible);
        }

        return pool;
    }

    private List<string> BuildPositivePool(NeowGenerationContext context, string negativePick, GameRng rng)
    {
        var pool = new List<string>(BasePositiveOptions);

        if (string.Equals(negativePick, NeowOptionIds.CursedPearl, StringComparison.OrdinalIgnoreCase))
        {
            pool.Remove(NeowOptionIds.GoldenPearl);
        }

        if (string.Equals(negativePick, NeowOptionIds.PrecariousShears, StringComparison.OrdinalIgnoreCase))
        {
            pool.Remove(NeowOptionIds.PreciseScissors);
        }

        if (string.Equals(negativePick, NeowOptionIds.LeafyPoultice, StringComparison.OrdinalIgnoreCase))
        {
            pool.Remove(NeowOptionIds.NewLeaf);
        }

        if (context.PlayerCount > 1)
        {
            pool.Add(NeowOptionIds.MassiveScroll);
        }

        pool.Add(rng.NextBool() ? NeowOptionIds.NutritiousOyster : NeowOptionIds.StoneHumidifier);

        if (!string.Equals(negativePick, NeowOptionIds.LargeCapsule, StringComparison.OrdinalIgnoreCase))
        {
            pool.Add(rng.NextBool() ? NeowOptionIds.LavaRock : NeowOptionIds.SmallCapsule);
        }

        return pool;
    }

    private NeowOptionResult ToResult(string optionId, string pool, NeowGenerationContext context)
    {
        if (!_dataset.OptionMap.TryGetValue(optionId, out var metadata))
        {
            metadata = new NeowOptionMetadata
            {
                Id = optionId,
                RelicId = optionId,
                Kind = pool == NegativePoolName ? NeowOptionKind.Negative : NeowOptionKind.Positive,
                Title = optionId,
                Description = "No localized description available in dataset."
            };
        }

        var details = _rewardPreviewer.Build(metadata.RelicId, context);

        return new NeowOptionResult
        {
            Id = metadata.Id,
            RelicId = metadata.RelicId,
            Kind = metadata.Kind,
            Pool = pool,
            Title = metadata.Title ?? metadata.Id,
            Description = metadata.Description,
            Note = metadata.Note,
            Details = details
        };
    }
}
