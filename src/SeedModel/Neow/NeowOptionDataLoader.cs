using System.IO;
using System.Text.Json;

namespace SeedModel.Neow;

public static class NeowOptionDataLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static NeowOptionDataset LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
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
}
