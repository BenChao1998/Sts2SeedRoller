using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    public SeedRunMatch Evaluate(
        SeedRunEvaluationContext context,
        SeedRunFilter filter,
        CancellationToken cancellationToken = default,
        IProgress<Sts2ExactRouteProgress>? exactRouteProgress = null)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateMatch(
                Array.Empty<NeowOptionResult>(),
                Array.Empty<NeowOptionResult>(),
                neowMatched: false,
                ancientMatched: true,
                poolMatched: true,
                shopMatched: true,
                exactRouteMatched: false);
        }

        var neowContext = NeowGenerationContext.Create(
            context.RunSeed,
            playerCount: context.PlayerCount,
            scrollBoxesEligible: context.ScrollBoxesEligible,
            hasRunModifiers: context.HasRunModifiers,
            character: context.Character,
            ascensionLevel: context.AscensionLevel,
            playerNetId: context.PlayerNetId);

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
                actPreview = PreviewActs(context, filter);
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
                        TeamCharacters = context.TeamCharacters,
                        AscensionLevel = context.AscensionLevel,
                        PlayerCount = context.PlayerCount,
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
                        TeamCharacters = context.TeamCharacters,
                        AscensionLevel = context.AscensionLevel,
                        PlayerCount = context.PlayerCount,
                        PlayerNetId = context.PlayerNetId,
                        Samples = filter.PoolFilter.VisibilitySamples,
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
                            TeamCharacters = context.TeamCharacters,
                            AscensionLevel = context.AscensionLevel,
                            PlayerCount = context.PlayerCount,
                            PlayerNetId = context.PlayerNetId,
                            Samples = filter.PoolFilter.VisibilitySamples,
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
                    var baseSeed = unchecked((uint)GameRng.GetDeterministicHashCode(context.SeedText) + (uint)context.PlayerNetId);
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
            actPreview = PreviewActs(context, filter);
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
                TeamCharacters = context.TeamCharacters,
                AscensionLevel = context.AscensionLevel,
                PlayerCount = context.PlayerCount,
                PlayerNetId = context.PlayerNetId,
                Samples = filter.PoolFilter.VisibilitySamples,
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
                TeamCharacters = context.TeamCharacters,
                AscensionLevel = context.AscensionLevel,
                PlayerCount = context.PlayerCount,
                PlayerNetId = context.PlayerNetId,
                Samples = filter.PoolFilter.VisibilitySamples,
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

        var exactRouteMatched = true;
        Sts2ExactRouteAnalysis? exactRouteAnalysis = null;
        long exactRouteElapsedMilliseconds = 0;
        if (filter.ExactRouteFilter.HasCriteria)
        {
            if (_ancientPreviewer == null)
            {
                exactRouteMatched = false;
            }
            else
            {
                var exactRouteResult = RunExactRouteFilter(context, filter, neowMatches, cancellationToken, exactRouteProgress);
                exactRouteMatched = exactRouteResult.Matched;
                exactRouteAnalysis = exactRouteResult.Analysis;
                exactRouteElapsedMilliseconds = exactRouteResult.ElapsedMilliseconds;
            }

            if (!exactRouteMatched)
            {
                return CreateMatch(
                    neowOptions,
                    neowMatches,
                    neowMatched: true,
                    ancientMatched: true,
                    poolMatched: true,
                    shopMatched: true,
                    exactRouteMatched: false,
                    actPreview: actPreview,
                    shopPreview: shopPreview,
                    poolAnalysis: poolAnalysis,
                    eventVisibilityAnalysis: eventVisibilityAnalysis,
                    relicVisibilityAnalysis: relicVisibilityAnalysis,
                    exactRouteAnalysis: exactRouteAnalysis,
                    diagnostics: new SeedRunDiagnostics { ExactRouteElapsedMilliseconds = exactRouteElapsedMilliseconds });
            }
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
            relicVisibilityAnalysis: relicVisibilityAnalysis,
            exactRouteAnalysis: exactRouteAnalysis,
            diagnostics: filter.ExactRouteFilter.HasCriteria
                ? new SeedRunDiagnostics { ExactRouteElapsedMilliseconds = exactRouteElapsedMilliseconds }
                : null);
    }

    private ExactRouteFilterRunResult RunExactRouteFilter(
        SeedRunEvaluationContext context,
        SeedRunFilter filter,
        IReadOnlyList<NeowOptionResult> neowMatches,
        CancellationToken cancellationToken,
        IProgress<Sts2ExactRouteProgress>? exactRouteProgress)
    {
        if (_ancientPreviewer == null)
        {
            return new ExactRouteFilterRunResult(false, null, 0);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var totalMaxRouteChecks = Math.Max(1L, filter.ExactRouteFilter.MaxRouteChecks);
        var analyses = AnalyzeExactRoutesForOpeningOptions(
            context,
            neowMatches,
            filter.ExactRouteFilter.EventTargets,
            filter.ExactRouteFilter.RelicTargets,
            totalMaxRouteChecks,
            maxResults: 1,
            useDefaultRouteWhenNoTargets: true,
            forceCoverageOnly: true,
            filter.ExactRouteFilter.SimulationMode,
            filter.ExactRouteFilter.ShopOutputLimit,
            exactRouteProgress,
            cancellationToken);
        var matchedAnalysis = analyses.FirstOrDefault(analysis => MatchesCoverageTargets(
            analysis.Coverage,
            filter.ExactRouteFilter.EventTargets,
            filter.ExactRouteFilter.RelicTargets));
        var latestAnalysis = matchedAnalysis
            ?? (analyses.Count > 0 ? analyses[^1] : null);
        stopwatch.Stop();
        return new ExactRouteFilterRunResult(
            matchedAnalysis != null,
            latestAnalysis,
            stopwatch.ElapsedMilliseconds);
    }

    private IReadOnlyList<Sts2ExactRouteAnalysis> AnalyzeExactRoutesForOpeningOptions(
        SeedRunEvaluationContext context,
        IReadOnlyList<NeowOptionResult> openingOptions,
        IReadOnlyList<Sts2ExactRouteEventTargetRequest> eventTargets,
        IReadOnlyList<Sts2ExactRouteRelicTargetRequest> relicTargets,
        long totalMaxRouteChecks,
        int maxResults,
        bool useDefaultRouteWhenNoTargets,
        bool forceCoverageOnly,
        Sts2ExactRouteSimulationMode simulationMode,
        int shopOutputLimit,
        IProgress<Sts2ExactRouteProgress>? exactRouteProgress,
        CancellationToken cancellationToken)
    {
        if (_ancientPreviewer == null)
        {
            return Array.Empty<Sts2ExactRouteAnalysis>();
        }

        var normalizedOpeningOptions = openingOptions.Count == 0 ? [null] : openingOptions.Select(static option => (NeowOptionResult?)option).ToList();
        var hasTargets = eventTargets.Count > 0 || relicTargets.Count > 0;
        var coverageOnly = forceCoverageOnly || (useDefaultRouteWhenNoTargets && !hasTargets);
        var analyses = new List<Sts2ExactRouteAnalysis>(normalizedOpeningOptions.Count);
        long completedChecks = 0;

        for (var index = 0; index < normalizedOpeningOptions.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var allocatedRouteChecks = GetAllocatedRouteChecks(totalMaxRouteChecks, normalizedOpeningOptions.Count, index);
            if (allocatedRouteChecks <= 0)
            {
                continue;
            }

            var act1OpeningOption = normalizedOpeningOptions[index];
            var completedBeforeOpening = completedChecks;
            IProgress<Sts2ExactRouteProgress>? forwardedProgress = exactRouteProgress == null
                ? null
                : new Progress<Sts2ExactRouteProgress>(update =>
                    exactRouteProgress.Report(new Sts2ExactRouteProgress(
                        StartedRoutes: (int)Math.Min(int.MaxValue, completedBeforeOpening + update.StartedRoutes),
                        CheckedRoutes: (int)Math.Min(int.MaxValue, completedBeforeOpening + update.CheckedRoutes),
                        FoundRoutes: analyses.Sum(result => result.FoundRouteCount) + update.FoundRoutes)));

            var analysis = _ancientPreviewer.AnalyzeExactRoutes(_neowDataset, new Sts2ExactRouteAnalysisRequest
            {
                SeedText = context.SeedText,
                SeedValue = context.RunSeed,
                Character = context.Character,
                UnlockedCharacters = context.UnlockedCharacters,
                TeamCharacters = context.TeamCharacters,
                AscensionLevel = context.AscensionLevel,
                PlayerCount = context.PlayerCount,
                PlayerNetId = context.PlayerNetId,
                AncientAvailability = context.ResolveAncientAvailability(),
                IncludeDarvSharedAncient = context.IncludeDarvSharedAncient,
                Act1OpeningOption = act1OpeningOption,
                EventTargets = eventTargets,
                RelicTargets = relicTargets,
                ShopStrategy = coverageOnly ? Sts2ExactRouteShopStrategy.NoPurchase : Sts2ExactRouteShopStrategy.TargetRelicsOnly,
                SimulationMode = coverageOnly ? Sts2ExactRouteSimulationMode.FastShopLimited : simulationMode,
                ShopOutputLimit = coverageOnly ? 0 : Math.Max(0, shopOutputLimit),
                MaxResults = Math.Max(1, maxResults),
                MaxRouteChecks = allocatedRouteChecks,
                Progress = forwardedProgress,
                CancellationToken = cancellationToken
            });
            analyses.Add(analysis);
            completedChecks += Math.Max(0, analysis.CheckedRoutes);
        }

        return analyses;
    }

    private static long GetAllocatedRouteChecks(long totalMaxRouteChecks, int openingCount, int openingIndex)
    {
        if (totalMaxRouteChecks <= 0 || openingCount <= 0 || openingIndex < 0 || openingIndex >= openingCount)
        {
            return 0;
        }

        var baseAllocation = totalMaxRouteChecks / openingCount;
        var remainder = totalMaxRouteChecks % openingCount;
        return baseAllocation + (openingIndex < remainder ? 1 : 0);
    }

    private static bool MatchesCoverageTargets(
        Sts2ExactRouteCoverage coverage,
        IReadOnlyList<Sts2ExactRouteEventTargetRequest> eventTargets,
        IReadOnlyList<Sts2ExactRouteRelicTargetRequest> relicTargets)
    {
        foreach (var target in eventTargets)
        {
            var matched = coverage.EventCoverage.Any(item =>
                (!target.ActNumber.HasValue || item.ActNumber == target.ActNumber.Value) &&
                string.Equals(item.Id, target.EventId, StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                return false;
            }
        }

        foreach (var target in relicTargets)
        {
            var matched = coverage.RelicCoverage.Any(item =>
                (!target.ActNumber.HasValue || item.ActNumber == target.ActNumber.Value) &&
                string.Equals(item.Id, target.RelicId, StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private Sts2RunPreview? PreviewActs(SeedRunEvaluationContext context, SeedRunFilter filter)
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
            TeamCharacters = context.TeamCharacters,
            PlayerCount = context.PlayerCount,
            PlayerNetId = context.PlayerNetId,
            AscensionLevel = context.AscensionLevel,
            AncientAvailability = context.ResolveAncientAvailability(),
            IncludeDarvSharedAncient = context.IncludeDarvSharedAncient,
            IncludeAct2 = context.IncludeAct2,
            IncludeAct3 = context.IncludeAct3,
            SeaGlassPreviewSamples = filter.AncientFilter.Act2SeaGlassCardIds.Count > 0
                ? filter.AncientFilter.SeaGlassPreviewSamples
                : null
        };

        return _ancientPreviewer.Preview(request, _neowDataset);
    }

    private static SeedRunMatch CreateMatch(
        IReadOnlyList<NeowOptionResult> neowOptions,
        IReadOnlyList<NeowOptionResult> neowMatches,
        bool neowMatched,
        bool ancientMatched,
        bool poolMatched,
        bool shopMatched,
        bool exactRouteMatched = true,
        Sts2RunPreview? actPreview = null,
        ShopPreview? shopPreview = null,
        Sts2SeedAnalysis? poolAnalysis = null,
        Sts2EventVisibilityAnalysis? eventVisibilityAnalysis = null,
        Sts2RelicVisibilityAnalysis? relicVisibilityAnalysis = null,
        Sts2ExactRouteAnalysis? exactRouteAnalysis = null,
        SeedRunDiagnostics? diagnostics = null)
    {
        return new SeedRunMatch
        {
            NeowOptions = neowOptions,
            NeowMatches = neowMatches,
            NeowFilterMatched = neowMatched,
            AncientFilterMatched = ancientMatched,
            IsFinalMatch = neowMatched && ancientMatched && poolMatched && shopMatched && exactRouteMatched,
            Sts2Preview = actPreview,
            ShopFilterMatched = shopMatched,
            ShopPreview = shopPreview,
            PoolFilterMatched = poolMatched,
            PoolAnalysis = poolAnalysis,
            EventVisibilityAnalysis = eventVisibilityAnalysis,
            RelicVisibilityAnalysis = relicVisibilityAnalysis,
            ExactRouteFilterMatched = exactRouteMatched,
            ExactRouteAnalysis = exactRouteAnalysis,
            Diagnostics = diagnostics
        };
    }

    private sealed record ExactRouteFilterRunResult(
        bool Matched,
        Sts2ExactRouteAnalysis? Analysis,
        long ElapsedMilliseconds);
}
