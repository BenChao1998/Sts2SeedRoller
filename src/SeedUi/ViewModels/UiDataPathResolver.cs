using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SeedUi.ViewModels;

internal static class UiDataPathResolver
{
    private static readonly Regex VersionDirectoryRegex = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private const string LegacyDefaultVersion = "0.99.1";

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

    public static IReadOnlyList<string> GetAvailableVersionDirectories()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaceRoot = TryFindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            CollectVersionDirectories(Path.Combine(workspaceRoot, "data"), results);
        }

        CollectVersionDirectories(Path.Combine(AppContext.BaseDirectory, "data"), results);

        return results
            .OrderByDescending(ParseVersionForSort)
            .ThenByDescending(static version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetPreferredVersionOrDefault(string? preferredVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            return preferredVersion;
        }

        var discoveredVersions = GetAvailableVersionDirectories();
        return discoveredVersions.Count > 0
            ? discoveredVersions[0]
            : LegacyDefaultVersion;
    }

    public static string? FindWorkspaceRoot() => TryFindWorkspaceRoot();

    private static void CollectVersionDirectories(string dataRoot, HashSet<string> results)
    {
        if (!Directory.Exists(dataRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(dataRoot))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name) || !VersionDirectoryRegex.IsMatch(name))
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, "neow", "options.json")))
            {
                results.Add(name);
            }
        }
    }

    private static Version ParseVersionForSort(string version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
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
