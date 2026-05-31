using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

namespace SeedModel.Sts2.RunValidation;

internal static class BatchReplayScorer
{
    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var inputPath = args.Count > 0 ? args[0] : "scratch";
            var reportPath = args.Count > 1
                ? args[1]
                : Path.Combine("scratch", "full-run-reward-alignment-batch.md");
            var runPaths = ResolveRunPaths(inputPath);
            if (runPaths.Count == 0)
            {
                throw new InvalidOperationException($"No sts2log run JSON files found at {inputPath}");
            }

            var results = new List<BatchRunScore>();
            foreach (var runPath in runPaths)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(runPath));
                var replay = Sts2LogReplayParser.Parse(document.RootElement);
                var comparisons = GeneratedRunComparer.Compare(replay);
                results.Add(BatchRunScore.From(replay, runPath, comparisons));
            }

            var report = Format(results);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".");
            File.WriteAllText(reportPath, report);
            Console.WriteLine(report);
            Console.WriteLine($"Batch report written: {reportPath}");
            return results.All(result => result.ItemScore.Rate >= 0.95) ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"batch replay scorer failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<string> ResolveRunPaths(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return [inputPath];
        }

        if (!Directory.Exists(inputPath))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(inputPath, "sts2log-run-*.json")
            .Where(path => !Path.GetFileName(path).Contains("oracle-summary", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Format(IReadOnlyList<BatchRunScore> results)
    {
        var total = BatchScore.Sum(results.Select(result => result.ItemScore));
        var card = BatchScore.Sum(results.Select(result => result.CardScore));
        var relic = BatchScore.Sum(results.Select(result => result.RelicScore));
        var shopRelic = BatchScore.Sum(results.Select(result => result.ShopRelicScore));
        var shopPotion = BatchScore.Sum(results.Select(result => result.ShopPotionScore));
        var shopCard = BatchScore.Sum(results.Select(result => result.ShopCardScore));
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Full Run Reward Alignment Batch");
        builder.AppendLine();
        builder.AppendLine($"- runs: {results.Count}");
        builder.AppendLine($"- item_score: {total.Matched}/{total.Total} ({total.Percent})");
        builder.AppendLine($"- target_95_met: {(total.Rate >= 0.95 ? "yes" : "no")}");
        builder.AppendLine($"- card_score: {card.Matched}/{card.Total} ({card.Percent})");
        builder.AppendLine($"- relic_score: {relic.Matched}/{relic.Total} ({relic.Percent})");
        builder.AppendLine($"- shop_card_score: {shopCard.Matched}/{shopCard.Total} ({shopCard.Percent})");
        builder.AppendLine($"- shop_relic_score: {shopRelic.Matched}/{shopRelic.Total} ({shopRelic.Percent})");
        builder.AppendLine($"- shop_potion_score: {shopPotion.Matched}/{shopPotion.Total} ({shopPotion.Percent})");
        builder.AppendLine();
        builder.AppendLine("| Run | Character | Asc | Floors | Item score | Cards | Relics | Shop cards | Shop relics | Shop potions | First mismatch |");
        builder.AppendLine("| ---: | --- | ---: | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var result in results)
        {
            builder.AppendLine($"| {result.RunId} | {result.CharacterId} | {result.Ascension} | {result.Floors} | {result.ItemScore} | {result.CardScore} | {result.RelicScore} | {result.ShopCardScore} | {result.ShopRelicScore} | {result.ShopPotionScore} | {result.FirstMismatch} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Worst Mismatch Categories");
        builder.AppendLine();
        foreach (var category in new[]
                 {
                     ("cards", card),
                     ("relics", relic),
                     ("shop cards", shopCard),
                     ("shop relics", shopRelic),
                     ("shop potions", shopPotion)
                 }.Where(item => item.Item2.Total > 0).OrderBy(item => item.Item2.Rate))
        {
            builder.AppendLine($"- {category.Item1}: {category.Item2.Matched}/{category.Item2.Total} ({category.Item2.Percent})");
        }

        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        builder.AppendLine();
        foreach (var diagnostic in BatchDiagnostics.From(results))
        {
            builder.AppendLine($"- {diagnostic}");
        }

        return builder.ToString();
    }
}

internal static class UnknownRoomTraceProbe
{
    public static IReadOnlyDictionary<int, StandardRoomType> GetPredictedOutcomesByFloor(ReplayRun replay)
    {
        return PredictUnknownRoomOutcomes(replay)
            .GroupBy(item => item.Floor)
            .ToDictionary(
                group => group.Key,
                group => Enum.TryParse<StandardRoomType>(group.First().PredictedRoomType, ignoreCase: true, out var parsed)
                    ? parsed
                    : StandardRoomType.Event);
    }

    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var runJsonPath = args.Count > 0 ? args[0] : Path.Combine("scratch", "sts2log-run-4903362.json");
            using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath));
            var replay = Sts2LogReplayParser.Parse(document.RootElement);
            var predicted = PredictUnknownRoomOutcomes(replay);

            Console.WriteLine($"run={replay.RunId} seed={replay.SeedText} character={replay.CharacterId} asc={replay.Ascension}");
            foreach (var item in predicted)
            {
                Console.WriteLine(
                    $"floor={item.Floor} room=V predicted={item.PredictedRoomType} hasCombat={item.HasCombat} " +
                    $"hasShopActions={(item.ShopActions.Count > 0 ? 1 : 0)} relics={Join(item.PickedRelics)} cards={Join(item.PickedCards)} text={item.EventText ?? ""}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"trace-unknowns failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<UnknownRoomTraceRow> PredictUnknownRoomOutcomes(ReplayRun replay)
    {
        if (replay.MapActs.Count == 0)
        {
            return Array.Empty<UnknownRoomTraceRow>();
        }

        var rows = new List<UnknownRoomTraceRow>();
        var local = LocalReplayPredictor.Build(replay);
        var unknownOdds = new UnknownRoomOdds(new GameRng(SeedFormatter.ToUIntSeed(replay.SeedText), "unknown_map_point"));
        StandardRoomType? previousRoomType = null;

        foreach (var act in replay.MapActs.OrderBy(act => act.ActNumber))
        {
            var localActNumber = act.ActNumber + 1;
            var localAct = local.MapActs.SingleOrDefault(item => item.ActNumber == localActNumber);
            if (localAct == null)
            {
                continue;
            }

            var pointTypeLookup = localAct.Nodes.ToDictionary(
                node => (node.Col, node.Row),
                node => node.PointType,
                EqualityComparer<(int Col, int Row)>.Default);
            unknownOdds.ResetToBase();
            previousRoomType = null;
            var floorLookup = replay.Floors.ToDictionary(floor => floor.Floor);
            var actFloorOffset = GetActAncientFloorOffset(localActNumber);
            foreach (var (floorIndexInAct, current, next) in EnumerateVisitedActSteps(act))
            {
                var floorNumber = actFloorOffset + floorIndexInAct;
                if (!floorLookup.TryGetValue(floorNumber, out var floor))
                {
                    continue;
                }

                var currentPointType = GetPointTypeFromRoomCode(floor.RoomType);
                if (!string.Equals(currentPointType, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    previousRoomType = MapPointTypeToStandardRoomType(currentPointType);
                    continue;
                }

                var childPointTypes = next == null
                    ? Array.Empty<string>()
                    : [MapCoordToPointType(pointTypeLookup, next.Value)];
                var predicted = ResolveUnknownRoomType(previousRoomType, childPointTypes, ownedRelics: Array.Empty<string>());
                rows.Add(new UnknownRoomTraceRow(
                    floor.Floor,
                    predicted.ToString(),
                    floor.HasCombat,
                    floor.ShopActions,
                    floor.PickedRelicIds,
                    floor.PickedCardIds,
                    floor.EventText));
                previousRoomType = predicted;
            }
        }

        return rows;

        StandardRoomType ResolveUnknownRoomType(
            StandardRoomType? previous,
            IReadOnlyList<string> childTypes,
            IReadOnlyList<string> ownedRelics)
        {
            var blacklist = new HashSet<StandardRoomType>();
            if (previous == StandardRoomType.Shop ||
                (childTypes.Count > 0 && childTypes.All(type => string.Equals(type, "Shop", StringComparison.OrdinalIgnoreCase))))
            {
                blacklist.Add(StandardRoomType.Shop);
            }

            if (ownedRelics.Contains("JUZU_BRACELET", StringComparer.OrdinalIgnoreCase))
            {
                blacklist.Add(StandardRoomType.Monster);
            }

            return unknownOdds.Roll(blacklist);
        }
    }

    private static IEnumerable<(int FloorIndexInAct, (int Col, int Row) Current, (int Col, int Row)? Next)> EnumerateVisitedActSteps(ReplayMapAct act)
    {
        for (var i = 1; i < act.VisitedCoords.Count; i++)
        {
            var current = act.VisitedCoords[i];
            var next = i + 1 < act.VisitedCoords.Count ? act.VisitedCoords[i + 1] : ((int Col, int Row)?)null;
            yield return (FloorIndexInAct: i, Current: current, Next: next);
        }
    }

    private static int GetActAncientFloorOffset(int actNumber) =>
        actNumber switch
        {
            1 => 1,
            2 => 18,
            _ => 34
        };

    private static string MapCoordToPointType(
        IReadOnlyDictionary<(int Col, int Row), string> pointTypeLookup,
        (int Col, int Row) coord)
    {
        return pointTypeLookup.TryGetValue(coord, out var pointType)
            ? pointType
            : "Unassigned";
    }

    private static string GetPointTypeFromRoomCode(string roomType)
    {
        return roomType.ToUpperInvariant() switch
        {
            "M" => "Monster",
            "E" => "Elite",
            "T" => "Treasure",
            "S" => "Shop",
            "R" => "RestSite",
            "B" => "Boss",
            "A" => "Ancient",
            "V" => "Unknown",
            _ => "Unassigned"
        };
    }

    private static StandardRoomType MapPointTypeToStandardRoomType(string pointType)
    {
        return pointType switch
        {
            "Monster" => StandardRoomType.Monster,
            "Elite" => StandardRoomType.Elite,
            "Treasure" => StandardRoomType.Treasure,
            "Shop" => StandardRoomType.Shop,
            "RestSite" => StandardRoomType.RestSite,
            "Boss" => StandardRoomType.Boss,
            "Ancient" => StandardRoomType.Event,
            "Unknown" => StandardRoomType.Event,
            _ => StandardRoomType.Monster
        };
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "-" : string.Join("/", values);
}

internal sealed record UnknownRoomTraceRow(
    int Floor,
    string PredictedRoomType,
    bool HasCombat,
    IReadOnlyList<ReplayShopAction> ShopActions,
    IReadOnlyList<string> PickedRelics,
    IReadOnlyList<string> PickedCards,
    string? EventText);

internal static class DelicateFrondPotionScanner
{
    private static readonly string[] Potion1EpochPotions =
    [
        "BEETLE_JUICE",
        "MAZALETHS_GIFT",
        "DROPLET_OF_PRECOGNITION"
    ];

    private static readonly string[] Potion2EpochPotions =
    [
        "POWDERED_DEMISE",
        "SHIP_IN_A_BOTTLE",
        "TOUCH_OF_INSANITY"
    ];

    private static readonly string[] Silent4EpochPotions =
    [
        "POISON_POTION",
        "GHOST_IN_A_JAR",
        "CUNNING_POTION"
    ];

    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var runJsonPath = args.Count > 0
                ? args[0]
                : Path.Combine("瀛樻。", "1780042178.run");
            var floorNumber = args.Count > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFloor)
                ? parsedFloor
                : 35;
            var maxOffset = args.Count > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxOffset)
                ? parsedMaxOffset
                : 160;

            using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath));
            var replay = Sts2LogReplayParser.Parse(document.RootElement);
            var targetFloor = replay.Floors.Single(floor => floor.Floor == floorNumber);
            var target = targetFloor.PotionChoiceIds.Count > 0
                ? targetFloor.PotionChoiceIds
                : targetFloor.PickedPotionChoiceIds;

            if (target.Count == 0)
            {
                throw new InvalidOperationException($"Floor {floorNumber} has no logged potion choices to scan for.");
            }

            if (!TryParseScannerCharacter(replay.CharacterId, out var character))
            {
                throw new InvalidOperationException($"Unsupported character: {replay.CharacterId}");
            }

            if (!SeedFormatter.TryNormalize(replay.SeedText, out var normalizedSeed, out var seedError))
            {
                throw new InvalidOperationException($"Invalid seed {replay.SeedText}: {seedError}");
            }

            var seed = SeedFormatter.ToUIntSeed(normalizedSeed);
            var pools = BuildPoolVariants(character);
            var matches = new List<string>();

            Console.WriteLine($"run={replay.RunId} seed={replay.SeedText} character={replay.CharacterId} floor={floorNumber}");
            Console.WriteLine($"target={string.Join("/", target)} maxOffset={maxOffset}");

            foreach (var pool in pools)
            {
                for (var offset = 0; offset <= maxOffset; offset++)
                {
                    var single = RollSequence(seed, offset, pool.Potions, target.Count, removeWithinBatch: false);
                    if (SamePotionSequence(target, single.Potions))
                    {
                        matches.Add($"{pool.Name} offset={offset} mode=frond-loop generated={string.Join("/", single.Potions)} trace={single.Trace}");
                    }

                    var batch = RollSequence(seed, offset, pool.Potions, target.Count, removeWithinBatch: true);
                    if (SamePotionSequence(target, batch.Potions))
                    {
                        matches.Add($"{pool.Name} offset={offset} mode=count-batch generated={string.Join("/", batch.Potions)} trace={batch.Trace}");
                    }
                }

                var offset0 = RollSequence(seed, 0, pool.Potions, target.Count, removeWithinBatch: false);
                Console.WriteLine($"pool={pool.Name} count={pool.Potions.Count} offset0={string.Join("/", offset0.Potions)} trace={offset0.Trace}");
            }

            if (matches.Count == 0)
            {
                Console.WriteLine("matches=none");
                return 2;
            }

            Console.WriteLine("matches:");
            foreach (var match in matches)
            {
                Console.WriteLine($"- {match}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"scan-frond-potions failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<PotionPoolVariant> BuildPoolVariants(CharacterId character)
    {
        var dataset = new NeowEventGeneratorFactory().LoadDataset(Path.Combine("data", "0.106.1", "neow", "options.json"));
        var fullPool = dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions)
            ? characterPotions.Concat(dataset.SharedPotionPoolList).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : dataset.SharedPotionPoolList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        fullPool = fullPool
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .ToList();

        var variants = new List<PotionPoolVariant>
        {
            Create("all", fullPool, dataset),
            Create("official_character_unlocked_all", OfficialCharacterPotions(character).Concat(fullPool).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), dataset),
            Create("without_potion1", Without(fullPool, Potion1EpochPotions), dataset),
            Create("without_potion2", Without(fullPool, Potion2EpochPotions), dataset),
            Create("without_silent4", Without(fullPool, Silent4EpochPotions), dataset),
            Create("without_potion1_2", Without(fullPool, Potion1EpochPotions.Concat(Potion2EpochPotions)), dataset),
            Create("without_potion1_silent4", Without(fullPool, Potion1EpochPotions.Concat(Silent4EpochPotions)), dataset),
            Create("without_potion2_silent4", Without(fullPool, Potion2EpochPotions.Concat(Silent4EpochPotions)), dataset),
            Create("without_potion1_2_silent4", Without(fullPool, Potion1EpochPotions.Concat(Potion2EpochPotions).Concat(Silent4EpochPotions)), dataset),
            Create("official_character_unlocked_without_potion1", OfficialCharacterPotions(character).Concat(Without(fullPool, Potion1EpochPotions)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), dataset),
            Create("official_character_unlocked_without_potion2", OfficialCharacterPotions(character).Concat(Without(fullPool, Potion2EpochPotions)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), dataset),
            Create("official_character_unlocked_without_potion1_2", OfficialCharacterPotions(character).Concat(Without(fullPool, Potion1EpochPotions.Concat(Potion2EpochPotions))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), dataset)
        };

        return variants
            .GroupBy(variant => string.Join("\n", variant.Potions.Select(potion => potion.Id)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static PotionPoolVariant Create(string name, IReadOnlyList<string> ids, NeowOptionDataset dataset)
    {
        var potions = ids
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .Select(id => new PotionCandidate(id, dataset.PotionMetadataMap[id].ParsedRarity))
            .ToArray();
        return new PotionPoolVariant(name, potions);
    }

    private static IReadOnlyList<string> Without(IEnumerable<string> ids, IEnumerable<string> removed)
    {
        var removedSet = removed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Where(id => !removedSet.Contains(id)).ToArray();
    }

    private static IReadOnlyList<string> OfficialCharacterPotions(CharacterId character) =>
        character switch
        {
            CharacterId.Ironclad => ["BLOOD_POTION", "SOLDIERS_STEW", "ASHWATER"],
            CharacterId.Silent => ["POISON_POTION", "GHOST_IN_A_JAR", "CUNNING_POTION"],
            CharacterId.Defect => ["FOCUS_POTION", "ESSENCE_OF_DARKNESS", "POTION_OF_CAPACITY"],
            CharacterId.Necrobinder => ["POTION_OF_DOOM", "POT_OF_GHOULS", "BONE_BREW"],
            CharacterId.Regent => ["STAR_POTION", "COSMIC_CONCOCTION", "KINGS_COURAGE"],
            _ => []
        };

    private static PotionScanRoll RollSequence(uint seed, int offset, IReadOnlyList<PotionCandidate> sourcePool, int count, bool removeWithinBatch)
    {
        var rng = new GameRng(seed, "combat_potion_generation");
        rng.FastForward(offset);
        var pool = sourcePool.ToList();
        var potions = new List<string>(count);
        var trace = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var counterBeforeRarity = rng.Counter;
            var rarityRoll = rng.NextFloat();
            var rarity = rarityRoll <= 0.1f
                ? PotionRarity.Rare
                : rarityRoll <= 0.35f
                    ? PotionRarity.Uncommon
                    : PotionRarity.Common;
            var options = pool.Where(potion => potion.Rarity == rarity).ToArray();
            if (options.Length == 0)
            {
                options = pool.ToArray();
            }

            var counterBeforePick = rng.Counter;
            var pickIndex = rng.NextInt(options.Length);
            var picked = options[pickIndex];
            potions.Add(picked.Id);
            trace.Add($"slot{i + 1}:rarity@{counterBeforeRarity}={rarityRoll:R}->{rarity};pick@{counterBeforePick}={pickIndex}/{options.Length}:{picked.Id}");
            if (removeWithinBatch)
            {
                pool.RemoveAll(potion => string.Equals(potion.Id, picked.Id, StringComparison.OrdinalIgnoreCase));
            }
        }

        return new PotionScanRoll(potions, string.Join(" | ", trace));
    }

    private static bool SamePotionSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        expected.Count == actual.Count &&
        expected.Zip(actual, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(match => match);

    private static bool TryParseScannerCharacter(string value, out CharacterId character)
    {
        var normalized = value.Equals("THE_SILENT", StringComparison.OrdinalIgnoreCase)
            ? "Silent"
            : value;
        return CharacterIdExtensions.TryParse(normalized, out character);
    }
}

internal sealed record PotionPoolVariant(string Name, IReadOnlyList<PotionCandidate> Potions);

internal sealed record PotionCandidate(string Id, PotionRarity Rarity);

internal sealed record PotionScanRoll(IReadOnlyList<string> Potions, string Trace);

internal enum StandardRoomType
{
    Monster,
    Elite,
    Treasure,
    Shop,
    RestSite,
    Event,
    Boss
}

internal enum PreCombatReplayMode
{
    None,
    ConsumeDelicateFrondCombatPotionGenerationOnly,
    ReplayDelicateFrondWithPotionSlots
}

internal sealed class UnknownRoomOdds
{
    private readonly GameRng _rng;
    private readonly Dictionary<StandardRoomType, float> _baseOdds = new()
    {
        [StandardRoomType.Monster] = 0.1f,
        [StandardRoomType.Elite] = -1f,
        [StandardRoomType.Treasure] = 0.02f,
        [StandardRoomType.Shop] = 0.03f
    };

    private readonly Dictionary<StandardRoomType, float> _currentOdds = new()
    {
        [StandardRoomType.Monster] = 0.1f,
        [StandardRoomType.Elite] = -1f,
        [StandardRoomType.Treasure] = 0.02f,
        [StandardRoomType.Shop] = 0.03f
    };

    public UnknownRoomOdds(GameRng rng)
    {
        _rng = rng;
    }

    public void ResetToBase()
    {
        foreach (var (roomType, odds) in _baseOdds)
        {
            _currentOdds[roomType] = odds;
        }
    }

    public StandardRoomType Roll(IReadOnlySet<StandardRoomType> blacklist)
    {
        var allowed = _currentOdds.Keys
            .Append(StandardRoomType.Event)
            .Where(roomType => !blacklist.Contains(roomType))
            .ToList();

        var selected = allowed.Contains(StandardRoomType.Event)
            ? StandardRoomType.Event
            : allowed.OrderBy(room => room).FirstOrDefault(StandardRoomType.Monster);

        var roll = _rng.NextFloat();
        var cumulative = 0f;
        foreach (var (roomType, odds) in _currentOdds)
        {
            if (!allowed.Contains(roomType) || odds < 0f)
            {
                continue;
            }

            cumulative += odds;
            if (roll <= cumulative)
            {
                selected = roomType;
                break;
            }
        }

        foreach (var (roomType, baseOdds) in _baseOdds)
        {
            if (roomType == selected)
            {
                _currentOdds[roomType] = baseOdds;
            }
            else if (allowed.Contains(roomType))
            {
                _currentOdds[roomType] += baseOdds;
            }
        }

        return selected;
    }
}

internal static class BatchDiagnostics
{
    public static IEnumerable<string> From(IReadOnlyList<BatchRunScore> results)
    {
        var supported = results.Where(result => result.ItemScore.Total > 0).ToArray();
        yield return $"supported_single_player_runs: {supported.Length}/{results.Count}";
        foreach (var result in supported.OrderBy(result => result.ItemScore.Rate))
        {
            yield return $"{result.RunId}: first_mismatch={result.FirstMismatch}, item_score={result.ItemScore}, weakest={WeakestCategory(result)}";
        }
    }

    private static string WeakestCategory(BatchRunScore result)
    {
        return new[]
            {
                ("cards", result.CardScore),
                ("relics", result.RelicScore),
                ("shop cards", result.ShopCardScore),
                ("shop relics", result.ShopRelicScore),
                ("shop potions", result.ShopPotionScore)
            }
            .Where(item => item.Item2.Total > 0)
            .OrderBy(item => item.Item2.Rate)
            .Select(item => $"{item.Item1} {item.Item2}")
            .FirstOrDefault() ?? "n/a";
    }
}

internal sealed record BatchRunScore(
    int RunId,
    string RunPath,
    string CharacterId,
    int Ascension,
    int Floors,
    BatchScore ItemScore,
    BatchScore CardScore,
    BatchScore RelicScore,
    BatchScore ShopCardScore,
    BatchScore ShopRelicScore,
    BatchScore ShopPotionScore,
    string FirstMismatch)
{
    public static BatchRunScore From(
        ReplayRun replay,
        string runPath,
        IReadOnlyList<GeneratedFloorComparison> comparisons)
    {
        var card = BatchScore.Sum(comparisons.Select(ScoreCards));
        var relic = BatchScore.Sum(comparisons.Select(ScoreRelics));
        var shopCard = BatchScore.Sum(comparisons.Select(ScoreShopCards));
        var shopRelic = BatchScore.Sum(comparisons.Select(ScoreShopRelics));
        var shopPotion = BatchScore.Sum(comparisons.Select(ScoreShopPotions));
        var itemScore = BatchScore.Sum([card, relic, shopCard, shopRelic, shopPotion]);
        var firstMismatch = comparisons.FirstOrDefault(comparison => comparison.IsMatch == false);
        return new BatchRunScore(
            replay.RunId,
            Path.GetFullPath(runPath),
            replay.CharacterId,
            replay.Ascension,
            replay.Floors.Count,
            itemScore,
            card,
            relic,
            shopCard,
            shopRelic,
            shopPotion,
            firstMismatch == null ? "" : $"{firstMismatch.Floor} {firstMismatch.RoomType}");
    }

    private static BatchScore ScoreCards(GeneratedFloorComparison comparison)
    {
        if (!comparison.CardMatch.HasValue)
        {
            return BatchScore.Empty;
        }

        if (comparison.CardMatch.Value)
        {
            return new BatchScore(comparison.ExpectedCards.Count, comparison.ExpectedCards.Count);
        }

        return ScoreSetLike(comparison.ExpectedCards, comparison.GeneratedCards, normalizeCards: true);
    }

    private static BatchScore ScoreRelics(GeneratedFloorComparison comparison)
    {
        if (!comparison.RelicMatch.HasValue)
        {
            return BatchScore.Empty;
        }

        if (comparison.RelicMatch.Value)
        {
            return new BatchScore(comparison.ExpectedRelics.Count, comparison.ExpectedRelics.Count);
        }

        return ScoreSetLike(comparison.ExpectedRelics, comparison.GeneratedRelics, normalizeCards: false);
    }

    private static BatchScore ScoreShopCards(GeneratedFloorComparison comparison)
    {
        if (!comparison.CardMatch.HasValue ||
            comparison.ExpectedShopColoredCards.Count + comparison.ExpectedShopColorlessCards.Count == 0)
        {
            return BatchScore.Empty;
        }

        var expectedCount = comparison.ExpectedShopColoredCards.Count + comparison.ExpectedShopColorlessCards.Count;
        if (comparison.CardMatch.Value)
        {
            return new BatchScore(expectedCount, expectedCount);
        }

        return ScoreSetLike(
            comparison.ExpectedShopColoredCards.Concat(comparison.ExpectedShopColorlessCards).ToArray(),
            comparison.GeneratedShopColoredCards.Concat(comparison.GeneratedShopColorlessCards).ToArray(),
            normalizeCards: true);
    }

    private static BatchScore ScoreShopRelics(GeneratedFloorComparison comparison)
    {
        if (!comparison.ShopRelicMatch.HasValue)
        {
            return BatchScore.Empty;
        }

        if (comparison.ShopRelicMatch.Value)
        {
            return new BatchScore(comparison.ExpectedShopRelics.Count, comparison.ExpectedShopRelics.Count);
        }

        return ScoreSetLike(comparison.ExpectedShopRelics, comparison.GeneratedShopRelics, normalizeCards: false);
    }

    private static BatchScore ScoreShopPotions(GeneratedFloorComparison comparison)
    {
        if (!comparison.ShopPotionMatch.HasValue)
        {
            return BatchScore.Empty;
        }

        if (comparison.ShopPotionMatch.Value)
        {
            return new BatchScore(comparison.ExpectedShopPotions.Count, comparison.ExpectedShopPotions.Count);
        }

        return ScoreSetLike(comparison.ExpectedShopPotions, comparison.GeneratedShopPotions, normalizeCards: false);
    }

    private static BatchScore ScoreSetLike(IReadOnlyList<string> expected, IReadOnlyList<string> actual, bool normalizeCards)
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0 ? new BatchScore(1, 1) : new BatchScore(0, actual.Count);
        }

        var expectedCounts = BuildCounts(expected, normalizeCards);
        var actualCounts = BuildCounts(actual, normalizeCards);
        var matched = expectedCounts.Sum(item => Math.Min(item.Value, actualCounts.GetValueOrDefault(item.Key)));
        return new BatchScore(matched, expected.Count);
    }

    private static Dictionary<string, int> BuildCounts(IEnumerable<string> values, bool normalizeCards)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var value = normalizeCards && raw.EndsWith("+", StringComparison.Ordinal)
                ? raw[..^1]
                : raw;
            result[value] = result.GetValueOrDefault(value) + 1;
        }

        return result;
    }
}

internal readonly record struct BatchScore(int Matched, int Total)
{
    public static BatchScore Empty => new(0, 0);

    public double Rate => Total == 0 ? 1.0 : (double)Matched / Total;

    public string Percent => Total == 0 ? "n/a" : Rate.ToString("P1", CultureInfo.InvariantCulture);

    public static BatchScore Sum(IEnumerable<BatchScore> scores)
    {
        var matched = 0;
        var total = 0;
        foreach (var score in scores)
        {
            matched += score.Matched;
            total += score.Total;
        }

        return new BatchScore(matched, total);
    }

    public override string ToString()
    {
        return Total == 0
            ? "n/a"
            : $"{Matched}/{Total} ({Percent})";
    }
}

internal static class ReplayProbeSelfTests
{
    public static int Run()
    {
        try
        {
            RunAssertions();
            Console.WriteLine("self-test passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"self-test failed: {ex.Message}");
            return 1;
        }
    }

    public static int RunRewardPoolAssertion()
    {
        try
        {
            AssertRewardPoolsUseUnlockedCharacterCards();
            Console.WriteLine("reward-pool self-test passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"reward-pool self-test failed: {ex.Message}");
            return 1;
        }
    }

    public static int RunFutureOfPotionsAssertion()
    {
        try
        {
            AssertFutureOfPotionsMatchesLoggedRun();
            Console.WriteLine("future-of-potions self-test passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"future-of-potions self-test failed: {ex.Message}");
            return 1;
        }
    }

    public static int RunPreCombatHookAssertion()
    {
        try
        {
            AssertPreCombatHookDiagnosticFor1780042178();
            Console.WriteLine("pre-combat hook self-test passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"pre-combat hook self-test failed: {ex.Message}");
            return 1;
        }
    }

    public static int RunDelicateFrondExperimentAssertion()
    {
        try
        {
            AssertDelicateFrondCombatPotionRngOnlyDoesNotFixFloor35();
            AssertDelicateFrondPotionSlotExperimentMatchesFloor35Potions();
            AssertBossRewardOrderingKeepsFloor35Aligned();
            AssertPotionCourierRansackKeepsFloor40Aligned();
            Console.WriteLine("delicate-frond experiment self-test passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"delicate-frond experiment self-test failed: {ex.Message}");
            return 1;
        }
    }

    private static void RunAssertions()
    {
        var samplePath = Path.Combine("scratch", "sts2log-run-4902307.json");
        using var document = JsonDocument.Parse(File.ReadAllText(samplePath));

        var replay = Sts2LogReplayParser.Parse(document.RootElement);

        AssertEqual(4902307, replay.RunId, "run id");
        AssertEqual("7KUHRMX75P", replay.SeedText, "seed text");
        AssertEqual("IRONCLAD", replay.CharacterId, "character");
        AssertEqual(10, replay.Ascension, "ascension");
        AssertEqual(1, replay.PlayerCount, "player count");
        AssertEqual(49, replay.Floors.Count, "floor count");
        AssertTrue(replay.FinalDeck.Any(card => string.Equals(card.CardId, "PERFECTED_STRIKE+", StringComparison.OrdinalIgnoreCase) && card.Count == 12), "final deck should include counted upgraded perfected strikes");

        var floor2 = replay.Floors.Single(floor => floor.Floor == 2);
        AssertEqual("M", floor2.RoomType, "floor 2 room");
        AssertSequence(["PERFECTED_STRIKE+", "HAVOC+", "ANGER+"], floor2.CardChoices, "floor 2 card choices");
        AssertEqual("PERFECTED_STRIKE+", floor2.PickedCardIds.Single(), "floor 2 picked card");

        var floor7 = replay.Floors.Single(floor => floor.Floor == 7);
        AssertEqual("V", floor7.RoomType, "floor 7 room");
        AssertSequence(["FLASH_OF_STEEL"], floor7.ShopActions.Select(action => action.ItemId).ToArray(), "floor 7 shop action");

        var act1 = replay.MapActs.Single(act => act.ActNumber == 1);
        AssertEqual(16, act1.VisitedCoords.Count, "act 1 visited coord count");
        AssertEqual((3, 0), act1.VisitedCoords[0], "act 1 start coord");
        AssertEqual((3, 15), act1.VisitedCoords[^1], "act 1 boss coord");

        var local = LocalReplayPredictor.Build(replay);
        var localAct1 = local.MapActs.Single(act => act.ActNumber == 1);
        var missingCoords = act1.VisitedCoords.Skip(1)
            .Where(coord => !localAct1.Nodes.Any(node => node.Col == coord.Col && node.Row == coord.Row))
            .ToArray();
        Console.WriteLine($"map diagnostic: local act1 missing visited coords [{string.Join(", ", missingCoords.Select(coord => $"{coord.Col}:{coord.Row}"))}]");

        var replayResult = LogDrivenReplay.Run(replay);
        AssertSequence(replay.FinalRelicIds, replayResult.ReplayedRelicIds, "final relic replay");
        AssertEqual(49, replayResult.Trace.Count, "trace floor count");
        var generated = GeneratedRunComparer.Compare(replay);
        AssertTrue(generated.Any(item => item.Floor == 2 && item.CardMatch == true), "floor 2 upgraded combat card rewards should match by base id");
        AssertTrue(generated.Any(item => item.Floor == 3 && item.CardMatch == true), "floor 3 upgraded event card rewards should match by base id");
        AssertTrue(generated.Any(item => item.Floor == 4 && item.CardMatch == true), "floor 4 upgraded combat card rewards should match by base id");
        AssertTrue(generated.Any(item => item.Floor == 6 && item.CardMatch == true), "floor 6 combat card rewards should match");
        AssertTrue(generated.Any(item => item.Floor == 7 && item.CardMatch == true), "floor 7 shop cards should include bought card in expected inventory");
        AssertTrue(generated.Any(item => item.Floor == 7 && item.ShopRelicMatch == true), "floor 7 shop relic offers should match");
        AssertTrue(generated.Any(item => item.Floor == 12 && item.ShopRelicMatch == true), "floor 12 shop relic offers should match as a set");
        AssertTrue(generated.Any(item => item.Floor == 12 && item.ShopRelicMatch == true), "floor 12 shop relic offers should still match after empty Silver Crucible chest");
        AssertTrue(generated.Any(item => item.Floor == 14 && item.RelicMatch == true), "floor 14 elite relic should match");
        AssertTrue(generated.Any(item => item.Floor == 26 && item.RelicMatch == true), "floor 26 treasure relic should match");
        AssertTrue(generated.Any(item => item.Floor == 41 && item.RelicMatch == true), "floor 41 treasure relic should match");
        var report = ReplayReportFormatter.Format(replay, replayResult, generated);
        AssertTrue(report.Contains("## Generated Comparisons", StringComparison.Ordinal), "report should include generated comparisons section");
        AssertTrue(report.Contains("first_generated_mismatch_floor: 21", StringComparison.Ordinal), "report should summarize first generated mismatch after shop/deck inference, picked-first reward ordering, and tolerated skip diagnostics");
        AssertTrue(report.Contains("Shop cards colored log/generated", StringComparison.Ordinal), "report should split shop colored card diagnostics");
        AssertTrue(report.Contains("gold 67->19", StringComparison.Ordinal), "report should include floor 12 shop gold delta diagnostics");
        AssertTrue(report.Contains("PERFECTED_STRIKE(", StringComparison.Ordinal), "report should include generated shop card prices");
        AssertTrue(report.Contains("ambiguous missing shop_action price matches", StringComparison.Ordinal), "report should flag ambiguous floor 12 missing purchase from gold delta");
        AssertTrue(report.Contains("deck-disambiguated inferred missing shop_action buy_card:PERFECTED_STRIKE", StringComparison.Ordinal), "report should use final deck counts to disambiguate floor 12 missing purchase");
        AssertTrue(generated.Any(item => item.Floor == 12 && item.CardMatch == true), "floor 12 shop cards should match after final-deck disambiguated missing buy");
        AssertTrue(generated.Any(item => item.Floor == 14 && item.CardMatch == true), "floor 14 elite card choices should match when sts2log moves the picked card first");
        AssertTrue(generated.Any(item => item.Floor == 15 && item.CardMatch == true), "floor 15 event card choices should match when sts2log moves the picked card first");
        AssertTrue(report.Contains("reward rng skip probe", StringComparison.Ordinal), "report should include reward RNG skip diagnostics for first hard mismatch");
        AssertTrue(report.Contains("reward rng skip probe matches 1=>JUGGERNAUT/UNMOVABLE/PACTS_END", StringComparison.Ordinal), "report should diagnose floor 17 boss rewards as a one-step reward RNG offset");
        AssertTrue(generated.Any(item => item.Floor == 38 && item.ShopRelicMatch == true), "floor 38 shop relics should remain matched; reward skip diagnostics must not globally desync relic state");
        AssertTrue(report.Contains("colored slots log/generated", StringComparison.Ordinal), "report should include colored card slot diagnostics");
        AssertTrue(report.Contains("colored log is generated subsequence", StringComparison.Ordinal), "report should flag omitted generated colored shop slots");
        AssertTrue(!report.Contains("shop_action buy_card:FLASH_OF_STEEL was not in shown shop state", StringComparison.Ordinal), "floor 7 buy_card should be present in shown shop state");
        AssertTrue(report.Contains("LIZARD_TAIL", StringComparison.Ordinal), "report should include floor 38 shop relic comparison");

        using var regentDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine("scratch", "sts2log-run-4903330.json")));
        var regentReplay = Sts2LogReplayParser.Parse(regentDocument.RootElement);
        var regentGenerated = GeneratedRunComparer.Compare(regentReplay);
        AssertTrue(regentGenerated.All(item => item.Floor != 8 || item.ShopRelicMatch == null), "floor 8 event relic should not be treated as a shop");
        AssertTrue(
            regentGenerated.Any(item => item.Floor == 9 &&
                                        item.CardMatch == true &&
                                        item.Notes != null &&
                                        item.Notes.Contains("event card reward mode=", StringComparison.Ordinal)),
            "floor 9 event card reward should include event reward mode diagnostics");
        AssertTrue(
            regentGenerated.Any(item => item.Floor == 12 && item.CardMatch == true),
            "floor 12 combat reward should stay aligned after applying the floor 9 event reward RNG calibration");
        AssertTrue(
            regentGenerated.Any(item => item.Floor == 15 && item.CardMatch == true),
            "floor 15 combat reward should stay aligned after applying the floor 9 event reward RNG calibration");

        using var necroDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine("scratch", "sts2log-run-4903362.json")));
        var necroReplay = Sts2LogReplayParser.Parse(necroDocument.RootElement);
        var necroFloor7 = necroReplay.Floors.Single(floor => floor.Floor == 7);
        AssertEqual("V", necroFloor7.RoomType, "necrobinder floor 7 room type");
        AssertTrue(necroFloor7.HasCombat, "necrobinder floor 7 should record a combat despite room_type=V");
        var necroResult = LogDrivenReplay.Run(necroReplay);
        AssertSequence(necroReplay.FinalRelicIds, necroResult.ReplayedRelicIds, "necrobinder final relic replay with inferred extra relics");
        AssertTrue(necroResult.Trace.Any(entry => entry.PickedRelics.Contains("FISHING_ROD", StringComparer.OrdinalIgnoreCase)), "trace should include inferred FISHING_ROD extra relic");
        AssertSequence(["NEOWS_BONES", "FISHING_ROD", "LEAD_PAPERWEIGHT"], ExactRewardStateBridge.GetAct1OpeningRelicIds(necroReplay), "necrobinder Act 1 opening relic details");
        var necroGenerated = GeneratedRunComparer.Compare(necroReplay);
        AssertTrue(necroGenerated.Any(item => item.Floor == 2), "necrobinder floor 2 comparison should be captured as a hard mismatch");
        AssertTrue(
            necroGenerated.Any(item => item.Floor == 7 && item.CardMatch == true),
            "question-mark rooms with actual combat should use combat reward generation");
        AssertTrue(
            necroGenerated.Any(item => item.Floor == 10 && item.RelicMatch == true),
            "floor 10 treasure relic should stay aligned after replaying floor 6 event relic generation");
        var necroUnknownOutcomes = UnknownRoomTraceProbe.GetPredictedOutcomesByFloor(necroReplay);
        AssertEqual(StandardRoomType.Treasure, necroUnknownOutcomes[6], "necrobinder floor 6 unknown room outcome");
        AssertEqual(StandardRoomType.Monster, necroUnknownOutcomes[43], "necrobinder floor 43 unknown room outcome");
        AssertEqual(StandardRoomType.Monster, necroUnknownOutcomes[44], "necrobinder floor 44 unknown room outcome");
        AssertTrue(
            necroGenerated.Any(item => item.Floor == 43 && item.RelicMatch == true),
            "question-mark rooms predicted as monster with relic reward should align floor 43 relic");
        var necroOpeningDiagnostics = OpeningReplayDiagnostics.Build(necroReplay);
        AssertTrue(necroOpeningDiagnostics.Any(item => item.Mode == OpeningDetailReplayMode.None && item.Floor2GeneratedCards.SequenceEqual(["DREDGE", "CALCIFY", "AFTERLIFE"])), "necrobinder opening diagnostic should include no-detail replay baseline");
        AssertTrue(necroOpeningDiagnostics.Any(item => item.Mode == OpeningDetailReplayMode.ReplayRelicDetails && item.Floor2GeneratedCards.SequenceEqual(["FRIENDSHIP", "SHROUD", "BURY"])), "necrobinder opening diagnostic should include 0.106.1 Neow's Bones reward-shuffle replay baseline");
        AssertTrue(GeneratedRunComparer.Compare(necroReplay).Any(item => item.Floor == 2 && item.CardMatch == true), "necrobinder floor 2 rewards should match after replaying Neow's Bones reward shuffle");

        using var shearsDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine("scratch", "sts2log-run-4903397.json")));
        var shearsReplay = Sts2LogReplayParser.Parse(shearsDocument.RootElement);
        var shearsDiagnostics = OpeningReplayDiagnostics.Build(shearsReplay);
        AssertTrue(shearsDiagnostics.Any(item =>
            item.Mode == OpeningDetailReplayMode.ReplayRelicDetails &&
            item.Floor2ExpectedCards.SequenceEqual(["DEATHBRINGER", "GRAVEBLAST", "FEAR"]) &&
            item.Floor2GeneratedCards.SequenceEqual(["DEATH_MARCH", "GRAVEBLAST", "FEAR"]) &&
            item.PickTrace.Any(trace =>
                trace.PickedCardId == "DEATH_MARCH" &&
                trace.ContainsTargets(["DEATHBRINGER", "DEATH_MARCH"]) &&
                trace.ToReportString().Contains("rawIndex=", StringComparison.Ordinal) &&
                trace.ToReportString().Contains("rareOffset=", StringComparison.Ordinal) &&
                trace.ToReportString().Contains("excluding DEATH_MARCH would pick DEATHBRINGER", StringComparison.Ordinal) &&
                item.Notes.Contains("prelude=", StringComparison.Ordinal) &&
                item.Notes.Contains("prelude variants", StringComparison.Ordinal))),
            "precarious shears opening diagnostic should preserve the current first-card hard mismatch with raw index, rarity odds, combat prelude, exclusion hypothesis, and prelude variant evidence");
        var shearsOracleSummary = Sts2CliOracleSummary.TryLoadForRun(shearsReplay);
        AssertTrue(shearsOracleSummary != null, "precarious shears sts2-cli oracle summary should exist");
        AssertSequence(["DEATH_MARCH", "GRAVEBLAST", "FEAR"], shearsOracleSummary!.OracleRewardCardIds, "precarious shears official oracle floor 2 cards");
        AssertTrue(GeneratedRunComparer.Compare(shearsReplay).Any(item =>
                item.Floor == 2 &&
                item.CardMatch == true &&
                item.Notes != null &&
                item.Notes.Contains("sts2-cli oracle confirms", StringComparison.Ordinal)),
            "precarious shears floor 2 should be treated as oracle-confirmed despite sts2log disagreement");

        var inspectionReportPath = Path.Combine("scratch", "sts2-v0.106.1-assembly-inspection.md");
        var inspectionReport = File.Exists(inspectionReportPath)
            ? File.ReadAllText(inspectionReportPath)
            : string.Empty;
        AssertTrue(inspectionReport.Contains("## Card Reward IL Sequence", StringComparison.Ordinal), "assembly inspection should include a focused card reward IL sequence section");
        AssertTrue(inspectionReport.Contains("Hook.ModifyCardRewardCreationOptions", StringComparison.Ordinal), "assembly inspection should expose reward creation hook ordering");
        AssertTrue(inspectionReport.Contains("## Card Reward Hook Overrides", StringComparison.Ordinal), "assembly inspection should list card reward hook override implementers");
        AssertTrue(inspectionReport.Contains("PrismaticGem.ModifyCardRewardCreationOptions", StringComparison.Ordinal), "assembly inspection should include known reward creation hook implementers");
        AssertTrue(inspectionReport.Contains("## Rng Parity", StringComparison.Ordinal), "assembly inspection should include official/local RNG parity diagnostics");
        AssertTrue(inspectionReport.Contains("- parity: pass", StringComparison.Ordinal), "official/local RNG parity should pass for checked samples");
        AssertTrue(inspectionReport.Contains("## Card Creation Options IL", StringComparison.Ordinal), "assembly inspection should include CardCreationOptions IL diagnostics");
        AssertTrue(inspectionReport.Contains("## Official Reward Pool Order", StringComparison.Ordinal), "assembly inspection should include official reward pool order diagnostics");
        AssertTrue(inspectionReport.Contains("NECROBINDER.Uncommon", StringComparison.Ordinal), "official reward pool order diagnostics should include Necrobinder uncommon reward bucket");

        AssertRewardPoolsUseUnlockedCharacterCards();
        AssertPreCombatHookDiagnosticFor1780042178();
    }

    private static void AssertRewardPoolsUseUnlockedCharacterCards()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine("瀛樻。", "1780042178.run")));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var bridge = ExactRewardStateBridge.Create(replay);
        var state = typeof(ExactRewardStateBridge).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(bridge)
            ?? throw new InvalidOperationException("Missing ExactRewardStateBridge._state.");
        var rewardModel = state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
            ?? throw new InvalidOperationException("Missing normalized RewardModel.");
        var getRewardCardPool = rewardModel.GetType().GetMethod("GetRewardCardPool", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RewardSimulationModel.GetRewardCardPool.");
        var rarePool = ((IEnumerable)(getRewardCardPool.Invoke(rewardModel, [CardRarity.Rare, false])
                ?? throw new InvalidOperationException("GetRewardCardPool returned null.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        AssertTrue(
            rarePool.Contains("THE_HUNT", StringComparer.OrdinalIgnoreCase),
            "silent rare reward pool should include THE_HUNT because CardCreationOptions.ForRoom uses unlocked character cards, not only canBeGeneratedInCombat cards");
    }

    private static void AssertFutureOfPotionsMatchesLoggedRun()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine("瀛樻。", "1780042178.run")));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var generated = GeneratedRunComparer.Compare(replay);
        AssertTrue(
            generated.Any(item => item.Floor == 23 && item.CardMatch == true),
            "floor 23 THE_FUTURE_OF_POTIONS should match the logged ESCAPE_PLAN/HAZE/BOUNCING_FLASK reward");
    }

    private static void AssertPreCombatHookDiagnosticFor1780042178()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve1780042178RunPath()));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var generated = GeneratedRunComparer.Compare(replay);
        var floor35 = generated.Single(item => item.Floor == 35);
        AssertTrue(
            floor35.Notes?.Contains("before-combat hook", StringComparison.OrdinalIgnoreCase) == true &&
            floor35.Notes.Contains("DELICATE_FROND", StringComparison.OrdinalIgnoreCase),
            "floor 35 should carry a diagnostic that replay state does not currently re-run owned BeforeCombatStart relics such as DELICATE_FROND");
    }

    private static void AssertDelicateFrondCombatPotionRngOnlyDoesNotFixFloor35()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve1780042178RunPath()));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var baseline = GeneratedRunComparer.Compare(replay);
        var experiment = GeneratedRunComparer.Compare(
            replay,
            preCombatReplayMode: PreCombatReplayMode.ConsumeDelicateFrondCombatPotionGenerationOnly);
        var baselineFloor35 = baseline.Single(item => item.Floor == 35);
        var experimentFloor35 = experiment.Single(item => item.Floor == 35);
        AssertSequence(
            baselineFloor35.GeneratedCards,
            experimentFloor35.GeneratedCards,
            "consuming only DELICATE_FROND combat_potion_generation should leave floor 35 generated cards unchanged");
        AssertTrue(
            experimentFloor35.Notes?.Contains("delicate-frond rng-only experiment", StringComparison.OrdinalIgnoreCase) == true,
            "floor 35 should record that the Delicate Frond rng-only experiment ran");
    }

    private static void AssertDelicateFrondPotionSlotExperimentMatchesFloor35Potions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve1780042178RunPath()));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var experiment = GeneratedRunComparer.Compare(
            replay,
            preCombatReplayMode: PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots);
        var floor35 = experiment.Single(item => item.Floor == 35);
        AssertTrue(
            floor35.Notes?.Contains("delicate-frond potion-slot experiment: before=1/4", StringComparison.OrdinalIgnoreCase) == true,
            "floor 35 should record potion slots before Delicate Frond fills empty slots");
        AssertTrue(
            floor35.Notes?.Contains("generated=BLOCK_POTION/FLEX_POTION/FIRE_POTION", StringComparison.OrdinalIgnoreCase) == true,
            "floor 35 Delicate Frond potion-slot experiment should generate the three logged procured potions");
    }

    private static void AssertBossRewardOrderingKeepsFloor35Aligned()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve1780042178RunPath()));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var experiment = GeneratedRunComparer.Compare(
            replay,
            preCombatReplayMode: PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots);
        var floor35 = experiment.Single(item => item.Floor == 35);
        AssertTrue(
            floor35.CardMatch == true,
            $"floor 35 should match after replaying boss rewards in official RewardsSet order; generated {string.Join("/", floor35.GeneratedCards)}");
    }

    private static void AssertPotionCourierRansackKeepsFloor40Aligned()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve1780042178RunPath()));
        var replay = Sts2LogReplayParser.Parse(document.RootElement);
        var experiment = GeneratedRunComparer.Compare(
            replay,
            preCombatReplayMode: PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots);
        var floor40 = experiment.Single(item => item.Floor == 40);
        AssertTrue(
            floor40.CardMatch == true,
            $"floor 40 cards should match after replaying Potion Courier Ransack Rewards RNG pick; generated {string.Join("/", floor40.GeneratedCards)}");
        AssertTrue(
            floor40.RelicMatch == true,
            $"floor 40 relic should match after replaying Potion Courier Ransack Rewards RNG pick; generated {string.Join("/", floor40.GeneratedRelics)}");
        var floor39 = experiment.Single(item => item.Floor == 39);
        AssertTrue(
            floor39.Notes?.Contains("potion-courier ransack generated CURE_ALL", StringComparison.OrdinalIgnoreCase) == true,
            $"floor 39 Potion Courier Ransack should generate the logged uncommon potion CURE_ALL; note was {floor39.Notes ?? ""}");
    }

    private static string Resolve1780042178RunPath()
    {
        var candidates = new[]
        {
            Path.Combine("瀛樻。", "1780042178.run"),
            Path.Combine("存档", "1780042178.run")
        };

        var match = candidates.FirstOrDefault(File.Exists);
        if (match == null)
        {
            throw new InvalidOperationException("Could not locate 1780042178.run under known archive directories.");
        }

        return match;
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    private static void AssertSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string label)
    {
        if (expected.Count != actual.Count ||
            expected.Zip(actual, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).Any(match => !match))
        {
            throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }
    }
}

internal static class LocalReplayPredictor
{
    public static Sts2ExactRouteAnalysis Build(ReplayRun replay)
    {
        if (!SeedFormatter.TryNormalize(replay.SeedText, out var normalizedSeed, out var seedError))
        {
            throw new InvalidOperationException($"Invalid seed {replay.SeedText}: {seedError}");
        }

        if (!TryParseCharacter(replay.CharacterId, out var character))
        {
            throw new InvalidOperationException($"Unsupported character: {replay.CharacterId}");
        }

        var dataVersion = ExactRewardStateBridge.ResolveRunValidationDataVersion(replay.GameVersion);
        var dataRoot = Path.Combine("data", dataVersion);
        var neowDataPath = Path.Combine(dataRoot, "neow", "options.json");
        var ancientDataPath = Path.Combine(dataRoot, "ancients", File.Exists(Path.Combine(dataRoot, "ancients", "options.zhs.json"))
            ? "options.zhs.json"
            : "options.json");
        var actDataPath = Path.Combine(dataRoot, "sts2", "acts.json");

        var dataset = new NeowEventGeneratorFactory().LoadDataset(neowDataPath);
        var previewer = Sts2RunPreviewer.CreateFromDataFiles(ancientDataPath, actDataPath);
        var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);

        return previewer.AnalyzeExactRoutes(dataset, new Sts2ExactRouteAnalysisRequest
        {
            SeedText = normalizedSeed,
            SeedValue = seedValue,
            Character = character,
            AscensionLevel = replay.Ascension,
            PlayerCount = replay.PlayerCount,
            MaxRouteChecks = 1,
            MaxResults = 1,
            ShopStrategy = Sts2ExactRouteShopStrategy.NoPurchase
        });
    }

    private static bool TryParseCharacter(string value, out CharacterId character)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        return CharacterIdExtensions.TryParse(normalized, out character);
    }
}

internal static class GeneratedRunComparer
{
    public static IReadOnlyList<GeneratedFloorComparison> Compare(
        ReplayRun replay,
        OpeningDetailReplayMode openingDetailReplayMode = OpeningDetailReplayMode.ReplayRelicDetails,
        ulong playerNetId = NeowGenerationContext.DefaultPlayerNetId,
        PreCombatReplayMode preCombatReplayMode = PreCombatReplayMode.None)
    {
        if (replay.PlayerCount != 1)
        {
            return [GeneratedFloorComparison.Note(0, "", "generated comparison currently targets single-player runs only")];
        }

        var state = ExactRewardStateBridge.Create(replay, openingDetailReplayMode, playerNetId);
        var comparisons = new List<GeneratedFloorComparison>();
        var generatedOwnedRelics = ReplayDefaults.GetStarterRelics(replay.CharacterId)
            .Concat(ExactRewardStateBridge.GetAct1OpeningRelicIds(replay))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unknownOutcomes = UnknownRoomTraceProbe.GetPredictedOutcomesByFloor(replay);
        var finalRelicCursor = generatedOwnedRelics.Count;
        var potionInventory = PotionInventoryState.Create(replay);

        foreach (var floor in replay.Floors)
        {
            var actNumber = ResolveActNumber(floor.Floor);
            unknownOutcomes.TryGetValue(floor.Floor, out var unknownOutcome);
            var combatPotionUseAppliedBeforeRewards = false;
            switch (floor.RoomType.ToUpperInvariant())
            {
                case "V" when floor.HasCombat:
                {
                    var isBoss = floor.Floor is 17 or 33 or 48;
                    var isElite = !isBoss && floor.PickedRelicIds.Count > 0;
                    var oddsType = isBoss
                        ? CardRarityOddsType.BossEncounter
                        : isElite
                            ? CardRarityOddsType.EliteEncounter
                            : CardRarityOddsType.RegularEncounter;
                    var alignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        state.PreviewCombatCards(oddsType, isElite, isBoss),
                        state.PreviewCombatSkips(oddsType, isElite, isBoss, maxSkip: isBoss ? 16 : 12));

                    if (isBoss)
                    {
                        if (floor.CardChoices.Count > 0)
                        {
                            comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, alignment.GeneratedCards, alignment.Notes));
                        }

                        state.ConsumeBossRewards();
                        break;
                    }

                    if (isElite)
                    {
                        var outcome = state.RollElite(actNumber);
                        comparisons.Add(GeneratedFloorComparison.ForReward(floor, outcome.GeneratedCards, outcome.GeneratedRelics, alignment.Notes));
                        break;
                    }

                    if (floor.CardChoices.Count > 0)
                    {
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, alignment.GeneratedCards, alignment.Notes));
                    }

                    state.ConsumeRegularCombat();
                    break;
                }
                case "M":
                {
                    var preCombatNotes = ReplayPreCombatHooksIfConfigured(state, floor, generatedOwnedRelics, replay, preCombatReplayMode, potionInventory);
                    if (preCombatReplayMode == PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots)
                    {
                        potionInventory.ApplyCombatUseBeforeRewards(floor);
                        combatPotionUseAppliedBeforeRewards = true;
                    }

                    var preludeTrace = state.PreviewCombatRewardPrelude(isElite: false, isBoss: false);
                    var pickTrace = state.PreviewCombatCardPickTrace(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false);
                    var alignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        state.PreviewCombatCards(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false),
                        state.PreviewCombatSkips(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false, maxSkip: 12));
                    var generatedCards = alignment.GeneratedCards;
                    var notes = GeneratedFloorComparison.ApplyOracleOverrideIfAvailable(
                        replay,
                        floor,
                        generatedCards,
                        MergeNotes(alignment.Notes, preCombatNotes),
                        out var cardMatchOverride);
                    if (floor.CardChoices.Count > 0 &&
                        !GeneratedFloorComparison.SameCardReward(floor.CardChoices, generatedCards) &&
                        pickTrace.Count > 0)
                    {
                        var traceNotes = $"prelude={preludeTrace.ToReportString()}; pick trace: {string.Join("; ", pickTrace.Select(trace => trace.ToReportString(floor.CardChoices)))}";
                        notes = string.IsNullOrWhiteSpace(notes)
                            ? traceNotes
                            : $"{notes}; {traceNotes}";
                    }

                    notes = AppendPreCombatHookDiagnostic(floor, generatedOwnedRelics, notes);
                    if (floor.CardChoices.Count > 0)
                    {
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, generatedCards, notes, cardMatchOverride));
                    }

                    state.ConsumeRegularCombat();
                    break;
                }
                case "E":
                {
                    var alignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        state.PreviewCombatCards(CardRarityOddsType.EliteEncounter, isElite: true, isBoss: false),
                        state.PreviewCombatSkips(CardRarityOddsType.EliteEncounter, isElite: true, isBoss: false, maxSkip: 12));
                    var outcome = state.RollElite(actNumber);
                    comparisons.Add(GeneratedFloorComparison.ForReward(floor, outcome.GeneratedCards, outcome.GeneratedRelics, alignment.Notes));
                    break;
                }
                case "T":
                {
                    var generatedRelics = floor.RelicChoices.Count > 0
                        ? state.ShowTreasure(actNumber)
                        : Array.Empty<string>();
                    if (floor.RelicChoices.Count == 0)
                    {
                        if (!floor.GoldBefore.HasValue || !floor.GoldAfter.HasValue || floor.GoldAfter.Value != floor.GoldBefore.Value)
                        {
                            state.ConsumeTreasureGoldOnly();
                        }
                    }

                    if (floor.RelicChoices.Count > 0)
                    {
                        comparisons.Add(GeneratedFloorComparison.ForRewardRelics(floor, generatedRelics));
                    }

                    break;
                }
                case "S":
                case "V" when floor.ShopActions.Count > 0:
                {
                    var session = state.PreviewShopSessionWithoutChangingBags(
                        actNumber,
                        floor.ShopActions,
                        initialShop => GeneratedFloorComparison.InferMissingShopActions(replay, floor, initialShop));
                    var inferredActions = session.InferredActions;
                    var visibleShop = session.VisibleShop;
                    var notes = GeneratedFloorComparison.ValidateShopActions(floor.ShopActions.Concat(inferredActions), visibleShop);
                    comparisons.Add(GeneratedFloorComparison.ForShop(floor, visibleShop, replay.GameVersion, inferredActions, notes));
                    state.RemoveFromRelicBags(floor.RelicChoices);
                    foreach (var action in floor.ShopActions.Concat(inferredActions))
                    {
                        state.ApplyShopAction(action, actNumber);
                    }

                    break;
                }
                case "V" when floor.CardChoices.Count == 3:
                {
                    if (IsCrystalSphereUncoverFuture(floor))
                    {
                        state.ConsumeCrystalSphereSetup();
                        var rarity = InferCrystalSphereCardRewardRarity(floor.CardChoices, replay.GameVersion);
                        var probes = state.PreviewCrystalSphereRewardVariants(
                            floor.PotionChoiceIds,
                            floor.GoldGained.GetValueOrDefault(),
                            cardCount: floor.CardChoices.Count,
                            rarityFilter: rarity);
                        var exactProbe = probes.FirstOrDefault(probe =>
                            GeneratedFloorComparison.SameCardReward(floor.CardChoices, probe.GeneratedCards));
                        var selectedProbe = exactProbe ?? probes.FirstOrDefault() ?? new CrystalSphereRewardProbe(
                            "no-reward-probe",
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            false);
                        var generatedCards = selectedProbe.GeneratedCards;
                        var legacyGeneratedCards = state.PreviewCharacterNonCombatCardsWithEventRng(
                            cardCount: floor.CardChoices.Count,
                            rarityFilter: rarity);
                        var variantNotes = string.Join(", ", probes.Select(probe =>
                            $"{probe.Mode}=>{string.Join("/", probe.GeneratedCards)} potions={string.Join("/", probe.GeneratedPotions)}"));
                        var crystalSphereModeNote = exactProbe != null
                            ? $"event card reward mode=crystal-sphere {selectedProbe.Mode}"
                            : $"event card reward mode=crystal-sphere unresolved; legacy={string.Join("/", legacyGeneratedCards)}";
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(
                            floor,
                            generatedCards,
                            string.IsNullOrWhiteSpace(variantNotes)
                                ? crystalSphereModeNote
                                : $"{crystalSphereModeNote}; variants {variantNotes}"));
                        state.ConsumeCrystalSphereRewardVariant(
                            floor.PotionChoiceIds,
                            selectedProbe.GoldBeforeCard,
                            floor.GoldGained.GetValueOrDefault(),
                            cardCount: floor.CardChoices.Count,
                            rarityFilter: rarity);
                        break;
                    }

                    if (IsBrainLeechRip(floor))
                    {
                        var generatedCards = state.PreviewColorlessNonCombatCards(
                            CardRarityOddsType.RegularEncounter,
                            floor.CardChoices.Count);
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(
                            floor,
                            generatedCards,
                            "event card reward mode=brain-leech-rip colorless non-combat"));
                        state.ConsumeColorlessNonCombatCards(CardRarityOddsType.RegularEncounter, floor.CardChoices.Count);
                        break;
                    }

                    if ((string.Equals(floor.EventText, "EVENT.THE_FUTURE_OF_POTIONS", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(floor.EventText, "THE_FUTURE_OF_POTIONS", StringComparison.OrdinalIgnoreCase)) &&
                        floor.PotionDiscardedIds.Count > 0)
                    {
                        var discardedPotionId = floor.PotionDiscardedIds[0];
                        var probes = state.PreviewFutureOfPotionsVariants(discardedPotionId);
                        var traceProbes = state.PreviewFutureOfPotionsTraceVariants(discardedPotionId);
                        var exactMatches = traceProbes
                            .Where(probe => GeneratedFloorComparison.SameCardReward(floor.CardChoices, probe.GeneratedCards))
                            .ToArray();
                        var selected = exactMatches.Length == 1
                            ? exactMatches[0]
                            : traceProbes.FirstOrDefault(probe => probe.PreludeSkip == 0) ?? traceProbes.FirstOrDefault();
                        var generatedCards = selected?.GeneratedCards ?? Array.Empty<string>();
                        var futureModeNote = exactMatches.Length == 1
                            ? $"event card reward mode=future-of-potions({selected!.CardType}, skip={selected.PreludeSkip})"
                            : "event card reward mode=future-of-potions-unresolved";
                        var futureNotes = string.Join(", ", probes.Select(probe =>
                            $"{probe.CardType}=>{string.Join("/", probe.GeneratedCards)}"));
                        var traceNotes = traceProbes
                            .Where(probe => probe.PreludeSkip <= 4)
                            .Select(probe => $"{probe.CardType}[skip {probe.PreludeSkip}]=>{string.Join("/", probe.GeneratedCards)} | {string.Join("; ", probe.PickTrace.Select(trace => trace.ToReportString()))}")
                            .ToArray();
                        futureNotes = string.IsNullOrWhiteSpace(futureNotes)
                            ? futureModeNote
                            : $"{futureNotes}; {futureModeNote}";
                        if (traceNotes.Length > 0)
                        {
                            futureNotes = $"{futureNotes}; trace {string.Join(" || ", traceNotes)}";
                        }
                        futureNotes = GeneratedFloorComparison.ApplyOracleOverrideIfAvailable(replay, floor, generatedCards, futureNotes, out var futureCardMatchOverride);
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, generatedCards, futureNotes, futureCardMatchOverride));
                        if (selected != null)
                        {
                            if (selected.PreludeSkip > 0)
                            {
                                state.ConsumeRewardRng(selected.PreludeSkip);
                            }

                            state.ConsumeFutureOfPotionsReward(discardedPotionId, selected.CardType);
                        }

                        break;
                    }

                    var combatAlignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        state.PreviewCombatCards(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false),
                        state.PreviewCombatSkips(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false, maxSkip: 12));
                    var directProbes = state.PreviewCardsAfterManualRewardPreludeSkips(CardRarityOddsType.RegularEncounter, maxSkip: 12);
                    var directGenerated = directProbes.FirstOrDefault(probe => probe.Skip == 0)?.GeneratedCards
                        ?? Array.Empty<string>();
                    var directAlignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        directGenerated,
                        directProbes);
                    var combatExact = GeneratedFloorComparison.SameCardReward(floor.CardChoices, combatAlignment.GeneratedCards) &&
                                      combatAlignment.AppliedSkip == 0;
                    var directExact = GeneratedFloorComparison.SameCardReward(floor.CardChoices, directGenerated);
                    var useDirectCardReward = directExact && !combatExact;
                    var alignment = useDirectCardReward ? directAlignment : combatAlignment;
                    var modeNote = useDirectCardReward
                        ? "event card reward mode=direct"
                        : "event card reward mode=combat-like";
                    var notes = string.IsNullOrWhiteSpace(alignment.Notes)
                        ? modeNote
                        : $"{alignment.Notes}; {modeNote}";
                    notes = GeneratedFloorComparison.ApplyOracleOverrideIfAvailable(replay, floor, alignment.GeneratedCards, notes, out var cardMatchOverride);
                    comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, alignment.GeneratedCards, notes, cardMatchOverride));
                    if (alignment.AppliedSkip > 0)
                    {
                        state.ConsumeRewardRng(alignment.AppliedSkip);
                    }

                    state.ConsumeEventCardReward(useDirectCardReward);
                    break;
                }
                case "V" when IsRoomFullOfCheeseGorge(floor):
                {
                    var generatedCards = state.PreviewCharacterNonCombatCards(
                        CardRarityOddsType.Uniform,
                        cardCount: 8,
                        rarityFilter: CardRarity.Common);
                    var notes = $"event grid selection mode=room-full-of-cheese-gorge; generated 8 choices: {string.Join(", ", generatedCards)}";
                    comparisons.Add(GeneratedFloorComparison.Note(floor.Floor, floor.RoomType, notes));
                    state.ConsumeCharacterNonCombatCards(
                        CardRarityOddsType.Uniform,
                        cardCount: 8,
                        rarityFilter: CardRarity.Common);
                    break;
                }
                case "V" when unknownOutcome == StandardRoomType.Treasure && floor.PickedRelicIds.Count > 0:
                {
                    var generatedRelics = state.ShowTreasure(actNumber);
                    comparisons.Add(GeneratedFloorComparison.ForRewardRelics(
                        floor,
                        generatedRelics,
                        "unknown room predicted as treasure"));
                    break;
                }
                case "V" when floor.PickedRelicIds.Count > 0:
                {
                    if (IsDollRoom(floor))
                    {
                        var dollRoomRelic = state.PreviewDollRoomRandomRelic();
                        comparisons.Add(GeneratedFloorComparison.ForRewardRelics(
                            floor,
                            dollRoomRelic,
                            "event relic mode=doll-room random doll"));
                        state.ConsumeDollRoomRandomRelic();
                        break;
                    }

                    var expectedRelic = floor.PickedRelicIds[0];
                    var candidates = state.PreviewEventRelicPullVariants(actNumber);
                    var defaultCandidate = candidates.FirstOrDefault(candidate => candidate.Rarity == null)
                        ?? new EventRelicPullProbe("rolled", null, Array.Empty<string>());
                    var generatedRelic = defaultCandidate.Relics;
                    var notes = string.Join(", ", candidates.Select(candidate =>
                        $"{candidate.Label}=>{string.Join("/", candidate.Relics)}"));
                    comparisons.Add(GeneratedFloorComparison.ForRewardRelics(floor, generatedRelic, notes));
                    var matchingCandidate = candidates.FirstOrDefault(candidate =>
                        candidate.Relics.Count > 0 &&
                        string.Equals(candidate.Relics[0], expectedRelic, StringComparison.OrdinalIgnoreCase));
                    if (matchingCandidate != null && matchingCandidate.Relics.Count > 0)
                    {
                        state.PullEventFrontAndObtain(actNumber, matchingCandidate.Rarity);
                    }

                    break;
                }
                case "V" when IsPotionCourierRansack(floor):
                {
                    var potionId = state.ConsumePotionCourierRansackReward();
                    var note = string.IsNullOrWhiteSpace(potionId)
                        ? "potion-courier ransack consumed Rewards RNG uncommon potion pick"
                        : $"potion-courier ransack generated {potionId}";
                    comparisons.Add(GeneratedFloorComparison.Note(floor.Floor, floor.RoomType, note));
                    break;
                }
                case "B":
                {
                    var preludeTrace = state.PreviewCombatRewardPrelude(isElite: false, isBoss: true);
                    var pickTrace = state.PreviewCombatCardPickTrace(CardRarityOddsType.BossEncounter, isElite: false, isBoss: true);
                    var alignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(
                        floor,
                        state.PreviewCombatCards(CardRarityOddsType.BossEncounter, isElite: false, isBoss: true),
                        state.PreviewCombatSkips(CardRarityOddsType.BossEncounter, isElite: false, isBoss: true, maxSkip: 16));
                    var notes = alignment.Notes;
                    if (floor.CardChoices.Count > 0 &&
                        !GeneratedFloorComparison.SameCardReward(floor.CardChoices, alignment.GeneratedCards))
                    {
                        var bossTraceNotes = $"boss prelude={preludeTrace.ToReportString()}; pick trace: {string.Join("; ", pickTrace.Select(trace => trace.ToReportString()))}";
                        notes = string.IsNullOrWhiteSpace(notes)
                            ? bossTraceNotes
                            : $"{notes}; {bossTraceNotes}";
                    }

                    var generatedCards = alignment.GeneratedCards;
                    if (floor.CardChoices.Count > 0)
                    {
                        comparisons.Add(GeneratedFloorComparison.ForRewardCards(floor, generatedCards, notes));
                    }

                    state.ConsumeBossRewards();
                    break;
                }
            }

            var obtained = floor.ShopActions
                .Where(action => string.Equals(action.ActionType, "buy_relic", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.ItemId)
                .Concat(floor.PickedAncientIds)
                .Concat(floor.PickedRelicIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var inferredObtained = InferObtainedRelicsFromFinalOrder(replay, generatedOwnedRelics, ref finalRelicCursor, obtained);
            state.RemoveFromRelicBags(inferredObtained);
            state.Obtain(inferredObtained);
            potionInventory.ApplyAfterFloor(floor, skipUseRemoval: combatPotionUseAppliedBeforeRewards);
        }

        return comparisons;
    }

    private static bool IsPotionCourierRansack(ReplayFloor floor)
    {
        if (!string.Equals(floor.EventText, "EVENT.POTION_COURIER", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(floor.EventText, "POTION_COURIER", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return floor.PickedPotionChoiceIds.Count > 0 &&
               floor.CardChoices.Count == 0 &&
               floor.PickedRelicIds.Count == 0 &&
               floor.ShopActions.Count == 0;
    }

    private static bool IsRoomFullOfCheeseGorge(ReplayFloor floor)
    {
        return IsEvent(floor, "ROOM_FULL_OF_CHEESE") &&
               floor.PickedCardIds.Count == 0 &&
               floor.CardChoices.Count == 0;
    }

    private static bool IsBrainLeechRip(ReplayFloor floor)
    {
        return IsEvent(floor, "BRAIN_LEECH") &&
               floor.CardChoices.Count == 3;
    }

    private static bool IsDollRoom(ReplayFloor floor) =>
        IsEvent(floor, "DOLL_ROOM");

    private static bool IsCrystalSphereUncoverFuture(ReplayFloor floor) =>
        IsEvent(floor, "CRYSTAL_SPHERE") &&
        floor.GoldBefore.HasValue &&
        floor.GoldAfter.HasValue &&
        floor.GoldAfter.Value < floor.GoldBefore.Value;

    private static CardRarity InferCrystalSphereCardRewardRarity(IReadOnlyList<string> cardChoices, string gameVersion)
    {
        var rarities = cardChoices
            .Select(card => CardMetadataIndex.Get(card, gameVersion)?.Rarity)
            .Select(ExactRewardStateBridge.ParseCardRarityForValidation)
            .Where(rarity => rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .Distinct()
            .ToArray();
        return rarities.Length == 1 ? rarities[0] : CardRarity.Rare;
    }

    private static bool IsEvent(ReplayFloor floor, string eventId)
    {
        if (string.IsNullOrWhiteSpace(floor.EventText))
        {
            return false;
        }

        return string.Equals(floor.EventText, eventId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(floor.EventText, $"EVENT.{eventId}", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReplayPreCombatHooksIfConfigured(
        ExactRewardStateBridge state,
        ReplayFloor floor,
        IReadOnlyList<string> ownedRelics,
        ReplayRun replay,
        PreCombatReplayMode preCombatReplayMode,
        PotionInventoryState? potionInventory = null)
    {
        if (floor.Floor < 35 || preCombatReplayMode == PreCombatReplayMode.None)
        {
            return null;
        }

        if (preCombatReplayMode == PreCombatReplayMode.ConsumeDelicateFrondCombatPotionGenerationOnly &&
            ownedRelics.Contains("DELICATE_FROND", StringComparer.OrdinalIgnoreCase))
        {
            var potionId = state.ConsumeDelicateFrondCombatPotionGenerationPreview(replay);
            return string.IsNullOrWhiteSpace(potionId)
                ? "delicate-frond rng-only experiment: consumed combat_potion_generation without deriving a potion id"
                : $"delicate-frond rng-only experiment: consumed combat_potion_generation -> {potionId}";
        }

        if (preCombatReplayMode == PreCombatReplayMode.ReplayDelicateFrondWithPotionSlots &&
            ownedRelics.Contains("DELICATE_FROND", StringComparer.OrdinalIgnoreCase) &&
            potionInventory != null)
        {
            var before = potionInventory.Count;
            var capacity = potionInventory.Capacity;
            var generated = new List<string>();
            while (potionInventory.HasOpenSlot)
            {
                var potionId = state.ConsumeDelicateFrondCombatPotionGenerationPreview(replay);
                if (string.IsNullOrWhiteSpace(potionId))
                {
                    break;
                }

                generated.Add(potionId);
                if (!potionInventory.TryAdd(potionId))
                {
                    break;
                }
            }

            var pickedChoices = floor.PickedPotionChoiceIds;
            var match = SameMultiset(generated, pickedChoices);
            return $"delicate-frond potion-slot experiment: before={before}/{capacity}, generated={string.Join("/", generated)}, logged_picked={string.Join("/", pickedChoices)}, match={(match ? "yes" : "no")}, after={potionInventory.Count}/{capacity}";
        }

        return null;
    }

    private static bool SameMultiset(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in left)
        {
            counts[item] = counts.GetValueOrDefault(item) + 1;
        }

        foreach (var item in right)
        {
            if (!counts.TryGetValue(item, out var count) || count == 0)
            {
                return false;
            }

            counts[item] = count - 1;
        }

        return counts.Values.All(count => count == 0);
    }

    private static int ResolveActNumber(int floor) =>
        floor switch
        {
            <= 16 => 1,
            <= 33 => 2,
            _ => 3
        };

    private static string? AppendPreCombatHookDiagnostic(
        ReplayFloor floor,
        IReadOnlyList<string> ownedRelics,
        string? notes)
    {
        if (floor.Floor < 35)
        {
            return notes;
        }

        var beforeCombatHookRelics = ownedRelics
            .Where(relicId => KnownBeforeCombatHookRelics.Contains(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (beforeCombatHookRelics.Length == 0)
        {
            return notes;
        }

        var diagnostic = $"before-combat hook relics active before floor {floor.Floor}: {string.Join("/", beforeCombatHookRelics)}; scratch replay does not re-run official BeforeCombatStart hooks";
        return string.IsNullOrWhiteSpace(notes)
            ? diagnostic
            : $"{notes}; {diagnostic}";
    }

    private static readonly HashSet<string> KnownBeforeCombatHookRelics = new(
        [
            "DELICATE_FROND"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static string? MergeNotes(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return $"{left}; {right}";
    }

    private static IReadOnlyList<string> InferObtainedRelicsFromFinalOrder(
        ReplayRun replay,
        List<string> ownedRelics,
        ref int finalRelicCursor,
        IReadOnlyList<string> recordedObtained)
    {
        var result = new List<string>();
        foreach (var relicId in recordedObtained)
        {
            if (ownedRelics.Contains(relicId, StringComparer.OrdinalIgnoreCase))
            {
                if (finalRelicCursor < replay.FinalRelicIds.Count &&
                    string.Equals(replay.FinalRelicIds[finalRelicCursor], relicId, StringComparison.OrdinalIgnoreCase))
                {
                    finalRelicCursor++;
                }

                continue;
            }

            while (finalRelicCursor < replay.FinalRelicIds.Count &&
                   !string.Equals(replay.FinalRelicIds[finalRelicCursor], relicId, StringComparison.OrdinalIgnoreCase))
            {
                AddIfNew(replay.FinalRelicIds[finalRelicCursor++]);
            }

            AddIfNew(relicId);
            if (finalRelicCursor < replay.FinalRelicIds.Count &&
                string.Equals(replay.FinalRelicIds[finalRelicCursor], relicId, StringComparison.OrdinalIgnoreCase))
            {
                finalRelicCursor++;
            }
        }

        return result;

        void AddIfNew(string relicId)
        {
            if (string.IsNullOrWhiteSpace(relicId) ||
                ownedRelics.Contains(relicId, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            ownedRelics.Add(relicId);
            result.Add(relicId);
        }
    }
}

internal sealed class PotionInventoryState
{
    private readonly List<string> _potions;

    private PotionInventoryState(int capacity)
    {
        Capacity = Math.Max(0, capacity);
        _potions = new List<string>(Capacity);
    }

    public int Capacity { get; }

    public int Count => _potions.Count;

    public bool HasOpenSlot => _potions.Count < Capacity;

    public static PotionInventoryState Create(ReplayRun replay)
    {
        return new PotionInventoryState(replay.MaxPotionSlotCount);
    }

    public bool TryAdd(string potionId)
    {
        if (!HasOpenSlot)
        {
            return false;
        }

        _potions.Add(potionId);
        return true;
    }

    public void ApplyCombatUseBeforeRewards(ReplayFloor floor)
    {
        foreach (var potionId in floor.PotionUsedIds.Concat(floor.PotionDiscardedIds))
        {
            RemoveOne(potionId);
        }
    }

    public void ApplyAfterFloor(ReplayFloor floor, bool skipUseRemoval = false)
    {
        foreach (var potionId in floor.PickedPotionChoiceIds)
        {
            TryAdd(potionId);
        }

        if (skipUseRemoval)
        {
            return;
        }

        foreach (var potionId in floor.PotionUsedIds.Concat(floor.PotionDiscardedIds))
        {
            RemoveOne(potionId);
        }
    }

    private void RemoveOne(string potionId)
    {
        var index = _potions.FindIndex(id => string.Equals(id, potionId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _potions.RemoveAt(index);
        }
    }
}

internal sealed class ExactRewardStateBridge
{
    internal const string DefaultRunValidationDataVersion = "0.106.1";
    private static readonly object RewardModelCacheLock = new();
    private static readonly Dictionary<(string Version, CharacterId Character, int PlayerCount), object> CorrectedRewardModelCache = new();
    private static readonly Dictionary<string, NeowOptionDataset> RewardDatasets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<object, StrongBox<int>> DelicateFrondCombatPotionCounters = new();
    private static readonly ConditionalWeakTable<object, StrongBox<GameRng>> EventRngs = new();
    private static readonly ConditionalWeakTable<object, Dictionary<string, StrongBox<GameRng>>> EventRngsById = new();

    private readonly object _state;
    private readonly string _dataVersion;
    private readonly CharacterId _character;
    private readonly int _playerCount;
    private readonly MethodInfo _consumeRegularCombat;
    private readonly MethodInfo _consumeBossRewards;
    private readonly MethodInfo _showTreasure;
    private readonly MethodInfo _showElite;
    private readonly MethodInfo _showShopPreview;
    private readonly MethodInfo _obtainEnumerable;
    private readonly MethodInfo _obtainWithImmediateEffects;
    private readonly MethodInfo _consumeRewardRng;
    private readonly MethodInfo _replayImmediatePickupEffectsOnly;
    private readonly MethodInfo _clone;
    private readonly PropertyInfo _rewardsRng;
    private readonly PropertyInfo _shopsRng;
    private readonly GameRng _eventRng;
    private readonly PropertyInfo _sharedBag;
    private readonly PropertyInfo _playerBag;
    private readonly PropertyInfo _currentRewardCards;
    private readonly MethodInfo _bagRemove;
    private readonly MethodInfo _rollPotionRewardChance;
    private readonly MethodInfo _rollPotionReward;
    private readonly MethodInfo _rollCardRarity;
    private readonly MethodInfo _tryPickAvailableCard;
    private readonly MethodInfo _rewardCardBufferAdd;
    private readonly MethodInfo _rewardCardBufferClear;
    private readonly MethodInfo _resolvePurchasedShopRelic;
    private readonly MethodInfo _resolvePurchasedShopPotion;
    private readonly MethodInfo _resolvePurchasedShopCard;
    private readonly MethodInfo _pullEventFrontAndObtain;
    private ExactRewardStateBridge(object state, string dataVersion)
    {
        _state = state;
        _dataVersion = dataVersion;
        NormalizeRewardModel(state);
        var stateType = state.GetType();
        var rewardModel = stateType.GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        _character = (CharacterId)(rewardModel.GetType().GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardModel)
            ?? throw new InvalidOperationException("Missing RewardModel.Character."));
        _playerCount = (int)(stateType.GetProperty("PlayerCount", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
            ?? throw new InvalidOperationException("Missing state.PlayerCount."));
        _consumeRegularCombat = GetMethod(stateType, "ConsumeRegularCombat");
        _consumeBossRewards = GetMethod(stateType, "ConsumeBossRewards");
        _showTreasure = GetMethod(stateType, "ShowTreasure");
        _showElite = GetMethod(stateType, "ShowElite");
        _showShopPreview = GetMethod(stateType, "ShowShopPreview");
        _consumeRewardRng = GetMethod(stateType, "ConsumeRewardRng");
        _replayImmediatePickupEffectsOnly = GetMethod(stateType, "ReplayImmediatePickupEffectsOnly");
        _clone = GetMethod(stateType, "Clone");
        _rewardsRng = stateType.GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RewardsRng property.");
        _shopsRng = stateType.GetProperty("ShopsRng", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing ShopsRng property.");
        _eventRng = GetSharedEventRng(state);
        _sharedBag = stateType.GetProperty("SharedBag", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing SharedBag property.");
        _playerBag = stateType.GetProperty("PlayerBag", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing PlayerBag property.");
        _currentRewardCards = stateType.GetProperty("CurrentRewardCards", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing CurrentRewardCards property.");
        _bagRemove = _playerBag.PropertyType.GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RelicBag.Remove method.");
        _rollPotionRewardChance = GetNonPublicMethod(stateType, "RollPotionRewardChance");
        _rollPotionReward = GetNonPublicMethod(stateType, "RollPotionReward");
        _rollCardRarity = GetNonPublicMethod(stateType, "RollCardRarity");
        _tryPickAvailableCard = GetNonPublicMethod(stateType, "TryPickAvailableCard");
        _rewardCardBufferAdd = _currentRewardCards.PropertyType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RewardCardBuffer.Add method.");
        _rewardCardBufferClear = _currentRewardCards.PropertyType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RewardCardBuffer.Clear method.");
        _obtainEnumerable = stateType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "Obtain" &&
                              method.GetParameters().Length == 1 &&
                              method.GetParameters()[0].ParameterType != typeof(string));
        _obtainWithImmediateEffects = GetMethod(stateType, "ObtainWithImmediateEffects");
        _resolvePurchasedShopRelic = GetMethod(stateType, "ResolvePurchasedShopRelic");
        _resolvePurchasedShopPotion = GetMethod(stateType, "ResolvePurchasedShopPotion");
        _resolvePurchasedShopCard = GetMethod(stateType, "ResolvePurchasedShopCard");
        _pullEventFrontAndObtain = GetMethod(stateType, "PullEventFrontAndObtain");
    }

    private static GameRng GetSharedEventRng(object state)
    {
        var box = EventRngs.GetValue(state, static key =>
        {
            var rewardsRng = key.GetType().GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(key)
                ?? throw new InvalidOperationException("Missing RewardsRng property.");
            var seed = (uint)(rewardsRng.GetType().GetProperty("Seed", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardsRng)
                ?? throw new InvalidOperationException("Missing RewardsRng.Seed property."));
            var runSeed = unchecked(seed - (uint)GetDeterministicHashCodeLocal("rewards"));
            return new StrongBox<GameRng>(new GameRng(runSeed, "niche"));
        });
        return box.Value;
    }

    private static GameRng GetEventRng(object state, string eventId)
    {
        var normalizedEventId = NormalizeEventId(eventId);
        var boxes = EventRngsById.GetValue(state, static _ => new Dictionary<string, StrongBox<GameRng>>(StringComparer.OrdinalIgnoreCase));
        if (!boxes.TryGetValue(normalizedEventId, out var box))
        {
            var rewardsRng = state.GetType().GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
                ?? throw new InvalidOperationException("Missing RewardsRng property.");
            var seed = (uint)(rewardsRng.GetType().GetProperty("Seed", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardsRng)
                ?? throw new InvalidOperationException("Missing RewardsRng.Seed property."));
            var playerSeed = unchecked(seed - (uint)GetDeterministicHashCodeLocal("rewards"));
            var runSeed = unchecked(playerSeed - (uint)NeowGenerationContext.DefaultPlayerNetId);
            var eventSeed = unchecked(runSeed + (uint)NeowGenerationContext.DefaultPlayerNetId + (uint)GetDeterministicHashCodeLocal(normalizedEventId));
            box = new StrongBox<GameRng>(new GameRng(eventSeed));
            boxes[normalizedEventId] = box;
        }

        return box.Value;
    }

    private static string NormalizeEventId(string eventId)
    {
        var normalized = eventId.Trim();
        return normalized.StartsWith("EVENT.", StringComparison.OrdinalIgnoreCase)
            ? normalized["EVENT.".Length..]
            : normalized;
    }

    private void NormalizeRewardModel(object state)
    {
        var rewardModelProperty = state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var existingRewardModel = rewardModelProperty.GetValue(state)
            ?? throw new InvalidOperationException("RewardModel returned null.");
        var correctedRewardModel = GetOrCreateCorrectedRewardModel(existingRewardModel, state);
        if (ReferenceEquals(existingRewardModel, correctedRewardModel))
        {
            return;
        }

        var field = state.GetType().GetField("<RewardModel>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing RewardModel backing field.");
        field.SetValue(state, correctedRewardModel);
    }

    private object GetOrCreateCorrectedRewardModel(object existingRewardModel, object state)
    {
        var rewardModelType = existingRewardModel.GetType();
        var character = (CharacterId)(rewardModelType.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)?.GetValue(existingRewardModel)
            ?? throw new InvalidOperationException("Missing RewardModel.Character."));
        var playerCount = (int)(state.GetType().GetProperty("PlayerCount", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
            ?? throw new InvalidOperationException("Missing state.PlayerCount."));
        var key = (_dataVersion, character, playerCount);
        lock (RewardModelCacheLock)
        {
            if (CorrectedRewardModelCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var created = CreateCorrectedRewardModel(rewardModelType, character, playerCount, _dataVersion);
            CorrectedRewardModelCache[key] = created;
            return created;
        }
    }

    private static object CreateCorrectedRewardModel(Type rewardModelType, CharacterId character, int playerCount, string dataVersion)
    {
        var dataset = GetRewardDataset(dataVersion);
        var characterPool = dataset.CharacterCardPoolMap.TryGetValue(character, out var characterCards)
            ? characterCards
            : Array.Empty<string>();
        var allCharacterCardPools = dataset.CharacterCardPoolMap.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray());

        var merchantCards = characterPool
            .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsCardAllowedForPlayer(metadata, playerCount) &&
                             metadata.ParsedRarity is not CardRarity.Basic)
            .ToList();
        var merchantCardPoolByType = merchantCards
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedType)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var merchantCardPoolByTypeAndRarity = merchantCards
            .GroupBy(cardId => (dataset.CardMetadataMap[cardId].ParsedType, dataset.CardMetadataMap[cardId].ParsedRarity))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var rewardCardPool = characterPool
            .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsUnlockedRewardCard(metadata, playerCount))
            .ToList();
        var rewardCardPoolByRarity = rewardCardPool
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var prismaticCardPool = dataset.CharacterCardPoolMap.Values
            .SelectMany(static pool => pool)
            .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsUnlockedRewardCard(metadata, playerCount))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prismaticCardPoolByRarity = prismaticCardPool
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var colorlessMerchantPool = dataset.ColorlessCardPoolList
            .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsCardAllowedForPlayer(metadata, playerCount))
            .ToList();
        var colorlessMerchantPoolByRarity = colorlessMerchantPool
            .GroupBy(cardId => dataset.CardMetadataMap[cardId].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var cardRarityMap = dataset.CardMetadataMap
            .Where(pair => IsUnlockedRewardCard(pair.Value, playerCount))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ParsedRarity, StringComparer.OrdinalIgnoreCase);

        var characterPotionPool = GetOfficialCharacterPotionPool(character);
        var potionPool = characterPotionPool.Concat(dataset.SharedPotionPoolList).ToList();
        potionPool = potionPool
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .ToList();
        var potionPoolByRarity = potionPool
            .GroupBy(id => dataset.PotionMetadataMap[id].ParsedRarity)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var ctor = rewardModelType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        return ctor.Invoke([
            character,
            allCharacterCardPools,
            rewardCardPool,
            rewardCardPoolByRarity,
            prismaticCardPool,
            prismaticCardPoolByRarity,
            characterPool,
            merchantCardPoolByType,
            merchantCardPoolByTypeAndRarity,
            dataset.ColorlessCardPoolList,
            colorlessMerchantPool,
            colorlessMerchantPoolByRarity,
            cardRarityMap,
            dataset.CardMetadataMap,
            dataset.RelicMetadataMap,
            potionPool,
            potionPoolByRarity
        ]);
    }

    private static bool IsCardAllowedForPlayer(NeowCardMetadata metadata, int playerCount)
    {
        if (playerCount <= 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.MultiplayerOnly)
        {
            return false;
        }

        if (playerCount > 1 && metadata.ParsedConstraint == CardMultiplayerConstraint.SingleplayerOnly)
        {
            return false;
        }

        return true;
    }

    private static bool IsUnlockedRewardCard(NeowCardMetadata metadata, int playerCount)
    {
        return IsCardAllowedForPlayer(metadata, playerCount) &&
               metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;
    }

    public static ExactRewardStateBridge Create(
        ReplayRun replay,
        OpeningDetailReplayMode openingDetailReplayMode = OpeningDetailReplayMode.ReplayRelicDetails,
        ulong playerNetId = NeowGenerationContext.DefaultPlayerNetId)
    {
        if (!SeedFormatter.TryNormalize(replay.SeedText, out var normalizedSeed, out var seedError))
        {
            throw new InvalidOperationException($"Invalid seed {replay.SeedText}: {seedError}");
        }

        if (!TryParseCharacter(replay.CharacterId, out var character))
        {
            throw new InvalidOperationException($"Unsupported character: {replay.CharacterId}");
        }

        var dataVersion = ResolveRunValidationDataVersion(replay.GameVersion);
        var dataRoot = Path.Combine("data", dataVersion);
        var neowDataPath = Path.Combine(dataRoot, "neow", "options.json");
        var ancientDataPath = Path.Combine(dataRoot, "ancients", File.Exists(Path.Combine(dataRoot, "ancients", "options.zhs.json"))
            ? "options.zhs.json"
            : "options.json");
        var actDataPath = Path.Combine(dataRoot, "sts2", "acts.json");

        var dataset = new NeowEventGeneratorFactory().LoadDataset(neowDataPath);
        var previewer = Sts2RunPreviewer.CreateFromDataFiles(ancientDataPath, actDataPath);
        var worldField = typeof(Sts2RunPreviewer).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing previewer world field.");
        var world = worldField.GetValue(previewer)
            ?? throw new InvalidOperationException("Missing previewer world value.");
        var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
        var request = new Sts2ExactRouteAnalysisRequest
        {
            SeedText = normalizedSeed,
            SeedValue = seedValue,
            Character = character,
            AscensionLevel = replay.Ascension,
            PlayerCount = replay.PlayerCount,
            PlayerNetId = playerNetId,
            Act1OpeningOption = BuildAct1OpeningOption(replay, dataset, seedValue, character)
        };

        var factoryType = typeof(Sts2ExactRouteAnalysis).Assembly.GetType("SeedModel.Sts2.Sts2ExactRouteAnalyzer+RewardRelicStateFactory")
            ?? throw new InvalidOperationException("Missing RewardRelicStateFactory.");
        var factory = Activator.CreateInstance(
                factoryType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [world, dataset, request],
                culture: null)
            ?? throw new InvalidOperationException("Failed to create RewardRelicStateFactory.");
        var create = factoryType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing factory Create.");
        var state = create.Invoke(factory, null)
            ?? throw new InvalidOperationException("Failed to create reward state.");
        ReplayOpeningDetailImmediateEffects(state, request.Act1OpeningOption, openingDetailReplayMode, dataset, replay.PlayerCount);

        return new ExactRewardStateBridge(state, dataVersion);
    }

    private static void ReplayOpeningDetailImmediateEffects(
        object state,
        NeowOptionResult? option,
        OpeningDetailReplayMode openingDetailReplayMode,
        NeowOptionDataset dataset,
        int playerCount)
    {
        if (option == null || openingDetailReplayMode == OpeningDetailReplayMode.None)
        {
            return;
        }

        if (string.Equals(option.RelicId, NeowOptionIds.NeowsBones, StringComparison.OrdinalIgnoreCase))
        {
            ReplayNeowsBonesRewardShuffle(state, dataset, playerCount);
        }

        var replay = GetMethod(state.GetType(), "ReplayImmediatePickupEffectsOnly");
        var relicIds = EnumerateOpeningRelicIds(option).Skip(1);
        if (openingDetailReplayMode == OpeningDetailReplayMode.ReplayLeadPaperweightOnly)
        {
            relicIds = relicIds.Where(relicId => string.Equals(relicId, NeowOptionIds.LeadPaperweight, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var relicId in relicIds)
        {
            replay.Invoke(state, [relicId]);
        }
    }

    private static void ReplayNeowsBonesRewardShuffle(
        object state,
        NeowOptionDataset dataset,
        int playerCount)
    {
        var rewardsRng = state.GetType().GetProperty("RewardsRng", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
            ?? throw new InvalidOperationException("Missing RewardsRng property.");
        var nextInt = rewardsRng.GetType().GetMethod("NextInt", BindingFlags.Instance | BindingFlags.Public, [typeof(int)])
            ?? throw new InvalidOperationException("Missing RewardsRng.NextInt(int).");
        var candidateCount = CountNeowsBonesValidRelics(dataset, playerCount);
        for (var i = candidateCount - 1; i > 0; i--)
        {
            _ = nextInt.Invoke(rewardsRng, [i + 1]);
        }
    }

    private static int CountNeowsBonesValidRelics(NeowOptionDataset dataset, int playerCount)
    {
        return dataset.Options
            .Select(option => option.RelicId)
            .Where(relicId => !string.IsNullOrWhiteSpace(relicId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(relicId => !string.Equals(relicId, NeowOptionIds.NeowsBones, StringComparison.OrdinalIgnoreCase))
            .Where(relicId => IsNeowsBonesRelicAllowed(relicId, playerCount))
            .Count();
    }

    private static bool IsNeowsBonesRelicAllowed(string relicId, int playerCount)
    {
        if (string.Equals(relicId, NeowOptionIds.MassiveScroll, StringComparison.OrdinalIgnoreCase))
        {
            return playerCount > 1;
        }

        if (string.Equals(relicId, NeowOptionIds.SilverCrucible, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relicId, NeowOptionIds.WingedBoots, StringComparison.OrdinalIgnoreCase))
        {
            return playerCount <= 1;
        }

        return true;
    }

    public static IReadOnlyList<string> GetAct1OpeningRelicIds(
        ReplayRun replay,
        ulong playerNetId = NeowGenerationContext.DefaultPlayerNetId)
    {
        if (!SeedFormatter.TryNormalize(replay.SeedText, out var normalizedSeed, out _) ||
            !TryParseCharacter(replay.CharacterId, out var character))
        {
            return Array.Empty<string>();
        }

        var dataVersion = ResolveRunValidationDataVersion(replay.GameVersion);
        var dataset = GetRewardDataset(dataVersion);
        var option = BuildAct1OpeningOption(replay, dataset, SeedFormatter.ToUIntSeed(normalizedSeed), character);
        return option == null
            ? Array.Empty<string>()
            : EnumerateOpeningRelicIds(option).ToArray();
    }

    private static NeowOptionResult? BuildAct1OpeningOption(
        ReplayRun replay,
        NeowOptionDataset dataset,
        uint seedValue,
        CharacterId character)
    {
        var pickedAncientId = replay.Floors
            .Where(floor => floor.Floor == 1 || string.Equals(floor.RoomType, "A", StringComparison.OrdinalIgnoreCase))
            .SelectMany(floor => floor.PickedAncientIds)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (string.IsNullOrWhiteSpace(pickedAncientId))
        {
            return null;
        }

        var generator = new NeowGenerator(dataset);
        var options = generator.Generate(NeowGenerationContext.Create(
            seed: seedValue,
            playerCount: replay.PlayerCount,
            scrollBoxesEligible: true,
            hasRunModifiers: false,
            character: character,
            ascensionLevel: replay.Ascension));

        var option = options.FirstOrDefault(option =>
            string.Equals(option.RelicId, pickedAncientId, StringComparison.OrdinalIgnoreCase));
        return option == null
            ? null
            : WithLoggedOpeningRelics(option, replay);
    }

    private static NeowOptionResult WithLoggedOpeningRelics(NeowOptionResult option, ReplayRun replay)
    {
        var loggedRelics = GetLoggedAct1OpeningRelics(replay, option.RelicId);
        if (loggedRelics.Count <= 1)
        {
            return option;
        }

        var loggedRelicDetails = loggedRelics
            .Skip(1)
            .Select(relicId => new RewardDetail(RewardDetailType.Relic, "Logged opening relic", relicId, relicId))
            .ToArray();
        var nonRelicDetails = option.Details
            .Where(detail => detail.Type != RewardDetailType.Relic)
            .ToArray();
        return option with
        {
            Details = loggedRelicDetails.Concat(nonRelicDetails).ToArray()
        };
    }

    private static IReadOnlyList<string> GetLoggedAct1OpeningRelics(ReplayRun replay, string selectedRelicId)
    {
        var openingFloor = replay.Floors.FirstOrDefault(floor =>
            floor.Floor == 1 || string.Equals(floor.RoomType, "A", StringComparison.OrdinalIgnoreCase));
        if (openingFloor == null)
        {
            return [selectedRelicId];
        }

        var loggedSet = openingFloor.RelicChoices
            .Concat(openingFloor.PickedRelicIds)
            .Concat(openingFloor.PickedAncientIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        loggedSet.Add(selectedRelicId);

        var ordered = replay.FinalRelicIds
            .Where(loggedSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!ordered.Contains(selectedRelicId, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Insert(0, selectedRelicId);
        }

        return ordered;
    }

    private static IEnumerable<string> EnumerateOpeningRelicIds(NeowOptionResult option)
    {
        if (!string.IsNullOrWhiteSpace(option.RelicId))
        {
            yield return option.RelicId;
        }

        foreach (var detail in option.Details)
        {
            if (detail.Type == RewardDetailType.Relic && !string.IsNullOrWhiteSpace(detail.ModelId))
            {
                yield return detail.ModelId;
            }
        }
    }

    public void ConsumeRegularCombat()
    {
        _consumeRegularCombat.Invoke(_state, null);
    }

    public void ConsumeEventCardReward(bool direct)
    {
        if (direct)
        {
            _ = RollThreeRewardCards(CardRarityOddsType.RegularEncounter);
            return;
        }

        _ = RollCombatLikeRewards(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false);
    }

    public GeneratedRewardOutcome RollElite(int actNumber)
    {
        var generatedCards = PreviewCombatCards(CardRarityOddsType.EliteEncounter, isElite: true, isBoss: false);
        var generatedRelics = InvokeStringEnumerable(_showElite, actNumber);
        return new GeneratedRewardOutcome(generatedCards, generatedRelics);
    }

    public void ConsumeBossRewards()
    {
        _ = RollBossRewardsInOfficialOrder();
    }

    public IReadOnlyList<string> ShowTreasure(int actNumber)
    {
        return InvokeStringEnumerable(_showTreasure, actNumber);
    }

    public IReadOnlyList<string> ShowElite(int actNumber)
    {
        return InvokeStringEnumerable(_showElite, actNumber);
    }

    public IReadOnlyList<string> PreviewEventRelicPull(int actNumber, string? rarityOverride = null)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        var relic = bridge.PullEventFrontAndObtain(actNumber, rarityOverride);
        return string.IsNullOrWhiteSpace(relic) ? Array.Empty<string>() : [relic];
    }

    public IReadOnlyList<EventRelicPullProbe> PreviewEventRelicPullVariants(int actNumber)
    {
        return new (string Label, string? Rarity)[]
            {
                ("rolled", null),
                ("common", "Common"),
                ("uncommon", "Uncommon"),
                ("rare", "Rare")
            }
            .Select(item => new EventRelicPullProbe(item.Label, item.Rarity, PreviewEventRelicPull(actNumber, item.Rarity)))
            .ToArray();
    }

    public ShopPreview ShowShopPreview(int actNumber)
    {
        return (ShopPreview)(_showShopPreview.Invoke(_state, [actNumber])
            ?? throw new InvalidOperationException("ShowShopPreview returned null."));
    }

    public ShopPreview PreviewShopWithoutChangingBags(int actNumber)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var preview = (ShopPreview)(_showShopPreview.Invoke(clone, [actNumber])
            ?? throw new InvalidOperationException("ShowShopPreview returned null."));
        SyncRngCounter(_rewardsRng.GetValue(clone), _rewardsRng.GetValue(_state), "RewardsRng");
        SyncRngCounter(_shopsRng.GetValue(clone), _shopsRng.GetValue(_state), "ShopsRng");
        return preview;
    }

    public ShopPreview PreviewShopWithActionsWithoutChangingBags(int actNumber, IEnumerable<ReplayShopAction> actions)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var preview = (ShopPreview)(_showShopPreview.Invoke(clone, [actNumber])
            ?? throw new InvalidOperationException("ShowShopPreview returned null."));
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        var coloredCards = preview.ColoredCards.ToList();
        var colorlessCards = preview.ColorlessCards.ToList();
        var relics = preview.Relics.ToList();
        var potions = preview.Potions.ToList();

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.ItemId))
            {
                continue;
            }

            bridge.ApplyShopAction(action, actNumber);
            var current = bridge.ReadShownShopPreviewSnapshot(preview);
            AddNewEntries(coloredCards, current.ColoredCards);
            AddNewEntries(colorlessCards, current.ColorlessCards);
            AddNewEntries(relics, current.Relics);
            AddNewEntries(potions, current.Potions);
        }

        SyncRngCounter(_rewardsRng.GetValue(clone), _rewardsRng.GetValue(_state), "RewardsRng");
        SyncRngCounter(_shopsRng.GetValue(clone), _shopsRng.GetValue(_state), "ShopsRng");
        return preview with
        {
            ColoredCards = coloredCards,
            ColorlessCards = colorlessCards,
            Relics = relics,
            Potions = potions
        };
    }

    public ShopSessionPreview PreviewShopSessionWithoutChangingBags(
        int actNumber,
        IReadOnlyList<ReplayShopAction> loggedActions,
        Func<ShopPreview, IReadOnlyList<ReplayShopAction>> inferMissingActions)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var initial = (ShopPreview)(_showShopPreview.Invoke(clone, [actNumber])
            ?? throw new InvalidOperationException("ShowShopPreview returned null."));
        var inferred = inferMissingActions(initial);
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        var coloredCards = initial.ColoredCards.ToList();
        var colorlessCards = initial.ColorlessCards.ToList();
        var relics = initial.Relics.ToList();
        var potions = initial.Potions.ToList();

        foreach (var action in loggedActions.Concat(inferred))
        {
            if (string.IsNullOrWhiteSpace(action.ItemId))
            {
                continue;
            }

            bridge.ApplyShopAction(action, actNumber);
            var current = bridge.ReadShownShopPreviewSnapshot(initial);
            AddNewEntries(coloredCards, current.ColoredCards);
            AddNewEntries(colorlessCards, current.ColorlessCards);
            AddNewEntries(relics, current.Relics);
            AddNewEntries(potions, current.Potions);
        }

        SyncRngCounter(_rewardsRng.GetValue(clone), _rewardsRng.GetValue(_state), "RewardsRng");
        SyncRngCounter(_shopsRng.GetValue(clone), _shopsRng.GetValue(_state), "ShopsRng");
        var visible = initial with
        {
            ColoredCards = coloredCards,
            ColorlessCards = colorlessCards,
            Relics = relics,
            Potions = RecomputeShopPotionsInOfficialOrder(initial.Potions)
        };
        return new ShopSessionPreview(initial, visible, inferred);
    }

    private IReadOnlyList<ShopPotionEntry> RecomputeShopPotionsInOfficialOrder(IReadOnlyList<ShopPotionEntry> fallback)
    {
        if (fallback.Count == 0)
        {
            return fallback;
        }

        var shopsRng = _shopsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("ShopsRng was not a GameRng.");
        var afterCurrent = shopsRng.Counter;
        var potionStartCounter = afterCurrent - (fallback.Count * 3);
        if (potionStartCounter < 0)
        {
            return fallback;
        }

        var rng = new GameRng(shopsRng.Seed, potionStartCounter);
        var rolled = new List<(string Id, PotionRarity Rarity)>(fallback.Count);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fallback.Count; i++)
        {
            var rarity = RollOutOfCombatPotionRarity(rng);
            var candidates = GetShopPotionCandidates(rarity, selected);
            if (candidates.Count == 0)
            {
                candidates = GetShopPotionCandidates(null, selected);
            }

            var potionId = rng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(potionId))
            {
                continue;
            }

            selected.Add(potionId);
            rolled.Add((potionId, rarity));
        }

        var result = new List<ShopPotionEntry>(rolled.Count);
        foreach (var (potionId, rarity) in rolled)
        {
            result.Add(new ShopPotionEntry
            {
                Id = potionId,
                Price = RollPotionPrice(rng, rarity)
            });
        }

        return result;
    }

    public void ConsumeTreasureGoldOnly()
    {
        ConsumeRewardRng(1);
    }

    public void ConsumeRewardRng(int count)
    {
        _consumeRewardRng.Invoke(_state, [count]);
    }

    public void ApplyShopAction(ReplayShopAction action, int actNumber)
    {
        if (string.IsNullOrWhiteSpace(action.ItemId))
        {
            return;
        }

        if (string.Equals(action.ActionType, "buy_relic", StringComparison.OrdinalIgnoreCase))
        {
            _resolvePurchasedShopRelic.Invoke(_state, [action.ItemId, actNumber]);
        }
        else if (string.Equals(action.ActionType, "buy_potion", StringComparison.OrdinalIgnoreCase))
        {
            _resolvePurchasedShopPotion.Invoke(_state, [action.ItemId]);
        }
        else if (string.Equals(action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase))
        {
            _resolvePurchasedShopCard.Invoke(_state, [action.ItemId]);
        }
    }

    public string? PullEventFrontAndObtain(int actNumber, string? rarityOverride = null)
    {
        return _pullEventFrontAndObtain.Invoke(_state, [actNumber, rarityOverride, null])?.ToString();
    }

    private ShopPreview ReadShownShopPreviewSnapshot(ShopPreview fallback)
    {
        var stateType = _state.GetType();
        var cards = ReadShownShopCardEntriesByType(stateType);
        var relics = ReadShopEntries<ShopRelicEntry>(stateType, "ShownShopRelics");
        var potions = ReadShopEntries<ShopPotionEntry>(stateType, "ShownShopPotions");
        return fallback with
        {
            ColoredCards = cards.ColoredCards,
            ColorlessCards = cards.ColorlessCards,
            Relics = relics,
            Potions = potions
        };
    }

    public void Obtain(IReadOnlyList<string> relicIds)
    {
        if (relicIds.Count == 0)
        {
            return;
        }

        foreach (var relicId in relicIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            _obtainWithImmediateEffects.Invoke(_state, [relicId]);
        }
    }

    public void RemoveFromRelicBags(IReadOnlyList<string> relicIds)
    {
        if (relicIds.Count == 0)
        {
            return;
        }

        var sharedBag = _sharedBag.GetValue(_state)
            ?? throw new InvalidOperationException("Missing shared bag.");
        var playerBag = _playerBag.GetValue(_state)
            ?? throw new InvalidOperationException("Missing player bag.");
        foreach (var relicId in relicIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            _bagRemove.Invoke(sharedBag, [relicId]);
            _bagRemove.Invoke(playerBag, [relicId]);
        }
    }

    public IReadOnlyList<string> PreviewCombatCards(CardRarityOddsType oddsType, bool isElite, bool isBoss)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        return new ExactRewardStateBridge(clone, _dataVersion).RollCombatLikeRewards(oddsType, isElite, isBoss);
    }

    public IReadOnlyList<RewardRngSkipProbe> PreviewCombatSkips(CardRarityOddsType oddsType, bool isElite, bool isBoss, int maxSkip)
    {
        var result = new List<RewardRngSkipProbe>(maxSkip + 1);
        for (var skip = 0; skip <= maxSkip; skip++)
        {
            var clone = _clone.Invoke(_state, null)
                ?? throw new InvalidOperationException("Reward state clone returned null.");
            var bridge = new ExactRewardStateBridge(clone, _dataVersion);
            bridge.ConsumeRewardRng(skip);
            result.Add(new RewardRngSkipProbe(skip, bridge.RollCombatLikeRewards(oddsType, isElite, isBoss)));
        }

        return result;
    }

    public IReadOnlyList<RewardRngSkipProbe> PreviewCardsAfterManualRewardPreludeSkips(CardRarityOddsType oddsType, int maxSkip)
    {
        var result = new List<RewardRngSkipProbe>(maxSkip + 1);
        for (var skip = 0; skip <= maxSkip; skip++)
        {
            var clone = _clone.Invoke(_state, null)
                ?? throw new InvalidOperationException("Reward state clone returned null.");
            var bridge = new ExactRewardStateBridge(clone, _dataVersion);
            bridge.ConsumeRewardRng(skip);
            result.Add(new RewardRngSkipProbe(skip, bridge.RollThreeRewardCards(oddsType)));
        }

        return result;
    }

    public IReadOnlyList<string> PreviewCharacterNonCombatCards(CardRarityOddsType oddsType, int cardCount, CardRarity? rarityFilter = null)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        return new ExactRewardStateBridge(clone, _dataVersion).RollNonCombatCards(
            GetCharacterRewardPool(rarityFilter),
            oddsType,
            cardCount,
            useBaseOdds: true);
    }

    public void ConsumeCharacterNonCombatCards(CardRarityOddsType oddsType, int cardCount, CardRarity? rarityFilter = null)
    {
        _ = RollNonCombatCards(
            GetCharacterRewardPool(rarityFilter),
            oddsType,
            cardCount,
            useBaseOdds: true);
    }

    public IReadOnlyList<string> PreviewCharacterNonCombatCardsWithEventRng(int cardCount, CardRarity rarityFilter)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        bridge.SyncEventRngFrom(_eventRng);
        return bridge.RollNonCombatCardsWithRng(
            bridge.GetCharacterRewardPool(rarityFilter),
            cardCount,
            bridge._eventRng);
    }

    public IReadOnlyList<RewardRngSkipProbe> PreviewCharacterNonCombatCardsWithEventRngSkips(
        int cardCount,
        CardRarity rarityFilter,
        int maxSkip)
    {
        var result = new List<RewardRngSkipProbe>(maxSkip + 1);
        for (var skip = 0; skip <= maxSkip; skip++)
        {
            var clone = _clone.Invoke(_state, null)
                ?? throw new InvalidOperationException("Reward state clone returned null.");
            var bridge = new ExactRewardStateBridge(clone, _dataVersion);
            bridge.SyncEventRngFrom(_eventRng);
            bridge._eventRng.FastForward(bridge._eventRng.Counter + skip);
            result.Add(new RewardRngSkipProbe(
                skip,
                bridge.RollNonCombatCardsWithRng(
                    bridge.GetCharacterRewardPool(rarityFilter),
                    cardCount,
                    bridge._eventRng)));
        }

        return result;
    }

    public IReadOnlyList<CrystalSphereRewardProbe> PreviewCrystalSphereRewardVariants(
        IReadOnlyList<string> potionChoices,
        int goldGained,
        int cardCount,
        CardRarity rarityFilter)
    {
        var result = new List<CrystalSphereRewardProbe>();
        var variants = BuildCrystalSphereRewardOrderVariants(
            hasPotion: potionChoices.Count > 0,
            hasGold: goldGained > 0);
        foreach (var variant in variants)
        {
            var clone = _clone.Invoke(_state, null)
                ?? throw new InvalidOperationException("Reward state clone returned null.");
            var bridge = new ExactRewardStateBridge(clone, _dataVersion);
            bridge.SyncCrystalSphereEventRngFrom(GetCrystalSphereEventRng());
            var generatedPotions = bridge.ConsumeCrystalSphereRewardsBeforeCards(potionChoices, variant.GoldBeforeCard, goldGained);
            var generatedCards = bridge.RollNonCombatCardsWithRng(
                bridge.GetCharacterRewardPool(rarityFilter),
                cardCount,
                bridge.GetCrystalSphereEventRng());
            result.Add(new CrystalSphereRewardProbe(
                variant.Label,
                generatedCards,
                generatedPotions,
                variant.GoldBeforeCard));
        }

        return result;
    }

    public void ConsumeCrystalSphereRewardVariant(
        IReadOnlyList<string> potionChoices,
        bool goldBeforeCard,
        int goldGained,
        int cardCount,
        CardRarity rarityFilter)
    {
        _ = ConsumeCrystalSphereRewardsBeforeCards(potionChoices, goldBeforeCard, goldGained);
        _ = RollNonCombatCardsWithRng(
            GetCharacterRewardPool(rarityFilter),
            cardCount,
            GetCrystalSphereEventRng());
        if (!goldBeforeCard && goldGained > 0)
        {
            var rng = GetCrystalSphereEventRng();
            _ = rng.NextInt(goldGained, goldGained + 1);
        }
    }

    private IReadOnlyList<string> ConsumeCrystalSphereRewardsBeforeCards(
        IReadOnlyList<string> potionChoices,
        bool goldBeforeCard,
        int goldGained)
    {
        var rng = GetCrystalSphereEventRng();
        var generatedPotions = new List<string>();
        foreach (var potion in potionChoices)
        {
            var rarity = ParsePotionRarity(PotionMetadataIndex.Get(potion, _dataVersion)?.Rarity);
            var generated = RollCrystalSpherePotion(rng, rarity);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                generatedPotions.Add(generated);
            }
        }

        if (goldBeforeCard && goldGained > 0)
        {
            _ = rng.NextInt(goldGained, goldGained + 1);
        }

        return generatedPotions;
    }

    private static PotionRarity ParsePotionRarity(string? rarity) =>
        Enum.TryParse(rarity, ignoreCase: true, out PotionRarity parsed)
            ? parsed
            : PotionRarity.Common;

    private string? RollCrystalSpherePotion(GameRng rng, PotionRarity rarity)
    {
        var dataset = GetRewardDataset(_dataVersion);
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var pool = ((IEnumerable)(rewardModel.GetType().GetProperty("PotionPool", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardModel)
                ?? throw new InvalidOperationException("Missing RewardModel.PotionPool.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         dataset.PotionMetadataMap.TryGetValue(id, out var metadata) &&
                         metadata.ParsedRarity == rarity)
            .ToArray();
        return rng.NextItem(pool);
    }

    private static IReadOnlyList<(string Label, bool GoldBeforeCard)> BuildCrystalSphereRewardOrderVariants(bool hasPotion, bool hasGold)
    {
        var labels = new List<(string Label, bool GoldBeforeCard)>();
        if (hasPotion && hasGold)
        {
            labels.Add(("potion-gold-card", true));
            labels.Add(("potion-card-gold", false));
            return labels;
        }

        labels.Add((hasPotion ? "potion-card" : hasGold ? "gold-card" : "card-only", hasGold));
        return labels;
    }

    public void ConsumeCharacterNonCombatCardsWithEventRng(int cardCount, CardRarity rarityFilter)
    {
        _ = RollNonCombatCardsWithRng(
            GetCharacterRewardPool(rarityFilter),
            cardCount,
            _eventRng);
    }

    public void ConsumeCrystalSphereSetup()
    {
        // CrystalSphere.CalculateVars consumes one event RNG roll for the gold cost.
        var eventRng = GetCrystalSphereEventRng();
        eventRng.NextInt(1, 50);
        ConsumeCrystalSphereMinigamePlacement(eventRng);
    }

    private GameRng GetCrystalSphereEventRng() => GetEventRng(_state, "CRYSTAL_SPHERE");

    private void ConsumeCrystalSphereMinigamePlacement(GameRng eventRng)
    {
        var grid = new CrystalSpherePlacementGrid(11, 11);
        var corners = new[]
        {
            (X: 0, Y: 0),
            (X: 10, Y: 0),
            (X: 10, Y: 10),
            (X: 0, Y: 10)
        };
        var revealed = corners;
        for (var i = 0; i < 2; i++)
        {
            revealed = revealed
                .Concat(revealed.SelectMany(cell => CrystalSphereHorizontalCells(cell.X, cell.Y)))
                .Concat(revealed.SelectMany(cell => CrystalSphereVerticalCells(cell.X, cell.Y)))
                .ToArray();
        }

        foreach (var cell in revealed)
        {
            grid.Clear(cell.X, cell.Y);
        }

        var itemSizes = new[]
        {
            (W: 4, H: 4),
            (W: 1, H: 3),
            (W: 1, H: 3),
            (W: 2, H: 2),
            (W: 2, H: 2),
            (W: 2, H: 2),
            (W: 2, H: 2),
            (W: 2, H: 2),
            (W: 1, H: 1),
            (W: 1, H: 1),
            (W: 1, H: 1),
            (W: 1, H: 1),
            (W: 1, H: 1),
            (W: 2, H: 1),
            (W: 2, H: 1)
        };
        foreach (var (width, height) in itemSizes)
        {
            var candidates = grid.GetPlacementCandidates(width, height);
            if (candidates.Count == 0)
            {
                continue;
            }

            var position = eventRng.NextItem(candidates);
            grid.Place(position.X, position.Y, width, height);
        }
    }

    private static IEnumerable<(int X, int Y)> CrystalSphereHorizontalCells(int x, int y)
    {
        for (var dx = -1; dx <= 1; dx += 2)
        {
            var nextX = x + dx;
            if (nextX >= 0 && nextX < 11)
            {
                yield return (nextX, y);
            }
        }
    }

    private static IEnumerable<(int X, int Y)> CrystalSphereVerticalCells(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy += 2)
        {
            var nextY = y + dy;
            if (nextY >= 0 && nextY < 11)
            {
                yield return (x, nextY);
            }
        }
    }

    public IReadOnlyList<string> PreviewColorlessNonCombatCards(CardRarityOddsType oddsType, int cardCount)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        return new ExactRewardStateBridge(clone, _dataVersion).RollNonCombatCards(
            GetColorlessRewardPool(),
            oddsType,
            cardCount,
            useBaseOdds: true);
    }

    public void ConsumeColorlessNonCombatCards(CardRarityOddsType oddsType, int cardCount)
    {
        _ = RollNonCombatCards(
            GetColorlessRewardPool(),
            oddsType,
            cardCount,
            useBaseOdds: true);
    }

    public IReadOnlyList<FutureOfPotionsTypeProbe> PreviewFutureOfPotionsVariants(string discardedPotionId)
    {
        var result = new List<FutureOfPotionsTypeProbe>();
        foreach (var cardType in GetFutureOfPotionsAllowedTypes(discardedPotionId, _dataVersion))
        {
            var clone = _clone.Invoke(_state, null)
                ?? throw new InvalidOperationException("Reward state clone returned null.");
            var bridge = new ExactRewardStateBridge(clone, _dataVersion);
            result.Add(new FutureOfPotionsTypeProbe(cardType, bridge.RollFutureOfPotionsCards(discardedPotionId, cardType)));
        }

        return result;
    }

    public IReadOnlyList<FutureOfPotionsTraceProbe> PreviewFutureOfPotionsTraceVariants(string discardedPotionId, int maxPreludeSkip = 4)
    {
        var result = new List<FutureOfPotionsTraceProbe>();
        foreach (var cardType in GetFutureOfPotionsAllowedTypes(discardedPotionId, _dataVersion))
        {
            for (var preludeSkip = 0; preludeSkip <= maxPreludeSkip; preludeSkip++)
            {
                var clone = _clone.Invoke(_state, null)
                    ?? throw new InvalidOperationException("Reward state clone returned null.");
                var bridge = new ExactRewardStateBridge(clone, _dataVersion);
                bridge.ConsumeRewardRng(preludeSkip);
                var trace = bridge.RollFutureOfPotionsCardTrace(discardedPotionId, cardType);
                result.Add(new FutureOfPotionsTraceProbe(cardType, preludeSkip, trace.Select(item => item.PickedCardId).ToArray(), trace));
            }
        }

        return result;
    }

    public void ConsumeFutureOfPotionsReward(string discardedPotionId, string cardType)
    {
        _ = RollFutureOfPotionsCards(discardedPotionId, cardType);
    }

    public string? ConsumeDelicateFrondCombatPotionGenerationPreview(ReplayRun replay)
    {
        if (!TryParseCharacter(replay.CharacterId, out var character))
        {
            return null;
        }

        var ownedRelicsValue = _state.GetType().GetProperty("OwnedRelics", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state);
        if (ownedRelicsValue is not IEnumerable ownedRelics)
        {
            return null;
        }

        var hasDelicateFrond = ownedRelics.Cast<object>()
            .Select(item => item?.ToString() ?? string.Empty)
            .Any(id => string.Equals(id, "DELICATE_FROND", StringComparison.OrdinalIgnoreCase));
        if (!hasDelicateFrond)
        {
            return null;
        }

        if (!SeedFormatter.TryNormalize(replay.SeedText, out var normalizedSeed, out _))
        {
            return null;
        }

        var rng = new RunRngSet(SeedFormatter.ToUIntSeed(normalizedSeed)).Get("combat_potion_generation");
        var currentCounter = GetDelicateFrondCombatPotionCounter();
        rng.FastForward(currentCounter);
        var potionId = RollOutOfCombatPotion(rng, character);
        AdvanceDelicateFrondCombatPotionCounter(rng.Counter);
        return potionId;
    }

    public string? ConsumePotionCourierRansackReward()
    {
        var dataset = GetRewardDataset(_dataVersion);
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var potionPool = ((IEnumerable)(rewardModel.GetType().GetProperty("PotionPool", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardModel)
                ?? throw new InvalidOperationException("Missing RewardModel.PotionPool.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                         dataset.PotionMetadataMap.TryGetValue(id, out var metadata) &&
                         metadata.ParsedRarity == PotionRarity.Uncommon)
            .ToArray();
        var rng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        return rng.NextItem(potionPool);
    }

    public IReadOnlyList<string> PreviewDollRoomRandomRelic()
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        bridge.SyncEventRngFrom(_eventRng);
        return bridge.ConsumeDollRoomRandomRelic();
    }

    public IReadOnlyList<string> ConsumeDollRoomRandomRelic()
    {
        var relics = new[]
        {
            "DAUGHTER_OF_THE_WIND",
            "MR_STRUGGLES",
            "BING_BONG"
        };
        var picked = _eventRng.NextItem(relics);
        return string.IsNullOrWhiteSpace(picked) ? Array.Empty<string>() : [picked];
    }

    private void SyncEventRngFrom(GameRng source)
    {
        _eventRng.FastForward(source.Counter);
    }

    private void SyncCrystalSphereEventRngFrom(GameRng source)
    {
        GetCrystalSphereEventRng().FastForward(source.Counter);
    }

    public IReadOnlyList<RewardCardPickTrace> PreviewCombatCardPickTrace(CardRarityOddsType oddsType, bool isElite, bool isBoss)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        return bridge.RollCombatLikeRewardTrace(oddsType, isElite, isBoss);
    }

    public CombatRewardPreludeTrace PreviewCombatRewardPrelude(bool isElite, bool isBoss)
    {
        var clone = _clone.Invoke(_state, null)
            ?? throw new InvalidOperationException("Reward state clone returned null.");
        var bridge = new ExactRewardStateBridge(clone, _dataVersion);
        return bridge.RollCombatRewardPreludeTrace(isElite, isBoss);
    }

    internal static string ResolveRunValidationDataVersion(string? gameVersion)
    {
        if (!string.IsNullOrWhiteSpace(gameVersion) &&
            File.Exists(Path.Combine("data", gameVersion, "neow", "options.json")))
        {
            return gameVersion;
        }

        return DefaultRunValidationDataVersion;
    }

    private static NeowOptionDataset GetRewardDataset(string dataVersion)
    {
        lock (RewardModelCacheLock)
        {
            if (RewardDatasets.TryGetValue(dataVersion, out var cached))
            {
                return cached;
            }

            var dataset = new NeowEventGeneratorFactory().LoadDataset(Path.Combine("data", dataVersion, "neow", "options.json"));
            RewardDatasets[dataVersion] = dataset;
            return dataset;
        }
    }

    private IReadOnlyList<string> RollCombatLikeRewards(CardRarityOddsType oddsType, bool isElite, bool isBoss)
    {
        var hasPotionReward = (bool)(_rollPotionRewardChance.Invoke(_state, [isElite])
            ?? throw new InvalidOperationException("RollPotionRewardChance returned null."));
        ConsumeRewardRng(1);

        if (hasPotionReward)
        {
            _rollPotionReward.Invoke(_state, null);
        }

        return RollThreeRewardCards(oddsType);
    }

    private IReadOnlyList<string> RollBossRewardsInOfficialOrder()
    {
        var hasPotionReward = (bool)(_rollPotionRewardChance.Invoke(_state, [false])
            ?? throw new InvalidOperationException("RollPotionRewardChance returned null."));
        RollBossGoldReward();
        if (hasPotionReward)
        {
            _rollPotionReward.Invoke(_state, null);
        }

        return RollThreeRewardCards(CardRarityOddsType.BossEncounter);
    }

    private IReadOnlyList<RewardCardPickTrace> RollCombatLikeRewardTrace(CardRarityOddsType oddsType, bool isElite, bool isBoss)
    {
        var hasPotionReward = (bool)(_rollPotionRewardChance.Invoke(_state, [isElite])
            ?? throw new InvalidOperationException("RollPotionRewardChance returned null."));
        ConsumeRewardRng(1);

        if (hasPotionReward)
        {
            _rollPotionReward.Invoke(_state, null);
        }

        return RollThreeRewardCardTraces(oddsType);
    }

    private CombatRewardPreludeTrace RollCombatRewardPreludeTrace(bool isElite, bool isBoss)
    {
        var counterBeforeBossGold = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var counterBeforePotionChance = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var potionChanceBefore = GetPotionChance();
        var hasPotionReward = (bool)(_rollPotionRewardChance.Invoke(_state, [isElite])
            ?? throw new InvalidOperationException("RollPotionRewardChance returned null."));
        var counterAfterPotionChance = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var potionChanceAfter = GetPotionChance();

        var counterBeforeGold = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        ConsumeRewardRng(1);
        var counterAfterGold = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");

        var counterBeforePotionReward = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        if (hasPotionReward)
        {
            _rollPotionReward.Invoke(_state, null);
        }

        var counterAfterPotionReward = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        return new CombatRewardPreludeTrace(
            counterBeforeBossGold,
            counterBeforePotionChance,
            counterAfterPotionChance,
            potionChanceBefore,
            potionChanceAfter,
            hasPotionReward,
            counterBeforeGold,
            counterAfterGold,
            counterBeforePotionReward,
            counterAfterPotionReward,
            GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng"));
    }

    private IReadOnlyList<string> RollThreeRewardCards(CardRarityOddsType oddsType)
    {
        var cards = new List<string>(3);
        for (var i = 0; i < 3; i++)
        {
            var cardId = RollOneRewardCardWithoutAutoClear(oddsType);
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                cards.Add(cardId);
            }
        }

        ClearRewardCardBuffer();
        return cards;
    }

    private IReadOnlyList<string> RollNonCombatCardsWithRng(
        IReadOnlyList<string> pool,
        int cardCount,
        GameRng rng)
    {
        var cards = new List<string>(Math.Max(0, cardCount));
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cardCount; i++)
        {
            var available = pool
                .Where(cardId => !selected.Contains(cardId))
                .ToArray();
            if (available.Length == 0)
            {
                break;
            }

            var cardId = rng.NextItem(available);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                break;
            }

            selected.Add(cardId);
            cards.Add(cardId);
            // CardFactory.CreateForReward rolls upgrade odds after every generated
            // reward card unless CardCreationFlags.NoUpgradeRoll is set. Crystal
            // Sphere does not set that flag, so the event RNG advances once per
            // card even when the card is rare or ultimately not upgraded.
            _ = rng.NextFloat();
        }

        ClearRewardCardBuffer();
        return cards;
    }

    private IReadOnlyList<string> RollNonCombatCards(
        IReadOnlyList<string> pool,
        CardRarityOddsType oddsType,
        int cardCount,
        bool useBaseOdds)
    {
        var cards = new List<string>(Math.Max(0, cardCount));
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cardCount; i++)
        {
            var cardId = RollOneNonCombatCard(pool, selected, oddsType, useBaseOdds);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                break;
            }

            selected.Add(cardId);
            cards.Add(cardId);
        }

        ClearRewardCardBuffer();
        return cards;
    }

    private string? RollOneNonCombatCard(
        IReadOnlyList<string> pool,
        ISet<string> selected,
        CardRarityOddsType oddsType,
        bool useBaseOdds)
    {
        var available = pool
            .Where(cardId => !selected.Contains(cardId))
            .ToArray();
        if (available.Length == 0)
        {
            return null;
        }

        var candidates = available;
        if (oddsType != CardRarityOddsType.Uniform)
        {
            var rarity = useBaseOdds
                ? RollCardRarityWithBaseOdds(oddsType)
                : (CardRarity)(_rollCardRarity.Invoke(_state, [oddsType])
                    ?? throw new InvalidOperationException("RollCardRarity returned null."));
            var allowedRarities = available
                .Select(cardId => CardMetadataIndex.Get(cardId, _dataVersion)?.Rarity)
                .Select(ParseCardRarity)
                .Where(cardRarity => cardRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                .ToHashSet();

            var seenRarities = new HashSet<CardRarity> { rarity };
            while (!allowedRarities.Contains(rarity) && rarity != CardRarity.None)
            {
                rarity = GetNextHighestRarity(rarity);
                if (!seenRarities.Add(rarity))
                {
                    rarity = CardRarity.None;
                    break;
                }
            }

            candidates = rarity == CardRarity.None
                ? Array.Empty<string>()
                : available
                    .Where(cardId => ParseCardRarity(CardMetadataIndex.Get(cardId, _dataVersion)?.Rarity) == rarity)
                    .ToArray();
        }

        if (candidates.Length == 0)
        {
            return null;
        }

        var rng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        var cardId = rng.NextItem(candidates);
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            AddRewardCardToBuffer(cardId);
        }

        return cardId;
    }

    private IReadOnlyList<string> RollFutureOfPotionsCards(string discardedPotionId, string cardType)
    {
        var targetRarity = GetFutureOfPotionsCardRarity(discardedPotionId);
        var cards = new List<string>(3);
        for (var i = 0; i < 3; i++)
        {
            var candidates = GetAvailableFutureOfPotionsCandidates(targetRarity, cardType);
            if (candidates.Count == 0)
            {
                break;
            }

            var rng = _rewardsRng.GetValue(_state) as GameRng
                ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
            var cardId = rng.NextItem(candidates);
            if (string.IsNullOrWhiteSpace(cardId))
            {
                break;
            }

            AddRewardCardToBuffer(cardId);
            cards.Add(cardId);
        }

        ClearRewardCardBuffer();
        return cards;
    }

    private IReadOnlyList<RewardCardPickTrace> RollFutureOfPotionsCardTrace(string discardedPotionId, string cardType)
    {
        var targetRarity = GetFutureOfPotionsCardRarity(discardedPotionId);
        var traces = new List<RewardCardPickTrace>(3);
        for (var i = 0; i < 3; i++)
        {
            var trace = RollOneFutureOfPotionsCardTraceWithoutAutoClear(targetRarity, cardType, slot: i + 1);
            if (trace == null)
            {
                break;
            }

            traces.Add(trace);
        }

        ClearRewardCardBuffer();
        return traces;
    }

    private RewardCardPickTrace? RollOneFutureOfPotionsCardTraceWithoutAutoClear(CardRarity rarity, string cardType, int slot)
    {
        var counterBeforePick = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var candidates = GetAvailableFutureOfPotionsCandidates(rarity, cardType);
        var rawPickIndex = candidates.Count > 0
            ? PredictNextInt(_rewardsRng.GetValue(_state), candidates.Count, "RewardsRng")
            : -1;
        if (candidates.Count == 0)
        {
            return null;
        }

        var rng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        var cardId = rng.NextItem(candidates);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        AddRewardCardToBuffer(cardId);
        var counterAfterPick = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        return new RewardCardPickTrace(
            slot,
            rarity.ToString(),
            GetCardRareOffset(),
            GetCardRareOffset(),
            counterBeforePick,
            counterBeforePick,
            counterBeforePick,
            counterAfterPick,
            rawPickIndex,
            IndexOf(candidates, cardId),
            cardId,
            candidates);
    }

    private IReadOnlyList<string> GetAvailableFutureOfPotionsCandidates(CardRarity rarity, string requiredType)
    {
        var dataset = GetRewardDataset(_dataVersion);
        var current = ReadCurrentRewardCards().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var characterPool = dataset.CharacterCardPoolMap.TryGetValue(_character, out var cards)
            ? cards
            : Array.Empty<string>();
        return characterPool
            .Where(cardId => dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
                             IsCardAllowedForPlayer(metadata, _playerCount) &&
                             metadata.ParsedRarity == rarity &&
                             string.Equals(metadata.Type, requiredType, StringComparison.OrdinalIgnoreCase) &&
                             !current.Contains(cardId))
            .ToArray();
    }

    private IReadOnlyList<RewardCardPickTrace> RollThreeRewardCardTraces(CardRarityOddsType oddsType)
    {
        var traces = new List<RewardCardPickTrace>(3);
        for (var i = 0; i < 3; i++)
        {
            var trace = RollOneRewardCardTraceWithoutAutoClear(oddsType, slot: i + 1);
            if (trace != null)
            {
                traces.Add(trace);
            }
        }

        ClearRewardCardBuffer();
        return traces;
    }

    private string? RollOneRewardCardWithoutAutoClear(CardRarityOddsType oddsType)
    {
        var rarity = RollCardRarityForEncounter(oddsType);
        while (true)
        {
            var args = new object?[] { rarity, null };
            var picked = (bool)(_tryPickAvailableCard.Invoke(_state, args)
                ?? throw new InvalidOperationException("TryPickAvailableCard returned null."));
            if (picked)
            {
                var cardId = args[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    AddRewardCardToBuffer(cardId);
                    ConsumeRewardRng(1);
                    return cardId;
                }
            }

            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return null;
            }
        }
    }

    private RewardCardPickTrace? RollOneRewardCardTraceWithoutAutoClear(CardRarityOddsType oddsType, int slot)
    {
        var rareOffsetBeforeRarity = GetCardRareOffset();
        var counterBeforeRarity = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var rarity = RollCardRarityForEncounter(oddsType);
        var counterAfterRarity = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
        var rareOffsetAfterRarity = GetCardRareOffset();
        while (true)
        {
            var args = new object?[] { rarity, null };
            var candidates = GetAvailableRewardCardCandidates(rarity);
            var counterBeforePick = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
            var rawPickIndex = candidates.Count > 0
                ? PredictNextInt(_rewardsRng.GetValue(_state), candidates.Count, "RewardsRng")
                : -1;
            var picked = (bool)(_tryPickAvailableCard.Invoke(_state, args)
                ?? throw new InvalidOperationException("TryPickAvailableCard returned null."));
            if (picked)
            {
                var cardId = args[1]?.ToString();
                if (!string.IsNullOrWhiteSpace(cardId))
                {
                    AddRewardCardToBuffer(cardId);
                    var counterAfterPick = GetRngCounter(_rewardsRng.GetValue(_state), "RewardsRng");
                    var pickedCandidateIndex = IndexOf(candidates, cardId);
                    ConsumeRewardRng(1);
                    return new RewardCardPickTrace(
                        slot,
                        rarity.ToString(),
                        rareOffsetBeforeRarity,
                        rareOffsetAfterRarity,
                        counterBeforeRarity,
                        counterAfterRarity,
                        counterBeforePick,
                        counterAfterPick,
                        rawPickIndex,
                        pickedCandidateIndex,
                        cardId,
                        candidates);
                }
            }

            rarity = GetNextHighestRarity(rarity);
            if (rarity == CardRarity.None)
            {
                return null;
            }
        }
    }

    private CardRarity RollCardRarityForEncounter(CardRarityOddsType oddsType)
    {
        if (oddsType != CardRarityOddsType.BossEncounter)
        {
            return (CardRarity)(_rollCardRarity.Invoke(_state, [oddsType])
                ?? throw new InvalidOperationException("RollCardRarity returned null."));
        }

        var rng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        _ = rng.NextFloat();
        SetCardRareOffset(-0.05f);
        return CardRarity.Rare;
    }

    private IReadOnlyList<string> GetAvailableRewardCardCandidates(CardRarity rarity, string? requiredType = null)
    {
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var ownedRelics = _state.GetType().GetProperty("OwnedRelics", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing OwnedRelics property.");
        var ownedContains = ownedRelics.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public, [typeof(string)])
            ?? throw new InvalidOperationException("Missing OwnedRelics.Contains method.");
        var prismatic = (bool)(ownedContains.Invoke(ownedRelics, ["PRISMATIC_GEM"]) ?? false);
        var getRewardCardPool = rewardModel.GetType().GetMethod("GetRewardCardPool", BindingFlags.Instance | BindingFlags.Public, [typeof(CardRarity), typeof(bool)])
            ?? throw new InvalidOperationException("Missing RewardModel.GetRewardCardPool method.");
        var pool = ((IEnumerable)(getRewardCardPool.Invoke(rewardModel, [rarity, prismatic])
                ?? throw new InvalidOperationException("GetRewardCardPool returned null.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        var current = ReadCurrentRewardCards().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pool.Where(candidate =>
            !current.Contains(candidate) &&
            (requiredType == null ||
             string.Equals(CardMetadataIndex.Get(candidate, _dataVersion)?.Type, requiredType, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private IReadOnlyList<string> GetCharacterRewardPool(CardRarity? rarityFilter)
    {
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var ownedRelics = _state.GetType().GetProperty("OwnedRelics", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing OwnedRelics property.");
        var ownedContains = ownedRelics.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public, [typeof(string)])
            ?? throw new InvalidOperationException("Missing OwnedRelics.Contains method.");
        var prismatic = (bool)(ownedContains.Invoke(ownedRelics, ["PRISMATIC_GEM"]) ?? false);
        var getCharacterCardPool = rewardModel.GetType().GetMethod("GetCharacterCardPool", BindingFlags.Instance | BindingFlags.Public, [typeof(bool)])
            ?? throw new InvalidOperationException("Missing RewardModel.GetCharacterCardPool method.");
        return ((IEnumerable)(getCharacterCardPool.Invoke(rewardModel, [prismatic])
                ?? throw new InvalidOperationException("GetCharacterCardPool returned null.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId) &&
                             IsNonCombatRewardCandidate(cardId, rarityFilter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<string> GetColorlessRewardPool()
    {
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        var getColorlessCardPool = rewardModel.GetType().GetMethod("GetColorlessCardPool", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? throw new InvalidOperationException("Missing RewardModel.GetColorlessCardPool method.");
        return ((IEnumerable)(getColorlessCardPool.Invoke(rewardModel, null)
                ?? throw new InvalidOperationException("GetColorlessCardPool returned null.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(cardId => !string.IsNullOrWhiteSpace(cardId) &&
                             IsNonCombatRewardCandidate(cardId, rarityFilter: null))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsNonCombatRewardCandidate(string cardId, CardRarity? rarityFilter)
    {
        var dataset = GetRewardDataset(_dataVersion);
        return dataset.CardMetadataMap.TryGetValue(cardId, out var metadata) &&
               IsCardAllowedForPlayer(metadata, _playerCount) &&
               metadata.ParsedRarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare &&
               (!rarityFilter.HasValue || metadata.ParsedRarity == rarityFilter.Value);
    }

    private static IReadOnlyList<string> GetFutureOfPotionsAllowedTypes(string discardedPotionId, string gameVersion)
    {
        var rarity = PotionMetadataIndex.Get(discardedPotionId, gameVersion)?.Rarity;
        if (string.Equals(rarity, "Common", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rarity, "Token", StringComparison.OrdinalIgnoreCase))
        {
            return ["Attack", "Skill"];
        }

        return ["Attack", "Skill", "Power"];
    }

    private CardRarity GetFutureOfPotionsCardRarity(string discardedPotionId)
    {
        var rarity = PotionMetadataIndex.Get(discardedPotionId, _dataVersion)?.Rarity;
        return rarity?.ToUpperInvariant() switch
        {
            "COMMON" => CardRarity.Common,
            "UNCOMMON" => CardRarity.Uncommon,
            "RARE" => CardRarity.Rare,
            _ => throw new InvalidOperationException($"Unsupported THE_FUTURE_OF_POTIONS potion rarity for {discardedPotionId}.")
        };
    }

    private void AddRewardCardToBuffer(string cardId)
    {
        var buffer = _currentRewardCards.GetValue(_state)
            ?? throw new InvalidOperationException("Missing current reward cards.");
        _rewardCardBufferAdd.Invoke(buffer, [cardId]);
    }

    private void ClearRewardCardBuffer()
    {
        var buffer = _currentRewardCards.GetValue(_state)
            ?? throw new InvalidOperationException("Missing current reward cards.");
        _rewardCardBufferClear.Invoke(buffer, null);
    }

    private static CardRarity GetNextHighestRarity(CardRarity rarity) =>
        rarity switch
        {
            CardRarity.None => CardRarity.None,
            CardRarity.Basic => CardRarity.Common,
            CardRarity.Common => CardRarity.Uncommon,
            CardRarity.Uncommon => CardRarity.Rare,
            CardRarity.Rare => CardRarity.Common,
            _ => CardRarity.None
        };

    private CardRarity RollCardRarityWithBaseOdds(CardRarityOddsType oddsType)
    {
        var rng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        var roll = rng.NextFloat();
        if (roll < GetBaseCardOdds(oddsType, CardRarity.Rare))
        {
            return CardRarity.Rare;
        }

        if (roll < GetBaseCardOdds(oddsType, CardRarity.Uncommon))
        {
            return CardRarity.Uncommon;
        }

        return CardRarity.Common;
    }

    private float GetBaseCardOdds(CardRarityOddsType oddsType, CardRarity rarity)
    {
        var scarcityActive = _state.GetType().GetProperty("AscensionLevel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state) is int ascensionLevel &&
                             ascensionLevel >= 7;
        return oddsType switch
        {
            CardRarityOddsType.RegularEncounter => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.615f : 0.6f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.0149f : 0.03f,
                _ => 0f
            },
            CardRarityOddsType.EliteEncounter => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.549f : 0.5f,
                CardRarity.Uncommon => 0.4f,
                CardRarity.Rare => scarcityActive ? 0.05f : 0.1f,
                _ => 0f
            },
            CardRarityOddsType.BossEncounter => rarity == CardRarity.Rare ? 1f : 0f,
            CardRarityOddsType.Shop => rarity switch
            {
                CardRarity.Common => scarcityActive ? 0.585f : 0.54f,
                CardRarity.Uncommon => 0.37f,
                CardRarity.Rare => scarcityActive ? 0.0449f : 0.09f,
                _ => 0f
            },
            CardRarityOddsType.Uniform => rarity switch
            {
                CardRarity.Common => 0.33f,
                CardRarity.Uncommon => 0.33f,
                CardRarity.Rare => 0.33f,
                _ => 0f
            },
            _ => 0f
        };
    }

    public static CardRarity ParseCardRarityForValidation(string? rarity) => ParseCardRarity(rarity);

    private static CardRarity ParseCardRarity(string? rarity)
    {
        return rarity?.ToUpperInvariant() switch
        {
            "COMMON" => CardRarity.Common,
            "UNCOMMON" => CardRarity.Uncommon,
            "RARE" => CardRarity.Rare,
            "BASIC" => CardRarity.Basic,
            _ => CardRarity.None
        };
    }

    private IReadOnlyList<string> ReadCurrentRewardCards()
    {
        var buffer = _currentRewardCards.GetValue(_state)
            ?? throw new InvalidOperationException("Missing current reward cards.");
        return new[] { "_first", "_second", "_third" }
            .Select(name => buffer.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(buffer)?.ToString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private float GetCardRareOffset()
    {
        return (float)(_state.GetType().GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing CardRareOffset property."));
    }

    private void SetCardRareOffset(float value)
    {
        var property = _state.GetType().GetProperty("CardRareOffset", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing CardRareOffset property.");
        if (property.CanWrite)
        {
            property.SetValue(_state, value);
            return;
        }

        var field = _state.GetType().GetField("<CardRareOffset>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing CardRareOffset backing field.");
        field.SetValue(_state, value);
    }

    private float GetPotionChance()
    {
        return (float)(_state.GetType().GetProperty("PotionChance", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing PotionChance property."));
    }

    private uint ResolveRunSeed()
    {
        var rewardsRng = _rewardsRng.GetValue(_state) as GameRng
            ?? throw new InvalidOperationException("RewardsRng was not a GameRng.");
        var rewardsHash = unchecked((uint)GetDeterministicHashCodeLocal("rewards"));
        return unchecked(rewardsRng.Seed - rewardsHash);
    }

    private int GetDelicateFrondCombatPotionCounter()
    {
        return DelicateFrondCombatPotionCounters.GetOrCreateValue(_state).Value;
    }

    private void AdvanceDelicateFrondCombatPotionCounter(int counter)
    {
        DelicateFrondCombatPotionCounters.GetOrCreateValue(_state).Value = counter;
    }

    private string? RollOutOfCombatPotion(GameRng rng, CharacterId character)
    {
        var dataset = GetRewardDataset(_dataVersion);
        var characterPool = dataset.CharacterPotionPoolMap.TryGetValue(character, out var characterPotions) && characterPotions.Count > 0
            ? characterPotions
            : GetOfficialCharacterPotionPool(character);
        var pool = characterPool.Concat(dataset.SharedPotionPoolList)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidates = pool
            .Where(id => dataset.PotionMetadataMap.ContainsKey(id))
            .Select(id => (Id: id, Metadata: dataset.PotionMetadataMap[id]))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var rarity = RollOutOfCombatPotionRarity(rng);
        var options = candidates.Where(candidate => candidate.Metadata.ParsedRarity == rarity).ToList();
        if (options.Count == 0)
        {
            options = candidates;
        }

        return options[rng.NextInt(options.Count)].Id;
    }

    private static PotionRarity RollOutOfCombatPotionRarity(GameRng rng)
    {
        var roll = rng.NextDouble();
        if (roll <= 0.1d)
        {
            return PotionRarity.Rare;
        }

        if (roll <= 0.35d)
        {
            return PotionRarity.Uncommon;
        }

        return PotionRarity.Common;
    }

    private IReadOnlyList<string> GetShopPotionCandidates(PotionRarity? rarity, IReadOnlySet<string> selected)
    {
        var rewardModel = _state.GetType().GetProperty("RewardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardModel property.");
        IEnumerable<string> pool;
        if (rarity.HasValue)
        {
            var byRarity = (IEnumerable)(rewardModel.GetType().GetProperty("PotionPoolByRarity", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardModel)
                    ?? throw new InvalidOperationException("Missing RewardModel.PotionPoolByRarity."));
            var found = Array.Empty<string>();
            foreach (var entry in byRarity)
            {
                var key = entry.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry);
                if (!Equals(key, rarity.Value))
                {
                    continue;
                }

                found = ((IEnumerable)(entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)
                        ?? Array.Empty<string>()))
                    .Cast<object>()
                    .Select(item => item.ToString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                break;
            }

            pool = found;
        }
        else
        {
            pool = ((IEnumerable)(rewardModel.GetType().GetProperty("PotionPool", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rewardModel)
                    ?? throw new InvalidOperationException("Missing RewardModel.PotionPool.")))
                .Cast<object>()
                .Select(item => item.ToString() ?? string.Empty);
        }

        return pool
            .Where(id => !string.IsNullOrWhiteSpace(id) && !selected.Contains(id))
            .ToArray();
    }

    private static int RollPotionPrice(GameRng rng, PotionRarity rarity)
    {
        var basePrice = rarity switch
        {
            PotionRarity.Uncommon => 75,
            PotionRarity.Rare => 100,
            _ => 50
        };

        return (int)Math.Round(basePrice * (0.95f + rng.NextFloat() * 0.10f));
    }

    private static IReadOnlyList<string> GetOfficialCharacterPotionPool(CharacterId character) =>
        character switch
        {
            CharacterId.Ironclad => ["BLOOD_POTION", "SOLDIERS_STEW", "ASHWATER"],
            CharacterId.Silent => ["POISON_POTION", "GHOST_IN_A_JAR", "CUNNING_POTION"],
            CharacterId.Defect => ["FOCUS_POTION", "ESSENCE_OF_DARKNESS", "POTION_OF_CAPACITY"],
            CharacterId.Necrobinder => ["POTION_OF_DOOM", "POT_OF_GHOULS", "BONE_BREW"],
            CharacterId.Regent => ["STAR_POTION", "COSMIC_CONCOCTION", "KINGS_COURAGE"],
            _ => []
        };

    private static int GetDeterministicHashCodeLocal(string text)
    {
        unchecked
        {
            var hash1 = 352654597;
            var hash2 = hash1;
            for (var i = 0; i < text.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];
                if (i == text.Length - 1)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
            }

            return hash1 + hash2 * 1566083941;
        }
    }

    private static int PredictNextInt(object? rng, int maxExclusive, string label)
    {
        if (rng == null)
        {
            throw new InvalidOperationException($"Missing {label}.");
        }

        var seed = rng.GetType().GetProperty("Seed", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rng)
            ?? throw new InvalidOperationException($"Missing {label} seed.");
        var counter = GetRngCounter(rng, label);
        var clone = Activator.CreateInstance(rng.GetType(), [seed, counter])
            ?? throw new InvalidOperationException($"Failed to clone {label}.");
        var nextInt = clone.GetType().GetMethod("NextInt", BindingFlags.Instance | BindingFlags.Public, [typeof(int)])
            ?? throw new InvalidOperationException($"Missing {label}.NextInt(int).");
        return (int)(nextInt.Invoke(clone, [maxExclusive])
            ?? throw new InvalidOperationException($"{label}.NextInt returned null."));
    }

    private void RollBossGoldReward()
    {
        var ascension = (int)(_state.GetType().GetProperty("AscensionLevel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(_state)
            ?? throw new InvalidOperationException("Missing state.AscensionLevel."));
        var goldAmount = ascension >= 3 ? 75 : 100;
        var rewardsRng = _rewardsRng.GetValue(_state)
            ?? throw new InvalidOperationException("Missing RewardsRng.");
        var nextInt = rewardsRng.GetType().GetMethod("NextInt", BindingFlags.Instance | BindingFlags.Public, [typeof(int), typeof(int)])
            ?? throw new InvalidOperationException("Missing RewardsRng.NextInt(int,int).");
        _ = nextInt.Invoke(rewardsRng, [goldAmount, goldAmount + 1]);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private IReadOnlyList<string> InvokeStringEnumerable(MethodInfo method, int actNumber)
    {
        return ((IEnumerable)(method.Invoke(_state, [actNumber])
                ?? throw new InvalidOperationException($"{method.Name} returned null.")))
            .Cast<object>()
            .Select(item => item.ToString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static void SyncRngCounter(object? source, object? target, string label)
    {
        if (source == null || target == null)
        {
            throw new InvalidOperationException($"Missing {label}.");
        }

        var counter = source.GetType().GetProperty("Counter", BindingFlags.Instance | BindingFlags.Public)?.GetValue(source)
            ?? throw new InvalidOperationException($"Missing {label} counter.");
        var fastForward = target.GetType().GetMethod("FastForward", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Missing {label} FastForward.");
        fastForward.Invoke(target, [counter]);
    }

    private static int GetRngCounter(object? rng, string label)
    {
        if (rng == null)
        {
            throw new InvalidOperationException($"Missing {label}.");
        }

        return (int)(rng.GetType().GetProperty("Counter", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rng)
            ?? throw new InvalidOperationException($"Missing {label} counter."));
    }

    private IReadOnlyList<TEntry> ReadShopEntries<TEntry>(Type stateType, string propertyName)
        where TEntry : IShopEntry
    {
        var property = stateType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Missing reward state property {propertyName}.");
        return ((IEnumerable)(property.GetValue(_state)
                ?? throw new InvalidOperationException($"Missing reward state property value {propertyName}.")))
            .Cast<TEntry>()
            .ToArray();
    }

    private ShopCardTypeSnapshot ReadShownShopCardEntriesByType(Type stateType)
    {
        var property = stateType.GetProperty("ShownShopCardStates", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing reward state property ShownShopCardStates.");
        var states = ((IEnumerable)(property.GetValue(_state)
                ?? throw new InvalidOperationException("Missing reward state property value ShownShopCardStates.")))
            .Cast<object>()
            .ToArray();
        var coloredCards = new List<ShopCardEntry>();
        var colorlessCards = new List<ShopCardEntry>();
        foreach (var state in states)
        {
            var stateTypeInner = state.GetType();
            var entry = (ShopCardEntry)(stateTypeInner.GetProperty("Entry", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
                ?? throw new InvalidOperationException("Missing shown merchant card Entry."));
            var isColorless = (bool)(stateTypeInner.GetProperty("IsColorless", BindingFlags.Instance | BindingFlags.Public)?.GetValue(state)
                ?? throw new InvalidOperationException("Missing shown merchant card IsColorless."));
            if (isColorless)
            {
                colorlessCards.Add(entry);
            }
            else
            {
                coloredCards.Add(entry);
            }
        }

        return new ShopCardTypeSnapshot(coloredCards, colorlessCards);
    }

    private static void AddNewEntries<TEntry>(List<TEntry> target, IEnumerable<TEntry> source)
        where TEntry : IShopEntry
    {
        foreach (var entry in source)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) ||
                target.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(entry);
        }
    }

    private static MethodInfo GetMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Missing reward state method {name}.");
    }

    private static MethodInfo GetNonPublicMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing reward state method {name}.");
    }

    private static bool TryParseCharacter(string value, out CharacterId character)
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        return CharacterIdExtensions.TryParse(normalized, out character);
    }
}

internal sealed record GeneratedRewardOutcome(
    IReadOnlyList<string> GeneratedCards,
    IReadOnlyList<string> GeneratedRelics);

internal sealed record ShopSessionPreview(
    ShopPreview InitialShop,
    ShopPreview VisibleShop,
    IReadOnlyList<ReplayShopAction> InferredActions);

internal sealed record ShopCardTypeSnapshot(
    IReadOnlyList<ShopCardEntry> ColoredCards,
    IReadOnlyList<ShopCardEntry> ColorlessCards);

internal sealed record FutureOfPotionsTypeProbe(
    string CardType,
    IReadOnlyList<string> GeneratedCards);

internal sealed record FutureOfPotionsTraceProbe(
    string CardType,
    int PreludeSkip,
    IReadOnlyList<string> GeneratedCards,
    IReadOnlyList<RewardCardPickTrace> PickTrace);

internal sealed record CrystalSphereRewardProbe(
    string Mode,
    IReadOnlyList<string> GeneratedCards,
    IReadOnlyList<string> GeneratedPotions,
    bool GoldBeforeCard);

internal sealed record EventRelicPullProbe(
    string Label,
    string? Rarity,
    IReadOnlyList<string> Relics);

internal sealed class CrystalSpherePlacementGrid
{
    private readonly bool[,] _hidden;
    private readonly bool[,] _occupied;

    public CrystalSpherePlacementGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _hidden = new bool[width, height];
        _occupied = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                _hidden[x, y] = true;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public void Clear(int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _hidden[x, y] = false;
        }
    }

    public IReadOnlyList<(int X, int Y)> GetPlacementCandidates(int itemWidth, int itemHeight)
    {
        var candidates = new List<(int X, int Y)>();
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                if (CanPlaceHere(x, y, itemWidth, itemHeight))
                {
                    candidates.Add((x, y));
                }
            }
        }

        return candidates;
    }

    public void Place(int x, int y, int itemWidth, int itemHeight)
    {
        for (var dx = 0; dx < itemWidth; dx++)
        {
            for (var dy = 0; dy < itemHeight; dy++)
            {
                _occupied[x + dx, y + dy] = true;
            }
        }
    }

    private bool CanPlaceHere(int x, int y, int itemWidth, int itemHeight)
    {
        for (var dx = 0; dx < itemWidth; dx++)
        {
            for (var dy = 0; dy < itemHeight; dy++)
            {
                var targetX = x + dx;
                var targetY = y + dy;
                if (targetX < 0 || targetX >= Width || targetY < 0 || targetY >= Height)
                {
                    return false;
                }

                if (!_hidden[targetX, targetY] || _occupied[targetX, targetY])
                {
                    return false;
                }
            }
        }

        return true;
    }
}

internal enum OpeningDetailReplayMode
{
    None,
    ReplayLeadPaperweightOnly,
    ReplayRelicDetails
}

internal sealed record RewardRngSkipProbe(
    int Skip,
    IReadOnlyList<string> GeneratedCards);

internal sealed record RewardRngAlignment(
    IReadOnlyList<string> GeneratedCards,
    int AppliedSkip,
    string? Notes);

internal sealed record CombatRewardPreludeTrace(
    int CounterBeforeBossGold,
    int CounterBeforePotionChance,
    int CounterAfterPotionChance,
    float PotionChanceBefore,
    float PotionChanceAfter,
    bool HasPotionReward,
    int CounterBeforeGold,
    int CounterAfterGold,
    int CounterBeforePotionReward,
    int CounterAfterPotionReward,
    int CounterBeforeCards)
{
    public string ToReportString()
    {
        return string.Join(", ",
        [
            $"potionChanceRng={CounterBeforePotionChance}->{CounterAfterPotionChance}",
            $"potionChance={FormatFloat(PotionChanceBefore)}->{FormatFloat(PotionChanceAfter)}",
            $"hasPotion={HasPotionReward}",
            $"goldRng={CounterBeforeGold}->{CounterAfterGold}",
            $"potionRewardRng={CounterBeforePotionReward}->{CounterAfterPotionReward}",
            $"cardsStart={CounterBeforeCards}"
        ]);
    }

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal sealed record RewardCardPickTrace(
    int Slot,
    string Rarity,
    float RareOffsetBeforeRarity,
    float RareOffsetAfterRarity,
    int CounterBeforeRarity,
    int CounterAfterRarity,
    int CounterBeforePick,
    int CounterAfterPick,
    int RawPickIndex,
    int PickedCandidateIndex,
    string PickedCardId,
    IReadOnlyList<string> Candidates)
{
    public bool ContainsTargets(IReadOnlyList<string> targets) =>
        targets.All(target => Candidates.Contains(target, StringComparer.OrdinalIgnoreCase));

    public string ToReportString()
    {
        var interesting = new[] { "DEATHBRINGER", "DEATH_MARCH", "GRAVEBLAST", "FEAR" };
        return ToReportStringCore(interesting, "DEATH_MARCH", "DEATHBRINGER");
    }

    public string ToReportString(IReadOnlyList<string> targets)
    {
        var interesting = (targets ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ToReportStringCore(interesting, null, null);
    }

    private string ToReportStringCore(IReadOnlyList<string> interesting, string? exclusionProbeId, string? exclusionFollowerId)
    {
        var present = interesting.Where(id => Candidates.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var targetIndices = interesting
            .Select(id => (Id: id, Index: IndexOf(id)))
            .Where(item => item.Index >= 0)
            .Select(item => $"{item.Id}@{item.Index}")
            .ToArray();
        var expectedIfExcluded = !string.IsNullOrWhiteSpace(exclusionProbeId) &&
                                 !string.IsNullOrWhiteSpace(exclusionFollowerId) &&
                                 RawPickIndex >= 0 &&
                                 RawPickIndex < Candidates.Count - 1 &&
                                 string.Equals(Candidates[RawPickIndex], exclusionProbeId, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(Candidates[RawPickIndex + 1], exclusionFollowerId, StringComparison.OrdinalIgnoreCase)
            ? $", excluding {exclusionProbeId} would pick {exclusionFollowerId}"
            : "";
        return string.Join(", ",
        [
            $"slot {Slot}: rarity={Rarity}",
            $"rarityRng={CounterBeforeRarity}->{CounterAfterRarity}",
            $"rareOffset={FormatFloat(RareOffsetBeforeRarity)}->{FormatFloat(RareOffsetAfterRarity)}",
            $"pickRng={CounterBeforePick}->{CounterAfterPick}",
            $"rawIndex={RawPickIndex}",
            $"pickedIndex={PickedCandidateIndex}",
            $"picked={PickedCardId}",
            $"candidates={Candidates.Count}",
            $"notable={string.Join("/", present)}",
            $"targetIndices={string.Join("/", targetIndices)}{expectedIfExcluded}"
        ]);
    }

    private int IndexOf(string cardId)
    {
        for (var i = 0; i < Candidates.Count; i++)
        {
            if (string.Equals(Candidates[i], cardId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class OpeningReplayDiagnostics
{
    public static IReadOnlyList<OpeningReplayDiagnostic> Build(ReplayRun replay)
    {
        var floor2 = replay.Floors.FirstOrDefault(floor => floor.Floor == 2);
        if (floor2 == null)
        {
            return Array.Empty<OpeningReplayDiagnostic>();
        }

        return
        [
            BuildForMode(replay, floor2, OpeningDetailReplayMode.None),
            BuildForMode(replay, floor2, OpeningDetailReplayMode.ReplayLeadPaperweightOnly),
            BuildForMode(replay, floor2, OpeningDetailReplayMode.ReplayRelicDetails)
        ];
    }

    private static OpeningReplayDiagnostic BuildForMode(
        ReplayRun replay,
        ReplayFloor floor,
        OpeningDetailReplayMode mode)
    {
        var state = ExactRewardStateBridge.Create(replay, mode);
        var generated = state.PreviewCombatCards(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false);
        var pickTrace = state.PreviewCombatCardPickTrace(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false);
        var preludeTrace = state.PreviewCombatRewardPrelude(isElite: false, isBoss: false);
        var preludeVariants = state.PreviewCardsAfterManualRewardPreludeSkips(CardRarityOddsType.RegularEncounter, maxSkip: 8);
        var probes = state.PreviewCombatSkips(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false, maxSkip: 24);
        var alignment = GeneratedFloorComparison.FindUniqueRewardRngSkipAlignment(floor, generated, probes);
        var notes = alignment.Notes ?? "";
        if (!GeneratedFloorComparison.SameCardReward(floor.CardChoices, generated) && pickTrace.Count > 0)
        {
            var matchingPreludeVariants = preludeVariants
                .Where(variant => GeneratedFloorComparison.SameCardReward(floor.CardChoices, variant.GeneratedCards))
                .Select(variant => $"{variant.Skip}=>{string.Join("/", variant.GeneratedCards)}")
                .ToArray();
            var variantNotes = matchingPreludeVariants.Length == 0
                ? "prelude variants no match"
                : $"prelude variants match {string.Join(", ", matchingPreludeVariants)}";
            var firstVariants = string.Join(", ", preludeVariants.Take(5).Select(variant => $"{variant.Skip}=>{string.Join("/", variant.GeneratedCards)}"));
            var hookNotes = BuildOpeningHookNotes(replay);
            var netIdNotes = BuildPlayerNetIdNotes(replay, floor, mode);
            var traceNotes = $"prelude={preludeTrace.ToReportString()}; {variantNotes}; first prelude variants {firstVariants}; {hookNotes}; {netIdNotes}; rng parity: official v0.106.1 Rng matches local GameRng for checked NextInt/NextFloat/NextItem samples; pick trace: {string.Join("; ", pickTrace.Select(trace => trace.ToReportString()))}";
            notes = string.IsNullOrWhiteSpace(notes)
                ? traceNotes
                : $"{notes}; {traceNotes}";
        }

        return new OpeningReplayDiagnostic(
            mode,
            ExactRewardStateBridge.GetAct1OpeningRelicIds(replay),
            floor.CardChoices,
            generated,
            notes,
            pickTrace);
    }

    private static string BuildPlayerNetIdNotes(
        ReplayRun replay,
        ReplayFloor floor,
        OpeningDetailReplayMode mode)
    {
        var candidates = Enumerable.Range(0, 64)
            .Select(value => (ulong)value)
            .Concat([100UL, 1_000UL, 10_000UL, 100_000UL, 1_000_000UL])
            .Distinct()
            .Select(playerNetId =>
            {
                try
                {
                    var state = ExactRewardStateBridge.Create(replay, mode, playerNetId);
                    var generated = state.PreviewCombatCards(CardRarityOddsType.RegularEncounter, isElite: false, isBoss: false);
                    return (PlayerNetId: playerNetId, Generated: generated);
                }
                catch
                {
                    return (PlayerNetId: playerNetId, Generated: (IReadOnlyList<string>)Array.Empty<string>());
                }
            })
            .ToArray();
        var matches = candidates
            .Where(item => GeneratedFloorComparison.SameCardReward(floor.CardChoices, item.Generated))
            .Take(8)
            .Select(item => $"{item.PlayerNetId}=>{string.Join("/", item.Generated)}")
            .ToArray();
        if (matches.Length > 0)
        {
            return $"playerNetId scan matches {string.Join(", ", matches)}";
        }

        var first = string.Join(", ", candidates.Take(6).Select(item => $"{item.PlayerNetId}=>{string.Join("/", item.Generated)}"));
        return $"playerNetId scan 0..63 no match; first {first}";
    }

    private static string BuildOpeningHookNotes(ReplayRun replay)
    {
        var openingRelics = ExactRewardStateBridge.GetAct1OpeningRelicIds(replay);
        var cardRewardHookRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DINGY_RUG",
            "PRISMATIC_GEM",
            "LASTING_CANDY",
            "FRESNEL_LENS",
            "FROZEN_EGG",
            "GLITTER",
            "LAVA_LAMP",
            "MOLTEN_EGG",
            "SILKEN_TRESS",
            "SILVER_CRUCIBLE",
            "TOXIC_EGG",
            "WING_CHARM"
        };
        var active = openingRelics.Where(cardRewardHookRelics.Contains).ToArray();
        return active.Length == 0
            ? "opening hook check: no opening relic is in current v0.106.1 card-reward hook override list"
            : $"opening hook check: active card-reward hook relics {string.Join("/", active)}";
    }
}

internal sealed record OpeningReplayDiagnostic(
    OpeningDetailReplayMode Mode,
    IReadOnlyList<string> OpeningRelics,
    IReadOnlyList<string> Floor2ExpectedCards,
    IReadOnlyList<string> Floor2GeneratedCards,
    string Notes,
    IReadOnlyList<RewardCardPickTrace> PickTrace);

internal static class GameAssemblyInspector
{
    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var assemblyPath = args.Count > 0
                ? args[0]
                : Path.Combine("I:", "SteamLibrary", "steamapps", "common", "Slay the Spire 2", "data_sts2_windows_x86_64", "sts2.dll");
            var outputPath = args.Count > 1
                ? args[1]
                : Path.Combine("scratch", "sts2-v0.106.1-assembly-inspection.md");

            var report = Inspect(assemblyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
            File.WriteAllText(outputPath, report);
            Console.WriteLine(report);
            Console.WriteLine($"Inspection written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"assembly inspection failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static string Inspect(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Game assembly not found.", assemblyPath);
        }

        var loadContext = new AssemblyLoadContext("sts2-inspection", isCollectible: true);
        loadContext.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(Path.GetDirectoryName(assemblyPath) ?? ".", $"{name.Name}.dll");
            return File.Exists(candidate) ? loadContext.LoadFromAssemblyPath(candidate) : null;
        };

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var interestingTypes = assembly.GetTypes()
                .Where(IsInterestingType)
                .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("# sts2.dll Assembly Inspection");
            builder.AppendLine();
            builder.AppendLine($"- assembly: {assemblyPath}");
            builder.AppendLine($"- assembly_full_name: {assembly.FullName}");
            builder.AppendLine($"- interesting_types: {interestingTypes.Length}");
            builder.AppendLine();
            builder.AppendLine("## Types");
            builder.AppendLine();
            foreach (var type in interestingTypes)
            {
                builder.AppendLine($"- {type.FullName}");
            }

            builder.AppendLine();
            builder.AppendLine("## Method Calls");
            foreach (var type in interestingTypes)
            {
                AppendTypeFields(builder, type);
                AppendTypeMethods(builder, type);
            }

            builder.AppendLine();
            builder.AppendLine("## Target IL Order");
            foreach (var type in interestingTypes.Where(type => type.FullName?.Contains("<AfterObtained>", StringComparison.OrdinalIgnoreCase) == true ||
                                                                string.Equals(type.Name, "Neow", StringComparison.OrdinalIgnoreCase) ||
                                                                string.Equals(type.Name, "FishingRod", StringComparison.OrdinalIgnoreCase)))
            {
                AppendTargetIl(builder, type);
            }

            AppendCardRewardIlSequence(builder, assembly);
            AppendCardRewardHookOverrides(builder, assembly);
            AppendCardCreationOptionsIl(builder, assembly);
            AppendOfficialRewardPoolOrder(builder, assembly);
            AppendRngParity(builder, assembly);

            return builder.ToString();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static bool IsInterestingType(Type type)
    {
        var name = type.FullName ?? type.Name;
        return ContainsInterestingText(name);
    }

    private static bool ContainsInterestingText(string value)
    {
        return value.Contains("Neow", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Bones", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Fishing", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CardFactory", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".Rng", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("LeadPaperweight", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Precarious", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Shears", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendTypeMethods(System.Text.StringBuilder builder, Type type)
    {
        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => ContainsInterestingText(method.Name) || MethodMentionsInterestingMember(method))
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (methods.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"### {type.FullName}");
        foreach (var method in methods)
        {
            builder.AppendLine();
            builder.AppendLine($"- `{FormatMethod(method)}`");
            foreach (var called in GetInterestingCalledMembers(method))
            {
                builder.AppendLine($"  - calls `{called}`");
            }

            var constants = GetInlineConstants(method);
            if (constants.Count > 0)
            {
                builder.AppendLine($"  - inline constants `{string.Join(", ", constants)}`");
            }
        }
    }

    private static void AppendTypeFields(System.Text.StringBuilder builder, Type type)
    {
        var fields = type
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => ContainsInterestingText(field.Name) ||
                            field.Name.Contains("Relic", StringComparison.OrdinalIgnoreCase) ||
                            field.Name.Contains("Curse", StringComparison.OrdinalIgnoreCase) ||
                            field.Name.Contains("Combat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(property => ContainsInterestingText(property.Name) ||
                               property.Name.Contains("Relic", StringComparison.OrdinalIgnoreCase) ||
                               property.Name.Contains("Curse", StringComparison.OrdinalIgnoreCase) ||
                               property.Name.Contains("Combat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (fields.Length == 0 && properties.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"### {type.FullName} Fields");
        foreach (var field in fields)
        {
            builder.AppendLine($"- field `{FormatType(field.FieldType)} {field.Name}`");
        }

        foreach (var property in properties)
        {
            builder.AppendLine($"- property `{FormatType(property.PropertyType)} {property.Name}`");
        }
    }

    private static bool MethodMentionsInterestingMember(MethodInfo method)
    {
        return GetCalledMembers(method).Any(member => ContainsInterestingText(member));
    }

    private static IReadOnlyList<string> GetInterestingCalledMembers(MethodInfo method)
    {
        return GetCalledMembers(method)
            .Where(member => ContainsInterestingText(member) ||
                             member.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("Curse", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("Deck", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("DynamicVars", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("Owner", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("Relic", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("Reward", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("CreateForReward", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("AddRelic", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("PlayerRng", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("RelicGrabBag", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("NextItem", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("TakeRandom", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("CardFactory", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("CardPileCmd", StringComparison.OrdinalIgnoreCase) ||
                             member.Contains("CardSelectCmd", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatMethod(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}"));
        return $"{FormatType(method.ReturnType)} {method.Name}({parameters})";
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }

    private static IReadOnlyList<string> GetCalledMembers(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            return Array.Empty<string>();
        }

        var module = method.Module;
        var result = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var opCode = IlReader.ReadOpCode(il, ref offset);
            var operandSize = IlReader.GetOperandSize(opCode.OperandType, il, offset);
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineString)
            {
                var token = BitConverter.ToInt32(il, offset);
                var resolved = TryResolveMember(module, token, opCode.OperandType);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    result.Add(resolved);
                }
            }

            offset += operandSize;
        }

        return result;
    }

    private static IReadOnlyList<string> GetInlineConstants(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var opCode = IlReader.ReadOpCode(il, ref offset);
            switch (opCode.OperandType)
            {
                case OperandType.ShortInlineI:
                    result.Add($"{opCode.Name}:{unchecked((sbyte)il[offset])}");
                    break;
                case OperandType.InlineI:
                    result.Add($"{opCode.Name}:{BitConverter.ToInt32(il, offset)}");
                    break;
                case OperandType.InlineI8:
                    result.Add($"{opCode.Name}:{BitConverter.ToInt64(il, offset)}");
                    break;
                case OperandType.ShortInlineR:
                    result.Add($"{opCode.Name}:{BitConverter.ToSingle(il, offset)}");
                    break;
                case OperandType.InlineR:
                    result.Add($"{opCode.Name}:{BitConverter.ToDouble(il, offset)}");
                    break;
            }

            offset += IlReader.GetOperandSize(opCode.OperandType, il, offset);
        }

        return result
            .Where(value => !value.EndsWith(":0", StringComparison.Ordinal) && !value.EndsWith(":1", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToArray();
    }

    private static void AppendTargetIl(System.Text.StringBuilder builder, Type type)
    {
        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "MoveNext" ||
                             method.Name == "GenerateInitialOptions" ||
                             method.Name == "AfterCombatEnd" ||
                             method.Name == "NextItem" ||
                             method.Name == "NextInt" ||
                             method.Name == "NextFloat" ||
                             method.Name == "CreateForReward")
            .ToArray();
        foreach (var method in methods)
        {
            var lines = GetInterestingIlLines(method);
            if (lines.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"### {type.FullName}.{method.Name}");
            foreach (var line in lines)
            {
                builder.AppendLine($"- {line}");
            }
        }
    }

    private static void AppendCardRewardIlSequence(System.Text.StringBuilder builder, Assembly assembly)
    {
        var cardFactory = assembly.GetType("MegaCrit.Sts2.Core.Factories.CardFactory");
        if (cardFactory == null)
        {
            return;
        }

        var methods = cardFactory
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "CreateForReward")
            .OrderBy(method => method.GetParameters().Length)
            .ThenBy(method => FormatMethod(method), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (methods.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Card Reward IL Sequence");
        foreach (var method in methods)
        {
            var lines = GetCardRewardIlLines(method);
            if (lines.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"### MegaCrit.Sts2.Core.Factories.CardFactory.{FormatMethod(method)}");
            foreach (var line in lines)
            {
                builder.AppendLine($"- {line}");
            }
        }
    }

    private static IReadOnlyList<string> GetCardRewardIlLines(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            return Array.Empty<string>();
        }

        var module = method.Module;
        var result = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opCode = IlReader.ReadOpCode(il, ref offset);
            var operandSize = IlReader.GetOperandSize(opCode.OperandType, il, offset);
            var operand = FormatIlOperand(module, opCode.OperandType, il, offset);
            if (!string.IsNullOrWhiteSpace(operand) && IsCardRewardIlOperand(operand))
            {
                result.Add($"IL_{instructionOffset:x4}: {opCode.Name} {operand}");
            }

            offset += operandSize;
        }

        return result;
    }

    private static bool IsCardRewardIlOperand(string operand)
    {
        return operand.Contains("CardFactory.CreateForReward", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardFactory.RollForRarity", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardFactory.RollForUpgrade", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("Hook.ModifyCardRewardCreationOptions", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("Hook.TryModifyCardRewardOptions", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("Hook.AfterModifyingCardRewardOptions", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationOptions.GetPossibleCards", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationOptions.get_Flags", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationOptions.get_RarityOdds", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationOptions.get_RngOverride", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationOptions.get_Source", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("PlayerRngSet.get_Rewards", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("Rng.NextItem", StringComparison.OrdinalIgnoreCase) ||
               operand.Contains("CardCreationResult", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendCardRewardHookOverrides(System.Text.StringBuilder builder, Assembly assembly)
    {
        var hookNames = new[]
        {
            "ModifyCardRewardCreationOptions",
            "ModifyCardRewardCreationOptionsLate",
            "TryModifyCardRewardOptions",
            "TryModifyCardRewardOptionsLate",
            "AfterModifyingCardRewardOptions",
            "ModifyCardRewardUpgradeOdds"
        };
        var overrides = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => hookNames.Contains(method.Name, StringComparer.Ordinal))
                .Where(method => method.GetBaseDefinition().DeclaringType != method.DeclaringType)
                .Select(method => (Type: type, Method: method)))
            .OrderBy(item => item.Method.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (overrides.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Card Reward Hook Overrides");
        foreach (var group in overrides.GroupBy(item => item.Method.Name).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine($"### {group.Key}");
            foreach (var item in group)
            {
                builder.AppendLine($"- `{item.Type.Name}.{item.Method.Name}` (`{FormatMethod(item.Method)}`)");
            }
        }
    }

    private static void AppendCardCreationOptionsIl(System.Text.StringBuilder builder, Assembly assembly)
    {
        var optionsType = assembly.GetType("MegaCrit.Sts2.Core.Runs.CardCreationOptions");
        if (optionsType == null)
        {
            return;
        }

        var methodNames = new[]
        {
            "ForRoom",
            "ForNonCombatWithDefaultOdds",
            "ForNonCombatWithUniformOdds",
            "GetPossibleCards",
            "WithCustomPool",
            "WithCardPools"
        };
        var methods = optionsType
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => methodNames.Contains(method.Name, StringComparer.Ordinal))
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(method => FormatMethod(method), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (methods.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Card Creation Options IL");
        foreach (var method in methods)
        {
            var lines = GetFocusedIlLines(method, operand =>
                operand.Contains("CardPoolModel.GetUnlockedCards", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("CardCreationOptions", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("CardCreationSource", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("CardRarityOddsType", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Player.get_Character", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Player.get_RunState", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Player.get_UnlockState", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("CharacterModel.get_CardPool", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("IRunState.get_CardMultiplayerConstraint", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Enumerable.SelectMany", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Enumerable.Where", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Enumerable.ToArray", StringComparison.OrdinalIgnoreCase) ||
                operand.Contains("Enumerable.ToList", StringComparison.OrdinalIgnoreCase));
            if (lines.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"### MegaCrit.Sts2.Core.Runs.CardCreationOptions.{FormatMethod(method)}");
            foreach (var line in lines)
            {
                builder.AppendLine($"- {line}");
            }
        }
    }

    private static void AppendOfficialRewardPoolOrder(System.Text.StringBuilder builder, Assembly assembly)
    {
        try
        {
            InvokeParameterlessStatic(assembly.GetType("MegaCrit.Sts2.Core.Models.ModelDb"), "Init");
            var unlockStateType = assembly.GetType("MegaCrit.Sts2.Core.Unlocks.UnlockState");
            var allUnlockState = unlockStateType?.GetField("all", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? unlockStateType?.GetProperty("all", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            var singleplayerOnly = ParseEnumValue(assembly.GetType("MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint"), "SingleplayerOnly");
            var common = ParseEnumValue(assembly.GetType("MegaCrit.Sts2.Core.Entities.Cards.CardRarity"), "Common");
            var uncommon = ParseEnumValue(assembly.GetType("MegaCrit.Sts2.Core.Entities.Cards.CardRarity"), "Uncommon");
            var rare = ParseEnumValue(assembly.GetType("MegaCrit.Sts2.Core.Entities.Cards.CardRarity"), "Rare");
            if (allUnlockState == null || singleplayerOnly == null || common == null || uncommon == null || rare == null)
            {
                return;
            }

            var cardFactoryType = assembly.GetType("MegaCrit.Sts2.Core.Factories.CardFactory");
            var filterForCombat = cardFactoryType?.GetMethod("FilterForCombat", BindingFlags.Static | BindingFlags.Public);
            var cardModelType = assembly.GetType("MegaCrit.Sts2.Core.Models.CardModel");
            if (filterForCombat == null || cardModelType == null)
            {
                return;
            }

            var builderLines = new List<(string Label, IReadOnlyList<string> Cards)>();
            foreach (var character in new[] { "IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT" })
            {
                var pool = GetOfficialCardPool(assembly, character);
                if (pool == null)
                {
                    continue;
                }

                var unlocked = InvokeEnumerable(pool, "GetUnlockedCards", allUnlockState, singleplayerOnly).ToArray();
                var typedUnlocked = ToTypedArray(cardModelType, unlocked);
                var combat = ((IEnumerable)(filterForCombat.Invoke(null, [typedUnlocked]) ?? Array.Empty<object>()))
                    .Cast<object>()
                    .Where(card => !IsEnumProperty(card, "MultiplayerConstraint", "MultiplayerOnly"))
                    .ToArray();

                foreach (var (rarityName, rarityValue) in new[] { ("Common", common), ("Uncommon", uncommon), ("Rare", rare) })
                {
                    var cards = combat
                        .Where(card => Equals(ReadProperty(card, "Rarity"), rarityValue))
                        .Select(ReadCardId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToArray();
                    builderLines.Add(($"{character}.{rarityName}", cards));
                }
            }

            if (builderLines.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Official Reward Pool Order");
            builder.AppendLine("- source: ModelDb.CardPool(...).GetUnlockedCards(UnlockState.all, SingleplayerOnly) -> CardFactory.FilterForCombat -> rarity bucket");
            foreach (var line in builderLines)
            {
                builder.AppendLine($"- {line.Label}: count={line.Cards.Count}, head={string.Join("/", line.Cards.Take(18))}");
                var targetIndices = new[] { "DEATH_MARCH", "DEATHBRINGER", "GRAVEBLAST", "FEAR" }
                    .Select(target => $"{target}@{IndexOf(line.Cards, target)}")
                    .Where(item => !item.EndsWith("@-1", StringComparison.Ordinal))
                    .ToArray();
                if (targetIndices.Length > 0)
                {
                    builder.AppendLine($"  - targets: {string.Join(", ", targetIndices)}");
                }
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine();
            builder.AppendLine("## Official Reward Pool Order");
            builder.AppendLine($"- failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> GetFocusedIlLines(MethodInfo method, Func<string, bool> includeOperand)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            return Array.Empty<string>();
        }

        var module = method.Module;
        var result = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opCode = IlReader.ReadOpCode(il, ref offset);
            var operandSize = IlReader.GetOperandSize(opCode.OperandType, il, offset);
            var operand = FormatIlOperand(module, opCode.OperandType, il, offset);
            if (!string.IsNullOrWhiteSpace(operand) && includeOperand(operand))
            {
                result.Add($"IL_{instructionOffset:x4}: {opCode.Name} {operand}");
            }

            if (string.IsNullOrWhiteSpace(operand) &&
                opCode.Name is "ldc.i4.0" or "ldc.i4.1" or "ldc.i4.2" or "ldc.i4.3" or "ldc.i4.4" or "ldc.i4.5")
            {
                result.Add($"IL_{instructionOffset:x4}: {opCode.Name}");
            }

            offset += operandSize;
        }

        return result;
    }

    private static void InvokeParameterlessStatic(Type? type, string methodName)
    {
        var method = type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        method?.Invoke(null, null);
    }

    private static object? ParseEnumValue(Type? enumType, string name)
    {
        return enumType == null ? null : Enum.Parse(enumType, name);
    }

    private static object? GetOfficialCardPool(Assembly assembly, string character)
    {
        var modelDb = assembly.GetType("MegaCrit.Sts2.Core.Models.ModelDb");
        var cardPoolMethod = modelDb?
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "CardPool" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
        var poolTypeName = character.ToUpperInvariant() switch
        {
            "IRONCLAD" => "MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool",
            "SILENT" => "MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool",
            "DEFECT" => "MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool",
            "NECROBINDER" => "MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool",
            "REGENT" => "MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool",
            _ => null
        };
        var poolType = poolTypeName == null ? null : assembly.GetType(poolTypeName);
        return cardPoolMethod == null || poolType == null
            ? null
            : cardPoolMethod.MakeGenericMethod(poolType).Invoke(null, null);
    }

    private static IEnumerable<object> InvokeEnumerable(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length)
            ?? throw new InvalidOperationException($"Missing {instance.GetType().Name}.{methodName}.");
        return ((IEnumerable)(method.Invoke(instance, args) ?? Array.Empty<object>())).Cast<object>();
    }

    private static Array ToTypedArray(Type elementType, IReadOnlyList<object> values)
    {
        var array = Array.CreateInstance(elementType, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(values[i], i);
        }

        return array;
    }

    private static bool IsEnumProperty(object instance, string propertyName, string enumName)
    {
        var value = ReadProperty(instance, propertyName);
        return value != null && string.Equals(value.ToString(), enumName, StringComparison.OrdinalIgnoreCase);
    }

    private static object? ReadProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static string ReadCardId(object card)
    {
        var id = ReadProperty(card, "Id");
        var entry = id == null ? null : ReadProperty(id, "Entry");
        return entry?.ToString() ?? id?.ToString() ?? string.Empty;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void AppendRngParity(System.Text.StringBuilder builder, Assembly assembly)
    {
        var rngType = assembly.GetType("MegaCrit.Sts2.Core.Random.Rng");
        if (rngType == null)
        {
            return;
        }

        var constructor = rngType.GetConstructor([typeof(uint), typeof(int)]);
        var nextIntMax = rngType.GetMethod("NextInt", BindingFlags.Instance | BindingFlags.Public, [typeof(int)]);
        var nextIntRange = rngType.GetMethod("NextInt", BindingFlags.Instance | BindingFlags.Public, [typeof(int), typeof(int)]);
        var nextFloatMax = rngType.GetMethod("NextFloat", BindingFlags.Instance | BindingFlags.Public, [typeof(float)]);
        var nextItem = rngType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "NextItem" && method.IsGenericMethodDefinition);
        if (constructor == null || nextIntMax == null || nextIntRange == null || nextFloatMax == null || nextItem == null)
        {
            return;
        }

        var samples = new (uint Seed, int Counter)[]
        {
            (0u, 0),
            (123456789u, 0),
            (SeedFormatter.ToUIntSeed("59YH5HF6V9"), 0),
            (unchecked(SeedFormatter.ToUIntSeed("59YH5HF6V9") + (uint)GetDeterministicHashCode("rewards")), 4)
        };
        var mismatches = new List<string>();
        var lines = new List<string>();
        foreach (var sample in samples)
        {
            var official = constructor.Invoke([sample.Seed, sample.Counter])
                ?? throw new InvalidOperationException("Failed to create official Rng.");
            var local = new GameRng(sample.Seed, sample.Counter);

            var officialInt = (int)(nextIntMax.Invoke(official, [35]) ?? throw new InvalidOperationException("Official NextInt returned null."));
            var localInt = local.NextInt(35);
            AddCheck(sample, "NextInt(35)", officialInt.ToString(CultureInfo.InvariantCulture), localInt.ToString(CultureInfo.InvariantCulture));

            var officialRange = (int)(nextIntRange.Invoke(official, [10, 21]) ?? throw new InvalidOperationException("Official NextInt range returned null."));
            var localRange = local.NextInt(10, 21);
            AddCheck(sample, "NextInt(10,21)", officialRange.ToString(CultureInfo.InvariantCulture), localRange.ToString(CultureInfo.InvariantCulture));

            var officialFloat = (float)(nextFloatMax.Invoke(official, [1f]) ?? throw new InvalidOperationException("Official NextFloat returned null."));
            var localFloat = local.NextFloat();
            AddCheck(sample, "NextFloat(1)", officialFloat.ToString("R", CultureInfo.InvariantCulture), localFloat.ToString("R", CultureInfo.InvariantCulture));

            var stringNextItem = nextItem.MakeGenericMethod(typeof(string));
            var items = new[] { "A", "B", "C", "D", "E" };
            var officialItem = (string?)stringNextItem.Invoke(official, [items]) ?? "";
            var localItem = local.NextItem(items) ?? "";
            AddCheck(sample, "NextItem(5)", officialItem, localItem);
        }

        builder.AppendLine();
        builder.AppendLine("## Rng Parity");
        builder.AppendLine(mismatches.Count == 0 ? "- parity: pass" : "- parity: fail");
        foreach (var line in lines)
        {
            builder.AppendLine($"- {line}");
        }

        if (mismatches.Count > 0)
        {
            builder.AppendLine($"- mismatches: {string.Join("; ", mismatches)}");
        }

        void AddCheck((uint Seed, int Counter) sample, string op, string official, string local)
        {
            var status = string.Equals(official, local, StringComparison.Ordinal) ? "ok" : "mismatch";
            lines.Add($"seed={sample.Seed}, counter={sample.Counter}, {op}: official={official}, local={local}, {status}");
            if (!string.Equals(official, local, StringComparison.Ordinal))
            {
                mismatches.Add($"seed={sample.Seed} counter={sample.Counter} {op} official={official} local={local}");
            }
        }
    }

    private static int GetDeterministicHashCode(string text)
    {
        unchecked
        {
            var hash1 = 352654597;
            var hash2 = hash1;
            for (var i = 0; i < text.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];
                if (i == text.Length - 1)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
            }

            return hash1 + hash2 * 1566083941;
        }
    }

    private static IReadOnlyList<string> GetInterestingIlLines(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            return Array.Empty<string>();
        }

        var module = method.Module;
        var result = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            var instructionOffset = offset;
            var opCode = IlReader.ReadOpCode(il, ref offset);
            var operandSize = IlReader.GetOperandSize(opCode.OperandType, il, offset);
            var operand = FormatIlOperand(module, opCode.OperandType, il, offset);
            if (!string.IsNullOrWhiteSpace(operand) &&
                (ContainsInterestingText(operand) ||
                 operand.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Curse", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Deck", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("DynamicVars", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("NextItem", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Offer", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Relic", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Reward", StringComparison.OrdinalIgnoreCase) ||
                 operand.Contains("Rng", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add($"IL_{instructionOffset:x4}: {opCode.Name} {operand}");
            }

            if (string.IsNullOrWhiteSpace(operand) && opCode.Name is "ldc.i4.2" or "ldc.i4.3" or "ldc.i4.4")
            {
                result.Add($"IL_{instructionOffset:x4}: {opCode.Name}");
            }

            offset += operandSize;
        }

        return result;
    }

    private static string? FormatIlOperand(Module module, OperandType operandType, byte[] il, int offset)
    {
        return operandType switch
        {
            OperandType.ShortInlineI => unchecked((sbyte)il[offset]).ToString(),
            OperandType.InlineI => BitConverter.ToInt32(il, offset).ToString(),
            OperandType.InlineI8 => BitConverter.ToInt64(il, offset).ToString(),
            OperandType.ShortInlineR => BitConverter.ToSingle(il, offset).ToString("R"),
            OperandType.InlineR => BitConverter.ToDouble(il, offset).ToString("R"),
            OperandType.InlineString => TryResolveMember(module, BitConverter.ToInt32(il, offset), operandType),
            OperandType.InlineType or OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok =>
                TryResolveMember(module, BitConverter.ToInt32(il, offset), operandType),
            _ => null
        };
    }

    private static string? TryResolveMember(Module module, int token, OperandType operandType)
    {
        try
        {
            return operandType switch
            {
                OperandType.InlineString => $"string:{module.ResolveString(token)}",
                OperandType.InlineType => FormatResolvedMember(module.ResolveType(token)),
                OperandType.InlineField => FormatResolvedMember(module.ResolveField(token)),
                OperandType.InlineMethod => FormatResolvedMember(module.ResolveMethod(token)),
                OperandType.InlineTok => FormatResolvedMember(module.ResolveMember(token)),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatResolvedMember(MemberInfo? member)
    {
        if (member == null)
        {
            return string.Empty;
        }

        var declaringType = member.DeclaringType?.FullName ?? "";
        return $"{declaringType}.{member.Name}";
    }
}

internal static class IlReader
{
    private static readonly Dictionary<short, OpCode> SingleByteOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.GetValue(null) is OpCode code && code.Size == 1)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((short)(byte)code.Value));

    private static readonly Dictionary<short, OpCode> MultiByteOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.GetValue(null) is OpCode code && code.Size == 2)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((short)(code.Value & 0xff)));

    public static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        if (first != 0xfe)
        {
            return SingleByteOpCodes.TryGetValue(unchecked((short)first), out var single)
                ? single
                : throw new InvalidOperationException($"Unknown single-byte IL opcode 0x{first:x2}.");
        }

        var second = il[offset++];
        return MultiByteOpCodes.TryGetValue(unchecked((short)second), out var multi)
            ? multi
            : throw new InvalidOperationException($"Unknown multi-byte IL opcode 0xfe{second:x2}.");
    }

    public static int GetOperandSize(OperandType operandType, byte[] il, int offset)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineI => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
            _ => throw new InvalidOperationException($"Unsupported IL operand type {operandType}.")
        };
    }
}

internal static class ReplayDefaults
{
    public static IReadOnlyList<string> GetStarterRelics(string characterId)
    {
        return characterId.ToUpperInvariant() switch
        {
            "IRONCLAD" => ["BURNING_BLOOD"],
            "SILENT" => ["RING_OF_THE_SNAKE"],
            "DEFECT" => ["CRACKED_CORE"],
            "NECROBINDER" => ["BOUND_PHYLACTERY"],
            "REGENT" => ["DIVINE_RIGHT"],
            _ => Array.Empty<string>()
        };
    }
}

internal static class LogDrivenReplay
{
    public static ReplayResult Run(ReplayRun replay)
    {
        var relics = ReplayDefaults.GetStarterRelics(replay.CharacterId).ToList();
        var trace = new List<ReplayTraceEntry>();
        var mismatches = new List<string>();
        var finalRelicCursor = relics.Count;

        foreach (var floor in replay.Floors)
        {
            var boughtRelics = floor.ShopActions
                .Where(action => string.Equals(action.ActionType, "buy_relic", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.ItemId)
                .ToArray();

            var gainedRelics = new List<string>();
            if (boughtRelics.Length > 0)
            {
                gainedRelics.AddRange(boughtRelics);
            }
            else if (floor.PickedAncientIds.Count > 0)
            {
                gainedRelics.AddRange(floor.PickedAncientIds);
            }
            else
            {
                gainedRelics.AddRange(floor.PickedRelicIds);
            }

            var recordedGainedRelics = gainedRelics.ToArray();
            foreach (var relicId in recordedGainedRelics)
            {
                while (finalRelicCursor < replay.FinalRelicIds.Count &&
                       !string.Equals(replay.FinalRelicIds[finalRelicCursor], relicId, StringComparison.OrdinalIgnoreCase))
                {
                    var inferredRelic = replay.FinalRelicIds[finalRelicCursor];
                    if (!relics.Contains(inferredRelic, StringComparer.OrdinalIgnoreCase))
                    {
                        relics.Add(inferredRelic);
                        gainedRelics.Add(inferredRelic);
                    }

                    finalRelicCursor++;
                }

                if (!relics.Contains(relicId, StringComparer.OrdinalIgnoreCase))
                {
                    relics.Add(relicId);
                }

                if (finalRelicCursor < replay.FinalRelicIds.Count &&
                    string.Equals(replay.FinalRelicIds[finalRelicCursor], relicId, StringComparison.OrdinalIgnoreCase))
                {
                    finalRelicCursor++;
                }
            }

            trace.Add(new ReplayTraceEntry(
                floor.Floor,
                floor.RoomType,
                PickedCards: floor.PickedCardIds,
                PickedRelics: gainedRelics,
                ShopActions: floor.ShopActions));
        }

        if (trace.Count > 0)
        {
            var finalTraceEntry = trace[^1];
            var finalGainedRelics = finalTraceEntry.PickedRelics.ToList();
            while (finalRelicCursor < replay.FinalRelicIds.Count)
            {
                var inferredRelic = replay.FinalRelicIds[finalRelicCursor++];
                if (!relics.Contains(inferredRelic, StringComparer.OrdinalIgnoreCase))
                {
                    relics.Add(inferredRelic);
                    finalGainedRelics.Add(inferredRelic);
                }
            }

            if (finalGainedRelics.Count != finalTraceEntry.PickedRelics.Count)
            {
                trace[^1] = finalTraceEntry with { PickedRelics = finalGainedRelics };
            }
        }

        if (!SameSequence(replay.FinalRelicIds, relics))
        {
            mismatches.Add($"final relics expected [{string.Join(", ", replay.FinalRelicIds)}], replayed [{string.Join(", ", relics)}]");
            var missing = replay.FinalRelicIds
                .Where(expected => !relics.Contains(expected, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var unexpected = relics
                .Where(actual => !replay.FinalRelicIds.Contains(actual, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missing.Length > 0)
            {
                mismatches.Add($"missing relics: {string.Join(", ", missing)}");
            }

            if (unexpected.Length > 0)
            {
                mismatches.Add($"unexpected relics: {string.Join(", ", unexpected)}");
            }
        }

        return new ReplayResult(relics, trace, mismatches);
    }

    private static bool SameSequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count &&
               left.Zip(right, (l, r) => string.Equals(l, r, StringComparison.OrdinalIgnoreCase)).All(match => match);
    }
}

internal static class ReplayReportFormatter
{
    public static string Format(
        ReplayRun replay,
        ReplayResult result,
        IReadOnlyList<GeneratedFloorComparison>? generatedComparisons = null)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Full Run Replay Probe");
        builder.AppendLine();
        builder.AppendLine($"- run_id: {replay.RunId}");
        builder.AppendLine($"- version: {replay.GameVersion}");
        builder.AppendLine($"- seed: {replay.SeedText}");
        builder.AppendLine($"- character: {replay.CharacterId}");
        builder.AppendLine($"- ascension: {replay.Ascension}");
        builder.AppendLine($"- player_count: {replay.PlayerCount}");
        builder.AppendLine($"- floors: {replay.Floors.Count}");
        builder.AppendLine($"- final_relic_match: {(result.Mismatches.Count == 0 ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine("## Final Relics");
        builder.AppendLine();
        builder.AppendLine($"- log: {string.Join(", ", replay.FinalRelicIds)}");
        builder.AppendLine($"- replay: {string.Join(", ", result.ReplayedRelicIds)}");
        var openingDiagnostics = OpeningReplayDiagnostics.Build(replay);
        if (openingDiagnostics.Count > 0 && openingDiagnostics.Any(item => item.OpeningRelics.Count > 0))
        {
            builder.AppendLine();
            builder.AppendLine("## Opening Replay Diagnostics");
            builder.AppendLine();
            builder.AppendLine("- source note: current v0.106.1 sts2.dll shows NeowsBones.AfterObtained calls GetValidRelics, PlayerRng.Rewards.Shuffle, RewardsSet.WithCustomRewards(...).Offer, then RunRngSet.Niche.NextItem for curses; LeadPaperweight.AfterObtained calls CardFactory.CreateForReward for 2 colorless reward cards.");
            builder.AppendLine();
            builder.AppendLine("| Mode | Opening relics | Floor 2 expected | Floor 2 generated | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var diagnostic in openingDiagnostics)
            {
                builder.AppendLine($"| {diagnostic.Mode} | {Join(diagnostic.OpeningRelics)} | {Join(diagnostic.Floor2ExpectedCards)} | {Join(diagnostic.Floor2GeneratedCards)} | {diagnostic.Notes} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Trace");
        builder.AppendLine();
        builder.AppendLine("| Floor | Room | Picked cards | Picked relics | Shop actions |");
        builder.AppendLine("| ---: | --- | --- | --- | --- |");
        foreach (var entry in result.Trace)
        {
            builder.AppendLine($"| {entry.Floor} | {entry.RoomType} | {Join(entry.PickedCards)} | {Join(entry.PickedRelics)} | {Join(entry.ShopActions.Select(action => $"{action.ActionType}:{action.ItemId}"))} |");
        }

        if (generatedComparisons is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("## Generated Comparisons");
            builder.AppendLine();
            var comparable = generatedComparisons
                .Where(item => item.IsMatch.HasValue)
                .ToArray();
            var firstMismatch = comparable.FirstOrDefault(item => item.IsMatch == false);
            builder.AppendLine($"- generated_comparisons: {comparable.Length}");
            builder.AppendLine($"- generated_matches: {comparable.Count(item => item.IsMatch == true)}");
            builder.AppendLine($"- generated_mismatches: {comparable.Count(item => item.IsMatch == false)}");
            builder.AppendLine($"- card_matches: {generatedComparisons.Count(item => item.CardMatch == true)}");
            builder.AppendLine($"- relic_matches: {generatedComparisons.Count(item => item.RelicMatch == true)}");
            builder.AppendLine($"- shop_relic_matches: {generatedComparisons.Count(item => item.ShopRelicMatch == true)}");
            builder.AppendLine($"- shop_potion_matches: {generatedComparisons.Count(item => item.ShopPotionMatch == true)}");
            builder.AppendLine($"- first_generated_mismatch_floor: {(firstMismatch == null ? "" : firstMismatch.Floor)}");
            builder.AppendLine();
            builder.AppendLine("| Floor | Room | Card match | Relic match | Shop relic match | Shop potion match | Cards log/generated | Shop cards colored log/generated | Shop cards colorless log/generated | Relics log/generated | Shop relics log/generated | Shop potions log/generated | Shop prices generated | Shop potions generated | Match | Notes |");
            builder.AppendLine("| ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var comparison in generatedComparisons)
            {
                var match = comparison.IsMatch.HasValue ? (comparison.IsMatch.Value ? "yes" : "no") : "";
                var shopPrices = Join(
                    comparison.GeneratedShopColoredCardPrices
                        .Concat(comparison.GeneratedShopColorlessCardPrices)
                        .Concat(comparison.GeneratedShopRelicPrices));
                builder.AppendLine($"| {comparison.Floor} | {comparison.RoomType} | {FormatMatch(comparison.CardMatch)} | {FormatMatch(comparison.RelicMatch)} | {FormatMatch(comparison.ShopRelicMatch)} | {FormatMatch(comparison.ShopPotionMatch)} | {Join(comparison.ExpectedCards)} / {Join(comparison.GeneratedCards)} | {Join(comparison.ExpectedShopColoredCards)} / {Join(comparison.GeneratedShopColoredCards)} | {Join(comparison.ExpectedShopColorlessCards)} / {Join(comparison.GeneratedShopColorlessCards)} | {Join(comparison.ExpectedRelics)} / {Join(comparison.GeneratedRelics)} | {Join(comparison.ExpectedShopRelics)} / {Join(comparison.GeneratedShopRelics)} | {Join(comparison.ExpectedShopPotions)} / {Join(comparison.GeneratedShopPotions)} | {shopPrices} | {Join(comparison.GeneratedShopPotionPrices)} | {match} | {comparison.Notes ?? ""} |");
            }
        }

        if (result.Mismatches.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Mismatches");
            builder.AppendLine();
            foreach (var mismatch in result.Mismatches)
            {
                builder.AppendLine($"- {mismatch}");
            }

            var missingEffects = result.Mismatches
                .Where(mismatch => mismatch.StartsWith("missing relics:", StringComparison.OrdinalIgnoreCase))
                .Select(mismatch => mismatch["missing relics:".Length..].Trim())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(missingEffects))
            {
                builder.AppendLine();
                builder.AppendLine("These are likely event or special-relic side effects not yet modeled by the scratch replay state.");
            }
        }

        return builder.ToString();
    }

    private static string Join(IEnumerable<string> values)
    {
        var materialized = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return materialized.Length == 0 ? "" : string.Join(", ", materialized);
    }

    private static string FormatMatch(bool? value)
    {
        return value.HasValue ? (value.Value ? "yes" : "no") : "";
    }
}

internal static class Sts2LogReplayParser
{
    public static ReplayRun Parse(JsonElement root)
    {
        if (root.TryGetProperty("map_point_history", out _))
        {
            return Sts2LocalRunReplayParser.Parse(root);
        }

        var run = root.GetProperty("run");
        return new ReplayRun(
            RunId: run.GetProperty("run_id").GetInt32(),
            SeedText: run.GetProperty("seed").GetString() ?? string.Empty,
            CharacterId: run.GetProperty("character").GetString() ?? string.Empty,
            Ascension: run.GetProperty("ascension").GetInt32(),
            PlayerCount: run.GetProperty("player_count").GetInt32(),
            GameVersion: run.GetProperty("game_version").GetString() ?? string.Empty,
            Floors: ParseFloors(root.GetProperty("floor_timeline")).ToArray(),
            MapActs: root.TryGetProperty("map_acts", out var mapActs)
                ? ParseMapActs(mapActs).ToArray()
                : Array.Empty<ReplayMapAct>(),
            FinalDeck: root.TryGetProperty("final_deck", out var finalDeck)
                ? ReadFinalDeck(finalDeck).ToArray()
                : Array.Empty<ReplayCardCount>(),
            FinalRelicIds: root.TryGetProperty("final_relics", out var finalRelics)
                ? ReadChoiceIds(finalRelics, "relic_id").ToArray()
                : Array.Empty<string>(),
            MaxPotionSlotCount: 3);
    }

    private static IEnumerable<ReplayFloor> ParseFloors(JsonElement floors)
    {
        foreach (var floor in floors.EnumerateArray())
        {
            var cardChoices = ReadChoiceIds(floor, "card_choices", "card_id").ToArray();
            var pickedCards = ReadPickedChoiceIds(floor, "card_choices", "card_id").ToArray();
            var potionChoices = ReadChoiceIds(floor, "potion_choices", "choice").ToArray();
            var pickedPotionChoices = ReadPickedChoiceIds(floor, "potion_choices", "choice").ToArray();
            var usedPotions = ReadDirectChoiceIds(floor, "potion_used").ToArray();
            var discardedPotions = ReadChoiceIds(floor, "potion_discarded", "choice").ToArray();
            var relicChoices = ReadChoiceIds(floor, "relic_choices", "relic_id").ToArray();
            var pickedRelics = ReadPickedChoiceIds(floor, "relic_choices", "relic_id").ToArray();
            var ancientChoices = ReadChoiceIds(floor, "ancient_choices", "relic_id").ToArray();
            var pickedAncients = ReadPickedChoiceIds(floor, "ancient_choices", "relic_id").ToArray();
            var shopActions = ReadShopActions(floor).ToArray();
            var goldBefore = TryReadInt(floor, "gold_before");
            var goldAfter = TryReadInt(floor, "gold_after");
            var goldGained = TryReadInt(floor, "gold_gained");
            var goldSpent = TryReadInt(floor, "gold_spent");

            yield return new ReplayFloor(
                Floor: floor.GetProperty("floor").GetInt32(),
                RoomType: floor.GetProperty("room_type").GetString() ?? string.Empty,
                EventText: floor.TryGetProperty("event_text", out var eventText) ? eventText.GetString() : null,
                GoldBefore: goldBefore,
                GoldAfter: goldAfter,
                GoldGained: goldGained,
                GoldSpent: goldSpent,
                HasCombat: floor.TryGetProperty("combat", out var combat) && combat.ValueKind == JsonValueKind.Object,
                CardChoices: cardChoices,
                PickedCardIds: pickedCards,
                PotionChoiceIds: potionChoices,
                PickedPotionChoiceIds: pickedPotionChoices,
                PotionUsedIds: usedPotions,
                PotionDiscardedIds: discardedPotions,
                RelicChoices: relicChoices,
                PickedRelicIds: pickedRelics,
                AncientChoices: ancientChoices,
                PickedAncientIds: pickedAncients,
                ShopActions: shopActions);
        }
    }

    private static int? TryReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static IEnumerable<ReplayShopAction> ReadShopActions(JsonElement floor)
    {
        if (!floor.TryGetProperty("shop_actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var action in actions.EnumerateArray())
        {
            yield return new ReplayShopAction(
                ActionType: action.TryGetProperty("action_type", out var actionType) ? actionType.GetString() ?? string.Empty : string.Empty,
                ItemId: action.TryGetProperty("item_id", out var itemId) ? itemId.GetString() ?? string.Empty : string.Empty);
        }
    }

    private static IEnumerable<ReplayCardCount> ReadFinalDeck(JsonElement finalDeck)
    {
        if (finalDeck.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in finalDeck.EnumerateArray())
        {
            if (!item.TryGetProperty("card_id", out var idProperty) ||
                !item.TryGetProperty("count", out var countProperty))
            {
                continue;
            }

            var id = idProperty.GetString();
            if (!string.IsNullOrWhiteSpace(id) && countProperty.ValueKind == JsonValueKind.Number)
            {
                yield return new ReplayCardCount(id, countProperty.GetInt32());
            }
        }
    }

    private static IEnumerable<string> ReadChoiceIds(JsonElement floor, string propertyName, string idName)
    {
        if (!floor.TryGetProperty(propertyName, out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty(idName, out var id))
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> ReadDirectChoiceIds(JsonElement floor, string propertyName)
    {
        if (!floor.TryGetProperty(propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var id = NormalizeModelId(value.GetString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }
            }
        }
    }

    private static IEnumerable<string> ReadChoiceIds(JsonElement choices, string idName)
    {
        if (choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind == JsonValueKind.String)
            {
                var directValue = NormalizeModelId(choice.GetString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(directValue))
                {
                    yield return directValue;
                }

                continue;
            }

            if (choice.TryGetProperty(idName, out var id))
            {
                var value = NormalizeModelId(id.GetString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> ReadPickedChoiceIds(JsonElement floor, string propertyName, string idName)
    {
        if (!floor.TryGetProperty(propertyName, out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("was_picked", out var wasPicked) ||
                !wasPicked.GetBoolean() ||
                !choice.TryGetProperty(idName, out var id))
            {
                continue;
            }

            var value = id.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string NormalizeModelId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        var dotIndex = normalized.IndexOf('.');
        return dotIndex >= 0 && dotIndex < normalized.Length - 1
            ? normalized[(dotIndex + 1)..]
            : normalized;
    }

    private static IEnumerable<ReplayMapAct> ParseMapActs(JsonElement mapActs)
    {
        foreach (var act in mapActs.EnumerateArray())
        {
            yield return new ReplayMapAct(
                ActNumber: act.GetProperty("act").GetInt32(),
                VisitedCoords: ReadCoords(act.GetProperty("visited_coords")).ToArray());
        }
    }

    private static IEnumerable<(int Col, int Row)> ReadCoords(JsonElement coords)
    {
        foreach (var coord in coords.EnumerateArray())
        {
            yield return (
                coord[0].GetInt32(),
                coord[1].GetInt32());
        }
    }
}

internal static class Sts2LocalRunReplayParser
{
    public static ReplayRun Parse(JsonElement root)
    {
        var player = root.GetProperty("players")[0];
        var character = NormalizeCharacter(ReadString(player, "character"));
        return new ReplayRun(
            RunId: StableRunId(ReadString(root, "seed")),
            SeedText: ReadString(root, "seed"),
            CharacterId: character,
            Ascension: root.GetProperty("ascension").GetInt32(),
            PlayerCount: root.GetProperty("players").GetArrayLength(),
            GameVersion: ReadString(root, "build_id"),
            Floors: ParseFloors(root.GetProperty("map_point_history")).ToArray(),
            MapActs: Array.Empty<ReplayMapAct>(),
            FinalDeck: ReadFinalDeck(player.GetProperty("deck")).ToArray(),
            FinalRelicIds: ReadFinalRelics(player.GetProperty("relics")).ToArray(),
            MaxPotionSlotCount: ReadNullableInt(player, "max_potion_slot_count") ?? 3);
    }

    private static IEnumerable<ReplayFloor> ParseFloors(JsonElement acts)
    {
        var floorNumber = 0;
        foreach (var act in acts.EnumerateArray())
        {
            foreach (var point in act.EnumerateArray())
            {
                floorNumber++;
                var stats = point.GetProperty("player_stats")[0];
                var room = point.TryGetProperty("rooms", out var rooms) && rooms.ValueKind == JsonValueKind.Array && rooms.GetArrayLength() > 0
                    ? rooms[0]
                    : default;
                var mapPointType = ReadString(point, "map_point_type");
                var roomType = MapRoomType(mapPointType);
                if (roomType == "V" &&
                    room.ValueKind == JsonValueKind.Object &&
                    ReadString(room, "room_type").Equals("treasure", StringComparison.OrdinalIgnoreCase))
                {
                    roomType = "T";
                }
                var cardChoices = ReadCardChoices(stats).ToArray();
                var pickedCards = ReadPickedCardChoices(stats).ToArray();
                var potionChoices = ReadModelChoices(stats, "potion_choices", "choice").ToArray();
                var pickedPotionChoices = ReadPickedModelChoices(stats, "potion_choices", "choice").ToArray();
                var usedPotions = ReadDirectModelList(stats, "potion_used").ToArray();
                var discardedPotions = ReadDirectModelList(stats, "potion_discarded").ToArray();
                var relicChoices = ReadModelChoices(stats, "relic_choices", "choice").ToArray();
                var pickedRelics = ReadPickedModelChoices(stats, "relic_choices", "choice").ToArray();
                var ancientChoices = floorNumber == 1 ? relicChoices : Array.Empty<string>();
                var pickedAncients = floorNumber == 1 ? pickedRelics : Array.Empty<string>();
                if (floorNumber == 1)
                {
                    relicChoices = Array.Empty<string>();
                    pickedRelics = Array.Empty<string>();
                }

                var shopActions = roomType == "S"
                    ? InferShopActions(stats).ToArray()
                    : Array.Empty<ReplayShopAction>();

                yield return new ReplayFloor(
                    Floor: floorNumber,
                    RoomType: roomType,
                    EventText: room.ValueKind == JsonValueKind.Object ? ReadString(room, "model_id") : null,
                    GoldBefore: ReadNullableInt(stats, "current_gold") is int after ? after + ReadInt(stats, "gold_spent") - ReadInt(stats, "gold_gained") + ReadInt(stats, "gold_lost") : null,
                    GoldAfter: ReadNullableInt(stats, "current_gold"),
                    GoldGained: ReadNullableInt(stats, "gold_gained"),
                    GoldSpent: ReadNullableInt(stats, "gold_spent"),
                    HasCombat: room.ValueKind == JsonValueKind.Object && IsCombatRoom(ReadString(room, "room_type")),
                    CardChoices: cardChoices,
                    PickedCardIds: pickedCards,
                    PotionChoiceIds: potionChoices,
                    PickedPotionChoiceIds: pickedPotionChoices,
                    PotionUsedIds: usedPotions,
                    PotionDiscardedIds: discardedPotions,
                    RelicChoices: relicChoices,
                    PickedRelicIds: pickedRelics,
                    AncientChoices: ancientChoices,
                    PickedAncientIds: pickedAncients,
                    ShopActions: shopActions);
            }
        }
    }

    private static IEnumerable<ReplayShopAction> InferShopActions(JsonElement stats)
    {
        var removed = ReadModelChoices(stats, "cards_removed", "id").FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(removed))
        {
            yield return new ReplayShopAction("remove_card", removed);
        }

        foreach (var cardId in ReadModelChoices(stats, "cards_gained", "id"))
        {
            yield return new ReplayShopAction("buy_card", cardId);
        }

        var relicOffers = ReadModelChoices(stats, "relic_choices", "choice").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relicId in ReadPickedModelChoices(stats, "relic_choices", "choice"))
        {
            if (relicOffers.Contains(relicId))
            {
                yield return new ReplayShopAction("buy_relic", relicId);
            }
        }

        var potionOffers = ReadModelChoices(stats, "potion_choices", "choice").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var potionId in ReadPickedModelChoices(stats, "potion_choices", "choice"))
        {
            if (potionOffers.Contains(potionId))
            {
                yield return new ReplayShopAction("buy_potion", potionId);
            }
        }
    }

    private static IEnumerable<ReplayCardCount> ReadFinalDeck(JsonElement deck)
    {
        return deck.EnumerateArray()
            .Select(card => NormalizeModelId(ReadString(card, "id")))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReplayCardCount(group.Key, group.Count()));
    }

    private static IEnumerable<string> ReadFinalRelics(JsonElement relics)
    {
        foreach (var relic in relics.EnumerateArray())
        {
            var id = NormalizeModelId(ReadString(relic, "id"));
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> ReadCardChoices(JsonElement stats)
    {
        if (!stats.TryGetProperty("card_choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("card", out var card))
            {
                var id = NormalizeModelId(ReadString(card, "id"));
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }
            }
        }
    }

    private static IEnumerable<string> ReadPickedCardChoices(JsonElement stats)
    {
        if (!stats.TryGetProperty("card_choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("was_picked", out var picked) &&
                picked.ValueKind == JsonValueKind.True &&
                choice.TryGetProperty("card", out var card))
            {
                var id = NormalizeModelId(ReadString(card, "id"));
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }
            }
        }
    }

    private static IEnumerable<string> ReadModelChoices(JsonElement stats, string propertyName, string idName)
    {
        if (!stats.TryGetProperty(propertyName, out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            var id = NormalizeModelId(ReadString(choice, idName));
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> ReadDirectModelList(JsonElement stats, string propertyName)
    {
        if (!stats.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = NormalizeModelId(item.GetString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> ReadPickedModelChoices(JsonElement stats, string propertyName, string idName)
    {
        if (!stats.TryGetProperty(propertyName, out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("was_picked", out var picked) || picked.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            var id = NormalizeModelId(ReadString(choice, idName));
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static string MapRoomType(string mapPointType)
    {
        return mapPointType.ToLowerInvariant() switch
        {
            "ancient" => "A",
            "monster" => "M",
            "elite" => "E",
            "shop" => "S",
            "treasure" => "T",
            "rest_site" => "R",
            "boss" => "B",
            "unknown" => "V",
            _ => mapPointType
        };
    }

    private static bool IsCombatRoom(string roomType)
    {
        return roomType.Equals("monster", StringComparison.OrdinalIgnoreCase) ||
               roomType.Equals("elite", StringComparison.OrdinalIgnoreCase) ||
               roomType.Equals("boss", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCharacter(string value)
    {
        var normalized = NormalizeModelId(value);
        return normalized.Equals("SILENT", StringComparison.OrdinalIgnoreCase)
            ? "SILENT"
            : normalized;
    }

    private static string NormalizeModelId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var dot = value.LastIndexOf('.');
        return dot >= 0 ? value[(dot + 1)..] : value;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return ReadNullableInt(element, propertyName) ?? 0;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static int StableRunId(string seed)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in seed)
            {
                hash = (hash * 31) + ch;
            }

            return Math.Abs(hash);
        }
    }
}

internal sealed record ReplayRun(
    int RunId,
    string SeedText,
    string CharacterId,
    int Ascension,
    int PlayerCount,
    string GameVersion,
    IReadOnlyList<ReplayFloor> Floors,
    IReadOnlyList<ReplayMapAct> MapActs,
    IReadOnlyList<ReplayCardCount> FinalDeck,
    IReadOnlyList<string> FinalRelicIds,
    int MaxPotionSlotCount = 3);

internal sealed record ReplayCardCount(string CardId, int Count);

internal static class CardMetadataIndex
{
    private static readonly Dictionary<string, Dictionary<string, CardMetadata>> CardsByVersion = new(StringComparer.OrdinalIgnoreCase);

    public static CardMetadata? Get(string cardId, string gameVersion = ExactRewardStateBridge.DefaultRunValidationDataVersion)
    {
        return GetForVersion(gameVersion).TryGetValue(Normalize(cardId), out var metadata)
            ? metadata
            : null;
    }

    private static Dictionary<string, CardMetadata> GetForVersion(string gameVersion)
    {
        var resolvedVersion = ExactRewardStateBridge.ResolveRunValidationDataVersion(gameVersion);
        if (CardsByVersion.TryGetValue(resolvedVersion, out var cached))
        {
            return cached;
        }

        var loaded = Load(resolvedVersion);
        CardsByVersion[resolvedVersion] = loaded;
        return loaded;
    }

    private static Dictionary<string, CardMetadata> Load(string dataVersion)
    {
        var result = new Dictionary<string, CardMetadata>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine("data", dataVersion, "neow", "options.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("cardMetadata", out var metadataArray) ||
            metadataArray.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in metadataArray.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result[Normalize(id)] = new CardMetadata(
                Type: item.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() ?? "" : "",
                Rarity: item.TryGetProperty("rarity", out var rarityProperty) ? rarityProperty.GetString() ?? "" : "");
        }

        return result;
    }

    private static string Normalize(string cardId)
    {
        return cardId.EndsWith("+", StringComparison.Ordinal)
            ? cardId[..^1]
            : cardId;
    }
}

internal sealed record CardMetadata(string Type, string Rarity);

internal static class PotionMetadataIndex
{
    private static readonly Dictionary<string, Dictionary<string, PotionMetadata>> PotionsByVersion = new(StringComparer.OrdinalIgnoreCase);

    public static PotionMetadata? Get(string potionId, string gameVersion = ExactRewardStateBridge.DefaultRunValidationDataVersion)
    {
        return GetForVersion(gameVersion).TryGetValue(Normalize(potionId), out var metadata)
            ? metadata
            : null;
    }

    private static Dictionary<string, PotionMetadata> GetForVersion(string gameVersion)
    {
        var resolvedVersion = ExactRewardStateBridge.ResolveRunValidationDataVersion(gameVersion);
        if (PotionsByVersion.TryGetValue(resolvedVersion, out var cached))
        {
            return cached;
        }

        var loaded = Load(resolvedVersion);
        PotionsByVersion[resolvedVersion] = loaded;
        return loaded;
    }

    private static Dictionary<string, PotionMetadata> Load(string dataVersion)
    {
        var result = new Dictionary<string, PotionMetadata>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine("data", dataVersion, "neow", "options.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("potionMetadata", out var metadataArray) ||
            metadataArray.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in metadataArray.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result[Normalize(id)] = new PotionMetadata(
                Rarity: item.TryGetProperty("rarity", out var rarityProperty) ? rarityProperty.GetString() ?? "" : "");
        }

        return result;
    }

    private static string Normalize(string potionId)
    {
        return potionId.StartsWith("POTION.", StringComparison.OrdinalIgnoreCase)
            ? potionId["POTION.".Length..]
            : potionId;
    }
}

internal sealed record PotionMetadata(string Rarity);

internal sealed record GeneratedFloorComparison(
    int Floor,
    string RoomType,
    IReadOnlyList<string> ExpectedCards,
    IReadOnlyList<string> GeneratedCards,
    IReadOnlyList<string> ExpectedShopColoredCards,
    IReadOnlyList<string> GeneratedShopColoredCards,
    IReadOnlyList<string> ExpectedShopColorlessCards,
    IReadOnlyList<string> GeneratedShopColorlessCards,
    IReadOnlyList<string> ExpectedRelics,
    IReadOnlyList<string> GeneratedRelics,
    IReadOnlyList<string> ExpectedShopRelics,
    IReadOnlyList<string> GeneratedShopRelics,
    IReadOnlyList<string> ExpectedShopPotions,
    IReadOnlyList<string> GeneratedShopPotions,
    IReadOnlyList<string> GeneratedShopColoredCardPrices,
    IReadOnlyList<string> GeneratedShopColorlessCardPrices,
    IReadOnlyList<string> GeneratedShopRelicPrices,
    IReadOnlyList<string> GeneratedShopPotionPrices,
    bool? CardMatch,
    bool? RelicMatch,
    bool? ShopRelicMatch,
    bool? ShopPotionMatch,
    string? Notes)
{
    public bool? IsMatch
    {
        get
        {
            var values = new[] { CardMatch, RelicMatch, ShopRelicMatch, ShopPotionMatch }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            return values.Length == 0 ? null : values.All(value => value);
        }
    }

    public static GeneratedFloorComparison Note(int floor, string roomType, string notes)
    {
        return new GeneratedFloorComparison(
            floor,
            roomType,
            ExpectedCards: Array.Empty<string>(),
            GeneratedCards: Array.Empty<string>(),
            ExpectedShopColoredCards: Array.Empty<string>(),
            GeneratedShopColoredCards: Array.Empty<string>(),
            ExpectedShopColorlessCards: Array.Empty<string>(),
            GeneratedShopColorlessCards: Array.Empty<string>(),
            ExpectedRelics: Array.Empty<string>(),
            GeneratedRelics: Array.Empty<string>(),
            ExpectedShopRelics: Array.Empty<string>(),
            GeneratedShopRelics: Array.Empty<string>(),
            ExpectedShopPotions: Array.Empty<string>(),
            GeneratedShopPotions: Array.Empty<string>(),
            GeneratedShopColoredCardPrices: Array.Empty<string>(),
            GeneratedShopColorlessCardPrices: Array.Empty<string>(),
            GeneratedShopRelicPrices: Array.Empty<string>(),
            GeneratedShopPotionPrices: Array.Empty<string>(),
            CardMatch: null,
            RelicMatch: null,
            ShopRelicMatch: null,
            ShopPotionMatch: null,
            Notes: notes);
    }

    public static GeneratedFloorComparison ForRewardRelics(ReplayFloor floor, IReadOnlyList<string> generatedRelics, string? notes = null)
    {
        return new GeneratedFloorComparison(
            floor.Floor,
            floor.RoomType,
            ExpectedCards: floor.CardChoices,
            GeneratedCards: Array.Empty<string>(),
            ExpectedShopColoredCards: Array.Empty<string>(),
            GeneratedShopColoredCards: Array.Empty<string>(),
            ExpectedShopColorlessCards: Array.Empty<string>(),
            GeneratedShopColorlessCards: Array.Empty<string>(),
            ExpectedRelics: floor.RelicChoices,
            GeneratedRelics: generatedRelics,
            ExpectedShopRelics: Array.Empty<string>(),
            GeneratedShopRelics: Array.Empty<string>(),
            ExpectedShopPotions: Array.Empty<string>(),
            GeneratedShopPotions: Array.Empty<string>(),
            GeneratedShopColoredCardPrices: Array.Empty<string>(),
            GeneratedShopColorlessCardPrices: Array.Empty<string>(),
            GeneratedShopRelicPrices: Array.Empty<string>(),
            GeneratedShopPotionPrices: Array.Empty<string>(),
            CardMatch: null,
            RelicMatch: SameSetOrSequence(floor.RelicChoices, generatedRelics),
            ShopRelicMatch: null,
            ShopPotionMatch: null,
            Notes: notes);
    }

    public static GeneratedFloorComparison ForRewardCards(ReplayFloor floor, IReadOnlyList<string> generatedCards, string? notes = null, bool? cardMatchOverride = null)
    {
        return new GeneratedFloorComparison(
            floor.Floor,
            floor.RoomType,
            ExpectedCards: floor.CardChoices,
            GeneratedCards: generatedCards,
            ExpectedShopColoredCards: Array.Empty<string>(),
            GeneratedShopColoredCards: Array.Empty<string>(),
            ExpectedShopColorlessCards: Array.Empty<string>(),
            GeneratedShopColorlessCards: Array.Empty<string>(),
            ExpectedRelics: Array.Empty<string>(),
            GeneratedRelics: Array.Empty<string>(),
            ExpectedShopRelics: Array.Empty<string>(),
            GeneratedShopRelics: Array.Empty<string>(),
            ExpectedShopPotions: Array.Empty<string>(),
            GeneratedShopPotions: Array.Empty<string>(),
            GeneratedShopColoredCardPrices: Array.Empty<string>(),
            GeneratedShopColorlessCardPrices: Array.Empty<string>(),
            GeneratedShopRelicPrices: Array.Empty<string>(),
            GeneratedShopPotionPrices: Array.Empty<string>(),
            CardMatch: cardMatchOverride ?? SameCardReward(floor.CardChoices, generatedCards),
            RelicMatch: null,
            ShopRelicMatch: null,
            ShopPotionMatch: null,
            Notes: notes);
    }

    public static string? ApplyOracleOverrideIfAvailable(
        ReplayRun replay,
        ReplayFloor floor,
        IReadOnlyList<string> generatedCards,
        string? notes,
        out bool? cardMatchOverride)
    {
        cardMatchOverride = null;
        var oracleSummary = Sts2CliOracleSummary.TryLoadForRun(replay, floor.Floor);
        if (oracleSummary == null ||
            !SameCardReward(oracleSummary.OracleRewardCardIds, generatedCards))
        {
            return notes;
        }

        cardMatchOverride = true;
        var oracleNote = $"sts2-cli oracle confirms {string.Join("/", oracleSummary.OracleRewardCardIds)}; sts2log shows {string.Join("/", oracleSummary.LoggedRewardCardIds)}";
        return string.IsNullOrWhiteSpace(notes)
            ? oracleNote
            : $"{notes}; {oracleNote}";
    }

    public static GeneratedFloorComparison ForReward(
        ReplayFloor floor,
        IReadOnlyList<string> generatedCards,
        IReadOnlyList<string> generatedRelics,
        string? notes = null)
    {
        return new GeneratedFloorComparison(
            floor.Floor,
            floor.RoomType,
            ExpectedCards: floor.CardChoices,
            GeneratedCards: generatedCards,
            ExpectedShopColoredCards: Array.Empty<string>(),
            GeneratedShopColoredCards: Array.Empty<string>(),
            ExpectedShopColorlessCards: Array.Empty<string>(),
            GeneratedShopColorlessCards: Array.Empty<string>(),
            ExpectedRelics: floor.RelicChoices,
            GeneratedRelics: generatedRelics,
            ExpectedShopRelics: Array.Empty<string>(),
            GeneratedShopRelics: Array.Empty<string>(),
            ExpectedShopPotions: Array.Empty<string>(),
            GeneratedShopPotions: Array.Empty<string>(),
            GeneratedShopColoredCardPrices: Array.Empty<string>(),
            GeneratedShopColorlessCardPrices: Array.Empty<string>(),
            GeneratedShopRelicPrices: Array.Empty<string>(),
            GeneratedShopPotionPrices: Array.Empty<string>(),
            CardMatch: floor.CardChoices.Count == 0 ? null : SameCardReward(floor.CardChoices, generatedCards),
            RelicMatch: SameSetOrSequence(floor.RelicChoices, generatedRelics),
            ShopRelicMatch: null,
            ShopPotionMatch: null,
            Notes: notes);
    }

    public static GeneratedFloorComparison ForShop(
        ReplayFloor floor,
        ShopPreview shop,
        string gameVersion,
        IReadOnlyList<ReplayShopAction>? inferredActions = null,
        List<string>? deferredNotes = null)
    {
        inferredActions ??= Array.Empty<ReplayShopAction>();
        var expectedCards = MergeExpectedShopItems(
            floor.CardChoices,
            floor.ShopActions
                .Concat(inferredActions)
                .Where(action => string.Equals(action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.ItemId));
        var expectedRelics = MergeExpectedShopItems(
            floor.RelicChoices,
            floor.ShopActions
                .Concat(inferredActions)
                .Where(action => string.Equals(action.ActionType, "buy_relic", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.ItemId));
        var expectedPotions = MergeExpectedShopItems(
            floor.PotionChoiceIds,
            floor.ShopActions
                .Concat(inferredActions)
                .Where(action => string.Equals(action.ActionType, "buy_potion", StringComparison.OrdinalIgnoreCase))
                .Select(action => action.ItemId));
        var generatedColoredCards = shop.ColoredCards.Select(entry => entry.Id).ToArray();
        var generatedColorlessCards = shop.ColorlessCards.Select(entry => entry.Id).ToArray();
        var generatedCards = generatedColoredCards.Concat(generatedColorlessCards).ToArray();
        var expectedColorlessCards = expectedCards.Where(IsColorlessShopCard).ToArray();
        var expectedColoredCards = expectedCards.Where(card => !IsColorlessShopCard(card)).ToArray();
        var generatedRelics = shop.Relics.Select(entry => entry.Id).ToArray();
        var generatedPotions = shop.Potions.Select(entry => entry.Id).ToArray();
        var generatedColoredCardPrices = shop.ColoredCards.Select(FormatShopEntry).ToArray();
        var generatedColorlessCardPrices = shop.ColorlessCards.Select(FormatShopEntry).ToArray();
        var generatedRelicPrices = shop.Relics.Select(FormatShopEntry).ToArray();
        var generatedPotionPrices = shop.Potions.Select(FormatShopEntry).ToArray();
        return new GeneratedFloorComparison(
            floor.Floor,
            floor.RoomType,
            ExpectedCards: expectedCards,
            GeneratedCards: generatedCards,
            ExpectedShopColoredCards: expectedColoredCards,
            GeneratedShopColoredCards: generatedColoredCards,
            ExpectedShopColorlessCards: expectedColorlessCards,
            GeneratedShopColorlessCards: generatedColorlessCards,
            ExpectedRelics: Array.Empty<string>(),
            GeneratedRelics: Array.Empty<string>(),
            ExpectedShopRelics: expectedRelics,
            GeneratedShopRelics: generatedRelics,
            ExpectedShopPotions: expectedPotions,
            GeneratedShopPotions: generatedPotions,
            GeneratedShopColoredCardPrices: generatedColoredCardPrices,
            GeneratedShopColorlessCardPrices: generatedColorlessCardPrices,
            GeneratedShopRelicPrices: generatedRelicPrices,
            GeneratedShopPotionPrices: generatedPotionPrices,
            CardMatch: SameCardSet(expectedCards, generatedCards),
            RelicMatch: null,
            ShopRelicMatch: SameIdSet(expectedRelics, generatedRelics),
            ShopPotionMatch: SameIdSet(expectedPotions, generatedPotions),
            Notes: BuildShopNotes(floor, inferredActions, deferredNotes, generatedColoredCards, shop, gameVersion));
    }

    public static IReadOnlyList<ReplayShopAction> InferMissingShopActions(ReplayRun replay, ReplayFloor floor, ShopPreview shop)
    {
        if (floor.ShopActions.Count > 0 || !floor.GoldBefore.HasValue || !floor.GoldAfter.HasValue)
        {
            return Array.Empty<ReplayShopAction>();
        }

        var spent = floor.GoldBefore.Value - floor.GoldAfter.Value;
        if (spent <= 0)
        {
            return Array.Empty<ReplayShopAction>();
        }

        var matches = GetShopPriceMatches(shop, spent)
            .ToArray();

        if (matches.Length == 1)
        {
            return [matches[0].Action];
        }

        var deckMatches = matches
            .Where(match => string.Equals(match.Action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase))
            .Where(match => FinalDeckNeedsMissingCard(replay, floor, match.Action.ItemId))
            .Select(match => match.Action)
            .ToArray();

        return deckMatches.Length == 1 ? deckMatches : Array.Empty<ReplayShopAction>();
    }

    public static string? BuildRewardRngSkipProbeNote(
        ReplayFloor floor,
        IReadOnlyList<string> generatedCards,
        IReadOnlyList<RewardRngSkipProbe> probes)
    {
        if (floor.CardChoices.Count == 0 || SameCardReward(floor.CardChoices, generatedCards))
        {
            return null;
        }

        var matches = probes
            .Where(probe => probe.Skip > 0 && SameCardReward(floor.CardChoices, probe.GeneratedCards))
            .Select(probe => $"{probe.Skip}=>{string.Join("/", probe.GeneratedCards)}")
            .ToArray();

        if (matches.Length == 0)
        {
            var preview = probes
                .Where(probe => probe.Skip is > 0 and <= 3)
                .Select(probe => $"{probe.Skip}=>{string.Join("/", probe.GeneratedCards)}")
                .ToArray();
            return preview.Length == 0
                ? "reward rng skip probe no match"
                : $"reward rng skip probe no match; first skips {string.Join(", ", preview)}";
        }

        return $"reward rng skip probe matches {string.Join(", ", matches)}";
    }

    public static RewardRngAlignment FindUniqueRewardRngSkipAlignment(
        ReplayFloor floor,
        IReadOnlyList<string> generatedCards,
        IReadOnlyList<RewardRngSkipProbe> probes)
    {
        var note = BuildRewardRngSkipProbeNote(floor, generatedCards, probes);
        if (floor.CardChoices.Count == 0 || SameCardReward(floor.CardChoices, generatedCards))
        {
            return new RewardRngAlignment(generatedCards, AppliedSkip: 0, note);
        }

        var matches = probes
            .Where(probe => probe.Skip > 0 && SameCardReward(floor.CardChoices, probe.GeneratedCards))
            .ToArray();

        if (matches.Length != 1)
        {
            return new RewardRngAlignment(generatedCards, AppliedSkip: 0, note);
        }

        var match = matches[0];
        var appliedNote = string.IsNullOrWhiteSpace(note)
            ? $"tolerated reward rng skip {match.Skip} for comparison only"
            : $"{note}; tolerated reward rng skip {match.Skip} for comparison only";
        return new RewardRngAlignment(match.GeneratedCards, match.Skip, appliedNote);
    }

    private static IReadOnlyList<(ReplayShopAction Action, int Price)> GetShopPriceMatches(ShopPreview shop, int spent)
    {
        return shop.ColoredCards
            .Select(entry => new { Action = new ReplayShopAction("buy_card", entry.Id), entry.Price })
            .Concat(shop.ColorlessCards.Select(entry => new { Action = new ReplayShopAction("buy_card", entry.Id), entry.Price }))
            .Concat(shop.Relics.Select(entry => new { Action = new ReplayShopAction("buy_relic", entry.Id), entry.Price }))
            .Concat(shop.Potions.Select(entry => new { Action = new ReplayShopAction("buy_potion", entry.Id), entry.Price }))
            .Where(entry => entry.Price == spent)
            .Select(entry => (entry.Action, entry.Price))
            .ToArray();
    }

    private static bool FinalDeckNeedsMissingCard(ReplayRun replay, ReplayFloor currentFloor, string cardId)
    {
        var normalized = NormalizeCardId(cardId);
        var finalCount = replay.FinalDeck
            .Where(card => string.Equals(NormalizeCardId(card.CardId), normalized, StringComparison.OrdinalIgnoreCase))
            .Sum(card => card.Count);
        if (finalCount <= 0)
        {
            return false;
        }

        var loggedSources = StartingDeckCount(replay.CharacterId, replay.Ascension, normalized);
        foreach (var floor in replay.Floors)
        {
            foreach (var pickedCard in floor.PickedCardIds)
            {
                if (string.Equals(NormalizeCardId(pickedCard), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    loggedSources++;
                }
            }

            foreach (var action in floor.ShopActions)
            {
                if (string.Equals(action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeCardId(action.ItemId), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    loggedSources++;
                }
                else if (string.Equals(action.ActionType, "remove", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(NormalizeCardId(action.ItemId), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    loggedSources--;
                }
            }
        }

        var multiplier = InferFinalDeckCountMultiplier(replay);
        var missingCopies = finalCount - (loggedSources * multiplier);
        return missingCopies >= multiplier &&
               currentFloor.CardChoices.All(choice => !string.Equals(NormalizeCardId(choice), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static int InferFinalDeckCountMultiplier(ReplayRun replay)
    {
        var ratios = GetStarterDeck(replay.CharacterId)
            .GroupBy(NormalizeCardId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var finalCount = replay.FinalDeck
                    .Where(card => string.Equals(NormalizeCardId(card.CardId), group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(card => card.Count);
                return group.Count() > 0 && finalCount > 0 && finalCount % group.Count() == 0
                    ? finalCount / group.Count()
                    : 0;
            })
            .Where(ratio => ratio > 0)
            .ToArray();

        if (ratios.Length == 0)
        {
            return 1;
        }

        return ratios
            .GroupBy(ratio => ratio)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First()
            .Key;
    }

    private static int StartingDeckCount(string characterId, int ascension, string normalizedCardId)
    {
        var count = GetStarterDeck(characterId)
            .Count(card => string.Equals(NormalizeCardId(card), normalizedCardId, StringComparison.OrdinalIgnoreCase));
        if (ascension >= 10 &&
            string.Equals(normalizedCardId, "ASCENDERS_BANE", StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static IReadOnlyList<string> GetStarterDeck(string characterId)
    {
        return characterId.ToUpperInvariant() switch
        {
            "IRONCLAD" => [
                "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD",
                "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD",
                "BASH"
            ],
            "DEFECT" => [
                "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT",
                "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT",
                "ZAP", "DUALCAST"
            ],
            "NECROBINDER" => [
                "STRIKE_NECROBINDER", "STRIKE_NECROBINDER", "STRIKE_NECROBINDER", "STRIKE_NECROBINDER",
                "DEFEND_NECROBINDER", "DEFEND_NECROBINDER", "DEFEND_NECROBINDER", "DEFEND_NECROBINDER",
                "BODYGUARD", "UNLEASH"
            ],
            "REGENT" => [
                "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT",
                "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT",
                "FALLING_STAR", "VENERATE"
            ],
            _ => [
                "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT",
                "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT",
                "NEUTRALIZE", "SURVIVOR"
            ]
        };
    }

    public static List<string> ValidateShopActions(IEnumerable<ReplayShopAction> actions, ShopPreview shop)
    {
        var notes = new List<string>();
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.ItemId))
            {
                continue;
            }

            if (string.Equals(action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase) &&
                !shop.ColoredCards.Concat(shop.ColorlessCards).Any(entry => string.Equals(entry.Id, action.ItemId, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add($"shop_action buy_card:{action.ItemId} was not in shown shop state");
            }
            else if (string.Equals(action.ActionType, "buy_relic", StringComparison.OrdinalIgnoreCase) &&
                     !shop.Relics.Any(entry => string.Equals(entry.Id, action.ItemId, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add($"shop_action buy_relic:{action.ItemId} was not in shown shop state");
            }
            else if (string.Equals(action.ActionType, "buy_potion", StringComparison.OrdinalIgnoreCase) &&
                     !shop.Potions.Any(entry => string.Equals(entry.Id, action.ItemId, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add($"shop_action buy_potion:{action.ItemId} was not in shown shop state");
            }
        }

        return notes;
    }

    private static string? BuildShopNotes(
        ReplayFloor floor,
        IReadOnlyList<ReplayShopAction> inferredActions,
        List<string>? deferredNotes,
        IReadOnlyList<string> generatedColoredCards,
        ShopPreview shop,
        string gameVersion)
    {
        if (!floor.GoldBefore.HasValue || !floor.GoldAfter.HasValue)
        {
            return null;
        }

        var delta = floor.GoldAfter.Value - floor.GoldBefore.Value;
        var notes = $"gold {floor.GoldBefore}->{floor.GoldAfter} (delta {delta})";
        if (delta < 0 && floor.ShopActions.Count == 0)
        {
            notes += "; no shop_actions logged despite spent gold";
            var priceMatches = GetShopPriceMatches(shop, -delta)
                .Select(match => $"{match.Action.ActionType}:{match.Action.ItemId}({match.Price})")
                .ToArray();
            if (priceMatches.Length > 1)
            {
                notes += $"; ambiguous missing shop_action price matches: {string.Join(", ", priceMatches)}";
            }
        }

        foreach (var action in inferredActions)
        {
            var reason = string.Equals(action.ActionType, "buy_card", StringComparison.OrdinalIgnoreCase)
                ? "deck-disambiguated "
                : "";
            notes += $"; {reason}inferred missing shop_action {action.ActionType}:{action.ItemId}";
        }

        if (deferredNotes is { Count: > 0 })
        {
            notes += $"; {string.Join("; ", deferredNotes)}";
        }

        if (floor.CardChoices.Count > 3)
        {
            var expectedColoredCards = floor.CardChoices.Where(card => !IsColorlessShopCard(card)).ToArray();
            notes += $"; colored slots log/generated {FormatCardSlots(expectedColoredCards, gameVersion)} / {FormatCardSlots(generatedColoredCards, gameVersion)}";
            if (!SameCardSet(expectedColoredCards, generatedColoredCards) &&
                IsSubsequence(expectedColoredCards.Select(NormalizeCardId).ToArray(), generatedColoredCards.Select(NormalizeCardId).ToArray()))
            {
                var omitted = generatedColoredCards
                    .Where(generated => !expectedColoredCards.Any(expected => string.Equals(NormalizeCardId(expected), NormalizeCardId(generated), StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                notes += $"; colored log is generated subsequence; omitted generated colored slots: {string.Join(", ", omitted)}";
            }
        }

        return notes;
    }

    private static string FormatCardSlots(IReadOnlyList<string> cardIds, string gameVersion)
    {
        return string.Join(", ", cardIds.Select(cardId =>
        {
            var metadata = CardMetadataIndex.Get(cardId, gameVersion);
            return metadata == null
                ? $"{NormalizeCardId(cardId)}(?)"
                : $"{NormalizeCardId(cardId)}({metadata.Type}/{metadata.Rarity})";
        }));
    }

    private static string FormatShopEntry(IShopEntry entry)
    {
        var price = entry switch
        {
            ShopCardEntry card => card.Price,
            ShopRelicEntry relic => relic.Price,
            ShopPotionEntry potion => potion.Price,
            _ => 0
        };
        return $"{entry.Id}({price})";
    }

    private static IReadOnlyList<string> MergeExpectedShopItems(
        IReadOnlyList<string> visibleChoices,
        IEnumerable<string> purchasedItems)
    {
        var result = visibleChoices.ToList();
        foreach (var item in purchasedItems.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!result.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static bool IsColorlessShopCard(string cardId)
    {
        return KnownColorlessShopCards.Contains(NormalizeCardId(cardId));
    }

    private static readonly HashSet<string> KnownColorlessShopCards = new(
        [
            "ALCHEMIZE",
            "BOLAS",
            "CALAMITY",
            "DARK_SHACKLES",
            "DISCOVERY",
            "DRAMATIC_ENTRANCE",
            "ENTROPY",
            "FLASH_OF_STEEL",
            "GOOD_INSTINCTS",
            "HAND_OF_GREED",
            "JACK_OF_ALL_TRADES",
            "MADNESS",
            "MAGNETISM",
            "MASTER_OF_STRATEGY",
            "MAYHEM",
            "MIND_BLAST",
            "OMNISLICE",
            "PANACHE",
            "PANIC_BUTTON",
            "PREP_TIME",
            "ROLLING_BOULDER",
            "SALVO",
            "SCRAWL",
            "SECRET_TECHNIQUE",
            "SECRET_WEAPON",
            "SEEKER_STRIKE",
            "THE_BOMB",
            "THE_GAMBIT",
            "THINKING_AHEAD",
            "ULTIMATE_DEFEND",
            "VIOLENCE"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static bool SameSetOrSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0;
        }

        return expected.Count == actual.Count &&
               expected.Zip(actual, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(match => match);
    }

    private static bool SameIdSet(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0;
        }

        var expectedSorted = expected.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var actualSorted = actual.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return expectedSorted.Length == actualSorted.Length &&
               expectedSorted.Zip(actualSorted, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(match => match);
    }

    private static bool? SameIdSubset(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
        {
            return null;
        }

        return expected.All(item => actual.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    private static bool SameCardSequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0;
        }

        return expected.Count == actual.Count &&
               expected.Zip(actual, (left, right) => string.Equals(NormalizeCardId(left), NormalizeCardId(right), StringComparison.OrdinalIgnoreCase)).All(match => match);
    }

    public static bool SameCardReward(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        return SameCardSequence(expected, actual) || SameCardSet(expected, actual);
    }

    private static bool SameCardSet(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0;
        }

        var expectedNormalized = expected.Select(NormalizeCardId).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var actualNormalized = actual.Select(NormalizeCardId).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return expectedNormalized.Length == actualNormalized.Length &&
               expectedNormalized.Zip(actualNormalized, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(match => match);
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var index = 0;
        foreach (var item in actual)
        {
            if (index < expected.Count &&
                string.Equals(expected[index], item, StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }
        }

        return index == expected.Count;
    }

    private static string NormalizeCardId(string cardId)
    {
        return cardId.EndsWith("+", StringComparison.Ordinal)
            ? cardId[..^1]
            : cardId;
    }

    private static bool IsFutureOfPotionsEvent(string? eventText)
    {
        if (string.IsNullOrWhiteSpace(eventText))
        {
            return false;
        }

        return string.Equals(eventText, "EVENT.THE_FUTURE_OF_POTIONS", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventText, "THE_FUTURE_OF_POTIONS", StringComparison.OrdinalIgnoreCase);
    }

}

internal sealed record ReplayFloor(
    int Floor,
    string RoomType,
    string? EventText,
    int? GoldBefore,
    int? GoldAfter,
    int? GoldGained,
    int? GoldSpent,
    bool HasCombat,
    IReadOnlyList<string> CardChoices,
    IReadOnlyList<string> PickedCardIds,
    IReadOnlyList<string> PotionChoiceIds,
    IReadOnlyList<string> PickedPotionChoiceIds,
    IReadOnlyList<string> PotionUsedIds,
    IReadOnlyList<string> PotionDiscardedIds,
    IReadOnlyList<string> RelicChoices,
    IReadOnlyList<string> PickedRelicIds,
    IReadOnlyList<string> AncientChoices,
    IReadOnlyList<string> PickedAncientIds,
    IReadOnlyList<ReplayShopAction> ShopActions);

internal sealed record ReplayShopAction(string ActionType, string ItemId);

internal sealed record ReplayMapAct(
    int ActNumber,
    IReadOnlyList<(int Col, int Row)> VisitedCoords);

internal sealed record ReplayResult(
    IReadOnlyList<string> ReplayedRelicIds,
    IReadOnlyList<ReplayTraceEntry> Trace,
    IReadOnlyList<string> Mismatches);

internal sealed record ReplayTraceEntry(
    int Floor,
    string RoomType,
    IReadOnlyList<string> PickedCards,
    IReadOnlyList<string> PickedRelics,
    IReadOnlyList<ReplayShopAction> ShopActions);

internal sealed record Sts2CliOracleSummary(
    int RunId,
    string SeedText,
    string Character,
    int Ascension,
    string PickedAncientRelicId,
    IReadOnlyList<string> RemovedCardIds,
    int CombatFloor,
    IReadOnlyList<string> LoggedRewardCardIds,
    IReadOnlyList<string> OracleRewardCardIds,
    IReadOnlyList<string> OracleRewardCardNames,
    string TranscriptPath)
{
    public static Sts2CliOracleSummary? TryLoadForRun(ReplayRun replay, int? combatFloor = null)
    {
        var summaries = LoadAllForRun(replay);
        if (summaries.Count == 0)
        {
            return null;
        }

        return combatFloor.HasValue
            ? summaries.FirstOrDefault(summary => summary.CombatFloor == combatFloor.Value)
            : summaries.OrderBy(summary => summary.CombatFloor).FirstOrDefault();
    }

    private static IReadOnlyList<Sts2CliOracleSummary> LoadAllForRun(ReplayRun replay)
    {
        var pattern = $"sts2log-run-{replay.RunId}-sts2-cli-oracle-summary*.json";
        return Directory.GetFiles("scratch", pattern)
            .Select(LoadFromPath)
            .OrderBy(summary => summary.CombatFloor)
            .ToArray();
    }

    private static Sts2CliOracleSummary LoadFromPath(string summaryPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var root = document.RootElement;
        return new Sts2CliOracleSummary(
            RunId: root.GetProperty("RunId").GetInt32(),
            SeedText: root.GetProperty("SeedText").GetString() ?? string.Empty,
            Character: root.GetProperty("Character").GetString() ?? string.Empty,
            Ascension: root.GetProperty("Ascension").GetInt32(),
            PickedAncientRelicId: root.GetProperty("PickedAncientRelicId").GetString() ?? string.Empty,
            RemovedCardIds: root.GetProperty("RemovedCardIds").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            CombatFloor: root.GetProperty("CombatFloor").GetInt32(),
            LoggedRewardCardIds: root.GetProperty("LoggedRewardCardIds").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            OracleRewardCardIds: root.GetProperty("OracleRewardCardIds").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            OracleRewardCardNames: root.GetProperty("OracleRewardCardNames").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
            TranscriptPath: root.GetProperty("TranscriptPath").GetString() ?? string.Empty);
    }
}

