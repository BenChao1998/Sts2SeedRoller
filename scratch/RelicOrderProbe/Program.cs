using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var options = Options.Parse(args, Directory.GetCurrentDirectory());
var world = WorldData.Load(options.ActsPath, options.SourceRootPath);
var save = SaveRun.Load(options.SavePath, world.RarityMap);

var initialState = SimulationState.Create(world, save);

var level1 = Analyzer.AnalyzeInitialBagPositions(save, initialState);
var level2 = Analyzer.RunOracleRarityReplay(save, initialState);
var level3 = Analyzer.RunRolledReplay(save, initialState, syncEliteToShared: false);
var level4 = Analyzer.RunRolledReplay(save, initialState, syncEliteToShared: true);
var permutationProbe = Analyzer.ProbeShuffleOrders(world, save);

var report = Reporter.Build(options, world, save, initialState, level1, level2, level3, level4, permutationProbe);
Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
File.WriteAllText(options.OutputPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(report);
Console.WriteLine();
Console.WriteLine($"报告已写入: {options.OutputPath}");

static class Analyzer
{
    public static Level1Result AnalyzeInitialBagPositions(SaveRun save, SimulationState initialState)
    {
        var rows = new List<Level1Row>();
        foreach (var pickup in save.RandomPickups)
        {
            var bag = pickup.Source == RewardSource.Treasure
                ? initialState.SharedBag
                : initialState.PlayerBag;

            var position = bag.FindPosition(pickup.ActualRarity, pickup.RelicId);
            rows.Add(new Level1Row(
                pickup.Floor,
                pickup.Source,
                pickup.RelicId,
                pickup.ActualRarity,
                position));
        }

        return new Level1Result(rows);
    }

    public static ReplayResult RunOracleRarityReplay(SaveRun save, SimulationState initialState)
    {
        var sharedBag = initialState.SharedBag.Clone();
        var playerBag = initialState.PlayerBag.Clone();
        var rows = new List<ReplayRow>();

        foreach (var action in save.Actions)
        {
            if (action.CombatReward != null)
            {
                continue;
            }

            if (action.ShopOffer != null)
            {
                foreach (var relicId in action.ShopOffer.OfferedRelics)
                {
                    playerBag.Remove(relicId);
                    sharedBag.Remove(relicId);
                }

                continue;
            }

            var pickup = action.Pickup!;
            var predicted = pickup.Source switch
            {
                RewardSource.Treasure => sharedBag.PullFromFront(
                    pickup.ActualRarity,
                    relicId => initialState.IsRelicAllowed(relicId, pickup.Floor)),
                RewardSource.Elite => playerBag.PullFromFront(
                    pickup.ActualRarity,
                    relicId => initialState.IsRelicAllowed(relicId, pickup.Floor)),
                _ => null
            };

            if (pickup.Source == RewardSource.Elite && predicted != null)
            {
                sharedBag.Remove(predicted);
            }
            else if (pickup.Source == RewardSource.Treasure && predicted != null)
            {
                playerBag.Remove(predicted);
            }

            rows.Add(new ReplayRow(
                pickup.Floor,
                pickup.Source,
                pickup.ActualRarity,
                pickup.ActualRarity,
                pickup.RelicId,
                predicted,
                string.Equals(pickup.RelicId, predicted, StringComparison.OrdinalIgnoreCase)));
        }

        return ReplayResult.FromRows("L2: 按真实来源 + 存档真实稀有度回放", rows);
    }

    public static ReplayResult RunRolledReplay(
        SaveRun save,
        SimulationState initialState,
        bool syncEliteToShared)
    {
        var sharedBag = initialState.SharedBag.Clone();
        var playerBag = initialState.PlayerBag.Clone();
        var treasureRng = new GameRng(initialState.RunSeed, "treasure_room_relics");
        var rewardsRng = new GameRng(initialState.PlayerSeed, "rewards");
        var rows = new List<ReplayRow>();

        foreach (var action in save.Actions)
        {
            if (action.CombatReward != null)
            {
                ConsumeCombatRewards(action.CombatReward, rewardsRng);
                continue;
            }

            if (action.ShopOffer != null)
            {
                ConsumeShop(action.ShopOffer, playerBag, sharedBag, rewardsRng, initialState);
                continue;
            }

            var pickup = action.Pickup!;
            var rolledRarity = pickup.Source switch
            {
                RewardSource.Treasure => RollRarity(treasureRng),
                RewardSource.Elite => RollRarity(rewardsRng),
                _ => RelicRarity.None
            };

            var predicted = pickup.Source switch
            {
                RewardSource.Treasure => sharedBag.PullFromFront(
                    rolledRarity,
                    relicId => initialState.IsRelicAllowed(relicId, pickup.Floor)),
                RewardSource.Elite => playerBag.PullFromFront(
                    rolledRarity,
                    relicId => initialState.IsRelicAllowed(relicId, pickup.Floor)),
                _ => null
            };

            if (syncEliteToShared &&
                pickup.Source == RewardSource.Elite &&
                predicted != null)
            {
                sharedBag.Remove(predicted);
            }
            else if (pickup.Source == RewardSource.Treasure && predicted != null)
            {
                playerBag.Remove(predicted);
            }

            rows.Add(new ReplayRow(
                pickup.Floor,
                pickup.Source,
                pickup.ActualRarity,
                rolledRarity,
                pickup.RelicId,
                predicted,
                string.Equals(pickup.RelicId, predicted, StringComparison.OrdinalIgnoreCase)));
        }

        var title = syncEliteToShared
            ? "L4: 按真实来源 + RNG 稀有度 + 精英跨袋移除"
            : "L3: 按真实来源 + RNG 稀有度（不做精英跨袋移除）";
        return ReplayResult.FromRows(title, rows);
    }

    public static PermutationProbeResult ProbeShuffleOrders(WorldData world, SaveRun save)
    {
        var allOrders = PermutationProbeResult.AllOrders;
        PermutationCandidate? best = null;
        var candidates = new List<PermutationCandidate>();
        foreach (var sharedOrder in allOrders)
        {
            foreach (var playerOrder in allOrders)
            {
                var state = SimulationState.Create(world, save, sharedOrder, playerOrder);
                var replay = RunOracleRarityReplay(save, state);
                var candidate = new PermutationCandidate(
                    sharedOrder,
                    playerOrder,
                    replay.MatchCount,
                    replay.Rows.Take(5).ToArray());
                candidates.Add(candidate);
                if (best == null || candidate.MatchCount > best.MatchCount)
                {
                    best = candidate;
                }
            }
        }

        var top = candidates
            .OrderByDescending(item => item.MatchCount)
            .ThenBy(item => item.SharedOrder)
            .ThenBy(item => item.PlayerOrder)
            .Take(8)
            .ToArray();
        return new PermutationProbeResult(best!, top);
    }

    private static void ConsumeShop(
        ShopOffer shopOffer,
        RelicBag playerBag,
        RelicBag sharedBag,
        GameRng rewardsRng,
        SimulationState initialState)
    {
        var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rarity in new[] { RollRarity(rewardsRng), RollRarity(rewardsRng), RelicRarity.Shop })
        {
            var relic = playerBag.PullFromBack(
                rarity,
                blacklist,
                ShopBlockedRelics,
                relicId => initialState.IsRelicAllowed(relicId, shopOffer.Floor));
            if (relic == null)
            {
                continue;
            }

            blacklist.Add(relic);
            sharedBag.Remove(relic);
        }
    }

    private static void ConsumeCombatRewards(
        CombatRewardBurn burn,
        GameRng rewardsRng)
    {
        _ = rewardsRng.NextFloat();

        if (burn.HasPotionReward)
        {
            _ = rewardsRng.NextFloat();
            _ = rewardsRng.NextInt(1);
        }

        if (burn.HasCardReward)
        {
            for (var i = 0; i < 3; i++)
            {
                _ = rewardsRng.NextFloat();
                _ = rewardsRng.NextInt(1);
                _ = rewardsRng.NextFloat();
            }
        }
    }

    private static readonly HashSet<string> ShopBlockedRelics =
    [
        "AMETHYST_AUBERGINE",
        "BOWLER_HAT",
        "LUCKY_FYSH",
        "OLD_COIN",
        "THE_COURIER"
    ];

    private static RelicRarity RollRarity(GameRng rng)
    {
        var value = rng.NextFloat();
        if (value < 0.5f)
        {
            return RelicRarity.Common;
        }

        if (value < 0.83f)
        {
            return RelicRarity.Uncommon;
        }

        return RelicRarity.Rare;
    }
}

static class Reporter
{
    public static string Build(
        Options options,
        WorldData world,
        SaveRun save,
        SimulationState initialState,
        Level1Result level1,
        ReplayResult level2,
        ReplayResult level3,
        ReplayResult level4,
        PermutationProbeResult permutationProbe)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Relic Order Probe");
        sb.AppendLine("=================");
        sb.AppendLine($"save      : {options.SavePath}");
        sb.AppendLine($"acts      : {options.ActsPath}");
        sb.AppendLine($"source    : {options.SourceRootPath}");
        sb.AppendLine($"pool order: {world.SequenceSource}");
        sb.AppendLine($"rarity map : {world.RaritySource}");
        sb.AppendLine($"bag init   : {initialState.BagSource}");
        sb.AppendLine($"seed      : {save.SeedText}");
        sb.AppendLine($"character : {save.Character}");
        sb.AppendLine($"player id : {save.PlayerNetId}");
        sb.AppendLine($"run seed  : {initialState.RunSeed}");
        sb.AppendLine($"player seed: {initialState.PlayerSeed}");
        sb.AppendLine();

        if (world.Comparisons.Count > 0)
        {
            sb.AppendLine("Pool order comparison");
            sb.AppendLine("---------------------");
            foreach (var comparison in world.Comparisons)
            {
                sb.AppendLine(
                    $"{comparison.Label,-8} acts={comparison.ActsCount,3} source={comparison.SourceCount,3} same_prefix={comparison.SamePrefixCount,3} first_diff={FormatPosition(comparison.FirstDifferenceIndex)}");
                sb.AppendLine($"  acts   : {string.Join(", ", comparison.ActsPreview)}");
                sb.AppendLine($"  source : {string.Join(", ", comparison.SourcePreview)}");
            }

            sb.AppendLine();
        }

        if (world.RarityDifferences.Count > 0)
        {
            sb.AppendLine("Rarity differences");
            sb.AppendLine("------------------");
            sb.AppendLine($"acts vs source mismatch count: {world.RarityDifferences.Count}");
            foreach (var difference in world.RarityDifferences.Take(16))
            {
                sb.AppendLine(
                    $"  {difference.RelicId,-24} acts={difference.ActsRarity,-9} source={difference.SourceRarity}");
            }

            if (world.RarityDifferences.Count > 16)
            {
                sb.AppendLine($"  ... {world.RarityDifferences.Count - 16} more");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Fixed / non-random relic pickups (excluded from bag replay)");
        sb.AppendLine("-----------------------------------------------------------");
        foreach (var pickup in save.FixedPickups)
        {
            sb.AppendLine(
                $"{pickup.Floor,2}F  {pickup.Source,-8}  {pickup.RelicId,-24}  rarity={pickup.ActualRarity,-8}  room={pickup.RoomModelId ?? "-"}");
        }
        sb.AppendLine();

        sb.AppendLine("Random relic pickups from save");
        sb.AppendLine("------------------------------");
        foreach (var pickup in save.RandomPickups)
        {
            sb.AppendLine(
                $"{pickup.Floor,2}F  {pickup.Source,-8}  {pickup.RelicId,-24}  rarity={pickup.ActualRarity,-8}  room={pickup.RoomModelId ?? "-"}");
        }
        sb.AppendLine();

        sb.AppendLine("L1: initial bag positions");
        sb.AppendLine("-------------------------");
        sb.AppendLine("floor  source    rarity     relic                     initial_pos");
        foreach (var row in level1.Rows)
        {
            sb.AppendLine(
                $"{row.Floor,5}  {row.Source,-8}  {row.Rarity,-9}  {row.RelicId,-24}  {FormatPosition(row.Position)}");
        }
        sb.AppendLine();

        AppendReplay(sb, level2);
        AppendReplay(sb, level3);
        AppendReplay(sb, level4);

        sb.AppendLine("Shuffle-order probe");
        sb.AppendLine("-------------------");
        sb.AppendLine("Bruteforced all 24 x 24 rarity-bucket shuffle orders for L2 replay.");
        sb.AppendLine($"Best exact matches: {permutationProbe.Best.MatchCount}/{save.RandomPickups.Count}");
        sb.AppendLine($"Best shared order : {permutationProbe.Best.SharedOrder}");
        sb.AppendLine($"Best player order : {permutationProbe.Best.PlayerOrder}");
        sb.AppendLine("Top candidates:");
        foreach (var candidate in permutationProbe.TopCandidates)
        {
            sb.AppendLine(
                $"  matches={candidate.MatchCount,2}  shared={candidate.SharedOrder,-30}  player={candidate.PlayerOrder}");
        }
        sb.AppendLine();

        sb.AppendLine("Initial bag previews");
        sb.AppendLine("--------------------");
        AppendBagPreview(sb, "Shared", initialState.SharedBag);
        AppendBagPreview(sb, "Player", initialState.PlayerBag);
        sb.AppendLine();

        sb.AppendLine("Quick read");
        sb.AppendLine("----------");
        sb.AppendLine($"L2 exact matches: {level2.MatchCount}/{level2.TotalCount}");
        sb.AppendLine($"L3 exact matches: {level3.MatchCount}/{level3.TotalCount}");
        sb.AppendLine($"L4 exact matches: {level4.MatchCount}/{level4.TotalCount}");
        if (level2.MatchCount == level2.TotalCount)
        {
            sb.AppendLine("L2 is now perfect: the bag source, rarity map, shared->player same-relic removal, and Act 3 availability gating line up with the save.");
            sb.AppendLine("The remaining gap is rewards RNG timing. Elite rewards still miss at L3/L4, which means gold/potion/card population is advancing PlayerRng.Rewards before some relic rarity rolls.");
        }
        else
        {
            sb.AppendLine("If L2 is already weak, the shuffled bag order itself is wrong or incomplete.");
            sb.AppendLine("If L2 is decent but L3/L4 collapse, the main missing factor is rewards RNG consumption before the relic roll.");
        }

        sb.AppendLine("Elite rewards in the live game populate gold/potion/card before relic, so card and potion generation can move PlayerRng.Rewards ahead of the relic rarity roll.");
        return sb.ToString();
    }

    private static void AppendReplay(StringBuilder sb, ReplayResult replay)
    {
        sb.AppendLine(replay.Title);
        sb.AppendLine(new string('-', replay.Title.Length));
        sb.AppendLine($"Exact matches: {replay.MatchCount}/{replay.TotalCount}");
        sb.AppendLine("floor  source    actual_r   used_r     actual_relic              predicted_relic           match");
        foreach (var row in replay.Rows)
        {
            sb.AppendLine(
                $"{row.Floor,5}  {row.Source,-8}  {row.ActualRarity,-9}  {row.UsedRarity,-9}  {row.ActualRelic,-24}  {(row.PredictedRelic ?? "<null>"),-24}  {(row.IsMatch ? "Y" : "N")}");
        }
        sb.AppendLine();
    }

    private static void AppendBagPreview(StringBuilder sb, string label, RelicBag bag)
    {
        sb.AppendLine($"{label} bag");
        foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
        {
            var preview = bag.Preview(rarity, 12);
            if (preview.Count == 0)
            {
                continue;
            }

            sb.AppendLine(
                $"  {rarity,-9} [{preview.Count}/{bag.Count(rarity)} shown] {string.Join(", ", preview)}");
        }
    }

    private static string FormatPosition(int? position)
    {
        return position.HasValue ? position.Value.ToString(CultureInfo.InvariantCulture) : "-";
    }
}

sealed record Options(string SavePath, string ActsPath, string SourceRootPath, string OutputPath)
{
    public static Options Parse(string[] args, string currentDirectory)
    {
        var savePath = Path.GetFullPath(Path.Combine(currentDirectory, "存档", "1777034559.run"));
        var actsPath = Path.GetFullPath(Path.Combine(currentDirectory, "data", "0.103.2", "sts2", "acts.json"));
        var sourceRootPath = Path.GetFullPath(Path.Combine(currentDirectory, "Slay the Spire 2 版本0.103.2源码（游戏源码，读取用）"));
        var outputPath = Path.GetFullPath(Path.Combine(currentDirectory, "artifacts", "relic_order_probe_1777034559.txt"));

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--save" when i + 1 < args.Length:
                    savePath = Path.GetFullPath(args[++i]);
                    break;
                case "--acts" when i + 1 < args.Length:
                    actsPath = Path.GetFullPath(args[++i]);
                    break;
                case "--source-root" when i + 1 < args.Length:
                    sourceRootPath = Path.GetFullPath(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    outputPath = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        return new Options(savePath, actsPath, sourceRootPath, outputPath);
    }
}

sealed class WorldData
{
    private WorldData(
        IReadOnlyList<string> sharedSequence,
        IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> characterSequences,
        IReadOnlyDictionary<string, RelicRarity> rarityMap,
        string sequenceSource,
        IReadOnlyList<PoolComparison> comparisons,
        string raritySource,
        IReadOnlyList<RarityDifference> rarityDifferences,
        IReadOnlySet<string> beforeAct3TreasureChestRelics)
    {
        SharedSequence = sharedSequence;
        CharacterSequences = characterSequences;
        RarityMap = rarityMap;
        SequenceSource = sequenceSource;
        Comparisons = comparisons;
        RaritySource = raritySource;
        RarityDifferences = rarityDifferences;
        BeforeAct3TreasureChestRelics = beforeAct3TreasureChestRelics;
    }

    private static readonly IReadOnlyDictionary<CharacterId, string> CharacterPoolFiles =
        new Dictionary<CharacterId, string>
        {
            [CharacterId.Ironclad] = "IroncladRelicPool.cs",
            [CharacterId.Silent] = "SilentRelicPool.cs",
            [CharacterId.Defect] = "DefectRelicPool.cs",
            [CharacterId.Necrobinder] = "NecrobinderRelicPool.cs",
            [CharacterId.Regent] = "RegentRelicPool.cs"
        };

    public IReadOnlyList<string> SharedSequence { get; }

    public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> CharacterSequences { get; }

    public IReadOnlyDictionary<string, RelicRarity> RarityMap { get; }

    public string SequenceSource { get; }

    public IReadOnlyList<PoolComparison> Comparisons { get; }

    public string RaritySource { get; }

    public IReadOnlyList<RarityDifference> RarityDifferences { get; }

    public IReadOnlySet<string> BeforeAct3TreasureChestRelics { get; }

    public IReadOnlyList<string> GetCharacterSequence(CharacterId character)
    {
        return CharacterSequences.TryGetValue(character, out var sequence)
            ? sequence
            : [];
    }

    public static WorldData Load(string path, string sourceRootPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var relicPools = document.RootElement.GetProperty("relicPools");

        var actsSharedSequence = relicPools.GetProperty("sharedSequence")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        var actsCharacterSequences = new Dictionary<CharacterId, IReadOnlyList<string>>();
        foreach (var property in relicPools.GetProperty("characters").EnumerateObject())
        {
            if (!Enum.TryParse<CharacterId>(property.Name, ignoreCase: true, out var character))
            {
                continue;
            }

            var sequence = property.Value.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            actsCharacterSequences[character] = sequence;
        }

        var actsRarityMap = new Dictionary<string, RelicRarity>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in relicPools.GetProperty("rarities").EnumerateObject())
        {
            if (!Enum.TryParse<RelicRarity>(property.Value.GetString(), ignoreCase: true, out var rarity))
            {
                rarity = RelicRarity.None;
            }

            actsRarityMap[property.Name] = rarity;
        }

        var sharedSequence = (IReadOnlyList<string>)actsSharedSequence;
        var characterSequences = new Dictionary<CharacterId, IReadOnlyList<string>>(actsCharacterSequences);
        var rarityMap = new Dictionary<string, RelicRarity>(actsRarityMap, StringComparer.OrdinalIgnoreCase);
        var sequenceSource = "acts.json";
        var comparisons = new List<PoolComparison>();
        var raritySource = "acts.json";
        var rarityDifferences = new List<RarityDifference>();
        var beforeAct3TreasureChestRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var poolDir = ResolvePoolDirectory(sourceRootPath);
        if (poolDir != null)
        {
            var sourceSharedSequence = LoadSequenceFromPoolFile(Path.Combine(poolDir, "SharedRelicPool.cs"));
            if (sourceSharedSequence.Count > 0)
            {
                sharedSequence = sourceSharedSequence;
                comparisons.Add(Compare("Shared", actsSharedSequence, sourceSharedSequence));
                sequenceSource = "official source RelicPool.cs";
            }

            foreach (var (character, fileName) in CharacterPoolFiles)
            {
                var sourceSequence = LoadSequenceFromPoolFile(Path.Combine(poolDir, fileName));
                if (sourceSequence.Count == 0)
                {
                    continue;
                }

                characterSequences[character] = sourceSequence;
                if (actsCharacterSequences.TryGetValue(character, out var actsSequence))
                {
                    comparisons.Add(Compare(character.ToString(), actsSequence, sourceSequence));
                }
            }
        }

        var relicDir = ResolveRelicDirectory(sourceRootPath);
        if (relicDir != null)
        {
            var sourceRarityMap = LoadRarityMapFromRelicFiles(relicDir);
            if (sourceRarityMap.Count > 0)
            {
                rarityMap = new Dictionary<string, RelicRarity>(sourceRarityMap, StringComparer.OrdinalIgnoreCase);
                foreach (var (relicId, rarity) in actsRarityMap)
                {
                    if (!rarityMap.ContainsKey(relicId))
                    {
                        rarityMap[relicId] = rarity;
                    }
                }

                rarityDifferences = CompareRarities(actsRarityMap, sourceRarityMap);
                raritySource = "official source Relics/*.cs";
            }

            beforeAct3TreasureChestRelics = LoadBeforeAct3TreasureChestRelics(relicDir);
        }

        return new WorldData(
            sharedSequence,
            characterSequences,
            rarityMap,
            sequenceSource,
            comparisons,
            raritySource,
            rarityDifferences,
            beforeAct3TreasureChestRelics);
    }

    private static string? ResolvePoolDirectory(string sourceRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourceRootPath) || !Directory.Exists(sourceRootPath))
        {
            return null;
        }

        var direct = Path.Combine(sourceRootPath, "src", "Core", "Models", "RelicPools");
        return Directory.Exists(direct) ? direct : null;
    }

    private static string? ResolveRelicDirectory(string sourceRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourceRootPath) || !Directory.Exists(sourceRootPath))
        {
            return null;
        }

        var direct = Path.Combine(sourceRootPath, "src", "Core", "Models", "Relics");
        return Directory.Exists(direct) ? direct : null;
    }

    private static IReadOnlyList<string> LoadSequenceFromPoolFile(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        var matches = Regex.Matches(text, @"ModelDb\.Relic<([A-Za-z0-9_]+)>\(\)");
        return matches
            .Select(match => NormalizeSourceRelicId(match.Groups[1].Value))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string NormalizeSourceRelicId(string typeName)
    {
        var snake = Regex.Replace(typeName, "([A-Z]+)([A-Z][a-z])", "$1_$2");
        snake = Regex.Replace(snake, "([a-z0-9])([A-Z])", "$1_$2");
        return snake.ToUpperInvariant();
    }

    private static Dictionary<string, RelicRarity> LoadRarityMapFromRelicFiles(string relicDir)
    {
        var map = new Dictionary<string, RelicRarity>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(relicDir, "*.cs"))
        {
            var text = File.ReadAllText(path);
            var match = Regex.Match(text, @"public override RelicRarity Rarity => RelicRarity\.(\w+);");
            if (!match.Success)
            {
                continue;
            }

            if (!Enum.TryParse<RelicRarity>(match.Groups[1].Value, ignoreCase: true, out var rarity))
            {
                continue;
            }

            var relicId = NormalizeSourceRelicId(Path.GetFileNameWithoutExtension(path));
            map[relicId] = rarity;
        }

        return map;
    }

    private static List<RarityDifference> CompareRarities(
        IReadOnlyDictionary<string, RelicRarity> acts,
        IReadOnlyDictionary<string, RelicRarity> source)
    {
        var differences = new List<RarityDifference>();
        var ids = acts.Keys
            .Concat(source.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            var hasActs = acts.TryGetValue(id, out var actsRarity);
            var hasSource = source.TryGetValue(id, out var sourceRarity);
            if (!hasActs || !hasSource || actsRarity != sourceRarity)
            {
                differences.Add(new RarityDifference(
                    id,
                    hasActs ? actsRarity : RelicRarity.None,
                    hasSource ? sourceRarity : RelicRarity.None));
            }
        }

        return differences;
    }

    private static HashSet<string> LoadBeforeAct3TreasureChestRelics(string relicDir)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(relicDir, "*.cs"))
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("IsBeforeAct3TreasureChest(runState)", StringComparison.Ordinal))
            {
                continue;
            }

            ids.Add(NormalizeSourceRelicId(Path.GetFileNameWithoutExtension(path)));
        }

        return ids;
    }

    private static PoolComparison Compare(string label, IReadOnlyList<string> acts, IReadOnlyList<string> source)
    {
        var max = Math.Min(acts.Count, source.Count);
        var samePrefixCount = 0;
        while (samePrefixCount < max &&
               string.Equals(acts[samePrefixCount], source[samePrefixCount], StringComparison.OrdinalIgnoreCase))
        {
            samePrefixCount++;
        }

        int? firstDifference = samePrefixCount < max
            ? samePrefixCount + 1
            : acts.Count == source.Count
                ? null
                : max + 1;

        return new PoolComparison(
            label,
            acts.Count,
            source.Count,
            samePrefixCount,
            firstDifference,
            acts.Take(8).ToArray(),
            source.Take(8).ToArray());
    }
}

sealed record PoolComparison(
    string Label,
    int ActsCount,
    int SourceCount,
    int SamePrefixCount,
    int? FirstDifferenceIndex,
    IReadOnlyList<string> ActsPreview,
    IReadOnlyList<string> SourcePreview);

sealed record RarityDifference(
    string RelicId,
    RelicRarity ActsRarity,
    RelicRarity SourceRarity);

sealed class SaveRun
{
    private SaveRun(
        string seedText,
        CharacterId character,
        int playerCount,
        ulong playerNetId,
        IReadOnlyList<RelicPickup> allPickups,
        IReadOnlyList<RunAction> actions)
    {
        SeedText = seedText;
        Character = character;
        PlayerCount = playerCount;
        PlayerNetId = playerNetId;
        AllPickups = allPickups;
        Actions = actions;
        RandomPickups = allPickups
            .Where(pickup => pickup.IsRandom)
            .ToArray();
        FixedPickups = allPickups
            .Where(pickup => !pickup.IsRandom)
            .ToArray();
    }

    public string SeedText { get; }

    public CharacterId Character { get; }

    public int PlayerCount { get; }

    public ulong PlayerNetId { get; }

    public IReadOnlyList<RelicPickup> AllPickups { get; }

    public IReadOnlyList<RunAction> Actions { get; }

    public IReadOnlyList<RelicPickup> RandomPickups { get; }

    public IReadOnlyList<RelicPickup> FixedPickups { get; }

    public static SaveRun Load(string path, IReadOnlyDictionary<string, RelicRarity> rarityMap)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var seedText = root.GetProperty("seed").GetString()
            ?? throw new InvalidOperationException("Save missing seed.");

        var player = root.GetProperty("players")[0];
        var playerCount = root.GetProperty("players").GetArrayLength();
        var characterText = player.GetProperty("character").GetString()
            ?? throw new InvalidOperationException("Save missing character.");
        var character = ParseCharacter(characterText);
        var playerNetId = player.GetProperty("id").GetUInt64();

        var pickups = new List<RelicPickup>();
        var actions = new List<RunAction>();
        var floor = 0;
        foreach (var point in EnumerateMapPoints(root.GetProperty("map_point_history")))
        {
            floor++;
            var room = point.GetProperty("rooms")[0];
            var roomType = room.TryGetProperty("room_type", out var roomTypeElement)
                ? ParseSource(roomTypeElement.GetString())
                : RewardSource.Other;
            var roomModelId = room.TryGetProperty("model_id", out var modelIdElement)
                ? modelIdElement.GetString()
                : null;

            if (!point.TryGetProperty("player_stats", out var playerStats))
            {
                continue;
            }

            foreach (var stat in playerStats.EnumerateArray())
            {
                if (stat.TryGetProperty("player_id", out var playerIdElement) &&
                    playerIdElement.GetUInt64() != playerNetId)
                {
                    continue;
                }

                if (roomType is RewardSource.Other or RewardSource.Elite)
                {
                    var normalizedRoomType = room.TryGetProperty("room_type", out var roomTypeNode)
                        ? roomTypeNode.GetString()?.ToLowerInvariant()
                        : null;
                    if (normalizedRoomType is "monster" or "elite" or "boss")
                    {
                        var hasCardReward = stat.TryGetProperty("card_choices", out _);
                        var hasPotionReward = stat.TryGetProperty("potion_choices", out _);
                        if (hasCardReward || hasPotionReward || normalizedRoomType == "elite")
                        {
                            var source = normalizedRoomType switch
                            {
                                "elite" => RewardSource.Elite,
                                _ => RewardSource.Other
                            };
                            actions.Add(RunAction.ForCombatReward(new CombatRewardBurn(
                                floor,
                                source,
                                hasCardReward,
                                hasPotionReward)));
                        }
                    }
                }

                if (!stat.TryGetProperty("relic_choices", out var relicChoices))
                {
                    continue;
                }

                var offeredRelics = new List<string>();
                RelicPickup? pickedRandomRelic = null;
                foreach (var choice in relicChoices.EnumerateArray())
                {
                    var rawId = choice.GetProperty("choice").GetString() ?? string.Empty;
                    var relicId = NormalizeRelicId(rawId);
                    offeredRelics.Add(relicId);
                    var rarity = rarityMap.TryGetValue(relicId, out var mappedRarity)
                        ? mappedRarity
                        : RelicRarity.None;
                    if (choice.TryGetProperty("was_picked", out var pickedElement) &&
                        pickedElement.GetBoolean())
                    {
                        var isRandom = roomType is RewardSource.Treasure or RewardSource.Elite &&
                                       rarity is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop;

                        var pickup = new RelicPickup(
                            floor,
                            roomType,
                            roomModelId,
                            relicId,
                            rarity,
                            isRandom);
                        pickups.Add(pickup);
                        if (isRandom)
                        {
                            pickedRandomRelic = pickup;
                        }
                    }
                }

                if (roomType == RewardSource.Shop && offeredRelics.Count > 0)
                {
                    actions.Add(RunAction.ForShop(new ShopOffer(floor, offeredRelics)));
                }

                if (pickedRandomRelic != null)
                {
                    actions.Add(RunAction.ForPickup(pickedRandomRelic));
                }
            }
        }

        return new SaveRun(seedText, character, playerCount, playerNetId, pickups, actions);
    }

    private static IEnumerable<JsonElement> EnumerateMapPoints(JsonElement mapPointHistory)
    {
        if (mapPointHistory.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in mapPointHistory.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Array)
            {
                foreach (var point in entry.EnumerateArray())
                {
                    yield return point;
                }

                continue;
            }

            if (entry.ValueKind == JsonValueKind.Object)
            {
                yield return entry;
            }
        }
    }

    private static CharacterId ParseCharacter(string characterText)
    {
        var normalized = characterText
            .Replace("CHARACTER.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (!Enum.TryParse<CharacterId>(normalized, ignoreCase: true, out var character))
        {
            throw new InvalidOperationException($"Unsupported character: {characterText}");
        }

        return character;
    }

    private static RewardSource ParseSource(string? roomType)
    {
        return roomType?.ToLowerInvariant() switch
        {
            "treasure" => RewardSource.Treasure,
            "elite" => RewardSource.Elite,
            "event" => RewardSource.Event,
            _ => RewardSource.Other
        };
    }

    private static string NormalizeRelicId(string rawId)
    {
        return rawId.StartsWith("RELIC.", StringComparison.OrdinalIgnoreCase)
            ? rawId["RELIC.".Length..]
            : rawId;
    }
}

sealed class SimulationState
{
    internal SimulationState(
        uint runSeed,
        uint playerSeed,
        RelicBag sharedBag,
        RelicBag playerBag,
        string bagSource,
        int playerCount,
        IReadOnlySet<string> beforeAct3TreasureChestRelics)
    {
        RunSeed = runSeed;
        PlayerSeed = playerSeed;
        SharedBag = sharedBag;
        PlayerBag = playerBag;
        BagSource = bagSource;
        PlayerCount = playerCount;
        BeforeAct3TreasureChestRelics = beforeAct3TreasureChestRelics;
    }

    public uint RunSeed { get; }

    public uint PlayerSeed { get; }

    public RelicBag SharedBag { get; }

    public RelicBag PlayerBag { get; }

    public string BagSource { get; }

    public int PlayerCount { get; }

    public IReadOnlySet<string> BeforeAct3TreasureChestRelics { get; }

    public bool IsRelicAllowed(string relicId, int floor)
    {
        if (BeforeAct3TreasureChestRelics.Contains(relicId))
        {
            var cutoff = PlayerCount > 1 ? 38 : 41;
            if (floor >= cutoff)
            {
                return false;
            }
        }

        return true;
    }

    public static SimulationState Create(
        WorldData world,
        SaveRun save,
        string? sharedShuffleOrder = null,
        string? playerShuffleOrder = null)
    {
        if (string.IsNullOrWhiteSpace(sharedShuffleOrder) &&
            string.IsNullOrWhiteSpace(playerShuffleOrder) &&
            OfficialBagInitializer.TryCreate(world, save, out var officialState))
        {
            return officialState;
        }

        var runSeed = unchecked((uint)GameRng.GetDeterministicHashCode(save.SeedText));
        var playerSeed = unchecked(runSeed + (uint)save.PlayerNetId);
        var upFrontRng = new GameRng(runSeed, "up_front");

        var sharedBuckets = RelicBag.BuildBuckets(world.SharedSequence, world.RarityMap);
        RelicBag.ShuffleBuckets(sharedBuckets, upFrontRng, sharedShuffleOrder);
        var sharedBag = new RelicBag(sharedBuckets);

        var playerSequence = new List<string>(world.SharedSequence.Count + world.GetCharacterSequence(save.Character).Count);
        playerSequence.AddRange(world.SharedSequence);
        playerSequence.AddRange(world.GetCharacterSequence(save.Character));

        var playerBuckets = RelicBag.BuildBuckets(playerSequence, world.RarityMap);
        RelicBag.ShuffleBuckets(playerBuckets, upFrontRng, playerShuffleOrder);
        var playerBag = new RelicBag(playerBuckets);

        return new SimulationState(
            runSeed,
            playerSeed,
            sharedBag,
            playerBag,
            "custom source replay",
            save.PlayerCount,
            world.BeforeAct3TreasureChestRelics);
    }
}

static class OfficialBagInitializer
{
    public static bool TryCreate(WorldData world, SaveRun save, out SimulationState state)
    {
        try
        {
            MegaCrit.Sts2.Core.Models.ModelDb.Init();

            var runSeed = unchecked((uint)GameRng.GetDeterministicHashCode(save.SeedText));
            var playerSeed = unchecked(runSeed + (uint)save.PlayerNetId);
            var upFront = new MegaCrit.Sts2.Core.Random.Rng(runSeed, "up_front");
            var unlock = MegaCrit.Sts2.Core.Unlocks.UnlockState.all;

            var sharedPool = MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool>()
                .GetUnlockedRelics(unlock)
                .ToList();
            var characterPool = GetCharacterRelics(save.Character, unlock).ToList();

            var sharedGrabBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag(refreshAllowed: true);
            sharedGrabBag.Populate(sharedPool, upFront);

            var playerGrabBag = new MegaCrit.Sts2.Core.Runs.RelicGrabBag(refreshAllowed: true);
            playerGrabBag.Populate(sharedPool.Concat(characterPool), upFront);

            state = new SimulationState(
                runSeed,
                playerSeed,
                Convert(sharedGrabBag.ToSerializable()),
                Convert(playerGrabBag.ToSerializable()),
                "official DLL RelicGrabBag.Populate",
                save.PlayerCount,
                world.BeforeAct3TreasureChestRelics);
            return true;
        }
        catch
        {
            state = null!;
            return false;
        }
    }

    private static IEnumerable<MegaCrit.Sts2.Core.Models.RelicModel> GetCharacterRelics(
        CharacterId character,
        MegaCrit.Sts2.Core.Unlocks.UnlockState unlock)
    {
        return character switch
        {
            CharacterId.Ironclad => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.IroncladRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Silent => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.SilentRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Defect => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.DefectRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Necrobinder => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool>()
                .GetUnlockedRelics(unlock),
            CharacterId.Regent => MegaCrit.Sts2.Core.Models.ModelDb
                .RelicPool<MegaCrit.Sts2.Core.Models.RelicPools.RegentRelicPool>()
                .GetUnlockedRelics(unlock),
            _ => Array.Empty<MegaCrit.Sts2.Core.Models.RelicModel>()
        };
    }

    private static RelicBag Convert(MegaCrit.Sts2.Core.Saves.Runs.SerializableRelicGrabBag officialBag)
    {
        var buckets = new Dictionary<RelicRarity, List<string>>();
        foreach (var (officialRarity, ids) in officialBag.RelicIdLists)
        {
            if (!Enum.TryParse<RelicRarity>(officialRarity.ToString(), ignoreCase: true, out var rarity))
            {
                continue;
            }

            buckets[rarity] = ids
                .Select(id => id.Entry)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        return new RelicBag(buckets);
    }
}

sealed class RelicBag
{
    private readonly Dictionary<RelicRarity, List<string>> _deques;

    public RelicBag(Dictionary<RelicRarity, List<string>> deques)
    {
        _deques = deques;
    }

    public RelicBag Clone()
    {
        var clone = _deques.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToList());
        return new RelicBag(clone);
    }

    public string? PullFromFront(RelicRarity rarity, Func<string, bool>? isAllowed = null)
    {
        if (isAllowed != null)
        {
            RemoveDisallowed(isAllowed);
        }

        RelicRarity? current = rarity;
        while (current is RelicRarity currentRarity)
        {
            if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
            {
                var relic = list[0];
                list.RemoveAt(0);
                return relic;
            }

            current = current switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => null
            };
        }

        return null;
    }

    public string? PullFromBack(
        RelicRarity rarity,
        IReadOnlySet<string>? selected = null,
        IReadOnlySet<string>? extraBlacklist = null,
        Func<string, bool>? isAllowed = null)
    {
        if (isAllowed != null)
        {
            RemoveDisallowed(isAllowed);
        }

        RelicRarity? current = rarity;
        while (current is RelicRarity currentRarity)
        {
            if (_deques.TryGetValue(currentRarity, out var list) && list.Count > 0)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (selected != null && selected.Contains(list[i]))
                    {
                        continue;
                    }

                    if (extraBlacklist != null && extraBlacklist.Contains(list[i]))
                    {
                        continue;
                    }

                    var relic = list[i];
                    list.RemoveAt(i);
                    return relic;
                }
            }

            current = current switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => null
            };
        }

        return null;
    }

    public void Remove(string relicId)
    {
        foreach (var list in _deques.Values)
        {
            list.RemoveAll(id => string.Equals(id, relicId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RemoveDisallowed(Func<string, bool> isAllowed)
    {
        foreach (var list in _deques.Values)
        {
            list.RemoveAll(id => !isAllowed(id));
        }
    }

    public int? FindPosition(RelicRarity rarity, string relicId)
    {
        if (!_deques.TryGetValue(rarity, out var list))
        {
            return null;
        }

        var index = list.FindIndex(item => string.Equals(item, relicId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : null;
    }

    public IReadOnlyList<string> Preview(RelicRarity rarity, int take)
    {
        return _deques.TryGetValue(rarity, out var list)
            ? list.Take(take).ToArray()
            : [];
    }

    public int Count(RelicRarity rarity)
    {
        return _deques.TryGetValue(rarity, out var list)
            ? list.Count
            : 0;
    }

    public static Dictionary<RelicRarity, List<string>> BuildBuckets(
        IEnumerable<string> relicIds,
        IReadOnlyDictionary<string, RelicRarity> rarityMap)
    {
        var buckets = new Dictionary<RelicRarity, List<string>>();
        foreach (var relicId in relicIds)
        {
            if (!rarityMap.TryGetValue(relicId, out var rarity))
            {
                continue;
            }

            if (rarity is not (RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare or RelicRarity.Shop))
            {
                continue;
            }

            if (!buckets.TryGetValue(rarity, out var list))
            {
                list = [];
                buckets[rarity] = list;
            }

            list.Add(relicId);
        }

        return buckets;
    }

    public static void ShuffleBuckets(
        Dictionary<RelicRarity, List<string>> buckets,
        GameRng rng,
        string? forcedOrder = null)
    {
        if (!string.IsNullOrWhiteSpace(forcedOrder))
        {
            foreach (var rarity in ParseOrder(forcedOrder))
            {
                if (buckets.TryGetValue(rarity, out var list))
                {
                    rng.Shuffle(list);
                }
            }
            return;
        }

        foreach (var list in buckets.Values)
        {
            rng.Shuffle(list);
        }
    }

    private static IReadOnlyList<RelicRarity> ParseOrder(string order)
    {
        return order.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => Enum.Parse<RelicRarity>(text, ignoreCase: true))
            .ToArray();
    }
}

sealed class GameRng
{
    private readonly Random _random;

    public GameRng(uint seed, int counter = 0)
    {
        Seed = seed;
        _random = new Random(unchecked((int)seed));
        FastForward(counter);
    }

    public GameRng(uint seed, string salt)
        : this(unchecked(seed + (uint)GetDeterministicHashCode(salt)))
    {
    }

    public uint Seed { get; }

    public int Counter { get; private set; }

    public void FastForward(int targetCounter)
    {
        if (targetCounter < Counter)
        {
            throw new InvalidOperationException($"Cannot rewind RNG from {Counter} to {targetCounter}.");
        }

        while (Counter < targetCounter)
        {
            Counter++;
            _random.Next();
        }
    }

    public float NextFloat()
    {
        Counter++;
        return (float)_random.NextDouble();
    }

    public int NextInt(int maxExclusive)
    {
        Counter++;
        return _random.Next(maxExclusive);
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var swapIndex = NextInt(i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }

    public static int GetDeterministicHashCode(string text)
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
}

sealed record RelicPickup(
    int Floor,
    RewardSource Source,
    string? RoomModelId,
    string RelicId,
    RelicRarity ActualRarity,
    bool IsRandom);

sealed record ShopOffer(
    int Floor,
    IReadOnlyList<string> OfferedRelics);

sealed record CombatRewardBurn(
    int Floor,
    RewardSource Source,
    bool HasCardReward,
    bool HasPotionReward);

sealed record RunAction(
    RelicPickup? Pickup,
    ShopOffer? ShopOffer,
    CombatRewardBurn? CombatReward)
{
    public static RunAction ForPickup(RelicPickup pickup) => new(pickup, null, null);

    public static RunAction ForShop(ShopOffer offer) => new(null, offer, null);

    public static RunAction ForCombatReward(CombatRewardBurn burn) => new(null, null, burn);
}

sealed record Level1Row(
    int Floor,
    RewardSource Source,
    string RelicId,
    RelicRarity Rarity,
    int? Position);

sealed record Level1Result(IReadOnlyList<Level1Row> Rows);

sealed record ReplayRow(
    int Floor,
    RewardSource Source,
    RelicRarity ActualRarity,
    RelicRarity UsedRarity,
    string ActualRelic,
    string? PredictedRelic,
    bool IsMatch);

sealed class ReplayResult
{
    private ReplayResult(string title, IReadOnlyList<ReplayRow> rows)
    {
        Title = title;
        Rows = rows;
        MatchCount = rows.Count(row => row.IsMatch);
        TotalCount = rows.Count;
    }

    public string Title { get; }

    public IReadOnlyList<ReplayRow> Rows { get; }

    public int MatchCount { get; }

    public int TotalCount { get; }

    public static ReplayResult FromRows(string title, IReadOnlyList<ReplayRow> rows)
    {
        return new ReplayResult(title, rows);
    }
}

sealed class PermutationProbeResult
{
    public static readonly string[] AllOrders = BuildAllOrders();

    public PermutationProbeResult(PermutationCandidate best, IReadOnlyList<PermutationCandidate> topCandidates)
    {
        Best = best;
        TopCandidates = topCandidates;
    }

    public PermutationCandidate Best { get; }

    public IReadOnlyList<PermutationCandidate> TopCandidates { get; }

    private static string[] BuildAllOrders()
    {
        var rarities = new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop };
        var orders = new List<string>();
        Build([], rarities.ToList(), orders);
        return orders.ToArray();

        static void Build(List<RelicRarity> prefix, List<RelicRarity> remaining, List<string> orders)
        {
            if (remaining.Count == 0)
            {
                orders.Add(string.Join(">", prefix));
                return;
            }

            for (var i = 0; i < remaining.Count; i++)
            {
                var nextPrefix = new List<RelicRarity>(prefix) { remaining[i] };
                var nextRemaining = new List<RelicRarity>(remaining);
                nextRemaining.RemoveAt(i);
                Build(nextPrefix, nextRemaining, orders);
            }
        }
    }
}

sealed record PermutationCandidate(
    string SharedOrder,
    string PlayerOrder,
    int MatchCount,
    IReadOnlyList<ReplayRow> SampleRows);

enum CharacterId
{
    Ironclad,
    Silent,
    Defect,
    Necrobinder,
    Regent
}

enum RewardSource
{
    Other,
    Event,
    Shop,
    Treasure,
    Elite
}

enum RelicRarity
{
    None,
    Common,
    Uncommon,
    Rare,
    Shop,
    Ancient,
    Event,
    Starter
}
