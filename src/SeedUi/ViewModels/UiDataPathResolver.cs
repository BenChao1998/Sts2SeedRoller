using System;
using System.IO;

namespace SeedUi.ViewModels;

internal static class UiDataPathResolver
{
    public static string ResolveVersionedDataFilePath(string version, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version is required.", nameof(version));
        }

        var relativeSegments = new string[segments.Length + 2];
        relativeSegments[0] = "data";
        relativeSegments[1] = version;
        Array.Copy(segments, 0, relativeSegments, 2, segments.Length);
        return ResolveRelativeFilePath(Path.Combine(relativeSegments));
    }

    public static string ResolveDataFilePath(params string[] segments)
    {
        var relativeSegments = new string[segments.Length + 1];
        relativeSegments[0] = "data";
        Array.Copy(segments, 0, relativeSegments, 1, segments.Length);
        return ResolveRelativeFilePath(Path.Combine(relativeSegments));
    }

    public static string ResolveRelativeFilePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var workspaceRoot = TryFindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            var workspaceCandidate = Path.GetFullPath(Path.Combine(workspaceRoot, normalized));
            if (File.Exists(workspaceCandidate))
            {
                return workspaceCandidate;
            }
        }

        var parentCandidate = TryResolveByWalkingParents(normalized);
        if (!string.IsNullOrWhiteSpace(parentCandidate))
        {
            return parentCandidate;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, normalized));
    }

    private static string? TryFindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = current.FullName;
            if (Directory.Exists(Path.Combine(candidate, ".git")) ||
                (Directory.Exists(Path.Combine(candidate, "src")) &&
                 Directory.Exists(Path.Combine(candidate, "data"))))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? TryResolveByWalkingParents(string normalizedRelativePath)
    {
        string? lastMatch = null;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.GetFullPath(Path.Combine(current.FullName, normalizedRelativePath));
            if (File.Exists(candidate))
            {
                lastMatch = candidate;
            }

            current = current.Parent;
        }

        return lastMatch;
    }
}
