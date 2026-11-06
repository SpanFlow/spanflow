namespace SpanFlow.Mapping.Engine;

public sealed class AggregatorRegistry
{
    private readonly Dictionary<string, IAggregator> _aggregators;

    private AggregatorRegistry(IEnumerable<IAggregator> aggregators)
    {
        _aggregators = aggregators.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The built-in aggregates. Add a new one to this list.</summary>
    public static AggregatorRegistry CreateDefault()
    {
        return new AggregatorRegistry(
        [
            new SumAggregator(),
            new CountAggregator(),
            new AvgAggregator(),
            new MinAggregator(),
            new MaxAggregator(),
            new FirstAggregator(),
            new LastAggregator(),
            new DistinctAggregator(),
            new ItemsAggregator(),
            new ProductAggregator(),
            new CountDistinctAggregator(),
            new MedianAggregator(),
            new RangeAggregator(),
            new ModeAggregator(),
            new AnyAggregator(),
            new AllAggregator(),
            new NoneAggregator(),
            new VarianceAggregator(),
            new StdDevAggregator(),
            new JoinStringAggregator(),
        ]);
    }

    public IAggregator Get(string name)
    {
        if (_aggregators.TryGetValue(name, out var aggregator))
        {
            return aggregator;
        }

        throw new InvalidOperationException($"Aggregator '{name}' is not registered.");
    }
}
