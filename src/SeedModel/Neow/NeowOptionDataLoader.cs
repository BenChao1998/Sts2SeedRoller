using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeedModel.Neow;

public static class NeowOptionDataLoader
{
    private static readonly Regex VersionRegex = new(@"\b\d+\.\d+\.\d+\b", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static NeowOptionDataset LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var dataset = Load(stream);
        dataset.Version ??= InferVersionFromPath(path);
        return dataset;
    }

    public static NeowOptionDataset Load(Stream stream)
    {
        var dataset = JsonSerializer.Deserialize<NeowOptionDataset>(stream, SerializerOptions);
        if (dataset == null || dataset.Options.Count == 0)
        {
            throw new InvalidDataException("No Neow options were found in the provided dataset.");
        }
        return dataset;
    }

    private static string? InferVersionFromPath(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse())
        {
            var match = VersionRegex.Match(segment);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }
}
