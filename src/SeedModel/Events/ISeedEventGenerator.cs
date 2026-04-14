namespace SeedModel.Events;

public interface ISeedEventGenerator<in TContext, out TResult>
{
    SeedEventType EventType { get; }

    TResult Generate(TContext context);
}
