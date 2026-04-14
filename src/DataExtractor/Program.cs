using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using DataExtractor;

var arguments = SimpleArguments.Parse(args);
var defaultSource = Path.Combine("Slay the Spire 2 源码", "seed_info.json");
var defaultOutput = Path.Combine("data", "neow", "options.json");

var sourcePath = arguments.Get("--source") ?? defaultSource;
var outputPath = arguments.Get("--output") ?? defaultOutput;

try
{
    var dataset = SeedInfoTransformer.ReadNeowData(sourcePath);
    var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var output = File.Create(outputPath);
    JsonSerializer.Serialize(output, dataset, serializerOptions);
    Console.WriteLine($"Exported {dataset.Options.Count} Neow options -> {outputPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to export Neow options: {ex.Message}");
    return 1;
}
