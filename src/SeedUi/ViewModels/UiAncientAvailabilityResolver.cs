using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SeedModel.Sts2;

namespace SeedUi.ViewModels;

internal static class UiAncientAvailabilityResolver
{
    private static readonly string[] CandidateRelativePaths =
    [
        Path.Combine("存档", "progress.save"),
        "progress.save"
    ];

    public static ResolvedAncientAvailabilityResult Resolve()
    {
        foreach (var relativePath in CandidateRelativePaths)
        {
            var candidatePath = UiDataPathResolver.ResolveRelativeFilePath(relativePath);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

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
                    $"读取 progress.save 失败，已回退为默认全解锁 Ancient：{ex.Message}");
            }
        }

        return new ResolvedAncientAvailabilityResult(
            Sts2AncientAvailability.Default,
            ProgressSavePath: null,
            UsedProgressSave: false,
            RevealedEpochIds: Array.Empty<string>(),
            "未找到 progress.save，已回退为默认全解锁 Ancient（包含 DARV / OROBAS）。");
    }

    private static ResolvedAncientAvailabilityResult LoadFromProgressSave(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var revealedEpochIds = ReadRevealedEpochIds(document.RootElement);
        var availability = Sts2AncientAvailability.FromRevealedEpochIds(revealedEpochIds);

        return new ResolvedAncientAvailabilityResult(
            availability,
            path,
            UsedProgressSave: true,
            revealedEpochIds,
            $"从 progress.save 读取 Ancient 解锁：path={path}, revealed=[{string.Join(", ", revealedEpochIds)}]");
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

    internal sealed record ResolvedAncientAvailabilityResult(
        Sts2AncientAvailability Availability,
        string? ProgressSavePath,
        bool UsedProgressSave,
        IReadOnlyList<string> RevealedEpochIds,
        string Summary);
}
