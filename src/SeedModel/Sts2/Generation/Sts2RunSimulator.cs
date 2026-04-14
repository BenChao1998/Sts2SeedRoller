using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Collections;
using SeedModel.Rng;
using static SeedModel.Sts2.Generation.Sts2WorldData;

namespace SeedModel.Sts2.Generation;

internal sealed class Sts2RunSimulator
{
    private const int EliteShelfSize = 15;

    private readonly Sts2WorldData _world;

    public Sts2RunSimulator(Sts2WorldData world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public IReadOnlyList<ActAncientResult> Simulate(GameRng rng)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        var sharedAssignments = AssignSharedAncients(rng);
        var results = new List<ActAncientResult>(_world.Acts.Count);

        for (var i = 0; i < _world.Acts.Count; i++)
        {
            var act = _world.Acts[i];
            sharedAssignments.TryGetValue(i, out var shared);
            var sharedAncients = (IReadOnlyList<string>? )shared ?? Array.Empty<string>();
            var chosenAncient = ConsumeAct(act, rng, sharedAncients);
            results.Add(new ActAncientResult(act.Index, act.ActNumber, chosenAncient));
        }

        return results;
    }

    private string ConsumeAct(Sts2ActBlueprint act, GameRng rng, IReadOnlyList<string> sharedAncients)
    {
        ShuffleEvents(act, rng);
        GenerateNormalEncounters(act, rng);
        GenerateEliteEncounters(act, rng);
        SelectBoss(act, rng);
        return SelectAncient(act, rng, sharedAncients);
    }

    private void ShuffleEvents(Sts2ActBlueprint act, GameRng rng)
    {
        var events = new List<string>(act.Events.Count + _world.SharedEvents.Count);
        events.AddRange(act.Events);
        events.AddRange(_world.SharedEvents);
        rng.Shuffle(events);
    }

    private static void GenerateNormalEncounters(Sts2ActBlueprint act, GameRng rng)
    {
        var normalEncounters = new List<EncounterMetadata>(act.BaseRooms);
        var weakBag = BuildBag(act.WeakEncounters);
        var regularBag = BuildBag(act.RegularEncounters);

        for (var i = 0; i < act.WeakRooms; i++)
        {
            if (!weakBag.Any())
            {
                weakBag = BuildBag(act.WeakEncounters);
            }

            AddWithoutRepeatingTags(normalEncounters, weakBag, rng);
        }

        for (var i = act.WeakRooms; i < act.BaseRooms; i++)
        {
            if (!regularBag.Any())
            {
                regularBag = BuildBag(act.RegularEncounters);
            }

            AddWithoutRepeatingTags(normalEncounters, regularBag, rng);
        }
    }

    private static void GenerateEliteEncounters(Sts2ActBlueprint act, GameRng rng)
    {
        if (act.EliteEncounters.Count == 0)
        {
            return;
        }

        var eliteList = new List<EncounterMetadata>(EliteShelfSize);
        var eliteBag = BuildBag(act.EliteEncounters);
        for (var i = 0; i < EliteShelfSize; i++)
        {
            if (!eliteBag.Any())
            {
                eliteBag = BuildBag(act.EliteEncounters);
            }

            AddWithoutRepeatingTags(eliteList, eliteBag, rng);
        }
    }

    private static void SelectBoss(Sts2ActBlueprint act, GameRng rng)
    {
        rng.NextItem(act.BossEncounters);
    }

    private static string SelectAncient(Sts2ActBlueprint act, GameRng rng, IReadOnlyList<string> sharedAncients)
    {
        var available = new List<string>(act.AncientIds.Count + sharedAncients.Count);
        available.AddRange(act.AncientIds);
        available.AddRange(sharedAncients);
        if (available.Count == 0)
        {
            throw new InvalidOperationException($"Act {act.ActNumber} 没有可用古神。");
        }

        return rng.NextItem(available) ?? available[0];
    }

    private Dictionary<int, List<string>> AssignSharedAncients(GameRng rng)
    {
        var assignments = new Dictionary<int, List<string>>();
        if (_world.SharedAncients.Count == 0)
        {
            return assignments;
        }

        var pool = _world.SharedAncients.ToList();
        rng.Shuffle(pool);

        for (var i = 1; i < _world.Acts.Count; i++)
        {
            if (pool.Count == 0)
            {
                break;
            }

            var pullCount = rng.NextInt(pool.Count + 1);
            if (pullCount <= 0)
            {
                continue;
            }

            var subset = pool.Take(pullCount).ToList();
            pool.RemoveRange(0, subset.Count);
            assignments[i] = subset;
        }

        return assignments;
    }

    private static GrabBag<EncounterMetadata> BuildBag(IReadOnlyList<EncounterMetadata> encounters)
    {
        var bag = new GrabBag<EncounterMetadata>();
        foreach (var encounter in encounters)
        {
            bag.Add(encounter);
        }

        return bag;
    }

    private static void AddWithoutRepeatingTags(
        List<EncounterMetadata> target,
        GrabBag<EncounterMetadata> bag,
        GameRng rng)
    {
        var last = target.Count > 0 ? target[^1] : null;
        EncounterMetadata? drawn = null;
        if (last != null)
        {
            drawn = bag.GrabAndRemove(
                rng,
                candidate => !SharesTags(candidate, last) && !ReferenceEquals(candidate, last));
        }

        drawn ??= bag.GrabAndRemove(rng);
        if (drawn != null)
        {
            target.Add(drawn);
        }
    }

    private static bool SharesTags(EncounterMetadata current, EncounterMetadata? previous)
    {
        if (previous == null)
        {
            return false;
        }

        if (current.Tags.Count == 0 || previous.Tags.Count == 0)
        {
            return false;
        }

        return current.Tags.Intersect(previous.Tags, StringComparer.OrdinalIgnoreCase).Any();
    }

    internal sealed record ActAncientResult(int ActIndex, int ActNumber, string AncientId);
}
