using System.Collections.Generic;
using System.Linq;

namespace SeedModel.Events;

public static class SeedEventRegistry
{
    private static readonly IReadOnlyList<SeedEventMetadata> Events =
    [
        new SeedEventMetadata
        {
            Type = SeedEventType.Act1Neow,
            Id = "act1-neow",
            DisplayName = "第一幕 · Neow",
            Description = "开局选项（已实现）。",
            DefaultDataPath = "data/neow/options.json",
            IsImplemented = true
        }
    ];

    public static IReadOnlyList<SeedEventMetadata> All => Events;

    public static SeedEventMetadata Get(SeedEventType type)
    {
        return Events.First(e => e.Type == type);
    }

    public static bool TryGetById(string? id, out SeedEventMetadata metadata)
    {
        metadata = default!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var match = Events.FirstOrDefault(e => string.Equals(e.Id, id, System.StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            return false;
        }

        metadata = match;
        return true;
    }
}
