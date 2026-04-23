using System;

namespace SeedModel.Neow;

[Flags]
internal enum NeowDetailHint
{
    None = 0,
    Card = 1 << 0,
    Potion = 1 << 1,
    Relic = 1 << 2,
    Text = 1 << 3
}
