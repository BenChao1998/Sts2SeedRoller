namespace SeedModel.Neow;

public sealed record RewardDetail(
    RewardDetailType Type,
    string Label,
    string Value,
    string? ModelId = null,
    int? Amount = null);
