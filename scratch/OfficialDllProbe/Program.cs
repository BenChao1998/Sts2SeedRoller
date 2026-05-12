using System.Text;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

Console.OutputEncoding = Encoding.UTF8;

var seedText = args.Length > 0 ? args[0] : "PCKDQFERHM";
var character = args.Length > 1 ? args[1] : "Silent";
var ascension = args.Length > 2 && int.TryParse(args[2], out var parsedAscension) ? parsedAscension : 10;
var netId = args.Length > 3 && ulong.TryParse(args[3], out var parsedNetId) ? parsedNetId : 1UL;
var targetRelics = args.Skip(4)
    .Where(arg => !string.IsNullOrWhiteSpace(arg))
    .Select(arg => arg.Trim().ToUpperInvariant())
    .ToArray();

ModelDb.Init();

var unlock = UnlockState.all;
var runSeed = (uint)StringHelper.GetDeterministicHashCode(seedText);

Console.WriteLine("Official DLL Probe");
Console.WriteLine("==================");
Console.WriteLine($"seed text : {seedText}");
Console.WriteLine($"hash seed : {runSeed}");
Console.WriteLine($"character : {character}");
Console.WriteLine($"ascension : {ascension}");
Console.WriteLine($"net id    : {netId}");
Console.WriteLine();

DumpRngComparison(runSeed, "up_front");
DumpRngComparison(unchecked(runSeed + (uint)netId), "rewards");

var sharedBag = new RelicGrabBag(refreshAllowed: true);
var playerBag = new RelicGrabBag(refreshAllowed: true);
var upFront = new Rng(runSeed, "up_front");

sharedBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlock), upFront);
playerBag.Populate(
    ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlock)
        .Concat(GetCharacterRelics(character, unlock)),
    upFront);

Console.WriteLine($"up_front counter after bag populate: {upFront.Counter}");
Console.WriteLine();

DumpBag("Shared", sharedBag.ToSerializable());
DumpBag("Player", playerBag.ToSerializable());
DumpPositions("Shared", sharedBag.ToSerializable(), targetRelics);
DumpPositions("Player", playerBag.ToSerializable(), targetRelics);

if (args.Length >= 6)
{
    Console.WriteLine("Full RunManager probe");
    Console.WriteLine("---------------------");
    Console.WriteLine("disabled: constructing official RunManager/Player triggers native Godot SaveManager init and crashes in this environment.");
    Console.WriteLine();
}

static void DumpBag(string label, SerializableRelicGrabBag bag)
{
    Console.WriteLine($"{label} bag");
    foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
    {
        if (!bag.RelicIdLists.TryGetValue(rarity, out var ids))
        {
            continue;
        }

        var preview = ids.Take(12).Select(id => id.Entry).ToArray();
        Console.WriteLine($"  {rarity,-9} [{preview.Length}/{ids.Count} shown] {string.Join(", ", preview)}");
    }

    Console.WriteLine();
}

static IEnumerable<MegaCrit.Sts2.Core.Models.RelicModel> GetCharacterRelics(string character, UnlockState unlock)
{
    return character.ToUpperInvariant() switch
    {
        "IRONCLAD" => ModelDb.RelicPool<IroncladRelicPool>().GetUnlockedRelics(unlock),
        "SILENT" => ModelDb.RelicPool<SilentRelicPool>().GetUnlockedRelics(unlock),
        "DEFECT" => ModelDb.RelicPool<DefectRelicPool>().GetUnlockedRelics(unlock),
        "NECROBINDER" => ModelDb.RelicPool<NecrobinderRelicPool>().GetUnlockedRelics(unlock),
        "REGENT" => ModelDb.RelicPool<RegentRelicPool>().GetUnlockedRelics(unlock),
        _ => Array.Empty<MegaCrit.Sts2.Core.Models.RelicModel>()
    };
}

static void DumpPositions(string label, SerializableRelicGrabBag bag, IReadOnlyList<string> targetRelics)
{
    if (targetRelics.Count == 0)
    {
        return;
    }

    Console.WriteLine($"{label} target positions");
    foreach (var target in targetRelics)
    {
        var found = false;
        foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
        {
            if (!bag.RelicIdLists.TryGetValue(rarity, out var ids))
            {
                continue;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (!string.Equals(ids[i].Entry, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Console.WriteLine($"  {target,-18} rarity={rarity,-9} position={i + 1}");
                found = true;
                break;
            }

            if (found)
            {
                break;
            }
        }

        if (!found)
        {
            Console.WriteLine($"  {target,-18} missing");
        }
    }

    Console.WriteLine();
}

static void DumpRngComparison(uint seed, string salt)
{
    var official = new Rng(seed, salt);
    var custom = new ProbeGameRng(seed, salt);
    var officialFloats = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => official.NextFloat().ToString("0.000000")));
    var customFloats = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => custom.NextFloat().ToString("0.000000")));

    official = new Rng(seed, salt);
    custom = new ProbeGameRng(seed, salt);
    var officialInts = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => official.NextInt(100)));
    var customInts = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => custom.NextInt(100)));

    official = new Rng(seed, salt);
    custom = new ProbeGameRng(seed, salt);
    var sample = new[] { "A", "B", "C", "D", "E", "F", "G" };
    var officialItems = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => official.NextItem(sample)));
    var customItems = string.Join(" / ", Enumerable.Range(0, 8).Select(_ => custom.NextItem(sample)));

    Console.WriteLine($"rng[{salt}].official.float={officialFloats}");
    Console.WriteLine($"rng[{salt}].custom.float={customFloats}");
    Console.WriteLine($"rng[{salt}].official.int={officialInts}");
    Console.WriteLine($"rng[{salt}].custom.int={customInts}");
    Console.WriteLine($"rng[{salt}].official.item={officialItems}");
    Console.WriteLine($"rng[{salt}].custom.item={customItems}");
    Console.WriteLine();
}

sealed class ProbeGameRng
{
    private readonly Random _random;

    public ProbeGameRng(uint seed, string salt)
    {
        _random = new Random(unchecked((int)(seed + (uint)GetDeterministicHashCode(salt))));
    }

    public float NextFloat() => (float)_random.NextDouble();

    public int NextInt(int maxExclusive) => _random.Next(maxExclusive);

    public T? NextItem<T>(IEnumerable<T> items)
    {
        var materialized = items as T[] ?? items.ToArray();
        if (materialized.Length == 0)
        {
            return default;
        }

        return materialized[NextInt(materialized.Length)];
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
}
