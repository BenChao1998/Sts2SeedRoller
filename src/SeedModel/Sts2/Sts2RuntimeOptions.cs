namespace SeedModel.Sts2;

public sealed record Sts2RuntimeOptions
{
    /// <summary>
    /// Full path to sts2.dll extracted from the official game build.
    /// </summary>
    public string GameAssemblyPath { get; init; } = string.Empty;

    /// <summary>
    /// Optional directory containing extracted mod assets (used for locating auxiliary files if needed).
    /// </summary>
    public string ModRootPath { get; init; } = string.Empty;

    /// <summary>
    /// When true, additional verbose diagnostics from the bridge will be surfaced via exceptions.
    /// </summary>
    public bool EnableDiagnostics { get; init; }
}
