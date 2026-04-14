namespace SeedModel.Events;

public interface ISeedEventGeneratorFactory<TDataset, in TContext, out TResult>
{
    SeedEventType EventType { get; }

    string DefaultDataPath { get; }

    TDataset LoadDataset(string path);

    ISeedEventGenerator<TContext, TResult> CreateGenerator(TDataset dataset);
}
