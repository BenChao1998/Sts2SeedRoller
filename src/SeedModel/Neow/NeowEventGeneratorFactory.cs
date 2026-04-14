using SeedModel.Events;

namespace SeedModel.Neow;

public sealed class NeowEventGeneratorFactory :
    ISeedEventGeneratorFactory<NeowOptionDataset, NeowGenerationContext, IReadOnlyList<NeowOptionResult>>
{
    public SeedEventType EventType => SeedEventType.Act1Neow;

    public string DefaultDataPath => SeedEventRegistry.Get(EventType).DefaultDataPath;

    public NeowOptionDataset LoadDataset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Dataset path must be provided.", nameof(path));
        }

        return NeowOptionDataLoader.LoadFromFile(path);
    }

    public ISeedEventGenerator<NeowGenerationContext, IReadOnlyList<NeowOptionResult>> CreateGenerator(NeowOptionDataset dataset)
    {
        if (dataset is null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        return new NeowGenerator(dataset);
    }
}
