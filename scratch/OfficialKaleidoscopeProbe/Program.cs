using System.Text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

Console.OutputEncoding = Encoding.UTF8;

var seed = args.Length > 0 ? args[0] : "8NANPV4C0D";
var ascension = args.Length > 1 && int.TryParse(args[1], out var parsedAscension) ? parsedAscension : 10;

ModelDb.Init();

var player = Player.CreateForNewRun(ModelDb.Character<Silent>(), UnlockState.all, 1UL);
var runState = RunState.CreateForTest(players: [player], ascensionLevel: ascension, seed: seed);

Console.WriteLine($"seed={seed}");
Console.WriteLine($"ascension={ascension}");
Console.WriteLine($"niche.counter.start={runState.Rng.Niche.Counter}");
Console.WriteLine($"rewards.counter.start={player.PlayerRng.Rewards.Counter}");

var allPools = player.UnlockState.CharacterCardPools.ToList();
Console.WriteLine("characterPools=" + string.Join(" | ", allPools.Select(pool => $"{pool.Id.Entry}:{ResolveCharacter(pool)}")));

for (var bundleIndex = 0; bundleIndex < 2; bundleIndex++)
{
    var shuffled = player.UnlockState.CharacterCardPools
        .Where(pool => pool != player.Character.CardPool)
        .ToList();

    shuffled.StableShuffle(runState.Rng.Niche);

    Console.WriteLine($"bundle[{bundleIndex + 1}].niche.counter={runState.Rng.Niche.Counter}");
    Console.WriteLine($"bundle[{bundleIndex + 1}].poolOrder={string.Join(" | ", shuffled.Select(pool => $"{pool.Id.Entry}:{ResolveCharacter(pool)}"))}");

    foreach (var pool in shuffled.Take(3))
    {
        var before = player.PlayerRng.Rewards.Counter;
        var options = new CardCreationOptions([pool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter)
            .WithFlags(CardCreationFlags.NoCardPoolModifications);
        var card = CardFactory.CreateForReward(player, 1, options).First().Card;
        var after = player.PlayerRng.Rewards.Counter;
        Console.WriteLine($"  pool={pool.Id.Entry}:{ResolveCharacter(pool)} card={card.Id.Entry} upgraded={card.IsUpgraded} rewards={before}->{after}");
    }
}

static string ResolveCharacter(CardPoolModel pool)
{
    if (pool == ModelDb.CardPool<IroncladCardPool>())
    {
        return "Ironclad";
    }

    if (pool == ModelDb.CardPool<SilentCardPool>())
    {
        return "Silent";
    }

    if (pool == ModelDb.CardPool<RegentCardPool>())
    {
        return "Regent";
    }

    if (pool == ModelDb.CardPool<NecrobinderCardPool>())
    {
        return "Necrobinder";
    }

    if (pool == ModelDb.CardPool<DefectCardPool>())
    {
        return "Defect";
    }

    return pool.Id.Entry;
}
