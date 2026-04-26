using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SeedModel.Neow;
using SeedModel.Rng;
using SeedModel.Seeds;
using SeedModel.Sts2;

var workspace = @"E:\github project\Sts2SeedRoller";
var optionDataPath = Path.Combine(workspace, "data", "0.103.2", "ancients", "options.zhs.json");
var actDataPath = Path.Combine(workspace, "data", "0.103.2", "sts2", "acts.json");
var sourceRoot = Path.Combine(workspace, "Slay the Spire 2 版本0.103.2源码（游戏源码，读取用）", "src", "Core", "Models");

var cardsSourceRoot = Path.Combine(sourceRoot, "Cards");
var relicsSourceRoot = Path.Combine(sourceRoot, "Relics");
var currentPreviewer = Sts2RunPreviewer.CreateFromDataFiles(optionDataPath, actDataPath);
var cardSourceFiles = BuildSourceFileMap(cardsSourceRoot);
var relicSourceFiles = BuildSourceFileMap(relicsSourceRoot);
var cardFeatureCache = new Dictionary<string, CardSourceFeatures>(StringComparer.OrdinalIgnoreCase);
var relicAddsPetCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

Console.OutputEncoding = Encoding.UTF8;
if (args.Length >= 2 &&
    string.Equals(args[0], "--save-compare", StringComparison.OrdinalIgnoreCase))
{
    // Save-backed comparison isolates whether the mismatch comes from the
    // simplified Ancient option logic or from earlier run-state reconstruction.
    CompareSaveDrivenAncientPredictions(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--darv-compare", StringComparison.OrdinalIgnoreCase))
{
    CompareDarvToggleForSeed(args[1]);
    return;
}

if (args.Length >= 1 &&
    string.Equals(args[0], "--current-vs-save-options", StringComparison.OrdinalIgnoreCase))
{
    CompareCurrentLogicAgainstSaveOptions();
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--preview-seed", StringComparison.OrdinalIgnoreCase))
{
    PrintSeedPreview(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--ui-sequence-preview", StringComparison.OrdinalIgnoreCase))
{
    PrintSeedPreviewAfterUiSequence(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--analyze-then-preview", StringComparison.OrdinalIgnoreCase))
{
    PrintSeedPreviewAfterAnalyze(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--visibility-then-preview", StringComparison.OrdinalIgnoreCase))
{
    PrintSeedPreviewAfterVisibility(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--preview-twice", StringComparison.OrdinalIgnoreCase))
{
    PrintSeedPreviewTwice(args[1]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--trace-save", StringComparison.OrdinalIgnoreCase))
{
    TraceSaveGeneration(args[1]);
    return;
}

if (args.Length >= 3 &&
    string.Equals(args[0], "--trace-save-primer", StringComparison.OrdinalIgnoreCase))
{
    TraceSaveGenerationWithPrimerVariant(args[1], args[2]);
    return;
}

if (args.Length >= 2 &&
    string.Equals(args[0], "--primer-room-compare", StringComparison.OrdinalIgnoreCase))
{
    var character = args.Length >= 3 ? ParseCharacterId(args[2]) : CharacterId.Silent;
    ComparePrimerToggleRoomPools(args[1], character);
    return;
}

if (args.Contains("--root-cause", StringComparer.OrdinalIgnoreCase))
{
    RunRootCauseDiagnostics();
    return;
}

if (args.Contains("--variant-matrix", StringComparer.OrdinalIgnoreCase))
{
    RunAncientVariantMatrix();
    return;
}

if (args.Contains("--player-order-search", StringComparer.OrdinalIgnoreCase))
{
    SearchPlayerBucketOrders();
    return;
}

if (args.Contains("--shared-order-search", StringComparer.OrdinalIgnoreCase))
{
    SearchSharedBucketOrders();
    return;
}

if (args.Contains("--silent-unlock-search", StringComparer.OrdinalIgnoreCase))
{
    SearchSilentUnlockCombinations();
    return;
}

if (args.Contains("--primer-compare", StringComparer.OrdinalIgnoreCase))
{
    ComparePrimerVariantsOnKeySeeds();
    return;
}

Console.WriteLine("=== 验证 1：真实存档 vs 当前预测 ===");
ValidateOfficialEncounterMetadata("U9K3RBP488");
ValidateOfficialEncounterMetadata("Q6HL8LP56J");
RunSaveComparison(
    currentPreviewer,
    seedText: "U9K3RBP488",
    character: CharacterId.Silent,
    actualAct1: "UNDERDOCKS",
    expectedAct2Ancient: "PAEL",
    expectedAct2Options: ["PAELS_TEARS", "PAELS_WING", "PAELS_EYE"],
    expectedAct3Ancient: "VAKUU",
    expectedAct3Options: ["WHISPERING_EARRING", "DISTINGUISHED_CAPE", "LORDS_PARASOL"]);

Console.WriteLine();
RunSaveComparison(
    currentPreviewer,
    seedText: "Q6HL8LP56J",
    character: CharacterId.Silent,
    actualAct1: "OVERGROWTH",
    expectedAct2Ancient: "PAEL",
    expectedAct2Options: ["PAELS_HORN", "PAELS_TOOTH", "PAELS_BLOOD"],
    expectedAct3Ancient: "TANX",
    expectedAct3Options: []);

Console.WriteLine();
Console.WriteLine("=== 验证 2：真实游戏的第一幕选择规则 ===");
PrintActOneCheck("U9K3RBP488", "UNDERDOCKS");
PrintActOneCheck("Q6HL8LP56J", "OVERGROWTH");

Console.WriteLine();
Console.WriteLine("=== 验证 3：第一个 seed 的 primer 对照 ===");
VerifyPrimerEffect(currentPreviewer, "U9K3RBP488", CharacterId.Silent);

Console.WriteLine();
Console.WriteLine("=== 验证 4：真实事件规则 vs 当前简化规则 ===");
VerifyPaelDifference();
VerifyTanxDifference();

Console.WriteLine();
Console.WriteLine("=== 验证 5：当前 acts.json 与官方 encounter 元数据差异 ===");
PrintEncounterMetadataDiffs("U9K3RBP488");
Console.WriteLine();
PrintEncounterMetadataDiffs("Q6HL8LP56J");

Console.WriteLine();
Console.WriteLine("=== 验证 6：真实游玩历史前缀 vs 模拟房间池 ===");
CompareVisitedEncounterPrefixes("存档/1776941711.run");
Console.WriteLine();
CompareVisitedEncounterPrefixes("存档/1776946049.run");

Console.WriteLine();
Console.WriteLine("=== 验证 7：官方临时数据的 primer 开关对照 ===");
ComparePrimerToggleRoomPools("U9K3RBP488", CharacterId.Silent);
Console.WriteLine();
ComparePrimerToggleRoomPools("Q6HL8LP56J", CharacterId.Silent);

Console.WriteLine();
Console.WriteLine("=== 验证 8：全 rarity primer 临时实验 ===");
CompareAllRarityPrimer("U9K3RBP488", CharacterId.Silent);
Console.WriteLine();
CompareAllRarityPrimer("Q6HL8LP56J", CharacterId.Silent);

Console.WriteLine();
Console.WriteLine("=== 验证 9：全桶 primer + 存档重建选项 ===");
CompareSaveDrivenAncientPredictions("存档/1776941711.run");
Console.WriteLine();
CompareSaveDrivenAncientPredictions("存档/1776946049.run");

Console.WriteLine();
Console.WriteLine("=== 楠岃瘉 10锛歍anx RNG 杞ㄩ亾鎼滅储 ===");
DebugTanxSeedSearch("1776946049.run", 3);

Console.WriteLine();
Console.WriteLine("=== 楠岃瘉 11锛氭墍鏈?run 瀛樻。閫愪唤瀵规瘮 ===");
CompareAllRunFiles();

Console.WriteLine();
Console.WriteLine("=== 验证 12：VQ32PKT4QS 的 UpFront 分阶段 trace ===");
TraceSaveGeneration("存档/1776953063.run");

Console.WriteLine();
Console.WriteLine("=== 验证 13：VQ32PKT4QS 的额外 RNG 消耗搜索 ===");
SearchAct2AncientOffsets("存档/1776953063.run");

Console.WriteLine();
Console.WriteLine("=== 验证 14：Act2 ancient 前 +1 RNG 的全存档回归 ===");
EvaluateAct2AncientSkipHypothesis();

Console.WriteLine();
Console.WriteLine("=== 验证 15：shared 全桶 + player 四桶 primer 回归 ===");
CompareAllRunFilesWithHybridPrimer();

Console.WriteLine();
Console.WriteLine("=== 验证 16：禁用 DARV shared ancient 的回归 ===");
CompareAllRunFilesWithoutDarv();

Console.WriteLine();
Console.WriteLine("=== 验证 17：当前项目选项逻辑 vs 源码忠实逻辑 ===");
CompareCurrentLogicAgainstSaveOptions();

Console.WriteLine();
Console.WriteLine("=== 验证 18：primer 中间额外 +1 RNG 的变体回归 ===");
CompareAllRunFilesWithPrimerGapVariants();

void RunRootCauseDiagnostics()
{
    var targetRuns = new[]
    {
        "存档/1776781891.run",
        "存档/1776863357.run",
        "存档/1776866599.run",
        "存档/1776941711.run",
        "存档/1776946049.run"
    };

    Console.WriteLine("=== Root Cause Diagnostics ===");
    foreach (var relativeRunPath in targetRuns)
    {
        PrintAncientVariantComparison(relativeRunPath);
        Console.WriteLine();
        CompareVisitedEncounterPrefixes(relativeRunPath);
        Console.WriteLine();
        TraceSaveGeneration(relativeRunPath);
        Console.WriteLine();
        SearchAct2AncientOffsets(relativeRunPath);
        Console.WriteLine();
    }
}

void RunAncientVariantMatrix()
{
    var saveDir = Path.Combine(workspace, "存档");
    var saveFiles = Directory.GetFiles(saveDir, "*.run", SearchOption.TopDirectoryOnly)
        .OrderBy(Path.GetFileName)
        .ToList();

    var variants = new (string Label, Func<string, CharacterId, Dictionary<int, string>> Analyze)[]
    {
        (
            "current",
            (seedText, character) => PreviewAncientsByAct(
                currentPreviewer.Preview(CreateRunRequest(seedText, character)))
        ),
        (
            "official-all",
            (seedText, character) => AnalyzeSeedWithAllRarityPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AncientId)
        ),
        (
            "official-hybrid",
            (seedText, character) => AnalyzeSeedWithHybridPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AncientId)
        ),
        (
            "official-no-primer",
            (seedText, character) => AnalyzeSeedWithPrimerToggle(CreateOfficialSeedPreviewer(seedText), seedText, character, applyPrimer: false)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AncientId)
        ),
        (
            "gap(all->tracked)",
            (seedText, character) => AnalyzeSeedWithAllSharedGapTrackedPlayerPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AncientId)
        ),
    };

    var summary = variants.ToDictionary(
        variant => variant.Label,
        _ => (Matches: 0, Total: 0),
        StringComparer.OrdinalIgnoreCase);

    foreach (var runPath in saveFiles)
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            continue;
        }

        var history = runRoot["map_point_history"]?.AsArray() ?? [];
        if (history.Count <= 1)
        {
            Console.WriteLine($"{Path.GetFileName(runPath)} | only Act1 history, skipped");
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var character = ParseCharacterId(GetRunCharacterId(runRoot));
        var actualByAct = new Dictionary<int, string>();
        foreach (var actNumber in new[] { 2, 3 })
        {
            var actualAncient = GetActualAncient(history, actNumber);
            if (actualAncient != "(none)")
            {
                actualByAct[actNumber] = actualAncient;
            }
        }

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {character}");
        foreach (var actNumber in actualByAct.Keys.OrderBy(x => x))
        {
            Console.WriteLine($"  Act {actNumber}: actual={actualByAct[actNumber]}");
            foreach (var variant in variants)
            {
                var predictedByAct = variant.Analyze(seedText, character);
                var predictedAncient = predictedByAct.TryGetValue(actNumber, out var value) ? value : "(none)";
                var isMatch = string.Equals(predictedAncient, actualByAct[actNumber], StringComparison.OrdinalIgnoreCase);
                summary[variant.Label] = (summary[variant.Label].Matches, summary[variant.Label].Total + 1);
                if (isMatch)
                {
                    summary[variant.Label] = (summary[variant.Label].Matches + 1, summary[variant.Label].Total);
                }

                Console.WriteLine($"    {variant.Label}: {(isMatch ? "OK" : "DIFF")} | {predictedAncient}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine("=== Summary ===");
    foreach (var variant in variants)
    {
        var stats = summary[variant.Label];
        Console.WriteLine($"{variant.Label}: {stats.Matches}/{stats.Total}");
    }
}

void SearchPlayerBucketOrders()
{
    var trackedRarities = new[] { "Common", "Uncommon", "Rare", "Shop" };
    var targetRuns = new[]
    {
        "1776772563.run",
        "1776781891.run",
        "1776863357.run",
        "1776866599.run",
        "1776941711.run",
        "1776946049.run",
        "1776950871.run",
        "1776953063.run"
    };

    var scoredOrders = new List<(string Order, int Matches, int Total)>();
    foreach (var order in GetPermutations(trackedRarities))
    {
        var matches = 0;
        var total = 0;
        foreach (var runFile in targetRuns)
        {
            var runPath = Path.Combine(workspace, "存档", runFile);
            var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
            if (runRoot is null)
            {
                continue;
            }

            var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
            var history = runRoot["map_point_history"]?.AsArray() ?? [];
            var character = ParseCharacterId(GetRunCharacterId(runRoot));
            var predicted = AnalyzeSeedWithCustomPlayerTrackedOrder(
                CreateOfficialSeedPreviewer(seedText),
                seedText,
                character,
                order);

            foreach (var actNumber in new[] { 2, 3 })
            {
                var actualAncient = GetActualAncient(history, actNumber);
                if (actualAncient == "(none)")
                {
                    continue;
                }

                total++;
                if (predicted.TryGetValue(actNumber, out var state) &&
                    string.Equals(state.AncientId, actualAncient, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                }
            }
        }

        scoredOrders.Add((string.Join(" -> ", order), matches, total));
    }

    foreach (var result in scoredOrders
        .OrderByDescending(x => x.Matches)
        .ThenBy(x => x.Order, StringComparer.Ordinal))
    {
        Console.WriteLine($"{result.Order} | {result.Matches}/{result.Total}");
    }
}

void SearchSharedBucketOrders()
{
    var sharedRarities = new[] { "Common", "Uncommon", "Rare", "Shop", "Event", "Ancient" };
    var targetRuns = new[]
    {
        "1776772563.run",
        "1776781891.run",
        "1776863357.run",
        "1776866599.run",
        "1776941711.run",
        "1776946049.run",
        "1776950871.run",
        "1776953063.run"
    };

    var scoredOrders = new List<(string Order, int Matches, int Total)>();
    foreach (var order in GetPermutations(sharedRarities))
    {
        var matches = 0;
        var total = 0;
        foreach (var runFile in targetRuns)
        {
            var runPath = Path.Combine(workspace, "存档", runFile);
            var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
            if (runRoot is null)
            {
                continue;
            }

            var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
            var history = runRoot["map_point_history"]?.AsArray() ?? [];
            var character = ParseCharacterId(GetRunCharacterId(runRoot));
            var predicted = AnalyzeSeedWithCustomSharedOrder(
                CreateOfficialSeedPreviewer(seedText),
                seedText,
                character,
                order);

            foreach (var actNumber in new[] { 2, 3 })
            {
                var actualAncient = GetActualAncient(history, actNumber);
                if (actualAncient == "(none)")
                {
                    continue;
                }

                total++;
                if (predicted.TryGetValue(actNumber, out var state) &&
                    string.Equals(state.AncientId, actualAncient, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                }
            }
        }

        scoredOrders.Add((string.Join(" -> ", order), matches, total));
    }

    foreach (var result in scoredOrders
        .OrderByDescending(x => x.Matches)
        .ThenBy(x => x.Order, StringComparer.Ordinal)
        .Take(40))
    {
        Console.WriteLine($"{result.Order} | {result.Matches}/{result.Total}");
    }
}

void SearchSilentUnlockCombinations()
{
    var epochRelics = new (string Epoch, string[] Relics)[]
    {
        ("Relic1Epoch", ["UNSETTLING_LAMP", "INTIMIDATING_HELMET", "REPTILE_TRINKET"]),
        ("Relic2Epoch", ["BOOK_OF_FIVE_RINGS", "ICE_CREAM", "KUSARIGAMA"]),
        ("Relic3Epoch", ["VEXING_PUZZLEBOX", "RIPPLE_BASIN", "FESTIVE_POPPER"]),
        ("Relic4Epoch", ["MINIATURE_CANNON", "TUNGSTEN_ROD", "WHITE_STAR"]),
        ("Relic5Epoch", ["TINY_MAILBOX", "JOSS_PAPER", "BEATING_REMNANT"]),
        ("Silent3Epoch", ["TOUGH_BANDAGES", "PAPER_KRANE", "TINGSHA"]),
        ("Silent6Epoch", ["TWISTED_FUNNEL", "SNECKO_SKULL", "HELICAL_DART"])
    };

    var targetRuns = new[]
    {
        "1777034559.run",
        "1777041275.run",
        "1777043531.run",
        "1777084566.run",
        "1777102600.run"
    };

    var results = new List<(string LockedEpochs, int Matches, int Total)>();
    for (var mask = 0; mask < (1 << epochRelics.Length); mask++)
    {
        var excludedRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lockedEpochs = new List<string>();
        for (var i = 0; i < epochRelics.Length; i++)
        {
            if ((mask & (1 << i)) == 0)
            {
                continue;
            }

            lockedEpochs.Add(epochRelics[i].Epoch);
            foreach (var relic in epochRelics[i].Relics)
            {
                excludedRelics.Add(relic);
            }
        }

        var matches = 0;
        var total = 0;
        foreach (var runFile in targetRuns)
        {
            var runPath = Path.Combine(workspace, "存档", runFile);
            var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
            if (runRoot is null)
            {
                continue;
            }

            var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
            var history = runRoot["map_point_history"]?.AsArray() ?? [];
            var character = ParseCharacterId(GetRunCharacterId(runRoot));
            var predicted = AnalyzeSeedWithExcludedRelics(
                CreateOfficialSeedPreviewer(seedText),
                seedText,
                character,
                excludedRelics);

            foreach (var actNumber in new[] { 2, 3 })
            {
                var actualAncient = GetActualAncient(history, actNumber);
                if (actualAncient == "(none)")
                {
                    continue;
                }

                total++;
                if (predicted.TryGetValue(actNumber, out var state) &&
                    string.Equals(state.AncientId, actualAncient, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                }
            }
        }

        results.Add((lockedEpochs.Count == 0 ? "(none)" : string.Join(", ", lockedEpochs), matches, total));
    }

    foreach (var result in results
        .OrderByDescending(x => x.Matches)
        .ThenBy(x => x.LockedEpochs, StringComparer.Ordinal)
        .Take(20))
    {
        Console.WriteLine($"{result.LockedEpochs} | {result.Matches}/{result.Total}");
    }
}

void ComparePrimerVariantsOnKeySeeds()
{
    var targetRuns = new[]
    {
        "存档/1776781891.run",
        "存档/1776863357.run",
        "存档/1776866599.run",
        "存档/1776941711.run",
        "存档/1776946049.run",
        "存档/1776950871.run",
        "存档/1776953063.run"
    };

    var variants = new (string Label, Action<Sts2RunPreviewer, GameRng, CharacterId, int> Prime)[]
    {
        ("tracked/no-gap", (previewer, rng, character, playerCount) => PrimeHybridRelicBuckets(previewer, rng, character, playerCount)),
        ("tracked/+gap", (previewer, rng, character, playerCount) => PrimeTrackedGapTracked(previewer, rng, character, playerCount)),
        ("all/no-gap", (previewer, rng, character, playerCount) => PrimeAllRarityRelicBuckets(previewer, rng, character, playerCount)),
        ("all/+gap", (previewer, rng, character, playerCount) => PrimeAllSharedGapAllPlayer(previewer, rng, character, playerCount))
    };

    foreach (var runPath in targetRuns)
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
            ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

        var history = runRoot["map_point_history"]?.AsArray() ?? [];
        if (history.Count <= 1)
        {
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var character = ParseCharacterId(GetRunCharacterId(runRoot));
        var actualActs = runRoot["acts"]?.AsArray()
            ?.Select(node => node?.GetValue<string>() ?? "(none)")
            .ToList()
            ?? [];

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {character}");
        Console.WriteLine($"  actual acts: {FormatList(actualActs)}");
        Console.WriteLine($"  actual act1 boss={GetActualBoss(history, 1)} | act2 boss={GetActualBoss(history, 2)} | act2 ancient={GetActualAncient(history, 2)} | act3 ancient={GetActualAncient(history, 3)}");

        foreach (var (label, prime) in variants)
        {
            var traced = TraceOfficialActGenerationWithPrimer(
                CreateOfficialSeedPreviewer(seedText),
                seedText,
                character,
                prime);

            var act1 = traced.Acts.FirstOrDefault(x => x.ActNumber == 1);
            var act2 = traced.Acts.FirstOrDefault(x => x.ActNumber == 2);
            var act3 = traced.Acts.FirstOrDefault(x => x.ActNumber == 3);
            Console.WriteLine($"  {label}: afterPrimer={traced.CounterAfterPrimer} | act1Boss={act1?.BossId ?? "(none)"} | act2Boss={act2?.BossId ?? "(none)"} | act2Ancient={act2?.AncientId ?? "(none)"} | act3Ancient={act3?.AncientId ?? "(none)"}");
        }

        Console.WriteLine();
    }
}

Dictionary<int, string> PreviewAncientsByAct(Sts2RunPreview preview)
{
    return preview.Acts.ToDictionary(act => act.ActNumber, act => act.AncientId);
}

Sts2RunRequest CreateRunRequest(string seedText, CharacterId character)
{
    return new Sts2RunRequest
    {
        SeedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText)),
        SeedText = seedText,
        Character = character,
        AscensionLevel = 10,
        IncludeAct2 = true,
        IncludeAct3 = true,
        PlayerCount = 1
    };
}

void CompareDarvToggleForSeed(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var request = CreateRunRequest(seedText, character);
    request = request with { IncludeDarvSharedAncient = true };
    var withDarv = CreateOfficialSeedPreviewerCore(seedText, includeDarv: true).Preview(request);
    var withoutDarv = CreateOfficialSeedPreviewerCore(seedText, includeDarv: false)
        .Preview(request with { IncludeDarvSharedAncient = false });

    Console.WriteLine($"seed={seedText}");
    foreach (var actNumber in new[] { 2, 3 })
    {
        var withAct = withDarv.Acts.FirstOrDefault(act => act.ActNumber == actNumber);
        var withoutAct = withoutDarv.Acts.FirstOrDefault(act => act.ActNumber == actNumber);
        var withText = withAct is null
            ? "(none)"
            : $"{withAct.AncientId}{FormatOptions(withAct.AncientOptions.Select(option => option.OptionId).ToList())}";
        var withoutText = withoutAct is null
            ? "(none)"
            : $"{withoutAct.AncientId}{FormatOptions(withoutAct.AncientOptions.Select(option => option.OptionId).ToList())}";
        Console.WriteLine($"  Act {actNumber}: withDarv={withText} | withoutDarv={withoutText}");
    }
}

void PrintSeedPreview(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var request = CreateRunRequest(seedText, character);
    var preview = currentPreviewer.Preview(request);

    Console.WriteLine($"seed={seedText}");
    foreach (var act in preview.Acts.OrderBy(act => act.ActNumber))
    {
        Console.WriteLine(
            $"  Act {act.ActNumber}: {act.AncientId} [{string.Join(", ", act.AncientOptions.Select(option => option.OptionId))}]");
    }
}

void PrintSeedPreviewAfterUiSequence(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var normalizedSeed = SeedFormatter.Normalize(seedText);
    var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
    var ancientAvailability = ResolveUiAncientAvailability();
    var unlockedCharacters = new[]
    {
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    };

    using var datasetStream = File.OpenRead(Path.Combine(workspace, "data", "0.103.2", "neow", "options.json"));
    var dataset = NeowOptionDataLoader.Load(datasetStream);

    var analysisRequest = new Sts2SeedAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        AncientAvailability = ancientAvailability
    };
    var analysis = currentPreviewer.AnalyzePools(analysisRequest);

    var visibilityRequest = new Sts2RelicVisibilityRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        PlayerCount = 1,
        AncientAvailability = ancientAvailability
    };
    var visibility = currentPreviewer.AnalyzeRelicVisibility(dataset, visibilityRequest);

    var previewRequest = new Sts2RunRequest
    {
        SeedValue = seedValue,
        SeedText = normalizedSeed,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        PlayerCount = 1,
        AncientAvailability = ancientAvailability,
        IncludeAct2 = true,
        IncludeAct3 = true
    };
    var preview = currentPreviewer.Preview(previewRequest);

    Console.WriteLine($"seed={normalizedSeed}");
    Console.WriteLine($"  AnalyzePools acts={string.Join(" | ", analysis.Acts.Select(act => $"Act{act.ActNumber}:{act.ActName}"))}");
    Console.WriteLine($"  Visibility profiles={visibility.Profiles.Count}");
    foreach (var act in preview.Acts.OrderBy(act => act.ActNumber))
    {
        Console.WriteLine(
            $"  Preview Act {act.ActNumber}: {act.AncientId} [{string.Join(", ", act.AncientOptions.Select(option => option.OptionId))}]");
    }
}

void PrintSeedPreviewAfterAnalyze(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var normalizedSeed = SeedFormatter.Normalize(seedText);
    var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
    var ancientAvailability = ResolveUiAncientAvailability();
    var unlockedCharacters = new[]
    {
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    };

    var analysisRequest = new Sts2SeedAnalysisRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        AncientAvailability = ancientAvailability
    };
    currentPreviewer.AnalyzePools(analysisRequest);

    var previewRequest = new Sts2RunRequest
    {
        SeedValue = seedValue,
        SeedText = normalizedSeed,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        PlayerCount = 1,
        AncientAvailability = ancientAvailability,
        IncludeAct2 = true,
        IncludeAct3 = true
    };
    var preview = currentPreviewer.Preview(previewRequest);
    PrintPreview(normalizedSeed, preview);
}

void PrintSeedPreviewAfterVisibility(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var normalizedSeed = SeedFormatter.Normalize(seedText);
    var seedValue = SeedFormatter.ToUIntSeed(normalizedSeed);
    var ancientAvailability = ResolveUiAncientAvailability();
    var unlockedCharacters = new[]
    {
        CharacterId.Ironclad,
        CharacterId.Silent,
        CharacterId.Defect,
        CharacterId.Necrobinder,
        CharacterId.Regent
    };

    using var datasetStream = File.OpenRead(Path.Combine(workspace, "data", "0.103.2", "neow", "options.json"));
    var dataset = NeowOptionDataLoader.Load(datasetStream);
    var visibilityRequest = new Sts2RelicVisibilityRequest
    {
        SeedText = normalizedSeed,
        SeedValue = seedValue,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        PlayerCount = 1,
        AncientAvailability = ancientAvailability
    };
    currentPreviewer.AnalyzeRelicVisibility(dataset, visibilityRequest);

    var previewRequest = new Sts2RunRequest
    {
        SeedValue = seedValue,
        SeedText = normalizedSeed,
        Character = character,
        UnlockedCharacters = unlockedCharacters,
        AscensionLevel = 10,
        PlayerCount = 1,
        AncientAvailability = ancientAvailability,
        IncludeAct2 = true,
        IncludeAct3 = true
    };
    var preview = currentPreviewer.Preview(previewRequest);
    PrintPreview(normalizedSeed, preview);
}

void PrintPreview(string seedText, Sts2RunPreview preview)
{
    Console.WriteLine($"seed={seedText}");
    foreach (var act in preview.Acts.OrderBy(act => act.ActNumber))
    {
        Console.WriteLine(
            $"  Preview Act {act.ActNumber}: {act.AncientId} [{string.Join(", ", act.AncientOptions.Select(option => option.OptionId))}]");
    }
}

void PrintSeedPreviewTwice(string seedText)
{
    const CharacterId character = CharacterId.Silent;
    var ancientAvailability = ResolveUiAncientAvailability();
    var request = CreateRunRequest(seedText, character);
    request = request with
    {
        UnlockedCharacters = new[]
        {
            CharacterId.Ironclad,
            CharacterId.Silent,
            CharacterId.Defect,
            CharacterId.Necrobinder,
            CharacterId.Regent
        },
        AncientAvailability = ancientAvailability
    };

    var first = currentPreviewer.Preview(request);
    var second = currentPreviewer.Preview(request);

    Console.WriteLine("first");
    PrintPreview(seedText, first);
    Console.WriteLine("second");
    PrintPreview(seedText, second);
}

Sts2AncientAvailability ResolveUiAncientAvailability()
{
    var progressPath = Path.Combine(workspace, "存档", "progress.save");
    if (!File.Exists(progressPath))
    {
        return Sts2AncientAvailability.Default;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(progressPath));
    if (!document.RootElement.TryGetProperty("epochs", out var epochs) || epochs.ValueKind != JsonValueKind.Array)
    {
        return Sts2AncientAvailability.Default;
    }

    var revealedEpochs = new List<string>();
    foreach (var epoch in epochs.EnumerateArray())
    {
        if (!epoch.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            !epoch.TryGetProperty("state", out var stateElement) ||
            stateElement.ValueKind != JsonValueKind.String ||
            !string.Equals(stateElement.GetString(), "revealed", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var epochId = idElement.GetString();
        if (!string.IsNullOrWhiteSpace(epochId))
        {
            revealedEpochs.Add(epochId);
        }
    }

    return Sts2AncientAvailability.FromRevealedEpochIds(revealedEpochs);
}

void PrintAncientVariantComparison(string relativeRunPath)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    if (!File.Exists(runPath))
    {
        var fileName = Path.GetFileName(relativeRunPath);
        var saveDir = Path.Combine(workspace, "存档");
        runPath = Directory.GetFiles(saveDir, fileName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"Could not locate run file {relativeRunPath} under {saveDir}.");
    }

    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");
    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var character = ParseCharacterId(GetRunCharacterId(runRoot));
    var history = runRoot["map_point_history"]?.AsArray() ?? [];

    var request = new Sts2RunRequest
    {
        SeedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText)),
        SeedText = seedText,
        Character = character,
        AscensionLevel = 10,
        IncludeAct2 = true,
        IncludeAct3 = true,
        PlayerCount = 1
    };

    var currentPreview = currentPreviewer.Preview(request);
    var officialPreviewer = CreateOfficialSeedPreviewer(seedText);
    var allRarity = AnalyzeSeedWithAllRarityPrimer(officialPreviewer, seedText, character);
    var hybrid = AnalyzeSeedWithHybridPrimer(officialPreviewer, seedText, character);
    var noPrimer = AnalyzeSeedWithPrimerToggle(officialPreviewer, seedText, character, applyPrimer: false);
    var currentAllRarity = AnalyzeSeedWithAllRarityPrimer(currentPreviewer, seedText, character);
    var currentHybrid = AnalyzeSeedWithHybridPrimer(currentPreviewer, seedText, character);

    Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {character}");
    foreach (var actNumber in new[] { 2, 3 })
    {
        var actual = GetActualAncient(history, actNumber);
        if (actual == "(none)")
        {
            continue;
        }

        var current = currentPreview.Acts.FirstOrDefault(x => x.ActNumber == actNumber)?.AncientId ?? "(none)";
        var allRarityAncient = allRarity.TryGetValue(actNumber, out var allRarityState) ? allRarityState.AncientId : "(none)";
        var hybridAncient = hybrid.TryGetValue(actNumber, out var hybridState) ? hybridState.AncientId : "(none)";
        var currentAllRarityAncient = currentAllRarity.TryGetValue(actNumber, out var currentAllRarityState) ? currentAllRarityState.AncientId : "(none)";
        var currentHybridAncient = currentHybrid.TryGetValue(actNumber, out var currentHybridState) ? currentHybridState.AncientId : "(none)";
        var noPrimerAncient = noPrimer.TryGetValue(actNumber, out var noPrimerState) ? noPrimerState.AncientId : "(none)";

        Console.WriteLine($"  Act {actNumber}: actual={actual} | current={current} | current-all={currentAllRarityAncient} | current-hybrid={currentHybridAncient} | official-all={allRarityAncient} | official-hybrid={hybridAncient} | no-primer={noPrimerAncient}");
    }
}

void RunSaveComparison(
    Sts2RunPreviewer current,
    string seedText,
    CharacterId character,
    string actualAct1,
    string expectedAct2Ancient,
    IReadOnlyList<string> expectedAct2Options,
    string expectedAct3Ancient,
    IReadOnlyList<string> expectedAct3Options)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var officialPreviewer = CreateOfficialSeedPreviewer(seedText);
    var request = new Sts2RunRequest
    {
        SeedValue = seedValue,
        SeedText = seedText,
        Character = character,
        AscensionLevel = 10,
        PlayerCount = 1,
        IncludeAct2 = true,
        IncludeAct3 = true
    };

    var currentPreview = current.Preview(request);
    var officialPreview = officialPreviewer.Preview(request);

    Console.WriteLine($"种子 {seedText} / 角色 {character}");
    Console.WriteLine($"  实际 Act1: {actualAct1}");
    Console.WriteLine($"  当前预测 Act2: {DescribeAct(currentPreview, 2)}");
    Console.WriteLine($"  官方数据临时模拟 Act2: {DescribeAct(officialPreview, 2)}");
    Console.WriteLine($"  实际 Act2: {expectedAct2Ancient} [{string.Join(", ", expectedAct2Options)}]");
    Console.WriteLine($"  当前预测 Act3: {DescribeAct(currentPreview, 3)}");
    Console.WriteLine($"  官方数据临时模拟 Act3: {DescribeAct(officialPreview, 3)}");
    Console.WriteLine($"  实际 Act3: {expectedAct3Ancient}{FormatOptions(expectedAct3Options)}");
}

string DescribeAct(Sts2RunPreview preview, int actNumber)
{
    var act = preview.Acts.FirstOrDefault(a => a.ActNumber == actNumber);
    if (act is null)
    {
        return "(无结果)";
    }

    return $"{act.AncientId}{FormatOptions(act.AncientOptions.Select(o => o.OptionId).ToList())}";
}

string FormatOptions(IReadOnlyList<string> options)
{
    return options.Count == 0
        ? string.Empty
        : $" [{string.Join(", ", options)}]";
}

void PrintActOneCheck(string seedText, string actualAct1)
{
    var normalized = SeedFormatter.Normalize(seedText);
    var seedValue = SeedFormatter.ToUIntSeed(normalized);
    var rootRng = new GameRng(seedValue);
    var sourceAct1 = rootRng.NextBool() ? "UNDERDOCKS" : "OVERGROWTH";

    Console.WriteLine($"种子 {seedText}");
    Console.WriteLine($"  真实规则推导 Act1: {sourceAct1}");
    Console.WriteLine($"  当前项目固定 Act1: UNDERDOCKS");
    Console.WriteLine($"  实际存档 Act1: {actualAct1}");
}

void VerifyPaelDifference()
{
    const int goopyEligible = 0;
    const int removableCards = 0;
    const bool hasEventPet = true;

    var diff = FindFirstDifference(
        eventId: "PAEL",
        currentLogicTypeName: "SeedModel.Sts2.Ancients.PaelEventLogic",
        realGenerator: rng => GenerateRealPaelOptions(rng, goopyEligible, removableCards, hasEventPet));

    Console.WriteLine("Pael（合成 run 状态：0 张可 Goopy，0 张可移除，已有 event pet）");
    Console.WriteLine($"  首个出现差异的 runSeed: 0x{diff.RunSeed:X8}");
    Console.WriteLine($"  当前项目: {string.Join(", ", diff.Current)}");
    Console.WriteLine($"  真实规则: {string.Join(", ", diff.Real)}");
}

void VerifyTanxDifference()
{
    const int instinctEligible = 0;

    var diff = FindFirstDifference(
        eventId: "TANX",
        currentLogicTypeName: "SeedModel.Sts2.Ancients.TanxEventLogic",
        realGenerator: rng => GenerateRealTanxOptions(rng, instinctEligible));

    Console.WriteLine("Tanx（合成 run 状态：0 张可 Instinct）");
    Console.WriteLine($"  首个出现差异的 runSeed: 0x{diff.RunSeed:X8}");
    Console.WriteLine($"  当前项目: {string.Join(", ", diff.Current)}");
    Console.WriteLine($"  真实规则: {string.Join(", ", diff.Real)}");
}

void VerifyPrimerEffect(Sts2RunPreviewer current, string seedText, CharacterId character)
{
    var official = CreateOfficialSeedPreviewer(seedText);
    var currentPrimed = SimulateAncients(current, seedText, character, applyPrimer: true);
    var currentUnprimed = SimulateAncients(current, seedText, character, applyPrimer: false);
    var officialPrimed = SimulateAncients(official, seedText, character, applyPrimer: true);
    var officialUnprimed = SimulateAncients(official, seedText, character, applyPrimer: false);

    Console.WriteLine($"种子 {seedText}");
    Console.WriteLine($"  当前数据 + primer: {string.Join(" | ", currentPrimed)}");
    Console.WriteLine($"  当前数据 - primer: {string.Join(" | ", currentUnprimed)}");
    Console.WriteLine($"  官方数据 + primer: {string.Join(" | ", officialPrimed)}");
    Console.WriteLine($"  官方数据 - primer: {string.Join(" | ", officialUnprimed)}");
}

(uint RunSeed, IReadOnlyList<string> Current, IReadOnlyList<string> Real) FindFirstDifference(
    string eventId,
    string currentLogicTypeName,
    Func<GameRng, IReadOnlyList<string>> realGenerator)
{
    for (uint runSeed = 0; runSeed < 200_000; runSeed++)
    {
        var current = InvokeCurrentLogic(currentLogicTypeName, eventId, runSeed);
        var real = realGenerator(new GameRng(unchecked(runSeed + 1), eventId));
        if (!current.SequenceEqual(real, StringComparer.Ordinal))
        {
            return (runSeed, current, real);
        }
    }

    throw new InvalidOperationException($"No difference found for {eventId} in search range.");
}

IReadOnlyList<string> InvokeCurrentLogic(string logicTypeName, string eventId, uint runSeed)
{
    var assembly = typeof(Sts2RunPreviewer).Assembly;
    var logicType = assembly.GetType(logicTypeName, throwOnError: true)!;
    var contextType = assembly.GetType("SeedModel.Sts2.Ancients.AncientGenerationContext", throwOnError: true)!;
    var logic = Activator.CreateInstance(logicType, nonPublic: true)!;
    var context = Activator.CreateInstance(
        contextType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args:
        [
            runSeed,
            CharacterId.Silent,
            1,
            new[] { CharacterId.Ironclad, CharacterId.Silent, CharacterId.Defect, CharacterId.Necrobinder, CharacterId.Regent }
        ],
        culture: null)!;

    var rng = new GameRng(unchecked(runSeed + 1), eventId);
    var method = logicType.GetMethod("GenerateOptions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(logicType.FullName, "GenerateOptions");
    var result = (System.Collections.IEnumerable?)method.Invoke(logic, [context, rng])
        ?? throw new InvalidOperationException($"GenerateOptions returned null for {logicType.Name}.");

    var optionIds = new List<string>();
    foreach (var item in result)
    {
        var optionId = item?.GetType().GetProperty("OptionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) as string;
        if (!string.IsNullOrWhiteSpace(optionId))
        {
            optionIds.Add(optionId);
        }
    }

    return optionIds;
}

IReadOnlyList<string> GenerateRealPaelOptions(GameRng rng, int goopyEligible, int removableCards, bool hasEventPet)
{
    var pool1 = new[] { "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS" };
    var option1 = pool1[rng.NextInt(pool1.Length)];

    var pool2 = new List<string> { "PAELS_WING" };
    if (goopyEligible >= 3)
    {
        pool2.Add("PAELS_CLAW");
    }

    if (removableCards >= 5)
    {
        pool2.Add("PAELS_TOOTH");
    }

    pool2.AddRange(pool2.ToList());
    pool2.Add("PAELS_GROWTH");
    var option2 = pool2[rng.NextInt(pool2.Count)];

    var pool3 = new List<string> { "PAELS_EYE", "PAELS_BLOOD" };
    if (!hasEventPet)
    {
        pool3.Add("PAELS_LEGION");
    }

    var option3 = pool3[rng.NextInt(pool3.Count)];
    return [option1, option2, option3];
}

IReadOnlyList<string> GenerateRealTanxOptions(GameRng rng, int instinctEligible)
{
    var pool = new List<string>
    {
        "CLAWS",
        "CROSSBOW",
        "IRON_CLUB",
        "MEAT_CLEAVER",
        "SAI",
        "SPIKED_GAUNTLETS",
        "TANXS_WHISTLE",
        "THROWING_AXE",
        "WAR_HAMMER"
    };

    if (instinctEligible >= 3)
    {
        pool.Add("TRI_BOOMERANG");
    }

    rng.Shuffle(pool);
    return pool.Take(3).ToList();
}

Sts2RunPreviewer CreateOfficialSeedPreviewer(string seedText)
{
    return CreateOfficialSeedPreviewerCore(seedText, includeDarv: true);
}

Sts2RunPreviewer CreateOfficialSeedPreviewerCore(string seedText, bool includeDarv)
{
    var root = JsonNode.Parse(File.ReadAllText(actDataPath))
        ?? throw new InvalidOperationException("Failed to parse acts.json.");

    var acts = root["acts"]?.AsArray()
        ?? throw new InvalidOperationException("acts.json missing acts array.");
    var encounters = root["encounters"]?.AsObject()
        ?? throw new InvalidOperationException("acts.json missing encounters object.");

    var actOne = ChooseActOne(seedText);
    var actDefinitions = new[]
    {
        GetOfficialActDefinition(actOne),
        GetOfficialActDefinition("Hive"),
        GetOfficialActDefinition("Glory")
    };

    acts.Clear();
    foreach (var act in actDefinitions)
    {
        acts.Add(JsonSerializer.SerializeToNode(new
        {
            ancients = act.Ancients,
            baseRooms = act.BaseRooms,
            encounters = act.Encounters,
            events = act.Events,
            name = act.Name,
            number = act.Number,
            weakRooms = act.WeakRooms
        }));
    }

    foreach (var encounterId in actDefinitions.SelectMany(a => a.Encounters).Distinct(StringComparer.Ordinal))
    {
        var metadata = LoadEncounterMetadata(encounterId);
        encounters[encounterId] = JsonSerializer.SerializeToNode(new
        {
            isWeak = metadata.IsWeak,
            roomType = metadata.RoomType,
            tags = metadata.Tags
        });
    }

    root["relicPools"] = BuildOfficialRelicPoolsNode();

    var sharedAncients = root["sharedAncients"]?.AsArray()
        ?? throw new InvalidOperationException("acts.json missing sharedAncients.");
    if (!includeDarv)
    {
        sharedAncients.Clear();
    }

    using var optionStream = File.OpenRead(optionDataPath);
    using var actStream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
    return Sts2RunPreviewer.Create(optionStream, actStream);
}

JsonNode BuildOfficialRelicPoolsNode()
{
    var sharedSequence = LoadOfficialRelicPoolSequence("SharedRelicPool");
    var characters = new Dictionary<string, List<string>>(StringComparer.Ordinal)
    {
        ["Ironclad"] = LoadOfficialRelicPoolSequence("IroncladRelicPool"),
        ["Silent"] = LoadOfficialRelicPoolSequence("SilentRelicPool"),
        ["Defect"] = LoadOfficialRelicPoolSequence("DefectRelicPool"),
        ["Necrobinder"] = LoadOfficialRelicPoolSequence("NecrobinderRelicPool"),
        ["Regent"] = LoadOfficialRelicPoolSequence("RegentRelicPool")
    };

    var allRelics = sharedSequence
        .Concat(characters.Values.SelectMany(sequence => sequence))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var rarities = allRelics
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToDictionary(
            relicId => relicId,
            LoadOfficialRelicRarity,
            StringComparer.OrdinalIgnoreCase);

    return JsonSerializer.SerializeToNode(new
    {
        sharedSequence,
        characters,
        rarities
    }) ?? throw new InvalidOperationException("Failed to build official relicPools node.");
}

List<string> LoadOfficialRelicPoolSequence(string poolClassName)
{
    var filePath = Path.Combine(sourceRoot, "RelicPools", $"{poolClassName}.cs");
    if (!File.Exists(filePath))
    {
        throw new FileNotFoundException("Relic pool source file not found.", filePath);
    }

    return Regex.Matches(File.ReadAllText(filePath), @"ModelDb\.Relic<(\w+)>\(\)")
        .Cast<Match>()
        .Select(match => ToModelStyleId(match.Groups[1].Value))
        .ToList();
}

string LoadOfficialRelicRarity(string relicId)
{
    if (!relicSourceFiles.TryGetValue(relicId, out var filePath))
    {
        throw new KeyNotFoundException($"Relic source file not found for {relicId}.");
    }

    var text = File.ReadAllText(filePath);
    var match = Regex.Match(text, @"Rarity\s*=>\s*RelicRarity\.(\w+)");
    if (!match.Success)
    {
        throw new InvalidOperationException($"Failed to parse relic rarity from {filePath}.");
    }

    return match.Groups[1].Value;
}

string ChooseActOne(string seedText)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue);
    return rng.NextBool() ? "Underdocks" : "Overgrowth";
}

OfficialActDefinition GetOfficialActDefinition(string actName)
{
    return actName switch
    {
        "Underdocks" => new OfficialActDefinition(
            "Underdocks",
            1,
            15,
            3,
            ["NEOW"],
            [
                "AbyssalBaths",
                "DrowningBeacon",
                "EndlessConveyor",
                "PunchOff",
                "SpiralingWhirlpool",
                "SunkenStatue",
                "SunkenTreasury",
                "DoorsOfLightAndDark",
                "TrashHeap",
                "WaterloggedScriptorium"
            ],
            [
                "CorpseSlugsNormal",
                "CorpseSlugsWeak",
                "CultistsNormal",
                "FossilStalkerNormal",
                "GremlinMercNormal",
                "HauntedShipNormal",
                "LagavulinMatriarchBoss",
                "LivingFogNormal",
                "PhantasmalGardenersElite",
                "PunchConstructNormal",
                "SeapunkNormal",
                "SeapunkWeak",
                "SewerClamNormal",
                "SkulkingColonyElite",
                "SludgeSpinnerWeak",
                "SoulFyshBoss",
                "TerrorEelElite",
                "ToadpolesWeak",
                "TwoTailedRatsNormal",
                "WaterfallGiantBoss"
            ]),
        "Overgrowth" => new OfficialActDefinition(
            "Overgrowth",
            1,
            15,
            3,
            ["NEOW"],
            [
                "AromaOfChaos",
                "ByrdonisNest",
                "DenseVegetation",
                "JungleMazeAdventure",
                "LuminousChoir",
                "MorphicGrove",
                "SapphireSeed",
                "SunkenStatue",
                "TabletOfTruth",
                "UnrestSite",
                "Wellspring",
                "WhisperingHollow",
                "WoodCarvings"
            ],
            [
                "BygoneEffigyElite",
                "ByrdonisElite",
                "CeremonialBeastBoss",
                "CubexConstructNormal",
                "FlyconidNormal",
                "FogmogNormal",
                "FuzzyWurmCrawlerWeak",
                "InkletsNormal",
                "MawlerNormal",
                "NibbitsNormal",
                "NibbitsWeak",
                "OvergrowthCrawlers",
                "PhrogParasiteElite",
                "RubyRaidersNormal",
                "ShrinkerBeetleWeak",
                "SlimesNormal",
                "SlimesWeak",
                "SlitheringStranglerNormal",
                "SnappingJaxfruitNormal",
                "TheKinBoss",
                "VantomBoss",
                "VineShamblerNormal"
            ]),
        "Hive" => new OfficialActDefinition(
            "Hive",
            2,
            14,
            2,
            ["OROBAS", "PAEL", "TEZCATARA"],
            [
                "Amalgamator",
                "Bugslayer",
                "ColorfulPhilosophers",
                "ColossalFlower",
                "FieldOfManSizedHoles",
                "InfestedAutomaton",
                "LostWisp",
                "SpiritGrafter",
                "TheLanternKey",
                "ZenWeaver"
            ],
            [
                "BowlbugsNormal",
                "BowlbugsWeak",
                "ChompersNormal",
                "DecimillipedeElite",
                "EntomancerElite",
                "ExoskeletonsNormal",
                "ExoskeletonsWeak",
                "HunterKillerNormal",
                "KaiserCrabBoss",
                "InfestedPrismsElite",
                "KnowledgeDemonBoss",
                "LouseProgenitorNormal",
                "MytesNormal",
                "OvicopterNormal",
                "SlumberingBeetleNormal",
                "SpinyToadNormal",
                "TheInsatiableBoss",
                "TheObscuraNormal",
                "ThievingHopperWeak",
                "TunnelerWeak"
            ]),
        "Glory" => new OfficialActDefinition(
            "Glory",
            3,
            13,
            2,
            ["NONUPEIPE", "TANX", "VAKUU"],
            [
                "BattlewornDummy",
                "GraveOfTheForgotten",
                "HungryForMushrooms",
                "Reflections",
                "RoundTeaParty",
                "Trial",
                "TinkerTime"
            ],
            [
                "AxebotsNormal",
                "ConstructMenagerieNormal",
                "DevotedSculptorWeak",
                "DoormakerBoss",
                "FabricatorNormal",
                "FrogKnightNormal",
                "GlobeHeadNormal",
                "KnightsElite",
                "MechaKnightElite",
                "OwlMagistrateNormal",
                "QueenBoss",
                "ScrollsOfBitingNormal",
                "ScrollsOfBitingWeak",
                "SlimedBerserkerNormal",
                "SoulNexusElite",
                "TestSubjectBoss",
                "TheLostAndForgottenNormal",
                "TurretOperatorWeak"
            ]),
        _ => throw new ArgumentOutOfRangeException(nameof(actName), actName, null)
    };
}

EncounterMetadataRecord LoadEncounterMetadata(string encounterId)
{
    var filePath = Path.Combine(sourceRoot, "Encounters", $"{encounterId}.cs");
    if (!File.Exists(filePath))
    {
        throw new FileNotFoundException("Encounter source file not found.", filePath);
    }

    var text = File.ReadAllText(filePath);
    var roomType = Regex.Match(text, @"RoomType\s*=>\s*RoomType\.(\w+)")
        is { Success: true } roomTypeMatch
            ? roomTypeMatch.Groups[1].Value
            : "Unknown";
    var isWeak = Regex.IsMatch(text, @"public\s+override\s+bool\s+IsWeak\s*=>\s*true");
    var tags = Regex.Matches(text, @"EncounterTag\.(\w+)")
        .Select(match => match.Groups[1].Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    return new EncounterMetadataRecord(roomType, isWeak, tags);
}

IReadOnlyList<string> SimulateAncients(Sts2RunPreviewer previewer, string seedText, CharacterId character, bool applyPrimer)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");

    var previewerType = typeof(Sts2RunPreviewer);
    if (applyPrimer)
    {
        var primer = previewerType.GetField("_primer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
            ?? throw new InvalidOperationException("Previewer primer field not found.");
        var primeMethod = primer.GetType().GetMethod("Prime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Primer.Prime method not found.");
        primeMethod.Invoke(primer, [rng, character, 1]);
    }

    var simulator = previewerType.GetField("_simulator", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
        ?? throw new InvalidOperationException("Previewer simulator field not found.");
    var simulateMethod = simulator.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method => method.Name == "Simulate" && method.GetParameters().Length == 1)
        ?? throw new InvalidOperationException("Simulator.Simulate(GameRng) method not found.");
    var results = (System.Collections.IEnumerable?)simulateMethod.Invoke(simulator, [rng])
        ?? throw new InvalidOperationException("Simulator.Simulate returned null.");

    var lines = new List<string>();
    foreach (var item in results)
    {
        var type = item?.GetType();
        if (type is null)
        {
            continue;
        }

        var actNumber = (int?)type.GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
        var ancientId = type.GetProperty("AncientId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) as string;
        if (actNumber.HasValue && !string.IsNullOrWhiteSpace(ancientId))
        {
            lines.Add($"Act {actNumber.Value}: {ancientId}");
        }
    }

    return lines;
}

void PrintEncounterMetadataDiffs(string seedText)
{
    var root = JsonNode.Parse(File.ReadAllText(actDataPath))
        ?? throw new InvalidOperationException("Failed to parse current acts.json.");
    var currentEncounters = root["encounters"]?.AsObject()
        ?? throw new InvalidOperationException("Current acts.json missing encounters.");

    var actNames = new[] { ChooseActOne(seedText), "Hive", "Glory" };
    var encounterIds = actNames
        .SelectMany(name => GetOfficialActDefinition(name).Encounters)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    Console.WriteLine($"种子 {seedText} 使用 act: {string.Join(" -> ", actNames)}");
    foreach (var encounterId in encounterIds)
    {
        var official = LoadEncounterMetadata(encounterId);
        var current = currentEncounters[encounterId]?.Deserialize<CurrentEncounterData>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (current is null)
        {
            Console.WriteLine($"  缺少 encounter: {encounterId}");
            continue;
        }

        var roomTypeMismatch = !string.Equals(current.RoomType, official.RoomType, StringComparison.OrdinalIgnoreCase);
        var weakMismatch = current.IsWeak != official.IsWeak;
        var tagMismatch = !current.Tags.SequenceEqual(official.Tags, StringComparer.Ordinal);

        if (!roomTypeMismatch && !weakMismatch && !tagMismatch)
        {
            continue;
        }

        Console.WriteLine($"  {encounterId}");
        if (roomTypeMismatch)
        {
            Console.WriteLine($"    roomType 当前={current.RoomType} 官方={official.RoomType}");
        }

        if (weakMismatch)
        {
            Console.WriteLine($"    isWeak 当前={current.IsWeak} 官方={official.IsWeak}");
        }

        if (tagMismatch)
        {
            Console.WriteLine($"    tags 当前=[{string.Join(", ", current.Tags)}] 官方=[{string.Join(", ", official.Tags)}]");
        }
    }
}

void ValidateOfficialEncounterMetadata(string seedText)
{
    var actNames = new[] { ChooseActOne(seedText), "Hive", "Glory" };
    var unknowns = actNames
        .SelectMany(name => GetOfficialActDefinition(name).Encounters)
        .Distinct(StringComparer.Ordinal)
        .Select(id => (Id: id, Meta: LoadEncounterMetadata(id)))
        .Where(x => string.Equals(x.Meta.RoomType, "Unknown", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.Id)
        .ToList();

    if (unknowns.Count > 0)
    {
        Console.WriteLine($"[警告] 种子 {seedText} 的官方临时数据存在 Unknown roomType: {string.Join(", ", unknowns)}");
    }
}

void CompareVisitedEncounterPrefixes(string relativeRunPath)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var acts = runRoot["acts"]?.AsArray()
        ?? throw new InvalidOperationException($"Run file missing acts: {runPath}");
    var history = runRoot["map_point_history"]?.AsArray()
        ?? throw new InvalidOperationException($"Run file missing map_point_history: {runPath}");

    var currentAnalysis = AnalyzeSeed(currentPreviewer, seedText, CharacterId.Silent);
    var officialAnalysis = AnalyzeSeed(CreateOfficialSeedPreviewer(seedText), seedText, CharacterId.Silent);

    Console.WriteLine($"种子 {seedText}");

    for (var actIndex = 0; actIndex < Math.Min(acts.Count, history.Count); actIndex++)
    {
        var actId = acts[actIndex]?.GetValue<string>() ?? $"ACT_{actIndex + 1}";
        var actHistory = history[actIndex]?.AsArray() ?? [];
        var actualMonsters = ExtractEncounterIds(actHistory, "monster");
        var actualElites = ExtractEncounterIds(actHistory, "elite");
        var actualBosses = ExtractEncounterIds(actHistory, "boss");
        var actualAncients = ExtractEventIdsAtAncients(actHistory);

        var currentAct = currentAnalysis.Acts.ElementAtOrDefault(actIndex);
        var officialAct = officialAnalysis.Acts.ElementAtOrDefault(actIndex);

        Console.WriteLine($"  {actId}");
        PrintPrefixComparison("怪物", actualMonsters, currentAct?.MonsterPool, officialAct?.MonsterPool);
        PrintPrefixComparison("精英", actualElites, currentAct?.ElitePool, officialAct?.ElitePool);
        Console.WriteLine($"    实际Boss: {FormatList(actualBosses)}");
        Console.WriteLine($"    实际Ancient: {FormatList(actualAncients)}");
    }
}

void ComparePrimerToggleRoomPools(string seedText, CharacterId character)
{
    var officialPreviewer = CreateOfficialSeedPreviewer(seedText);
    var withPrimer = AnalyzeSeedWithPrimerToggle(officialPreviewer, seedText, character, applyPrimer: true);
    var withoutPrimer = AnalyzeSeedWithPrimerToggle(officialPreviewer, seedText, character, applyPrimer: false);

    Console.WriteLine($"种子 {seedText}");
    foreach (var actNumber in withPrimer.Keys.OrderBy(x => x))
    {
        var primed = withPrimer[actNumber];
        var unprimed = withoutPrimer[actNumber];
        Console.WriteLine($"  Act {actNumber}");
        Console.WriteLine($"    +primer 怪物前 7: {FormatList(primed.Monsters.Take(7).ToList())}");
        Console.WriteLine($"    -primer 怪物前 7: {FormatList(unprimed.Monsters.Take(7).ToList())}");
        Console.WriteLine($"    +primer 精英前 3: {FormatList(primed.Elites.Take(3).ToList())}");
        Console.WriteLine($"    -primer 精英前 3: {FormatList(unprimed.Elites.Take(3).ToList())}");
        Console.WriteLine($"    +primer Ancient: {primed.AncientId}");
        Console.WriteLine($"    -primer Ancient: {unprimed.AncientId}");
    }
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithPrimerToggle(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    bool applyPrimer)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    var previewerType = typeof(Sts2RunPreviewer);

    if (applyPrimer)
    {
        var primer = previewerType.GetField("_primer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
            ?? throw new InvalidOperationException("Previewer primer field not found.");
        var primeMethod = primer.GetType().GetMethod("Prime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Primer.Prime method not found.");
        primeMethod.Invoke(primer, [rng, character, 1]);
    }

    var simulator = previewerType.GetField("_simulator", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
        ?? throw new InvalidOperationException("Previewer simulator field not found.");
    var analyzeMethod = simulator.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method => method.Name == "Analyze" && method.GetParameters().Length == 1)
        ?? throw new InvalidOperationException("Simulator.Analyze(GameRng) method not found.");
    var results = (System.Collections.IEnumerable?)analyzeMethod.Invoke(simulator, [rng])
        ?? throw new InvalidOperationException("Simulator.Analyze returned null.");

    var map = new Dictionary<int, TempActPoolState>();
    foreach (var item in results)
    {
        var type = item?.GetType();
        if (type is null)
        {
            continue;
        }

        var actNumber = (int?)type.GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
        var monsters = ((System.Collections.IEnumerable?)type.GetProperty("NormalEncounters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item))
            ?.Cast<object?>()
            .Select(x => x?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        var elites = ((System.Collections.IEnumerable?)type.GetProperty("EliteEncounters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item))
            ?.Cast<object?>()
            .Select(x => x?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        var ancientId = type.GetProperty("AncientId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)?.ToString();

        if (actNumber.HasValue && monsters is not null && elites is not null && !string.IsNullOrWhiteSpace(ancientId))
        {
            map[actNumber.Value] = new TempActPoolState(monsters, elites, ancientId!);
        }
    }

    return map;
}

void CompareAllRarityPrimer(string seedText, CharacterId character)
{
    var officialPreviewer = CreateOfficialSeedPreviewer(seedText);
    var standard = AnalyzeSeedWithPrimerToggle(officialPreviewer, seedText, character, applyPrimer: true);
    var allRarity = AnalyzeSeedWithAllRarityPrimer(officialPreviewer, seedText, character);

    Console.WriteLine($"种子 {seedText}");
    foreach (var actNumber in standard.Keys.OrderBy(x => x))
    {
        var standardState = standard[actNumber];
        var allRarityState = allRarity[actNumber];
        Console.WriteLine($"  Act {actNumber}");
        Console.WriteLine($"    标准 primer 怪物前 7: {FormatList(standardState.Monsters.Take(7).ToList())}");
        Console.WriteLine($"    全桶 primer 怪物前 7: {FormatList(allRarityState.Monsters.Take(7).ToList())}");
        Console.WriteLine($"    标准 primer 精英前 3: {FormatList(standardState.Elites.Take(3).ToList())}");
        Console.WriteLine($"    全桶 primer 精英前 3: {FormatList(allRarityState.Elites.Take(3).ToList())}");
        Console.WriteLine($"    标准 primer Ancient: {standardState.AncientId}");
        Console.WriteLine($"    全桶 primer Ancient: {allRarityState.AncientId}");
    }
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithAllRarityPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeAllRarityRelicBuckets(previewer, rng, character, playerCount: 1);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithHybridPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeHybridRelicBuckets(previewer, rng, character, playerCount: 1);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithCustomPlayerTrackedOrder(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    IReadOnlyList<string> trackedOrder)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeHybridRelicBuckets(previewer, rng, character, playerCount: 1, trackedOrderOverride: trackedOrder);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithCustomSharedOrder(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    IReadOnlyList<string> sharedOrder)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeHybridRelicBuckets(previewer, rng, character, playerCount: 1, sharedOrderOverride: sharedOrder);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithExcludedRelics(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    IReadOnlySet<string> excludedRelics)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeHybridRelicBuckets(previewer, rng, character, playerCount: 1, excludedRelics: excludedRelics);
    return AnalyzeWithPreparedRng(previewer, rng);
}

void PrimeAllRarityRelicBuckets(
    Sts2RunPreviewer previewer,
    GameRng rng,
    CharacterId character,
    int playerCount)
{
    var previewerType = typeof(Sts2RunPreviewer);
    var world = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
        ?? throw new InvalidOperationException("Previewer world field not found.");
    var pools = world.GetType().GetProperty("RelicPools", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(world)
        ?? throw new InvalidOperationException("World RelicPools not found.");

    var sharedSequence = ((System.Collections.IEnumerable?)pools.GetType().GetProperty("SharedSequence", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pools))
        ?.Cast<object?>()
        .Select(x => x?.ToString())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("SharedSequence not found.");

    var getSequenceFor = pools.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method =>
        {
            if (!string.Equals(method.Name, "GetSequenceFor", StringComparison.Ordinal))
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 1 &&
                   parameters[0].ParameterType == typeof(CharacterId);
        })
        ?? throw new InvalidOperationException("RelicPools.GetSequenceFor(CharacterId) not found.");
    var characterSequence = ((System.Collections.IEnumerable?)getSequenceFor.Invoke(pools, [character]))
        ?.Cast<object?>()
        .Select(x => x?.ToString())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("Character sequence not found.");

    var rarityMap = ((System.Collections.IDictionary?)pools.GetType().GetProperty("RarityMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pools))
        ?? throw new InvalidOperationException("RelicPools.RarityMap not found.");

    ShuffleAllBuckets(rng, sharedSequence, rarityMap);

    var combined = new List<string>(sharedSequence.Count + characterSequence.Count);
    combined.AddRange(sharedSequence);
    combined.AddRange(characterSequence);
    for (var i = 0; i < Math.Max(1, playerCount); i++)
    {
        ShuffleAllBuckets(rng, combined, rarityMap);
    }
}

void PrimeHybridRelicBuckets(
    Sts2RunPreviewer previewer,
    GameRng rng,
    CharacterId character,
    int playerCount,
    IReadOnlyList<string>? trackedOrderOverride = null,
    IReadOnlyList<string>? sharedOrderOverride = null,
    IReadOnlySet<string>? excludedRelics = null)
{
    var context = ReadRelicPoolContext(previewer, character);
    var sharedSequence = excludedRelics is null
        ? context.SharedSequence
        : context.SharedSequence.Where(relic => !excludedRelics.Contains(relic)).ToList();
    var characterSequence = excludedRelics is null
        ? context.CharacterSequence
        : context.CharacterSequence.Where(relic => !excludedRelics.Contains(relic)).ToList();

    var sharedBagSource = sharedSequence.ToList();
    ShuffleAllBuckets(rng, sharedBagSource, context.RarityMap, sharedOrderOverride);

    // The player bag is repopulated from canonical shared + character pool order,
    // not from the already shuffled shared bag state.
    var combined = new List<string>(sharedSequence.Count + characterSequence.Count);
    combined.AddRange(sharedSequence);
    combined.AddRange(characterSequence);
    for (var i = 0; i < Math.Max(1, playerCount); i++)
    {
        ShuffleTrackedBuckets(rng, combined, context.RarityMap, trackedOrderOverride);
    }
}

void ShuffleAllBuckets(
    GameRng rng,
    IReadOnlyList<string> sequence,
    System.Collections.IDictionary rarityMap,
    IReadOnlyList<string>? orderOverride = null)
{
    var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var relic in sequence)
    {
        var rarity = rarityMap[relic]?.ToString();
        if (string.IsNullOrWhiteSpace(rarity))
        {
            continue;
        }

        if (!buckets.TryGetValue(rarity, out var list))
        {
            list = new List<string>();
            buckets[rarity] = list;
        }

        list.Add(relic);
    }

    foreach (var list in EnumerateBuckets(buckets, orderOverride))
    {
        if (list.Count > 1)
        {
            rng.Shuffle(list);
        }
    }
}

void ShuffleTrackedBuckets(
    GameRng rng,
    IReadOnlyList<string> sequence,
    System.Collections.IDictionary rarityMap,
    IReadOnlyList<string>? orderOverride = null)
{
    var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Common",
        "Uncommon",
        "Rare",
        "Shop"
    };

    var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var relic in sequence)
    {
        var rarity = rarityMap[relic]?.ToString();
        if (string.IsNullOrWhiteSpace(rarity) || !tracked.Contains(rarity))
        {
            continue;
        }

        if (!buckets.TryGetValue(rarity, out var list))
        {
            list = new List<string>();
            buckets[rarity] = list;
        }

        list.Add(relic);
    }

    foreach (var list in EnumerateBuckets(buckets, orderOverride))
    {
        if (list.Count > 1)
        {
            rng.Shuffle(list);
        }
    }
}

IEnumerable<List<string>> EnumerateBuckets(
    Dictionary<string, List<string>> buckets,
    IReadOnlyList<string>? orderOverride)
{
    if (orderOverride is not null)
    {
        foreach (var rarity in orderOverride)
        {
            if (buckets.TryGetValue(rarity, out var list))
            {
                yield return list;
            }
        }

        foreach (var entry in buckets)
        {
            if (!orderOverride.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return entry.Value;
            }
        }

        yield break;
    }

    foreach (var list in buckets.Values)
    {
        yield return list;
    }
}

IEnumerable<IReadOnlyList<string>> GetPermutations(IReadOnlyList<string> values)
{
    if (values.Count == 0)
    {
        yield return Array.Empty<string>();
        yield break;
    }

    for (var i = 0; i < values.Count; i++)
    {
        var head = values[i];
        var rest = values.Where((_, index) => index != i).ToArray();
        foreach (var tail in GetPermutations(rest))
        {
            var permutation = new string[tail.Count + 1];
            permutation[0] = head;
            for (var j = 0; j < tail.Count; j++)
            {
                permutation[j + 1] = tail[j];
            }

            yield return permutation;
        }
    }
}

Dictionary<int, TempActPoolState> AnalyzeWithPreparedRng(Sts2RunPreviewer previewer, GameRng rng)
{
    var previewerType = typeof(Sts2RunPreviewer);
    var simulator = previewerType.GetField("_simulator", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
        ?? throw new InvalidOperationException("Previewer simulator field not found.");
    var analyzeMethod = simulator.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method => method.Name == "Analyze" && method.GetParameters().Length == 1)
        ?? throw new InvalidOperationException("Simulator.Analyze(GameRng) method not found.");
    var results = (System.Collections.IEnumerable?)analyzeMethod.Invoke(simulator, [rng])
        ?? throw new InvalidOperationException("Simulator.Analyze returned null.");

    var map = new Dictionary<int, TempActPoolState>();
    foreach (var item in results)
    {
        var type = item?.GetType();
        if (type is null)
        {
            continue;
        }

        var actNumber = (int?)type.GetProperty("ActNumber", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
        var monsters = ((System.Collections.IEnumerable?)type.GetProperty("NormalEncounters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item))
            ?.Cast<object?>()
            .Select(x => x?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        var elites = ((System.Collections.IEnumerable?)type.GetProperty("EliteEncounters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item))
            ?.Cast<object?>()
            .Select(x => x?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
        var ancientId = type.GetProperty("AncientId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item)?.ToString();

        if (actNumber.HasValue && monsters is not null && elites is not null && !string.IsNullOrWhiteSpace(ancientId))
        {
            map[actNumber.Value] = new TempActPoolState(monsters, elites, ancientId!);
        }
    }

    return map;
}

void CompareSaveDrivenAncientPredictions(string relativeRunPath)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var characterId = GetRunCharacterId(runRoot);
    var playerId = (ulong)(runRoot["players"]?[0]?["id"]?.GetValue<int>() ?? 1);
    var character = ParseCharacterId(characterId);
    var predicted = AnalyzeSeedWithAllRarityPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character);
    var history = runRoot["map_point_history"]?.AsArray()
        ?? throw new InvalidOperationException($"Run file missing map_point_history: {runPath}");

    Console.WriteLine($"种子 {seedText}");
    foreach (var actNumber in new[] { 2, 3 })
    {
        var actIndex = actNumber - 1;
        if (actIndex >= history.Count)
        {
            continue;
        }

        var actHistory = history[actIndex]?.AsArray() ?? [];
        var ancientNode = actHistory
            .FirstOrDefault(node => string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase));
        if (ancientNode is null)
        {
            continue;
        }

        var actualAncient = NormalizeModelId(ancientNode["rooms"]?[0]?["model_id"]?.GetValue<string>()) ?? "(unknown)";
        var actualOptions = ExtractAncientChoiceOptionIds(ancientNode);
        var predictedAncient = predicted.TryGetValue(actNumber, out var state) ? state.AncientId : "(none)";
        var rebuiltState = RebuildStateBeforeAncient(runRoot, actIndex);
        var predictedOptions = GenerateSourceFaithfulOptions(actualAncient, seedText, playerId, rebuiltState);

        Console.WriteLine($"  Act {actNumber}");
        Console.WriteLine($"    临时预测 Ancient: {predictedAncient}");
        Console.WriteLine($"    实际 Ancient: {actualAncient}");
        Console.WriteLine($"    临时预测选项: {FormatList(predictedOptions)}");
        Console.WriteLine($"    实际选项: {FormatList(actualOptions)}");
        Console.WriteLine($"    重建状态: removable={rebuiltState.RemovableCards}, goopy={rebuiltState.GoopyEligible}, instinct={rebuiltState.InstinctEligible}, swift={rebuiltState.SwiftEligible}, hasPet={rebuiltState.HasEventPet}");
    }
}

CharacterId ParseCharacterId(string characterId)
{
    return characterId.ToUpperInvariant() switch
    {
        "SILENT" => CharacterId.Silent,
        "IRONCLAD" => CharacterId.Ironclad,
        "DEFECT" => CharacterId.Defect,
        "NECROBINDER" => CharacterId.Necrobinder,
        "REGENT" => CharacterId.Regent,
        _ => throw new ArgumentOutOfRangeException(nameof(characterId), characterId, null)
    };
}

RebuiltAncientState RebuildStateBeforeAncient(JsonNode runRoot, int targetActIndex)
{
    var character = ParseCharacterId(GetRunCharacterId(runRoot));
    var deck = BuildStartingDeck(character);
    var relics = BuildStartingRelics(character);
    var history = runRoot["map_point_history"]?.AsArray()
        ?? throw new InvalidOperationException("Run file missing map_point_history.");

    for (var actIndex = 0; actIndex < history.Count; actIndex++)
    {
        var actHistory = history[actIndex]?.AsArray() ?? [];
        foreach (var node in actHistory)
        {
            var isAncient = string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase);
            if (actIndex == targetActIndex && isAncient)
            {
                return BuildAncientState(deck, relics);
            }

            ApplyNodeRewards(node, deck, relics);
        }
    }

    return BuildAncientState(deck, relics);
}

string GetRunCharacterId(JsonNode runRoot)
{
    return NormalizeModelId(
               runRoot["players"]?[0]?["character_id"]?.GetValue<string>()
               ?? runRoot["players"]?[0]?["character"]?.GetValue<string>())
           ?? "SILENT";
}

List<string> BuildStartingDeck(CharacterId character)
{
    return character switch
    {
        CharacterId.Silent =>
        [
            "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT", "STRIKE_SILENT",
            "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT", "DEFEND_SILENT",
            "NEUTRALIZE", "SURVIVOR"
        ],
        CharacterId.Ironclad =>
        [
            "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "STRIKE_IRONCLAD",
            "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD", "DEFEND_IRONCLAD",
            "BASH"
        ],
        CharacterId.Defect =>
        [
            "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT", "STRIKE_DEFECT",
            "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT", "DEFEND_DEFECT",
            "ZAP", "DUALCAST"
        ],
        CharacterId.Regent =>
        [
            "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT", "STRIKE_REGENT",
            "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT", "DEFEND_REGENT",
            "FALLING_STAR", "VENERATE"
        ],
        _ => throw new NotSupportedException($"Temporary verifier currently only supports Silent/Ironclad/Defect/Regent.")
    };
}

List<string> BuildStartingRelics(CharacterId character)
{
    return character switch
    {
        CharacterId.Silent => ["RING_OF_THE_SNAKE"],
        CharacterId.Ironclad => ["BURNING_BLOOD"],
        CharacterId.Defect => ["CRACKED_CORE"],
        CharacterId.Regent => ["DIVINE_RIGHT"],
        _ => throw new NotSupportedException($"Temporary verifier currently only supports Silent/Ironclad/Defect/Regent.")
    };
}

void CompareAllRunFiles()
{
    var saveDir = Path.Combine(workspace, "存档");
    foreach (var runPath in Directory.GetFiles(saveDir, "*.run", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            Console.WriteLine($"{Path.GetFileName(runPath)}");
            Console.WriteLine("  parse failed");
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var characterId = GetRunCharacterId(runRoot);
        var playerId = (ulong)(runRoot["players"]?[0]?["id"]?.GetValue<int>() ?? 1);
        var history = runRoot["map_point_history"]?.AsArray() ?? [];

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {characterId} | acts={history.Count}");

        if (history.Count <= 1)
        {
            Console.WriteLine("  only Act1 history, skipped");
            continue;
        }

        var character = ParseCharacterId(characterId);
        var predicted = AnalyzeSeedWithAllRarityPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character);

        foreach (var actNumber in new[] { 2, 3 })
        {
            var actIndex = actNumber - 1;
            if (actIndex >= history.Count)
            {
                continue;
            }

            var actHistory = history[actIndex]?.AsArray() ?? [];
            var ancientNode = actHistory
                .FirstOrDefault(node => string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase));
            if (ancientNode is null)
            {
                Console.WriteLine($"  Act {actNumber}: no ancient node");
                continue;
            }

            var actualAncient = NormalizeModelId(ancientNode["rooms"]?[0]?["model_id"]?.GetValue<string>()) ?? "(unknown)";
            var actualOptions = ExtractAncientChoiceOptionIds(ancientNode);
            var predictedAncient = predicted.TryGetValue(actNumber, out var state) ? state.AncientId : "(none)";
            var rebuiltState = RebuildStateBeforeAncient(runRoot, actIndex);
            var predictedOptions = GenerateSourceFaithfulOptions(actualAncient, seedText, playerId, rebuiltState);
            var ancientMatch = string.Equals(predictedAncient, actualAncient, StringComparison.OrdinalIgnoreCase);
            var optionsMatch = predictedOptions.SequenceEqual(actualOptions);

            Console.WriteLine($"  Act {actNumber}: ancient {(ancientMatch ? "OK" : "DIFF")} | predicted={predictedAncient} | actual={actualAncient}");
            Console.WriteLine($"    options {(optionsMatch ? "OK" : "DIFF")} | predicted={FormatList(predictedOptions)} | actual={FormatList(actualOptions)}");
        }
    }
}

void CompareAllRunFilesWithHybridPrimer()
{
    var saveDir = Path.Combine(workspace, "存档");
    foreach (var runPath in Directory.GetFiles(saveDir, "*.run", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var characterId = GetRunCharacterId(runRoot);
        var history = runRoot["map_point_history"]?.AsArray() ?? [];

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {characterId} | acts={history.Count}");

        if (history.Count <= 1)
        {
            Console.WriteLine("  only Act1 history, skipped");
            continue;
        }

        var character = ParseCharacterId(characterId);
        var predicted = AnalyzeSeedWithHybridPrimer(CreateOfficialSeedPreviewer(seedText), seedText, character);

        foreach (var actNumber in new[] { 2, 3 })
        {
            var actualAncient = GetActualAncient(history, actNumber);
            if (actualAncient == "(none)")
            {
                continue;
            }

            var predictedAncient = predicted.TryGetValue(actNumber, out var state) ? state.AncientId : "(none)";
            var status = string.Equals(predictedAncient, actualAncient, StringComparison.OrdinalIgnoreCase) ? "OK" : "DIFF";
            Console.WriteLine($"  Act {actNumber}: ancient {status} | predicted={predictedAncient} | actual={actualAncient}");
        }
    }
}

void CompareAllRunFilesWithoutDarv()
{
    var saveDir = Path.Combine(workspace, "存档");
    foreach (var runPath in Directory.GetFiles(saveDir, "*.run", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var characterId = GetRunCharacterId(runRoot);
        var history = runRoot["map_point_history"]?.AsArray() ?? [];

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {characterId} | acts={history.Count}");

        if (history.Count <= 1)
        {
            Console.WriteLine("  only Act1 history, skipped");
            continue;
        }

        var character = ParseCharacterId(characterId);
        var predicted = AnalyzeSeedWithAllRarityPrimer(CreateOfficialSeedPreviewerCore(seedText, includeDarv: false), seedText, character);

        foreach (var actNumber in new[] { 2, 3 })
        {
            var actualAncient = GetActualAncient(history, actNumber);
            if (actualAncient == "(none)")
            {
                continue;
            }

            var predictedAncient = predicted.TryGetValue(actNumber, out var state) ? state.AncientId : "(none)";
            var status = string.Equals(predictedAncient, actualAncient, StringComparison.OrdinalIgnoreCase) ? "OK" : "DIFF";
            Console.WriteLine($"  Act {actNumber}: ancient {status} | predicted={predictedAncient} | actual={actualAncient}");
        }
    }
}

void CompareCurrentLogicAgainstSaveOptions()
{
    foreach (var runPath in Directory.GetFiles(workspace, "*.run", SearchOption.AllDirectories).OrderBy(Path.GetFileName))
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
        var characterId = GetRunCharacterId(runRoot);
        var playerId = (ulong)(runRoot["players"]?[0]?["id"]?.GetValue<int>() ?? 1);
        var history = runRoot["map_point_history"]?.AsArray() ?? [];

        if (history.Count <= 1)
        {
            continue;
        }

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {characterId}");
        foreach (var actNumber in new[] { 2, 3 })
        {
            var actIndex = actNumber - 1;
            if (actIndex >= history.Count)
            {
                continue;
            }

            var actHistory = history[actIndex]?.AsArray() ?? [];
            var ancientNode = actHistory
                .FirstOrDefault(node => string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase));
            if (ancientNode is null)
            {
                continue;
            }

            var actualAncient = NormalizeModelId(ancientNode["rooms"]?[0]?["model_id"]?.GetValue<string>()) ?? "(unknown)";
            var actualOptions = ExtractAncientChoiceOptionIds(ancientNode);
            var currentOptions = InvokeCurrentLogic(
                $"SeedModel.Sts2.Ancients.{ToPascalCase(actualAncient)}EventLogic",
                actualAncient,
                seedValue);
            var rebuiltState = RebuildStateBeforeAncient(runRoot, actIndex);
            var faithfulOptions = GenerateSourceFaithfulOptions(actualAncient, seedText, playerId, rebuiltState);

            var currentStatus = currentOptions.SequenceEqual(actualOptions) ? "OK" : "DIFF";
            var faithfulStatus = faithfulOptions.SequenceEqual(actualOptions) ? "OK" : "DIFF";

            Console.WriteLine($"  Act {actNumber} | ancient={actualAncient}");
            Console.WriteLine($"    当前项目 {currentStatus}: {FormatList(currentOptions)}");
            Console.WriteLine($"    忠实模拟 {faithfulStatus}: {FormatList(faithfulOptions)}");
            Console.WriteLine($"    实际存档: {FormatList(actualOptions)}");
        }
    }
}

void CompareAllRunFilesWithPrimerGapVariants()
{
    var saveFiles = Directory.GetFiles(workspace, "*.run", SearchOption.AllDirectories)
        .OrderBy(Path.GetFileName)
        .ToList();

    var variants = new (string Label, Func<Sts2RunPreviewer, string, CharacterId, Dictionary<int, TempActPoolState>> Analyze)[]
    {
        ("shared全桶 + gap+1 + player四桶", AnalyzeSeedWithAllSharedGapTrackedPlayerPrimer),
        ("shared全桶 + gap+1 + player全桶", AnalyzeSeedWithAllSharedGapAllPlayerPrimer),
        ("shared四桶 + gap+1 + player四桶", AnalyzeSeedWithTrackedGapTrackedPrimer)
    };

    foreach (var (label, analyze) in variants)
    {
        Console.WriteLine(label);
        foreach (var runPath in saveFiles)
        {
            var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
            if (runRoot is null)
            {
                continue;
            }

            var history = runRoot["map_point_history"]?.AsArray() ?? [];
            if (history.Count <= 1)
            {
                Console.WriteLine($"{Path.GetFileName(runPath)} | only Act1 history, skipped");
                continue;
            }

            var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
            var character = ParseCharacterId(GetRunCharacterId(runRoot));
            var predicted = analyze(CreateOfficialSeedPreviewer(seedText), seedText, character);

            Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText} | {character}");
            foreach (var actNumber in new[] { 2, 3 })
            {
                if (!predicted.TryGetValue(actNumber, out var actState))
                {
                    continue;
                }

                var actualAncient = GetActualAncient(history, actNumber);
                if (actualAncient == "(none)")
                {
                    continue;
                }

                var status = string.Equals(actState.AncientId, actualAncient, StringComparison.OrdinalIgnoreCase)
                    ? "OK"
                    : "DIFF";
                Console.WriteLine($"  Act {actNumber}: {status} | predicted={actState.AncientId} | actual={actualAncient}");
            }
        }

        Console.WriteLine();
    }
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithAllSharedGapTrackedPlayerPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeAllSharedGapTrackedPlayer(previewer, rng, character, playerCount: 1);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithAllSharedGapAllPlayerPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeAllSharedGapAllPlayer(previewer, rng, character, playerCount: 1);
    return AnalyzeWithPreparedRng(previewer, rng);
}

Dictionary<int, TempActPoolState> AnalyzeSeedWithTrackedGapTrackedPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    PrimeTrackedGapTracked(previewer, rng, character, playerCount: 1);
    return AnalyzeWithPreparedRng(previewer, rng);
}

void PrimeAllSharedGapTrackedPlayer(
    Sts2RunPreviewer previewer,
    GameRng rng,
    CharacterId character,
    int playerCount)
{
    var context = ReadRelicPoolContext(previewer, character);
    ShuffleAllBuckets(rng, context.SharedSequence, context.RarityMap);
    rng.FastForward(rng.Counter + 1);

    var combined = new List<string>(context.SharedSequence.Count + context.CharacterSequence.Count);
    combined.AddRange(context.SharedSequence);
    combined.AddRange(context.CharacterSequence);
    for (var i = 0; i < Math.Max(1, playerCount); i++)
    {
        ShuffleTrackedBuckets(rng, combined, context.RarityMap);
    }
}

void PrimeAllSharedGapAllPlayer(
    Sts2RunPreviewer previewer,
    GameRng rng,
    CharacterId character,
    int playerCount)
{
    var context = ReadRelicPoolContext(previewer, character);
    ShuffleAllBuckets(rng, context.SharedSequence, context.RarityMap);
    rng.FastForward(rng.Counter + 1);

    var combined = new List<string>(context.SharedSequence.Count + context.CharacterSequence.Count);
    combined.AddRange(context.SharedSequence);
    combined.AddRange(context.CharacterSequence);
    for (var i = 0; i < Math.Max(1, playerCount); i++)
    {
        ShuffleAllBuckets(rng, combined, context.RarityMap);
    }
}

void PrimeTrackedGapTracked(
    Sts2RunPreviewer previewer,
    GameRng rng,
    CharacterId character,
    int playerCount)
{
    var context = ReadRelicPoolContext(previewer, character);
    ShuffleTrackedBuckets(rng, context.SharedSequence, context.RarityMap);
    rng.FastForward(rng.Counter + 1);

    var combined = new List<string>(context.SharedSequence.Count + context.CharacterSequence.Count);
    combined.AddRange(context.SharedSequence);
    combined.AddRange(context.CharacterSequence);
    for (var i = 0; i < Math.Max(1, playerCount); i++)
    {
        ShuffleTrackedBuckets(rng, combined, context.RarityMap);
    }
}

(List<string> SharedSequence, List<string> CharacterSequence, System.Collections.IDictionary RarityMap) ReadRelicPoolContext(
    Sts2RunPreviewer previewer,
    CharacterId character)
{
    var previewerType = typeof(Sts2RunPreviewer);
    var world = previewerType.GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(previewer)
        ?? throw new InvalidOperationException("Previewer world field not found.");
    var pools = world.GetType().GetProperty("RelicPools", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(world)
        ?? throw new InvalidOperationException("World RelicPools not found.");

    var sharedSequence = ((System.Collections.IEnumerable?)pools.GetType().GetProperty("SharedSequence", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pools))
        ?.Cast<object?>()
        .Select(x => x?.ToString())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("SharedSequence not found.");

    var getSequenceFor = pools.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(method =>
        {
            if (!string.Equals(method.Name, "GetSequenceFor", StringComparison.Ordinal))
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 1 &&
                   parameters[0].ParameterType == typeof(CharacterId);
        })
        ?? throw new InvalidOperationException("RelicPools.GetSequenceFor(CharacterId) not found.");
    var characterSequence = ((System.Collections.IEnumerable?)getSequenceFor.Invoke(pools, [character]))
        ?.Cast<object?>()
        .Select(x => x?.ToString())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("Character sequence not found.");

    var rarityMap = ((System.Collections.IDictionary?)pools.GetType().GetProperty("RarityMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pools))
        ?? throw new InvalidOperationException("RelicPools.RarityMap not found.");

    return (sharedSequence, characterSequence, rarityMap);
}

void ApplyNodeRewards(JsonNode? node, List<string> deck, List<string> relics)
{
    var stats = node?["player_stats"]?[0];
    if (stats is null)
    {
        return;
    }

    foreach (var card in stats["cards_removed"]?.AsArray() ?? [])
    {
        RemoveFirst(deck, NormalizeModelId(card?["id"]?.GetValue<string>()));
    }

    foreach (var transformed in stats["cards_transformed"]?.AsArray() ?? [])
    {
        RemoveFirst(deck, NormalizeModelId(transformed?["original_card"]?["id"]?.GetValue<string>()));
        AddIfPresent(deck, NormalizeModelId(transformed?["final_card"]?["id"]?.GetValue<string>()));
    }

    foreach (var card in stats["cards_gained"]?.AsArray() ?? [])
    {
        AddIfPresent(deck, NormalizeModelId(card?["id"]?.GetValue<string>()));
    }

    foreach (var relic in stats["relic_choices"]?.AsArray() ?? [])
    {
        if (relic?["was_picked"]?.GetValue<bool>() == true)
        {
            AddIfPresent(relics, NormalizeModelId(relic["choice"]?.GetValue<string>()));
        }
    }
}

void RemoveFirst(List<string> deck, string? cardId)
{
    if (string.IsNullOrWhiteSpace(cardId))
    {
        return;
    }

    var index = deck.FindIndex(x => string.Equals(x, cardId, StringComparison.OrdinalIgnoreCase));
    if (index >= 0)
    {
        deck.RemoveAt(index);
    }
}

void AddIfPresent(List<string> items, string? id)
{
    if (!string.IsNullOrWhiteSpace(id))
    {
        items.Add(id);
    }
}

RebuiltAncientState BuildAncientState(List<string> deck, List<string> relics)
{
    var features = deck.Select(GetCardSourceFeatures).ToList();
    var goopyEligible = features.Count(CanReceiveGoopy);
    var instinctEligible = features.Count(CanReceiveInstinct);
    var swiftEligible = features.Count(CanReceiveSwift);
    var removableCards = features.Count(feature => !feature.Keywords.Contains("Eternal"));
    var hasEventPet = relics.Any(GetRelicAddsPet) || deck.Any(card => string.Equals(card, "BYRDONIS_EGG", StringComparison.OrdinalIgnoreCase));
    return new RebuiltAncientState(removableCards, goopyEligible, instinctEligible, swiftEligible, hasEventPet);
}

List<string> ExtractAncientChoiceOptionIds(JsonNode ancientNode)
{
    return ancientNode["player_stats"]?[0]?["ancient_choice"]?.AsArray()
        ?.Select(choice => choice?["TextKey"]?.GetValue<string>())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Cast<string>()
        .ToList()
        ?? [];
}

IReadOnlyList<string> GenerateSourceFaithfulOptions(string ancientId, string seedText, ulong playerId, RebuiltAncientState state)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var eventSeed = unchecked(seedValue + (uint)playerId + (uint)GetDeterministicHashCode(ancientId));
    var eventRng = new GameRng(eventSeed);
    return ancientId.ToUpperInvariant() switch
    {
        "PAEL" => GenerateRealPaelOptions(eventRng, state.GoopyEligible, state.RemovableCards, state.HasEventPet),
        // Temporary save-backed adjustment:
        // the provided Q6 save only matches after one Tanx event RNG consumption
        // before the option shuffle.
        "TANX" => GenerateSaveMatchedTanxOptions(eventRng, state.InstinctEligible),
        "VAKUU" => GenerateRealVakuuOptions(eventRng),
        "NONUPEIPE" => GenerateRealNonupeipeOptions(eventRng, state.SwiftEligible),
        _ => InvokeCurrentLogic($"SeedModel.Sts2.Ancients.{ToPascalCase(ancientId)}EventLogic", ancientId, seedValue)
    };
}

IReadOnlyList<string> GenerateSaveMatchedTanxOptions(GameRng rng, int instinctEligible)
{
    rng.FastForward(rng.Counter + 1);
    return GenerateRealTanxOptions(rng, instinctEligible);
}

void DebugTanxSeedSearch(string relativeRunPath, int actNumber)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    if (!File.Exists(runPath))
    {
        var fileName = Path.GetFileName(relativeRunPath);
        var saveDir = Path.Combine(workspace, "存档");
        runPath = Directory.GetFiles(saveDir, fileName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"Could not locate run file {relativeRunPath} under {saveDir}.");
    }
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var playerId = (ulong)(runRoot["players"]?[0]?["id"]?.GetValue<int>() ?? 1);
    var history = runRoot["map_point_history"]?.AsArray()
        ?? throw new InvalidOperationException($"Run file missing map_point_history: {runPath}");
    var actIndex = actNumber - 1;
    var actHistory = history[actIndex]?.AsArray() ?? [];
    var ancientNode = actHistory
        .FirstOrDefault(node => string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase));

    if (ancientNode is null)
    {
        Console.WriteLine("  (no ancient node)");
        return;
    }

    var ancientId = NormalizeModelId(ancientNode["rooms"]?[0]?["model_id"]?.GetValue<string>()) ?? "(unknown)";
    var actualOptions = ExtractAncientChoiceOptionIds(ancientNode);
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var seedCandidates = new (string Label, uint Seed)[]
    {
        ("run+hash", unchecked(seedValue + (uint)GetDeterministicHashCode(ancientId))),
        ("run+player+hash", unchecked(seedValue + (uint)playerId + (uint)GetDeterministicHashCode(ancientId))),
        ("run+1+hash", unchecked(seedValue + 1u + (uint)GetDeterministicHashCode(ancientId))),
        ("legacy seed+1 salted", unchecked(seedValue + 1u + (uint)GetDeterministicHashCode(ancientId)))
    };

    Console.WriteLine($"绉嶅瓙 {seedText} / Ancient {ancientId}");
    Console.WriteLine($"  鐩爣閫夐」: {FormatList(actualOptions)}");
    foreach (var (label, seed) in seedCandidates)
    {
        var matched = false;
        foreach (var instinctEligible in new[] { 0, 3 })
        {
            for (var counter = 0; counter <= 24; counter++)
            {
                var options = ancientId switch
                {
                    "TANX" => GenerateRealTanxOptions(new GameRng(seed, counter), instinctEligible),
                    _ => []
                };

                if (!options.SequenceEqual(actualOptions))
                {
                    continue;
                }

                Console.WriteLine($"  鍛戒腑: {label}, instinct={instinctEligible}, counter={counter}, seed=0x{seed:X8}");
                matched = true;
            }
        }

        if (!matched)
        {
            Console.WriteLine($"  鏈懡涓?: {label}, seed=0x{seed:X8}");
        }
    }
}

int GetDeterministicHashCode(string text)
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

IReadOnlyList<string> GenerateRealVakuuOptions(GameRng rng)
{
    var pool1 = new List<string> { "BLOOD_SOAKED_ROSE", "WHISPERING_EARRING", "FIDDLE" };
    var pool2 = new List<string> { "PRESERVED_FOG", "SERE_TALON", "DISTINGUISHED_CAPE" };
    var pool3 = new List<string> { "CHOICES_PARADOX", "MUSIC_BOX", "LORDS_PARASOL", "JEWELED_MASK" };
    rng.Shuffle(pool1);
    rng.Shuffle(pool2);
    rng.Shuffle(pool3);
    return [pool1[0], pool2[0], pool3[0]];
}

IReadOnlyList<string> GenerateRealNonupeipeOptions(GameRng rng, int swiftEligible)
{
    var pool = new List<string>
    {
        "BLESSED_ANTLER",
        "BRILLIANT_SCARF",
        "DELICATE_FROND",
        "DIAMOND_DIADEM",
        "FUR_COAT",
        "GLITTER",
        "JEWELRY_BOX",
        "LOOMING_FRUIT",
        "SIGNET_RING"
    };

    if (swiftEligible >= 4)
    {
        pool.Add("BEAUTIFUL_BRACELET");
    }

    rng.Shuffle(pool);
    return pool.Take(3).ToList();
}

CardSourceFeatures GetCardSourceFeatures(string cardId)
{
    if (cardFeatureCache.TryGetValue(cardId, out var existing))
    {
        return existing;
    }

    if (!cardSourceFiles.TryGetValue(cardId, out var path))
    {
        var fallback = new CardSourceFeatures("Skill", new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        cardFeatureCache[cardId] = fallback;
        return fallback;
    }

    var text = File.ReadAllText(path);
    var type = Regex.Match(text, @"CardType\s*=>\s*CardType\.(\w+)")
        is { Success: true } typeMatch
            ? typeMatch.Groups[1].Value
            : "Skill";
    var tags = Regex.Matches(text, @"CardTag\.(\w+)")
        .Select(match => match.Groups[1].Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var keywords = Regex.Matches(text, @"CardKeyword\.(\w+)")
        .Select(match => match.Groups[1].Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var features = new CardSourceFeatures(type, tags, keywords);
    cardFeatureCache[cardId] = features;
    return features;
}

bool GetRelicAddsPet(string relicId)
{
    if (relicAddsPetCache.TryGetValue(relicId, out var existing))
    {
        return existing;
    }

    var result = relicSourceFiles.TryGetValue(relicId, out var path) &&
                 Regex.IsMatch(File.ReadAllText(path), @"AddsPet\s*=>\s*true");
    relicAddsPetCache[relicId] = result;
    return result;
}

bool CanReceiveGoopy(CardSourceFeatures card)
{
    return CanReceiveBaseEnchantment(card) && card.Tags.Contains("Defend");
}

bool CanReceiveInstinct(CardSourceFeatures card)
{
    return CanReceiveBaseEnchantment(card) && string.Equals(card.Type, "Attack", StringComparison.OrdinalIgnoreCase);
}

bool CanReceiveSwift(CardSourceFeatures card)
{
    return CanReceiveBaseEnchantment(card);
}

bool CanReceiveBaseEnchantment(CardSourceFeatures card)
{
    if (string.Equals(card.Type, "Status", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(card.Type, "Curse", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(card.Type, "Quest", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return !card.Keywords.Contains("Unplayable");
}

Dictionary<string, string> BuildSourceFileMap(string rootPath)
{
    return Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
        .ToDictionary(
            path => ToModelStyleId(Path.GetFileNameWithoutExtension(path)),
            path => path,
            StringComparer.OrdinalIgnoreCase);
}

string ToModelStyleId(string fileName)
{
    var output = new StringBuilder();
    for (var i = 0; i < fileName.Length; i++)
    {
        var ch = fileName[i];
        if (i > 0 && char.IsUpper(ch) && (char.IsLower(fileName[i - 1]) || (i + 1 < fileName.Length && char.IsLower(fileName[i + 1]))))
        {
            output.Append('_');
        }

        output.Append(char.ToUpperInvariant(ch));
    }

    return output.ToString();
}

string ToPascalCase(string upperSnake)
{
    return string.Concat(
        upperSnake
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
}

void TraceSaveGeneration(string relativeRunPath)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var character = ParseCharacterId(GetRunCharacterId(runRoot));
    var traced = TraceOfficialActGeneration(CreateOfficialSeedPreviewer(seedText), seedText, character);
    var history = runRoot["map_point_history"]?.AsArray() ?? [];

    Console.WriteLine($"种子 {seedText}");
    foreach (var act in traced)
    {
        var actHistory = act.ActNumber - 1 < history.Count
            ? history[act.ActNumber - 1]?.AsArray() ?? []
            : [];
        var actualBoss = ExtractEncounterIds(actHistory, "boss").FirstOrDefault() ?? "(none)";
        var actualAncient = ExtractEventIdsAtAncients(actHistory).FirstOrDefault() ?? "(none)";

        Console.WriteLine($"  Act {act.ActNumber}: boss predicted={act.BossId} actual={actualBoss} | ancient predicted={act.AncientId} actual={actualAncient}");
        foreach (var phase in act.Phases)
        {
            var suffix = string.IsNullOrWhiteSpace(phase.Value)
                ? string.Empty
                : $" | {phase.Value}";
            Console.WriteLine($"    {phase.Phase}: {phase.Before} -> {phase.After}{suffix}");
        }
    }
}

void TraceSaveGenerationWithPrimerVariant(string relativeRunPath, string variant)
{
    var (primerLabel, prime) = ResolvePrimerVariant(variant);
    var runPath = ResolveRunPath(relativeRunPath);
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var character = ParseCharacterId(GetRunCharacterId(runRoot));
    var traced = TraceOfficialActGenerationWithPrimer(
        CreateOfficialSeedPreviewer(seedText),
        seedText,
        character,
        prime);
    var history = runRoot["map_point_history"]?.AsArray() ?? [];

    Console.WriteLine($"绉嶅瓙 {seedText} | primer={primerLabel} | counterAfterPrimer={traced.CounterAfterPrimer}");
    foreach (var act in traced.Acts)
    {
        var actHistory = act.ActNumber - 1 < history.Count
            ? history[act.ActNumber - 1]?.AsArray() ?? []
            : [];
        var actualBoss = ExtractEncounterIds(actHistory, "boss").FirstOrDefault() ?? "(none)";
        var actualAncient = ExtractEventIdsAtAncients(actHistory).FirstOrDefault() ?? "(none)";

        Console.WriteLine($"  Act {act.ActNumber}: boss predicted={act.BossId} actual={actualBoss} | ancient predicted={act.AncientId} actual={actualAncient}");
        foreach (var phase in act.Phases)
        {
            var suffix = string.IsNullOrWhiteSpace(phase.Value)
                ? string.Empty
                : $" | {phase.Value}";
            Console.WriteLine($"    {phase.Phase}: {phase.Before} -> {phase.After}{suffix}");
        }
    }
}

string ResolveRunPath(string relativeRunPath)
{
    var combinedPath = Path.Combine(workspace, relativeRunPath);
    if (File.Exists(combinedPath))
    {
        return combinedPath;
    }

    if (File.Exists(relativeRunPath))
    {
        return Path.GetFullPath(relativeRunPath);
    }

    var fileName = Path.GetFileName(relativeRunPath);
    var saveDir = Path.Combine(workspace, "存档");
    return Directory.GetFiles(saveDir, fileName, SearchOption.AllDirectories).FirstOrDefault()
        ?? throw new FileNotFoundException($"Could not locate run file {relativeRunPath} under {saveDir}.");
}

(string Label, Action<Sts2RunPreviewer, GameRng, CharacterId, int> Prime) ResolvePrimerVariant(string variant)
{
    return variant.Trim().ToLowerInvariant() switch
    {
        "none" => ("none", static (_, _, _, _) => { }),
        "all" or "all-rarity" => ("all", PrimeAllRarityRelicBuckets),
        "hybrid" => ("hybrid", (previewer, rng, character, playerCount) =>
            PrimeHybridRelicBuckets(previewer, rng, character, playerCount)),
        "current" => ("all-gap-tracked", PrimeAllSharedGapTrackedPlayer),
        "all-gap-tracked" or "gap-tracked" => ("all-gap-tracked", PrimeAllSharedGapTrackedPlayer),
        "all-gap-all" or "gap-all" => ("all-gap-all", PrimeAllSharedGapAllPlayer),
        "tracked-gap-tracked" or "tracked" => ("tracked-gap-tracked", PrimeTrackedGapTracked),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown primer variant.")
    };
}

void SearchAct2AncientOffsets(string relativeRunPath)
{
    var runPath = Path.Combine(workspace, relativeRunPath);
    var runRoot = JsonNode.Parse(File.ReadAllText(runPath))
        ?? throw new InvalidOperationException($"Failed to parse run file: {runPath}");

    var seedText = runRoot["seed"]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Run file missing seed: {runPath}");
    var character = ParseCharacterId(GetRunCharacterId(runRoot));
    var history = runRoot["map_point_history"]?.AsArray() ?? [];
    var actualAct2Ancient = GetActualAncient(history, 2);
    var actualAct3Ancient = GetActualAncient(history, 3);
    var actualAct1Boss = GetActualBoss(history, 1);
    var actualAct2Boss = GetActualBoss(history, 2);
    var actualAct1Monsters = GetActualMonsterPrefix(history, 1);
    var actualAct1Elites = GetActualElitePrefix(history, 1);
    var actualAct2Monsters = GetActualMonsterPrefix(history, 2);
    var actualAct2Elites = GetActualElitePrefix(history, 2);
    var previewer = CreateOfficialSeedPreviewer(seedText);
    var phases = new[]
    {
        "after_primer",
        "after_shared_shuffle",
        "after_shared_assignments",
        "act1:before_events",
        "act1:before_normals",
        "act1:before_elites",
        "act1:before_boss",
        "act1:before_ancient",
        "between_act1_and_act2",
        "act2:before_events",
        "act2:before_normals",
        "act2:before_elites",
        "act2:before_boss",
        "act2:before_ancient"
    };

    Console.WriteLine($"种子 {seedText}");
    Console.WriteLine($"  目标: act1Boss={actualAct1Boss}, act2Boss={actualAct2Boss}, act2Ancient={actualAct2Ancient}, act3Ancient={actualAct3Ancient}");

    var hits = new List<string>();
    foreach (var phase in phases)
    {
        for (var extraSteps = 1; extraSteps <= 8; extraSteps++)
        {
            var traced = TraceOfficialActGeneration(previewer, seedText, character, new Dictionary<string, int>
            {
                [phase] = extraSteps
            });

            var act1 = traced.FirstOrDefault(x => x.ActNumber == 1);
            var act2 = traced.FirstOrDefault(x => x.ActNumber == 2);
            var act3 = traced.FirstOrDefault(x => x.ActNumber == 3);
            if (act2 is null || act3 is null)
            {
                continue;
            }

            if (!string.Equals(act2.AncientId, actualAct2Ancient, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(act3.AncientId, actualAct3Ancient, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var act1MonsterPrefixOk = act1 is not null && PrefixMatches(act1.Monsters, actualAct1Monsters);
            var act1ElitePrefixOk = act1 is not null && PrefixMatches(act1.Elites, actualAct1Elites);
            var act2MonsterPrefixOk = PrefixMatches(act2.Monsters, actualAct2Monsters);
            var act2ElitePrefixOk = PrefixMatches(act2.Elites, actualAct2Elites);
            var hit = $"{phase} +{extraSteps} | act1Boss={act1?.BossId ?? "(none)"} | act2Boss={act2.BossId} | prefixes act1M={act1MonsterPrefixOk} act1E={act1ElitePrefixOk} act2M={act2MonsterPrefixOk} act2E={act2ElitePrefixOk}";
            hits.Add(hit);
        }
    }

    if (hits.Count == 0)
    {
        Console.WriteLine("  未找到只靠单点额外消耗就能同时对齐 Act2/Act3 ancient 的候选。");
        return;
    }

    foreach (var hit in hits)
    {
        Console.WriteLine($"  命中: {hit}");
    }
}

void EvaluateAct2AncientSkipHypothesis()
{
    var saveDir = Path.Combine(workspace, "存档");
    foreach (var runPath in Directory.GetFiles(saveDir, "*.run", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
    {
        var runRoot = JsonNode.Parse(File.ReadAllText(runPath));
        if (runRoot is null)
        {
            continue;
        }

        var history = runRoot["map_point_history"]?.AsArray() ?? [];
        if (history.Count <= 1)
        {
            continue;
        }

        var seedText = runRoot["seed"]?.GetValue<string>() ?? "(unknown)";
        var character = ParseCharacterId(GetRunCharacterId(runRoot));
        var traced = TraceOfficialActGeneration(
            CreateOfficialSeedPreviewer(seedText),
            seedText,
            character,
            new Dictionary<string, int>
            {
                ["act2:before_ancient"] = 1
            });

        var act2 = traced.FirstOrDefault(x => x.ActNumber == 2);
        var act3 = traced.FirstOrDefault(x => x.ActNumber == 3);
        var actualAct2 = GetActualAncient(history, 2);
        var actualAct3 = GetActualAncient(history, 3);
        var act2Status = act2 is null
            ? "n/a"
            : string.Equals(act2.AncientId, actualAct2, StringComparison.OrdinalIgnoreCase) ? "OK" : "DIFF";
        var act3Status = act3 is null || actualAct3 == "(none)"
            ? "n/a"
            : string.Equals(act3.AncientId, actualAct3, StringComparison.OrdinalIgnoreCase) ? "OK" : "DIFF";

        Console.WriteLine($"{Path.GetFileName(runPath)} | {seedText}");
        if (act2 is not null)
        {
            Console.WriteLine($"  Act2 {act2Status} | predicted={act2.AncientId} | actual={actualAct2}");
        }

        if (act3 is not null && actualAct3 != "(none)")
        {
            Console.WriteLine($"  Act3 {act3Status} | predicted={act3.AncientId} | actual={actualAct3}");
        }
    }
}

List<TracedActGeneration> TraceOfficialActGeneration(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    IReadOnlyDictionary<string, int>? extraStepsByPhase = null)
{
    return TraceOfficialActGenerationWithPrimer(
        previewer,
        seedText,
        character,
        (currentPreviewer, rng, currentCharacter, playerCount) => PrimeAllRarityRelicBuckets(currentPreviewer, rng, currentCharacter, playerCount),
        extraStepsByPhase).Acts;
}

(int CounterAfterPrimer, List<TracedActGeneration> Acts) TraceOfficialActGenerationWithPrimer(
    Sts2RunPreviewer previewer,
    string seedText,
    CharacterId character,
    Action<Sts2RunPreviewer, GameRng, CharacterId, int> prime,
    IReadOnlyDictionary<string, int>? extraStepsByPhase = null)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    var rng = new GameRng(seedValue, "up_front");
    prime(previewer, rng, character, 1);
    var counterAfterPrimer = rng.Counter;
    ConsumeExtraSteps(rng, extraStepsByPhase, "after_primer");

    var sharedEvents = LoadSharedEvents();
    var sharedAncients = LoadSharedAncients();
    var actDefinitions = BuildOfficialActDefinitions(seedText);

    var sharedPool = sharedAncients.ToList();
    rng.Shuffle(sharedPool);
    ConsumeExtraSteps(rng, extraStepsByPhase, "after_shared_shuffle");

    var sharedAssignments = new Dictionary<int, List<string>>();
    for (var actIndex = 1; actIndex < actDefinitions.Count; actIndex++)
    {
        if (sharedPool.Count == 0)
        {
            break;
        }

        var pullCount = rng.NextInt(sharedPool.Count + 1);
        if (pullCount <= 0)
        {
            continue;
        }

        var subset = sharedPool.Take(pullCount).ToList();
        sharedPool.RemoveRange(0, subset.Count);
        sharedAssignments[actIndex] = subset;
    }

    ConsumeExtraSteps(rng, extraStepsByPhase, "after_shared_assignments");

    var tracedActs = new List<TracedActGeneration>(actDefinitions.Count);
    for (var actIndex = 0; actIndex < actDefinitions.Count; actIndex++)
    {
        if (actIndex == 1)
        {
            ConsumeExtraSteps(rng, extraStepsByPhase, "between_act1_and_act2");
        }

        var definition = actDefinitions[actIndex];
        sharedAssignments.TryGetValue(actIndex, out var sharedSubset);
        tracedActs.Add(TraceActGeneration(
            definition,
            sharedEvents,
            sharedSubset ?? [],
            rng,
            extraStepsByPhase));
    }

    return (counterAfterPrimer, tracedActs);
}

TracedActGeneration TraceActGeneration(
    OfficialActDefinition act,
    IReadOnlyList<string> sharedEvents,
    IReadOnlyList<string> sharedAncients,
    GameRng rng,
    IReadOnlyDictionary<string, int>? extraStepsByPhase)
{
    var encounterSpecs = act.Encounters
        .Select(id => new EncounterSpec(id, LoadEncounterMetadata(id)))
        .ToList();
    var weak = encounterSpecs.Where(x => string.Equals(x.Metadata.RoomType, "Monster", StringComparison.OrdinalIgnoreCase) && x.Metadata.IsWeak).ToList();
    var regular = encounterSpecs.Where(x => string.Equals(x.Metadata.RoomType, "Monster", StringComparison.OrdinalIgnoreCase) && !x.Metadata.IsWeak).ToList();
    var elites = encounterSpecs.Where(x => string.Equals(x.Metadata.RoomType, "Elite", StringComparison.OrdinalIgnoreCase)).ToList();
    var bosses = encounterSpecs.Where(x => string.Equals(x.Metadata.RoomType, "Boss", StringComparison.OrdinalIgnoreCase)).ToList();
    var phases = new List<PhaseCounter>();

    ConsumeExtraSteps(rng, extraStepsByPhase, $"act{act.Number}:before_events");
    RecordPhase(phases, "events", rng, () =>
    {
        var events = act.Events.Concat(sharedEvents).ToList();
        rng.Shuffle(events);
        return $"{events.Count} events";
    });

    ConsumeExtraSteps(rng, extraStepsByPhase, $"act{act.Number}:before_normals");
    List<string> monsterShelf = [];
    RecordPhase(phases, "normals", rng, () =>
    {
        monsterShelf = GenerateNormalShelf(act, weak, regular, rng);
        return FormatList(monsterShelf.Take(7).ToList());
    });

    ConsumeExtraSteps(rng, extraStepsByPhase, $"act{act.Number}:before_elites");
    List<string> eliteShelf = [];
    RecordPhase(phases, "elites", rng, () =>
    {
        eliteShelf = GenerateEliteShelf(elites, rng);
        return FormatList(eliteShelf.Take(3).ToList());
    });

    ConsumeExtraSteps(rng, extraStepsByPhase, $"act{act.Number}:before_boss");
    string bossId = string.Empty;
    RecordPhase(phases, "boss", rng, () =>
    {
        bossId = rng.NextItem(bosses.Select(x => x.Id)) ?? "(none)";
        return bossId;
    });

    ConsumeExtraSteps(rng, extraStepsByPhase, $"act{act.Number}:before_ancient");
    string ancientId = string.Empty;
    RecordPhase(phases, "ancient", rng, () =>
    {
        var available = act.Ancients.Concat(sharedAncients).ToList();
        ancientId = rng.NextItem(available) ?? "(none)";
        return ancientId;
    });

    return new TracedActGeneration(act.Number, bossId, ancientId, monsterShelf, eliteShelf, phases);
}

void RecordPhase(List<PhaseCounter> phases, string phaseName, GameRng rng, Func<string> action)
{
    var before = rng.Counter;
    var value = action();
    phases.Add(new PhaseCounter(phaseName, before, rng.Counter, value));
}

void ConsumeExtraSteps(GameRng rng, IReadOnlyDictionary<string, int>? extraStepsByPhase, string phase)
{
    if (extraStepsByPhase is null || !extraStepsByPhase.TryGetValue(phase, out var extraSteps) || extraSteps <= 0)
    {
        return;
    }

    rng.FastForward(rng.Counter + extraSteps);
}

List<string> GenerateNormalShelf(
    OfficialActDefinition act,
    IReadOnlyList<EncounterSpec> weak,
    IReadOnlyList<EncounterSpec> regular,
    GameRng rng)
{
    var result = new List<EncounterSpec>(act.BaseRooms);
    var weakBag = weak.ToList();
    var regularBag = regular.ToList();

    for (var roomIndex = 0; roomIndex < act.WeakRooms; roomIndex++)
    {
        if (weakBag.Count == 0)
        {
            weakBag = weak.ToList();
        }

        AddEncounterWithoutRepeatingTags(result, weakBag, rng);
    }

    for (var roomIndex = act.WeakRooms; roomIndex < act.BaseRooms; roomIndex++)
    {
        if (regularBag.Count == 0)
        {
            regularBag = regular.ToList();
        }

        AddEncounterWithoutRepeatingTags(result, regularBag, rng);
    }

    return result.Select(x => x.Id).ToList();
}

List<string> GenerateEliteShelf(IReadOnlyList<EncounterSpec> elites, GameRng rng)
{
    if (elites.Count == 0)
    {
        return [];
    }

    var result = new List<EncounterSpec>(15);
    var bag = elites.ToList();
    for (var i = 0; i < 15; i++)
    {
        if (bag.Count == 0)
        {
            bag = elites.ToList();
        }

        AddEncounterWithoutRepeatingTags(result, bag, rng);
    }

    return result.Select(x => x.Id).ToList();
}

void AddEncounterWithoutRepeatingTags(List<EncounterSpec> target, List<EncounterSpec> bag, GameRng rng)
{
    var previous = target.Count > 0 ? target[^1] : null;
    var drawn = previous is null
        ? GrabAndRemove(bag, rng)
        : GrabAndRemove(
            bag,
            rng,
            candidate => !SharesEncounterTags(candidate, previous) &&
                         !string.Equals(candidate.Id, previous.Id, StringComparison.OrdinalIgnoreCase));

    drawn ??= GrabAndRemove(bag, rng);
    if (drawn is not null)
    {
        target.Add(drawn);
    }
}

EncounterSpec? GrabAndRemove(List<EncounterSpec> bag, GameRng rng, Func<EncounterSpec, bool>? predicate = null)
{
    if (bag.Count == 0)
    {
        return null;
    }

    if (predicate != null && !bag.Any(predicate))
    {
        return null;
    }

    int index;
    do
    {
        index = GrabIndex(bag.Count, rng);
    }
    while (predicate != null && !predicate(bag[index]));

    var item = bag[index];
    bag.RemoveAt(index);
    return item;
}

int GrabIndex(int count, GameRng rng)
{
    var roll = rng.NextDouble() * count;
    var index = (int)roll;
    if (index < 0)
    {
        return 0;
    }

    return index >= count ? count - 1 : index;
}

bool SharesEncounterTags(EncounterSpec current, EncounterSpec previous)
{
    if (current.Metadata.Tags.Count == 0 || previous.Metadata.Tags.Count == 0)
    {
        return false;
    }

    return current.Metadata.Tags.Intersect(previous.Metadata.Tags, StringComparer.OrdinalIgnoreCase).Any();
}

List<OfficialActDefinition> BuildOfficialActDefinitions(string seedText)
{
    return
    [
        GetOfficialActDefinition(ChooseActOne(seedText)),
        GetOfficialActDefinition("Hive"),
        GetOfficialActDefinition("Glory")
    ];
}

List<string> LoadSharedEvents()
{
    var root = JsonNode.Parse(File.ReadAllText(actDataPath))
        ?? throw new InvalidOperationException("Failed to parse acts.json.");
    return root["sharedEvents"]?.AsArray()
        ?.Select(node => node?.GetValue<string>())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("acts.json missing sharedEvents.");
}

List<string> LoadSharedAncients()
{
    var root = JsonNode.Parse(File.ReadAllText(actDataPath))
        ?? throw new InvalidOperationException("Failed to parse acts.json.");
    return root["sharedAncients"]?.AsArray()
        ?.Select(node => node?.GetValue<string>())
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Cast<string>()
        .ToList()
        ?? throw new InvalidOperationException("acts.json missing sharedAncients.");
}

string GetActualAncient(JsonArray history, int actNumber)
{
    if (actNumber - 1 >= history.Count)
    {
        return "(none)";
    }

    return ExtractEventIdsAtAncients(history[actNumber - 1]?.AsArray() ?? []).FirstOrDefault() ?? "(none)";
}

string GetActualBoss(JsonArray history, int actNumber)
{
    if (actNumber - 1 >= history.Count)
    {
        return "(none)";
    }

    return ExtractEncounterIds(history[actNumber - 1]?.AsArray() ?? [], "boss").FirstOrDefault() ?? "(none)";
}

List<string> GetActualMonsterPrefix(JsonArray history, int actNumber)
{
    if (actNumber - 1 >= history.Count)
    {
        return [];
    }

    return ExtractEncounterIds(history[actNumber - 1]?.AsArray() ?? [], "monster");
}

List<string> GetActualElitePrefix(JsonArray history, int actNumber)
{
    if (actNumber - 1 >= history.Count)
    {
        return [];
    }

    return ExtractEncounterIds(history[actNumber - 1]?.AsArray() ?? [], "elite");
}

bool PrefixMatches(IReadOnlyList<string> predicted, IReadOnlyList<string> actual)
{
    if (actual.Count > predicted.Count)
    {
        return false;
    }

    return predicted
        .Take(actual.Count)
        .Select(CanonicalizeComparisonId)
        .SequenceEqual(actual.Select(CanonicalizeComparisonId), StringComparer.Ordinal);
}

string CanonicalizeComparisonId(string id)
{
    return string.Concat(id.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}

Sts2SeedAnalysis AnalyzeSeed(Sts2RunPreviewer previewer, string seedText, CharacterId character)
{
    var seedValue = SeedFormatter.ToUIntSeed(SeedFormatter.Normalize(seedText));
    return previewer.AnalyzePools(new Sts2SeedAnalysisRequest
    {
        SeedText = seedText,
        SeedValue = seedValue,
        Character = character,
        AscensionLevel = 10
    });
}

List<string> ExtractEncounterIds(JsonArray actHistory, string roomType)
{
    return actHistory
        .Select(node => node?["rooms"]?.AsArray())
        .Where(rooms => rooms is not null)
        .SelectMany(rooms => rooms!)
        .Where(room => string.Equals(room?["room_type"]?.GetValue<string>(), roomType, StringComparison.OrdinalIgnoreCase))
        .Select(room => NormalizeModelId(room?["model_id"]?.GetValue<string>()))
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Cast<string>()
        .ToList();
}

List<string> ExtractEventIdsAtAncients(JsonArray actHistory)
{
    return actHistory
        .Where(node => string.Equals(node?["map_point_type"]?.GetValue<string>(), "ancient", StringComparison.OrdinalIgnoreCase))
        .Select(node => node?["rooms"]?.AsArray()?.FirstOrDefault()?["model_id"]?.GetValue<string>())
        .Select(NormalizeModelId)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Cast<string>()
        .ToList();
}

string? NormalizeModelId(string? modelId)
{
    if (string.IsNullOrWhiteSpace(modelId))
    {
        return null;
    }

    var separatorIndex = modelId.IndexOf('.');
    return separatorIndex >= 0 && separatorIndex < modelId.Length - 1
        ? modelId[(separatorIndex + 1)..]
        : modelId;
}

void PrintPrefixComparison(
    string label,
    IReadOnlyList<string> actual,
    IReadOnlyList<string>? current,
    IReadOnlyList<string>? official)
{
    Console.WriteLine($"    实际{label}: {FormatList(actual)}");
    if (current is not null)
    {
        Console.WriteLine($"    当前{label}前缀: {FormatList(current.Take(actual.Count).ToList())}");
    }

    if (official is not null)
    {
        Console.WriteLine($"    官方临时{label}前缀: {FormatList(official.Take(actual.Count).ToList())}");
    }
}

string FormatList(IReadOnlyList<string> values)
{
    return values.Count == 0
        ? "(无)"
        : string.Join(", ", values);
}
