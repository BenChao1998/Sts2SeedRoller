using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedModel.Run;

public sealed record SeedRunFilter
{
    public required NeowOptionFilter NeowFilter { get; init; }

    public Sts2AncientFilter AncientFilter { get; init; } = Sts2AncientFilter.Disabled;

    public Sts2ShopFilter ShopFilter { get; init; } = Sts2ShopFilter.Empty;

    public Sts2PoolFilter PoolFilter { get; init; } = Sts2PoolFilter.Empty;
}
