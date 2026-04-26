using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Run;
using SeedModel.Sts2.Ancients;
using SeedModel.Sts2.Generation;

namespace SeedModel.Sts2;

public sealed class Sts2RunPreviewer
{
    private const uint DefaultPlayerNetId = 1;
    private static readonly CharacterId[] DefaultUnlockedCharacters =
    [
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    ];

    private static readonly IReadOnlyDictionary<string, AncientEventLogic> EventLogics =
        new Dictionary<string, AncientEventLogic>(StringComparer.OrdinalIgnoreCase)
        {
            ["OROBAS"] = new OrobasEventLogic(),
            ["PAEL"] = new PaelEventLogic(),
            ["TEZCATARA"] = new TezcataraEventLogic(),
            ["NONUPEIPE"] = new NonupeipeEventLogic(),
            ["TANX"] = new TanxEventLogic(),
            ["VAKUU"] = new VakuuEventLogic(),
            ["DARV"] = new DarvEventLogic()
        };

    private readonly AncientOptionCatalog _catalog;
    private readonly HashSet<string> _ancientOptionIds;
    private readonly Sts2WorldData _world;
    private readonly string? _workspaceRoot;
    private readonly Sts2RunSimulator _simulator;
    private readonly Sts2RelicShufflePrimer _primer;
    private readonly Sts2RelicVisibilityAnalyzer _relicVisibilityAnalyzer;
    private readonly Sts2EventVisibilityAnalyzer _eventVisibilityAnalyzer;

    internal Sts2RunPreviewer(AncientOptionCatalog catalog, Sts2WorldData world, string? workspaceRoot = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _ancientOptionIds = _catalog.OptionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _workspaceRoot = workspaceRoot;
        _simulator = new Sts2RunSimulator(_world);
        _primer = new Sts2RelicShufflePrimer(_world);
        _relicVisibilityAnalyzer = new Sts2RelicVisibilityAnalyzer(_world);
        _eventVisibilityAnalyzer = new Sts2EventVisibilityAnalyzer(_workspaceRoot);
    }

    public static Sts2RunPreviewer CreateFromDataFiles(string optionDataPath, string actDataPath)
    {
        if (string.IsNullOrWhiteSpace(optionDataPath))
        {
            throw new ArgumentException("Ancient option data path is required.", nameof(optionDataPath));
        }

        if (!File.Exists(optionDataPath))
        {
            throw new FileNotFoundException("Ancient option data file not found.", optionDataPath);
        }

        using var optionStream = File.OpenRead(optionDataPath);
        using var actStream = File.OpenRead(actDataPath);
        return new Sts2RunPreviewer(
            AncientOptionCatalog.Load(optionStream),
            Sts2WorldData.Load(actStream),
            ResolveWorkspaceRoot(optionDataPath, actDataPath));
    }

    public static Sts2RunPreviewer Create(Stream optionDataStream, Stream actDataStream)
    {
        var catalog = AncientOptionCatalog.Load(optionDataStream);
        var world = Sts2WorldData.Load(actDataStream);
        return new Sts2RunPreviewer(catalog, world);
    }

    public Sts2RunPreview Preview(Sts2RunRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var preview = new Sts2RunPreview
        {
            Seed = request.SeedValue,
            SeedText = request.SeedText
        };

        if (!request.IncludeAct2 && !request.IncludeAct3)
        {
            return preview;
        }

        var unlockedCharacters = ResolveUnlockedCharacters(request.Character, request.UnlockedCharacters);
        var ancientAvailability = request.ResolveAncientAvailability();
        var upFrontRng = new GameRng(request.SeedValue, "up_front");
        _primer.Prime(upFrontRng, request.Character, request.PlayerCount, ancientAvailability);

        var generationContext = new AncientGenerationContext(
            request.SeedValue,
            request.Character,
            ActIndex: 0,
            unlockedCharacters);

        var actResults = _simulator.Simulate(
            upFrontRng,
            request.SeedValue,
            ancientAvailability);
        foreach (var result in actResults)
        {
            var shouldInclude = (result.ActNumber == 2 && request.IncludeAct2) ||
                                (result.ActNumber == 3 && request.IncludeAct3);

            if (!shouldInclude)
            {
                continue;
            }

            if (!EventLogics.TryGetValue(result.AncientId, out var logic))
            {
                continue;
            }

            var eventRngSeed = unchecked(request.SeedValue + DefaultPlayerNetId);
            var eventRng = new GameRng(eventRngSeed, logic.Id);
            var context = generationContext with { ActIndex = result.ActIndex };
            var optionResults = logic.GenerateOptions(context, eventRng);

            var actPreview = new Sts2ActPreview
            {
                ActNumber = result.ActNumber,
                AncientId = logic.Id,
                AncientName = logic.Name
            };

            foreach (var option in optionResults)
            {
                var metadata = _catalog.Get(option.OptionId);
                actPreview.AncientOptions.Add(new Sts2AncientOption
                {
                    OptionId = metadata.Id,
                    Title = metadata.Title,
                    Description = metadata.Description,
                    RelicId = metadata.Id,
                    Note = option.Note
                });
            }

            preview.Acts.Add(actPreview);
        }

        return preview;
    }

    public Sts2SeedAnalysis AnalyzePools(Sts2SeedAnalysisRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var ancientAvailability = request.ResolveAncientAvailability();
        var upFrontRng = new GameRng(request.SeedValue, "up_front");
        var relicPools = _primer.PrimeAndCapture(upFrontRng, request.Character, playerCount: 1, ancientAvailability);
        var actPools = _simulator.Analyze(
            upFrontRng,
            request.SeedValue,
            ancientAvailability);

        return new Sts2SeedAnalysis
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            SharedRelicPools = relicPools.SharedPools,
            PlayerRelicPools = relicPools.PlayerPools,
            Acts = actPools
                .Select(act => new Sts2ActPoolPreview
                {
                    ActNumber = act.ActNumber,
                    ActName = act.ActName,
                    PriorityEventCount = Math.Min(act.EventPreviewLimit, act.Events.Count),
                    TotalEventCount = act.Events.Count,
                    EventPool = act.Events
                        .Take(act.EventPreviewLimit)
                        .ToList(),
                    FullEventPool = act.Events
                        .ToList(),
                    MonsterPool = act.NormalEncounters,
                    ElitePool = act.EliteEncounters
                })
                .ToList()
        };
    }

    public Sts2RelicVisibilityAnalysis AnalyzeRelicVisibility(NeowOptionDataset dataset, Sts2RelicVisibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var (ancientActs, rarityMap) = BuildRelicVisibilityInputs(dataset, request);

        return _relicVisibilityAnalyzer.Analyze(dataset, request, ancientActs, rarityMap);
    }

    public Sts2EventVisibilityAnalysis AnalyzeEventVisibility(NeowOptionDataset dataset, Sts2EventVisibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var ancientAvailability = request.ResolveAncientAvailability();
        var upFrontRng = new GameRng(request.SeedValue, "up_front");
        _primer.Prime(upFrontRng, request.Character, request.PlayerCount, ancientAvailability);

        var actPools = _simulator.Analyze(
            upFrontRng,
            request.SeedValue,
            ancientAvailability);

        var ancientPreview = Preview(new Sts2RunRequest
        {
            SeedText = request.SeedText,
            SeedValue = request.SeedValue,
            Character = request.Character,
            UnlockedCharacters = ResolveUnlockedCharacters(request.Character, request.UnlockedCharacters),
            AscensionLevel = request.AscensionLevel,
            PlayerCount = request.PlayerCount,
            AncientAvailability = ancientAvailability,
            IncludeDarvSharedAncient = request.IncludeDarvSharedAncient,
            IncludeAct2 = true,
            IncludeAct3 = true
        });

        return _eventVisibilityAnalyzer.Analyze(request, dataset, actPools, ancientPreview);
    }

    internal bool MatchesHighProbabilityRelics(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        Sts2PoolFilter filter)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(filter);

        var requiredRelicIds = filter.HighProbabilityRelicIds;
        var needsAncientPreview = requiredRelicIds.Any(relicId => _ancientOptionIds.Contains(relicId));
        var (ancientActs, rarityMap) = BuildRelicVisibilityInputs(dataset, request, includeAncientPreview: needsAncientPreview);
        return _relicVisibilityAnalyzer.MatchesHighProbabilityRelics(
            dataset,
            request,
            ancientActs,
            rarityMap,
            filter);
    }

    public ShopPreview PreviewFirstShop(
        NeowOptionDataset dataset,
        SeedRunEvaluationContext context,
        IReadOnlyList<NeowOptionResult> neowOptions)
    {
        return PreviewFirstShop(dataset, context, neowOptions, ShopPreviewRequest.Full);
    }

    internal ShopPreview PreviewFirstShop(
        NeowOptionDataset dataset,
        SeedRunEvaluationContext context,
        IReadOnlyList<NeowOptionResult> neowOptions,
        ShopPreviewRequest request)
    {
        var previewer = new Sts2StandardShopPreviewer(dataset, _world);
        return previewer.PreviewFirstShop(context, neowOptions, request);
    }

    internal FirstShopRouteInfo? GetFirstShopRouteInfo(SeedRunEvaluationContext context)
    {
        return Sts2StandardShopPreviewer.GetFirstShopRouteInfo(_world, context);
    }

    private static IReadOnlyList<CharacterId> ResolveUnlockedCharacters(
        CharacterId selectedCharacter,
        IReadOnlyList<CharacterId>? configuredCharacters)
    {
        var source = configuredCharacters == null || configuredCharacters.Count == 0
            ? DefaultUnlockedCharacters
            : configuredCharacters;

        var unlocked = source
            .Where(id => Enum.IsDefined(typeof(CharacterId), id))
            .Distinct()
            .ToList();

        if (!unlocked.Contains(CharacterId.Ironclad))
        {
            unlocked.Insert(0, CharacterId.Ironclad);
        }

        if (!unlocked.Contains(selectedCharacter))
        {
            unlocked.Add(selectedCharacter);
        }

        return unlocked;
    }

    private (IReadOnlyList<Sts2RelicVisibilityAncientAct> AncientActs, IReadOnlyDictionary<string, string> RarityMap) BuildRelicVisibilityInputs(
        NeowOptionDataset dataset,
        Sts2RelicVisibilityRequest request,
        bool includeAncientPreview = true)
    {
        IReadOnlyList<Sts2RelicVisibilityAncientAct> ancientActs = Array.Empty<Sts2RelicVisibilityAncientAct>();
        if (includeAncientPreview)
        {
            var ancientAvailability = request.ResolveAncientAvailability();
            var ancientPreview = Preview(new Sts2RunRequest
            {
                SeedText = request.SeedText,
                SeedValue = request.SeedValue,
                Character = request.Character,
                UnlockedCharacters = ResolveUnlockedCharacters(request.Character, request.UnlockedCharacters),
                AscensionLevel = request.AscensionLevel,
                PlayerCount = request.PlayerCount,
                AncientAvailability = ancientAvailability,
                IncludeDarvSharedAncient = request.IncludeDarvSharedAncient,
                IncludeAct2 = true,
                IncludeAct3 = true
            });

            ancientActs = ancientPreview.Acts
                .Select(act => new Sts2RelicVisibilityAncientAct
                {
                    ActNumber = act.ActNumber,
                    AncientId = act.AncientId ?? string.Empty,
                    AncientName = act.AncientName ?? act.AncientId ?? string.Empty,
                    Options = act.AncientOptions
                        .Where(option => !string.IsNullOrWhiteSpace(option.RelicId))
                        .Select(option => new Sts2RelicVisibilityAncientOption
                        {
                            RelicId = option.RelicId ?? string.Empty,
                            Title = option.Title ?? option.RelicId ?? string.Empty,
                            Description = option.Description,
                            Note = option.Note
                        })
                        .ToList()
                })
                .ToList();
        }

        var rarityMap = dataset.RelicMetadataMap.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Rarity,
            StringComparer.OrdinalIgnoreCase);

        return (ancientActs, rarityMap);
    }

    private static string? ResolveWorkspaceRoot(string optionDataPath, string actDataPath)
    {
        foreach (var candidatePath in new[] { actDataPath, optionDataPath })
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            var directory = new DirectoryInfo(Path.GetDirectoryName(candidatePath)!);
            while (directory != null)
            {
                var hasData = Directory.Exists(Path.Combine(directory.FullName, "data"));
                var hasSrc = Directory.Exists(Path.Combine(directory.FullName, "src"));
                if (hasData && hasSrc)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
