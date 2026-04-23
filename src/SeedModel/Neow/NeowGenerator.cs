using System;
using System.Collections.Generic;
using System.Linq;
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

    private static readonly string[] ModernPositiveOptions =
    [
        NeowOptionIds.ArcaneScroll,
        NeowOptionIds.BoomingConch,
        NeowOptionIds.GoldenPearl,
        NeowOptionIds.LeadPaperweight,
        NeowOptionIds.LostCoffer,
        NeowOptionIds.NeowsTorment,
        NeowOptionIds.NewLeaf,
        NeowOptionIds.PreciseScissors,
        NeowOptionIds.PhialHolster,
        NeowOptionIds.WingedBoots,
        NeowOptionIds.MassiveScroll
    ];

    private static readonly string[] ModernNegativeOptions =
    [
        NeowOptionIds.CursedPearl,
        NeowOptionIds.HeftyTablet,
        NeowOptionIds.LargeCapsule,
        NeowOptionIds.LeafyPoultice,
        NeowOptionIds.PrecariousShears,
        NeowOptionIds.SilverCrucible,
        NeowOptionIds.NeowsBones
    ];

    private readonly NeowOptionDataset _dataset;
    private readonly NeowRewardPreviewer _rewardPreviewer;

    public SeedEventType EventType => SeedEventType.Act1Neow;

    private bool UsesModernRules => string.Equals(_dataset.Version, "0.103.2", StringComparison.OrdinalIgnoreCase);

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

        var positivePickA = rng.NextInt(positivePool.Count);
        var positivePickB = rng.NextInt(positivePool.Count - 1);
        if (positivePickB >= positivePickA)
        {
            positivePickB++;
        }

        var results = new List<NeowOptionResult>(3)
        {
            ToResult(positivePool[positivePickA], PositivePoolName, context),
            ToResult(positivePool[positivePickB], PositivePoolName, context)
        };
        results.Add(ToResult(negativePick, NegativePoolName, context));
        return results;
    }

    private List<string> BuildNegativePool(NeowGenerationContext context)
    {
        if (UsesModernRules)
        {
            var modernPool = new List<string>(ModernNegativeOptions);
            if (context.ScrollBoxesEligible)
            {
                modernPool.Add(NeowOptionIds.ScrollBoxes);
            }

            if (context.PlayerCount > 1)
            {
                modernPool.Remove(NeowOptionIds.SilverCrucible);
            }

            return modernPool;
        }

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
        if (UsesModernRules)
        {
            var modernPool = new List<string>(ModernPositiveOptions);

            if (context.PlayerCount <= 1)
            {
                modernPool.Remove(NeowOptionIds.MassiveScroll);
            }

            if (string.Equals(negativePick, NeowOptionIds.CursedPearl, StringComparison.OrdinalIgnoreCase))
            {
                modernPool.Remove(NeowOptionIds.GoldenPearl);
            }

            if (string.Equals(negativePick, NeowOptionIds.HeftyTablet, StringComparison.OrdinalIgnoreCase))
            {
                modernPool.Remove(NeowOptionIds.ArcaneScroll);
            }

            if (string.Equals(negativePick, NeowOptionIds.LeafyPoultice, StringComparison.OrdinalIgnoreCase))
            {
                modernPool.Remove(NeowOptionIds.NewLeaf);
            }

            if (string.Equals(negativePick, NeowOptionIds.PrecariousShears, StringComparison.OrdinalIgnoreCase))
            {
                modernPool.Remove(NeowOptionIds.PreciseScissors);
            }

            modernPool.Add(rng.NextBool() ? NeowOptionIds.NutritiousOyster : NeowOptionIds.StoneHumidifier);

            if (!string.Equals(negativePick, NeowOptionIds.LargeCapsule, StringComparison.OrdinalIgnoreCase))
            {
                modernPool.Add(rng.NextBool() ? NeowOptionIds.LavaRock : NeowOptionIds.SmallCapsule);
            }

            modernPool.Add(rng.NextBool() ? NeowOptionIds.NeowsTalisman : NeowOptionIds.Pomander);
            return modernPool;
        }

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

        var detailHint = NeowRewardPreviewer.GetDetailHint(metadata.RelicId);
        IReadOnlyList<RewardDetail> details = detailHint == NeowDetailHint.None
            ? Array.Empty<RewardDetail>()
            : new DeferredRewardDetails(() => _rewardPreviewer.Build(metadata.RelicId, context));

        return new NeowOptionResult
        {
            Id = metadata.Id,
            RelicId = metadata.RelicId,
            Kind = metadata.Kind,
            Pool = pool,
            Title = metadata.Title ?? metadata.Id,
            Description = metadata.Description,
            Note = metadata.Note,
            Details = details,
            DetailHint = detailHint
        };
    }
}
