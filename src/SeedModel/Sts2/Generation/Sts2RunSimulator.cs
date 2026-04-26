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
        return Simulate(rng, InferRunSeed(rng));
    }

    public IReadOnlyList<ActAncientResult> Simulate(
        GameRng rng,
        uint runSeed,
        Sts2AncientAvailability? ancientAvailability = null)
    {
        return Analyze(rng, runSeed, ancientAvailability)
            .Select(result => new ActAncientResult(result.ActIndex, result.ActNumber, result.AncientId))
            .ToList();
    }

    public IReadOnlyList<ActPoolResult> Analyze(GameRng rng)
    {
        return Analyze(rng, InferRunSeed(rng));
    }

    public IReadOnlyList<ActPoolResult> Analyze(
        GameRng rng,
        uint runSeed,
        Sts2AncientAvailability? ancientAvailability = null)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }

        ancientAvailability ??= Sts2AncientAvailability.Default;
        var acts = _world.ResolveActs(runSeed, ancientAvailability);
        var sharedAssignments = AssignSharedAncients(rng, acts, ancientAvailability);
        var results = new List<ActPoolResult>(acts.Count);

        for (var i = 0; i < acts.Count; i++)
        {
            var act = acts[i];
            sharedAssignments.TryGetValue(i, out var shared);
            var sharedAncients = (IReadOnlyList<string>?)shared ?? Array.Empty<string>();
            results.Add(ConsumeAct(i, act, rng, sharedAncients, ancientAvailability));
        }

        return results;
    }

    private ActPoolResult ConsumeAct(
        int actIndex,
        Sts2ActBlueprint act,
        GameRng rng,
        IReadOnlyList<string> sharedAncients,
        Sts2AncientAvailability ancientAvailability)
    {
        var events = ShuffleEvents(act, rng, ancientAvailability);
        var normalEncounters = GenerateNormalEncounters(act, rng);
        var eliteEncounters = GenerateEliteEncounters(act, rng);
        SelectBoss(act, rng);
        var chosenAncient = SelectAncient(act, rng, sharedAncients, ancientAvailability);
        return new ActPoolResult(
            actIndex,
            act.ActNumber,
            act.Name,
            act.BaseRooms,
            events,
            normalEncounters.Select(encounter => encounter.Id).ToList(),
            eliteEncounters.Select(encounter => encounter.Id).ToList(),
            chosenAncient);
    }

    private IReadOnlyList<string> ShuffleEvents(
        Sts2ActBlueprint act,
        GameRng rng,
        Sts2AncientAvailability ancientAvailability)
    {
        var events = new List<string>(act.Events.Count + _world.SharedEvents.Count);
        events.AddRange(act.Events.Where(eventId => ancientAvailability.IsActEventAvailable(act.ActNumber, eventId)));
        events.AddRange(_world.SharedEvents);
        rng.Shuffle(events);
        return events;
    }

    private static IReadOnlyList<EncounterMetadata> GenerateNormalEncounters(Sts2ActBlueprint act, GameRng rng)
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

        return normalEncounters;
    }

    private static IReadOnlyList<EncounterMetadata> GenerateEliteEncounters(Sts2ActBlueprint act, GameRng rng)
    {
        if (act.EliteEncounters.Count == 0)
        {
            return Array.Empty<EncounterMetadata>();
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

        return eliteList;
    }

    private static void SelectBoss(Sts2ActBlueprint act, GameRng rng)
    {
        rng.NextItem(act.BossEncounters);
    }

    private static string SelectAncient(
        Sts2ActBlueprint act,
        GameRng rng,
        IReadOnlyList<string> sharedAncients,
        Sts2AncientAvailability ancientAvailability)
    {
        var available = new List<string>(act.AncientIds.Count + sharedAncients.Count);
        available.AddRange(act.AncientIds.Where(id => ancientAvailability.IsActAncientAvailable(act.ActNumber, id)));
        available.AddRange(sharedAncients);
        if (available.Count == 0)
        {
            throw new InvalidOperationException($"Act {act.ActNumber} 没有可用古神。");
        }

        return rng.NextItem(available) ?? available[0];
    }

    private Dictionary<int, List<string>> AssignSharedAncients(
        GameRng rng,
        IReadOnlyList<Sts2ActBlueprint> acts,
        Sts2AncientAvailability ancientAvailability)
    {
        var assignments = new Dictionary<int, List<string>>();
        var pool = _world.SharedAncients
            .Where(ancientAvailability.IsSharedAncientAvailable)
            .ToList();

        if (pool.Count == 0)
        {
            return assignments;
        }
        rng.Shuffle(pool);

        for (var i = 1; i < acts.Count; i++)
        {
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

    private static uint InferRunSeed(GameRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return unchecked(rng.Seed - (uint)GameRng.GetDeterministicHashCode("up_front"));
    }

    internal sealed record ActAncientResult(int ActIndex, int ActNumber, string AncientId);

    internal sealed record ActPoolResult(
        int ActIndex,
        int ActNumber,
        string ActName,
        int EventPreviewLimit,
        IReadOnlyList<string> Events,
        IReadOnlyList<string> NormalEncounters,
        IReadOnlyList<string> EliteEncounters,
        string AncientId);
}
