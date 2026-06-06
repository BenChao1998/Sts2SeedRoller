using SeedModel.Sts2.RunValidation;

var tests = new (string Name, Action Test)[]
{
    ("RunValidationOptions_DefaultsToFullRun", RunValidationOptions_DefaultsToFullRun),
    ("RunValidationOptions_CanRequestOpeningOnly", RunValidationOptions_CanRequestOpeningOnly),
    ("RunValidationResult_ExposesValidationDataVersion", RunValidationResult_ExposesValidationDataVersion),
    ("RunValidationFloorResult_AllowsOpeningOptionCategory", RunValidationFloorResult_AllowsOpeningOptionCategory),
    ("ValidateFile_InvalidDataVersionThrows", ValidateFile_InvalidDataVersionThrows),
    ("ValidateFile_OpeningOnlyReturnsOpeningOptionRows", ValidateFile_OpeningOnlyReturnsOpeningOptionRows),
    ("ValidateFile_1780713040OpeningOptionsMatch", ValidateFile_1780713040OpeningOptionsMatch)
};

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
        return;
    }
}

static void RunValidationOptions_DefaultsToFullRun()
{
    var options = new Sts2RunValidationOptions();
    Assert(options.DataVersionOverride == null, "Default data version override should be null.");
    Assert(options.Scope == Sts2RunValidationScope.FullRun, "Default scope should be FullRun.");
}

static void RunValidationOptions_CanRequestOpeningOnly()
{
    var options = new Sts2RunValidationOptions
    {
        DataVersionOverride = "0.107.1",
        Scope = Sts2RunValidationScope.OpeningOptionsOnly
    };

    Assert(options.DataVersionOverride == "0.107.1", "Data version override should be retained.");
    Assert(options.Scope == Sts2RunValidationScope.OpeningOptionsOnly, "Scope should be OpeningOptionsOnly.");
}

static void RunValidationResult_ExposesValidationDataVersion()
{
    var result = new Sts2RunValidationResult
    {
        FilePath = "sample.run",
        RunId = 1,
        GameVersion = "0.106.1",
        ValidationDataVersion = "0.107.1",
        ValidationScope = Sts2RunValidationScope.OpeningOptionsOnly,
        SeedText = "0",
        CharacterId = "IRONCLAD",
        Ascension = 0,
        PlayerCount = 1,
        Floors = 1,
        FinalRelicMatch = true,
        GeneratedComparisons = 1,
        GeneratedMatches = 1,
        GeneratedMismatches = 0,
        CardMatches = 0,
        RelicMatches = 0,
        ShopRelicMatches = 0,
        ShopPotionMatches = 0,
        FirstMismatchFloor = null,
        MarkdownReport = "",
        FloorsResults =
        [
            new Sts2RunValidationFloorResult
            {
                Floor = 1,
                RoomType = "A",
                Category = Sts2RunValidationCategories.OpeningOption,
                IsMatch = true,
                Expected = "NEOWS_BONES",
                Generated = "NEOWS_BONES",
                Notes = ""
            }
        ]
    };

    Assert(result.ValidationDataVersion == "0.107.1", "Validation data version should be exposed.");
    Assert(result.ValidationScope == Sts2RunValidationScope.OpeningOptionsOnly, "Validation scope should be exposed.");
    Assert(result.FloorsResults.All(row => row.Category == Sts2RunValidationCategories.OpeningOption), "Rows should expose opening option category.");
}

static void RunValidationFloorResult_AllowsOpeningOptionCategory()
{
    var row = new Sts2RunValidationFloorResult
    {
        Floor = 18,
        RoomType = "A",
        Category = Sts2RunValidationCategories.OpeningOption,
        IsMatch = null,
        Expected = "",
        Generated = "",
        Notes = "未到达/无记录"
    };

    Assert(row.MatchText == "仅记录", "Null match should be displayed as record-only.");
}

static void ValidateFile_InvalidDataVersionThrows()
{
    var runFile = FindSampleRunFile();
    if (runFile == null)
    {
        Console.WriteLine("SKIP ValidateFile_InvalidDataVersionThrows: no local .run fixture found.");
        return;
    }

    try
    {
        Sts2RunValidationService.ValidateFile(runFile, FindWorkspaceRoot(), new Sts2RunValidationOptions
        {
            DataVersionOverride = "9.9.9",
            Scope = Sts2RunValidationScope.OpeningOptionsOnly
        });
    }
    catch (FileNotFoundException ex) when (ex.Message.Contains("9.9.9", StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException("Invalid data version should throw FileNotFoundException mentioning the requested version.");
}

static void ValidateFile_OpeningOnlyReturnsOpeningOptionRows()
{
    var runFile = FindSampleRunFile();
    if (runFile == null)
    {
        Console.WriteLine("SKIP ValidateFile_OpeningOnlyReturnsOpeningOptionRows: no local .run fixture found.");
        return;
    }

    var result = Sts2RunValidationService.ValidateFile(runFile, FindWorkspaceRoot(), new Sts2RunValidationOptions
    {
        DataVersionOverride = "0.107.1",
        Scope = Sts2RunValidationScope.OpeningOptionsOnly
    });

    Assert(result.ValidationDataVersion == "0.107.1", "Opening-only validation should use the requested data version.");
    Assert(result.ValidationScope == Sts2RunValidationScope.OpeningOptionsOnly, "Opening-only validation should expose its scope.");
    Assert(result.FloorsResults.Count > 0, "Opening-only validation should produce at least one row.");
    Assert(result.FloorsResults.All(row => row.Category == Sts2RunValidationCategories.OpeningOption || row.MatchText == "仅记录"),
        "Opening-only validation should not produce regular reward/shop rows.");
}

static void ValidateFile_1780713040OpeningOptionsMatch()
{
    var runFile = Path.Combine(FindWorkspaceRoot(), "存档", "1780713040.run");
    if (!File.Exists(runFile))
    {
        Console.WriteLine("SKIP ValidateFile_1780713040OpeningOptionsMatch: run file not found.");
        return;
    }

    var result = Sts2RunValidationService.ValidateFile(runFile, FindWorkspaceRoot(), new Sts2RunValidationOptions
    {
        DataVersionOverride = "0.107.1",
        Scope = Sts2RunValidationScope.OpeningOptionsOnly
    });

    Assert(result.FloorsResults
        .Where(row => row.IsMatch.HasValue)
        .All(row => row.IsMatch == true), "1780713040 opening options should match generated 0.107.1 openings.");
}

static string? FindSampleRunFile()
{
    var archiveDir = Path.Combine(FindWorkspaceRoot(), "存档");
    return Directory.Exists(archiveDir)
        ? Directory.EnumerateFiles(archiveDir, "*.run", SearchOption.AllDirectories).FirstOrDefault()
        : null;
}

static string FindWorkspaceRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (File.Exists(Path.Combine(current, "Sts2SeedRoller.sln")))
        {
            return current;
        }

        var parent = Directory.GetParent(current)?.FullName;
        if (parent == current)
        {
            break;
        }

        current = parent ?? string.Empty;
    }

    return Directory.GetCurrentDirectory();
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
