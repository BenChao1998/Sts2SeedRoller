using System;

namespace SeedModel.Sts2;

public sealed record Sts2ExactRouteFilter
{
    public static Sts2ExactRouteFilter Empty { get; } = new();

    public IReadOnlyList<Sts2ExactRouteEventTargetRequest> EventTargets { get; init; } = Array.Empty<Sts2ExactRouteEventTargetRequest>();

    public IReadOnlyList<Sts2ExactRouteRelicTargetRequest> RelicTargets { get; init; } = Array.Empty<Sts2ExactRouteRelicTargetRequest>();

    public long MaxRouteChecks { get; init; } = 2_000;

    public Sts2ExactRouteSimulationMode SimulationMode { get; init; } = Sts2ExactRouteSimulationMode.FastShopLimited;

    public int ShopOutputLimit { get; init; } = 12;

    public bool HasCriteria => EventTargets.Count > 0 || RelicTargets.Count > 0;
}
