using System;
using System.Collections;
using System.Collections.Generic;

namespace SeedModel.Neow;

internal sealed class DeferredRewardDetails : IReadOnlyList<RewardDetail>
{
    private Func<IReadOnlyList<RewardDetail>>? _factory;
    private IReadOnlyList<RewardDetail>? _value;

    public DeferredRewardDetails(Func<IReadOnlyList<RewardDetail>> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public int Count => EnsureValue().Count;

    public RewardDetail this[int index] => EnsureValue()[index];

    public IEnumerator<RewardDetail> GetEnumerator() => EnsureValue().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IReadOnlyList<RewardDetail> EnsureValue()
    {
        if (_value != null)
        {
            return _value;
        }

        var factory = _factory ?? throw new InvalidOperationException("Reward detail factory is unavailable.");
        _value = factory();
        _factory = null;
        return _value;
    }
}
