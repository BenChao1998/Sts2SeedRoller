using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using DataExtractor;

var arguments = SimpleArguments.Parse(args);
var defaultSource = Path.Combine("Slay the Spire 2 婧愮爜", "seed_info.json");
var defaultOutput = Path.Combine("data", "neow", "options.json");
var mode = arguments.Get("--mode");

var sourcePath = arguments.Get("--source") ?? defaultSource;
var outputPath = arguments.Get("--output") ?? defaultOutput;

try
{
    if (string.Equals(mode, "extract-source", StringComparison.OrdinalIgnoreCase))
    {
        var version = arguments.Get("--version") ?? SourceVersionExtractor.InferVersionFromSourcePath(sourcePath);
        var outputRoot = arguments.Get("--output-root") ?? Path.Combine("data", version);
        var extractor = new SourceVersionExtractor(sourcePath, outputRoot, version);
        extractor.Extract();
        Console.WriteLine($"Extracted source dataset for {version} -> {outputRoot}");
        return 0;
    }

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
    Console.Error.WriteLine($"Failed to export data: {ex.Message}");
    return 1;
}
