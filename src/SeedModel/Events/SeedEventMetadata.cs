namespace SeedModel.Events;

/// <summary>
/// Describes a deterministic event that can be rolled by the tooling.
/// </summary>
public sealed record SeedEventMetadata
{
    public required SeedEventType Type { get; init; }

    /// <summary>
    /// Stable identifier used for CLI/UI bindings.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Localizable display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional description shown in UI help text.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Default dataset path relative to the application root.
    /// </summary>
    public required string DefaultDataPath { get; init; }

    /// <summary>
    /// Whether the generator is fully implemented in the current build.
    /// </summary>
    public bool IsImplemented { get; init; }
}
