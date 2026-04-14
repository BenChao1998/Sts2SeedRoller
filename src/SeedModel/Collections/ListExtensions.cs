using System.Collections.Generic;
using SeedModel.Rng;

namespace SeedModel.Collections;

public static class ListExtensions
{
    public static List<T> UnstableShuffle<T>(this List<T> list, GameRng rng)
    {
        rng.Shuffle(list);
        return list;
    }
}
