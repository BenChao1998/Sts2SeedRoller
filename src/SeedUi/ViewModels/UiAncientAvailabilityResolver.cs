using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal static class UiAncientAvailabilityResolver
{
    public static ResolvedAncientAvailabilityResult Resolve(string? preferredProgressSavePath = null)
    {
        foreach (var candidatePath in EnumerateProgressSaveCandidates(preferredProgressSavePath))
        {
            try
            {
                return LoadFromProgressSave(candidatePath);
            }
            catch (Exception ex)
            {
                return new ResolvedAncientAvailabilityResult(
                    Sts2AncientAvailability.Default,
                    candidatePath,
                    UsedProgressSave: false,
                    RevealedEpochIds: Array.Empty<string>(),
                    $"读取 progress.save 失败，已回退为默认全解锁古神规则：路径={candidatePath}，错误={ex.Message}");
            }
        }

        return new ResolvedAncientAvailabilityResult(
            Sts2AncientAvailability.Default,
            ProgressSavePath: null,
            UsedProgressSave: false,
            RevealedEpochIds: Array.Empty<string>(),
            "未找到 progress.save，已回退为默认全解锁古神规则（包含 DARV / OROBAS）。");
    }

    private static IEnumerable<string> EnumerateProgressSaveCandidates(string? preferredProgressSavePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredProgressSavePath))
        {
            yield return Path.GetFullPath(preferredProgressSavePath);
            yield break;
        }

        var localOverride = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "progress.save"));
        if (File.Exists(localOverride))
        {
            yield return localOverride;
        }

        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(appData))
        {
            yield break;
        }

        var root = Path.Combine(appData, "SlayTheSpire2");
        if (!Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(root, "progress.save", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(
                    Path.Combine("saves", "progress.save"),
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Contains(
                    $"{Path.DirectorySeparatorChar}modded{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0)
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static ResolvedAncientAvailabilityResult LoadFromProgressSave(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var revealedEpochIds = ReadRevealedEpochIds(document.RootElement);
        var discoveredActIds = ReadDiscoveredActIds(document.RootElement);
        var availability = Sts2AncientAvailability.FromProgressState(revealedEpochIds, discoveredActIds);

        return new ResolvedAncientAvailabilityResult(
            availability,
            path,
            UsedProgressSave: true,
            revealedEpochIds,
            $"已从 progress.save 读取古神解锁：路径={path}，已识别纪元=[{string.Join(", ", revealedEpochIds)}]");
    }

    private static IReadOnlyList<string> ReadRevealedEpochIds(JsonElement root)
    {
        if (!root.TryGetProperty("epochs", out var epochs) || epochs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var epoch in epochs.EnumerateArray())
        {
            if (!epoch.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!epoch.TryGetProperty("state", out var stateElement) ||
                stateElement.ValueKind != JsonValueKind.String ||
                !string.Equals(stateElement.GetString(), "revealed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result.Add(id.Trim());
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadDiscoveredActIds(JsonElement root)
    {
        if (!root.TryGetProperty("discovered_acts", out var discoveredActs) ||
            discoveredActs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var act in discoveredActs.EnumerateArray())
        {
            if (act.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = act.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result.Add(id.Trim());
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal sealed record ResolvedAncientAvailabilityResult(
        Sts2AncientAvailability Availability,
        string? ProgressSavePath,
        bool UsedProgressSave,
        IReadOnlyList<string> RevealedEpochIds,
        string Summary);
}
