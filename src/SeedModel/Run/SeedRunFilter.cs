using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedModel.Run;

public sealed record SeedRunFilter
{
    public required NeowOptionFilter NeowFilter { get; init; }

    public Sts2AncientFilter AncientFilter { get; init; } = Sts2AncientFilter.Disabled;
}
