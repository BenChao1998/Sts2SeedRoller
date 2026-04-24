using System;
using System.Collections.Generic;
using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedUi.Storage;

internal enum SeedArchiveJobStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed
}

internal enum SeedArchiveMode
{
    Sequential,
    Random
}

internal sealed record SeedArchiveVersionInfo(
    int SchemaVersion,
    string DataVersion,
    string AppVersion);

internal sealed record SeedArchiveJobCreateRequest
{
    public required SeedArchiveMode Mode { get; init; }

    public required string Character { get; init; }

    public required int Ascension { get; init; }

    public required string StartSeedText { get; init; }

    public required int SeedStep { get; init; }

    public required string SequenceToken { get; init; }

    public required int RequestedCount { get; init; }
}

internal sealed record SeedArchiveStoredRun
{
    public long RunId { get; init; }

    public required string JobId { get; init; }

    public required string SeedText { get; init; }

    public required uint SeedValue { get; init; }

    public required long SeedOrderValue { get; init; }

    public required string Character { get; init; }

    public required int Ascension { get; init; }

    public required IReadOnlyList<NeowOptionResult> Act1Options { get; init; }

    public required Sts2RunPreview Sts2Preview { get; init; }
}

internal sealed record SeedArchiveScanJob
{
    public required string JobId { get; init; }

    public required SeedArchiveMode Mode { get; init; }

    public required SeedArchiveJobStatus Status { get; init; }

    public required string Character { get; init; }

    public required int Ascension { get; init; }

    public required string StartSeedText { get; init; }

    public required int SeedStep { get; init; }

    public required string SequenceToken { get; init; }

    public required int NextIndex { get; init; }

    public required int RequestedCount { get; init; }

    public required int ProcessedCount { get; init; }

    public required int StoredCount { get; init; }

    public required int SkippedCount { get; init; }

    public string? LastSeedText { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record SeedArchiveDatabaseSummary
{
    public required string DatabasePath { get; init; }

    public required SeedArchiveVersionInfo VersionInfo { get; init; }

    public required int JobCount { get; init; }

    public required int RunCount { get; init; }

    public SeedArchiveScanJob? LatestJob { get; init; }
}

internal sealed record SeedArchiveBatchWriteResult(
    int InsertedRuns,
    int SkippedRuns,
    int NextIndex,
    string? LastSeedText,
    SeedArchiveScanJob UpdatedJob);

internal sealed record SeedArchiveSearchCriteria
{
    public string? Character { get; init; }

    public int? Ascension { get; init; }

    public string? SeedTextFrom { get; init; }

    public string? SeedTextTo { get; init; }

    public IReadOnlyList<string>? Act1RelicIds { get; init; }

    public IReadOnlyList<string>? Act1CardIds { get; init; }

    public IReadOnlyList<string>? Act1PotionIds { get; init; }

    public string? Act2AncientId { get; init; }

    public IReadOnlyList<string>? Act2OptionIds { get; init; }

    public string? Act3AncientId { get; init; }

    public IReadOnlyList<string>? Act3OptionIds { get; init; }
}

internal sealed record SeedArchiveRunSummary
{
    public required long RunId { get; init; }

    public required string JobId { get; init; }

    public required string SeedText { get; init; }

    public required string Character { get; init; }

    public required int Ascension { get; init; }

    public string? Act2AncientId { get; init; }

    public string? Act3AncientId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
