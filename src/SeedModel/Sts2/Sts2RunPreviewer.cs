using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;
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
    private readonly Sts2WorldData _world;
    private readonly Sts2RunSimulator _simulator;
    private readonly Sts2RelicShufflePrimer _primer;

    internal Sts2RunPreviewer(AncientOptionCatalog catalog, Sts2WorldData world)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _simulator = new Sts2RunSimulator(_world);
        _primer = new Sts2RelicShufflePrimer(_world);
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

        using var stream = File.OpenRead(optionDataPath);
        var catalog = AncientOptionCatalog.Load(stream);
        var world = Sts2WorldData.LoadFromFile(actDataPath);
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

        var upFrontRng = new GameRng(request.SeedValue, "up_front");
        _primer.Prime(upFrontRng, request.Character, request.PlayerCount);

        var generationContext = new AncientGenerationContext(
            request.SeedValue,
            request.Character,
            ActIndex: 0,
            DefaultUnlockedCharacters);

        var actResults = _simulator.Simulate(upFrontRng);
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
}
