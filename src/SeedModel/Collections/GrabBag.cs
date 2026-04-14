using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Rng;

namespace SeedModel.Collections;

internal sealed class GrabBag<T>
{
    private readonly List<Entry> _entries = new();
    private double _totalWeight;

    public bool Any() => _entries.Count > 0;

    public void Add(T item, double weight = 1.0)
    {
        if (weight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive.");
        }

        _entries.Add(new Entry(item, weight));
        _totalWeight += weight;
    }

    public void Clear()
    {
        _entries.Clear();
        _totalWeight = 0;
    }

    public T? Grab(GameRng rng, Func<T, bool>? predicate = null)
    {
        var index = GrabIndex(rng, predicate);
        if (index < 0)
        {
            return default;
        }

        return _entries[index].Item;
    }

    public T? GrabAndRemove(GameRng rng, Func<T, bool>? predicate = null)
    {
        var index = GrabIndex(rng, predicate);
        if (index < 0)
        {
            return default;
        }

        var value = _entries[index].Item;
        RemoveAt(index);
        return value;
    }

    private int GrabIndex(GameRng rng, Func<T, bool>? predicate)
    {
        if (predicate != null && !_entries.Any(entry => predicate(entry.Item)))
        {
            return -1;
        }

        int index;
        do
        {
            index = GrabIndex(rng);
        }
        while (index >= 0 && predicate != null && !predicate(_entries[index].Item));

        return index;
    }

    private int GrabIndex(GameRng rng)
    {
        if (_entries.Count == 0 || _totalWeight <= 0)
        {
            return -1;
        }

        var roll = rng.NextDouble() * _totalWeight;
        var cumulative = 0.0;
        for (var i = 0; i < _entries.Count; i++)
        {
            cumulative += _entries[i].Weight;
            if (roll < cumulative)
            {
                return i;
            }
        }

        return _entries.Count - 1;
    }

    private void RemoveAt(int index)
    {
        var entry = _entries[index];
        _totalWeight -= entry.Weight;
        _entries.RemoveAt(index);
    }

    private readonly record struct Entry(T Item, double Weight);
}
