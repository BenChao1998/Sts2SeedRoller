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
var ascension = args.Length > 1 && int.TryParse(args[1], out var parsedAscension) ? parsedAscension : 10;
var netId = args.Length > 2 && ulong.TryParse(args[2], out var parsedNetId) ? parsedNetId : 1UL;

ModelDb.Init();

var unlock = UnlockState.all;
var runSeed = (uint)StringHelper.GetDeterministicHashCode(seedText);

Console.WriteLine("Official DLL Probe");
Console.WriteLine("==================");
Console.WriteLine($"seed text : {seedText}");
Console.WriteLine($"hash seed : {runSeed}");
Console.WriteLine($"ascension : {ascension}");
Console.WriteLine($"net id    : {netId}");
Console.WriteLine();

var sharedBag = new RelicGrabBag(refreshAllowed: true);
var playerBag = new RelicGrabBag(refreshAllowed: true);
var upFront = new Rng(runSeed, "up_front");

sharedBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlock), upFront);
playerBag.Populate(
    ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlock)
        .Concat(ModelDb.RelicPool<SilentRelicPool>().GetUnlockedRelics(unlock)),
    upFront);

Console.WriteLine($"up_front counter after bag populate: {upFront.Counter}");
Console.WriteLine();

DumpBag("Shared", sharedBag.ToSerializable());
DumpBag("Player", playerBag.ToSerializable());

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
