using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SeedModel.Neow;

namespace SeedModel.Sts2.Generation;

internal sealed class Sts2WorldData
{
    public IReadOnlyList<Sts2ActBlueprint> Acts { get; }

    public IReadOnlyList<string> SharedEvents { get; }

    public IReadOnlyList<string> SharedAncients { get; }

    public RelicPoolInfo RelicPools { get; }

    private Sts2WorldData(
        IReadOnlyList<Sts2ActBlueprint> acts,
        IReadOnlyList<string> sharedEvents,
        IReadOnlyList<string> sharedAncients,
        RelicPoolInfo relicPools)
    {
        Acts = acts;
        SharedEvents = sharedEvents;
        SharedAncients = sharedAncients;
        RelicPools = relicPools;
    }

    public static Sts2WorldData LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Act data path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Act data file not found.", path);
        }

        using var stream = File.OpenRead(path);
        var model = JsonSerializer.Deserialize<Sts2DataModel>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException($"无法解析 {path} 中的 Act 数据。");

        if (model.Acts.Count == 0)
        {
            throw new InvalidDataException("Act 数据为空。");
        }

        var encounters = model.Encounters.ToDictionary(
            kvp => kvp.Key,
            kvp => EncounterMetadata.Create(kvp.Key, kvp.Value),
            StringComparer.OrdinalIgnoreCase);

        var acts = model.Acts
            .OrderBy(act => act.Number)
            .Select(act => Sts2ActBlueprint.Create(act, encounters))
            .ToList();

        var relicPools = RelicPoolInfo.Create(model.RelicPools);

        return new Sts2WorldData(
            acts,
            model.SharedEvents.AsReadOnly(),
            model.SharedAncients.AsReadOnly(),
            relicPools);
    }

    private sealed class Sts2DataModel
    {
        public List<Sts2ActDataModel> Acts { get; init; } = new();

        public Dictionary<string, EncounterDataModel> Encounters { get; init; } = new();

        public List<string> SharedEvents { get; init; } = new();

        public List<string> SharedAncients { get; init; } = new();

        public RelicPoolDataModel RelicPools { get; init; } = new();
    }

    internal sealed class Sts2ActDataModel
    {
        public string Name { get; init; } = string.Empty;

        public int Number { get; init; }

        public int BaseRooms { get; init; }

        public int WeakRooms { get; init; }

        public List<string> Events { get; init; } = new();

        public List<string> Encounters { get; init; } = new();

        public List<string> Ancients { get; init; } = new();
    }

    internal sealed class EncounterDataModel
    {
        public string RoomType { get; init; } = "Unknown";

        public bool IsWeak { get; init; }

        public List<string> Tags { get; init; } = new();
    }

    internal sealed class RelicPoolDataModel
    {
        public List<string> SharedSequence { get; init; } = new();

        public Dictionary<string, List<string>> Characters { get; init; } = new();

        public Dictionary<string, string> Rarities { get; init; } = new();
    }

    internal sealed class Sts2ActBlueprint
    {
        public string Name { get; }

        public int ActNumber { get; }

        public int Index { get; }

        public int BaseRooms { get; }

        public int WeakRooms { get; }

        public IReadOnlyList<string> Events { get; }

        public IReadOnlyList<string> AncientIds { get; }

        public IReadOnlyList<EncounterMetadata> WeakEncounters { get; }

        public IReadOnlyList<EncounterMetadata> RegularEncounters { get; }

        public IReadOnlyList<EncounterMetadata> EliteEncounters { get; }

        public IReadOnlyList<EncounterMetadata> BossEncounters { get; }

        private Sts2ActBlueprint(
            string name,
            int actNumber,
            int index,
            int baseRooms,
            int weakRooms,
            IReadOnlyList<string> events,
            IReadOnlyList<string> ancientIds,
            IReadOnlyList<EncounterMetadata> weakEncounters,
            IReadOnlyList<EncounterMetadata> regularEncounters,
            IReadOnlyList<EncounterMetadata> eliteEncounters,
            IReadOnlyList<EncounterMetadata> bossEncounters)
        {
            Name = name;
            ActNumber = actNumber;
            Index = index;
            BaseRooms = baseRooms;
            WeakRooms = weakRooms;
            Events = events;
            AncientIds = ancientIds;
            WeakEncounters = weakEncounters;
            RegularEncounters = regularEncounters;
            EliteEncounters = eliteEncounters;
            BossEncounters = bossEncounters;
        }

        internal static Sts2ActBlueprint Create(
            Sts2ActDataModel data,
            IReadOnlyDictionary<string, EncounterMetadata> encounters)
        {
            var orderedEncounters = data.Encounters
                .Select(id => encounters.TryGetValue(id, out var meta)
                    ? meta
                    : throw new InvalidDataException($"缺少遭遇 {id} 的元数据。"))
                .ToList();

            var weak = orderedEncounters
                .Where(e => e.RoomType == EncounterRoomType.Monster && e.IsWeak)
                .ToList();
            var regular = orderedEncounters
                .Where(e => e.RoomType == EncounterRoomType.Monster && !e.IsWeak)
                .ToList();
            var elites = orderedEncounters
                .Where(e => e.RoomType == EncounterRoomType.Elite)
                .ToList();
            var bosses = orderedEncounters
                .Where(e => e.RoomType == EncounterRoomType.Boss)
                .ToList();

            if (bosses.Count == 0)
            {
                throw new InvalidDataException($"Act {data.Number} 缺失 Boss 列表。");
            }

            var ancientIds = data.Ancients
                .Select(id => id.ToUpperInvariant())
                .ToList();

            return new Sts2ActBlueprint(
                data.Name,
                data.Number,
                index: data.Number - 1,
                baseRooms: data.BaseRooms,
                weakRooms: data.WeakRooms,
                events: data.Events.AsReadOnly(),
                ancientIds: ancientIds.AsReadOnly(),
                weakEncounters: weak.AsReadOnly(),
                regularEncounters: regular.AsReadOnly(),
                eliteEncounters: elites.AsReadOnly(),
                bossEncounters: bosses.AsReadOnly());
        }
    }

    internal sealed class EncounterMetadata
    {
        public string Id { get; }

        public EncounterRoomType RoomType { get; }

        public bool IsWeak { get; }

        public IReadOnlyList<string> Tags { get; }

        private EncounterMetadata(string id, EncounterRoomType roomType, bool isWeak, IReadOnlyList<string> tags)
        {
            Id = id;
            RoomType = roomType;
            IsWeak = isWeak;
            Tags = tags;
        }

        internal static EncounterMetadata Create(string id, EncounterDataModel model)
        {
            if (!Enum.TryParse<EncounterRoomType>(model.RoomType, ignoreCase: true, out var roomType))
            {
                roomType = EncounterRoomType.Unknown;
            }

            return new EncounterMetadata(
                id,
                roomType,
                model.IsWeak,
                model.Tags.AsReadOnly());
        }
    }

    internal enum EncounterRoomType
    {
        Unknown,
        Monster,
        Elite,
        Boss
    }

    internal sealed class RelicPoolInfo
    {
        private RelicPoolInfo(
            IReadOnlyList<string> sharedSequence,
            IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> characterSequences,
            IReadOnlyDictionary<string, IReadOnlyList<string>> rawCharacterSequences,
            IReadOnlyDictionary<string, string> rarityMap)
        {
            SharedSequence = sharedSequence;
            CharacterSequences = characterSequences;
            RawCharacterSequences = rawCharacterSequences;
            RarityMap = rarityMap;
        }

        public IReadOnlyList<string> SharedSequence { get; }

        public IReadOnlyDictionary<CharacterId, IReadOnlyList<string>> CharacterSequences { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> RawCharacterSequences { get; }

        public IReadOnlyDictionary<string, string> RarityMap { get; }

        public static RelicPoolInfo Create(RelicPoolDataModel model)
        {
            var shared = model.SharedSequence.AsReadOnly();
            var byCharacter = new Dictionary<CharacterId, IReadOnlyList<string>>();
            var raw = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, sequence) in model.Characters)
            {
                var snapshot = sequence.AsReadOnly();
                raw[name] = snapshot;

                if (Enum.TryParse<CharacterId>(name, ignoreCase: true, out var id))
                {
                    byCharacter[id] = snapshot;
                }
            }

            var rarityMap = new Dictionary<string, string>(model.Rarities, StringComparer.OrdinalIgnoreCase);
            return new RelicPoolInfo(shared, byCharacter, raw, rarityMap);
        }

        public IReadOnlyList<string> GetSequenceFor(CharacterId id)
        {
            if (CharacterSequences.TryGetValue(id, out var sequence))
            {
                return sequence;
            }

            return SharedSequence;
        }
    }
}
