using Domain.Services.Interfaces;
using Domain.Trading;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Memory;
public sealed class InMemorySeriesStore(ILogger<InMemorySeriesStore> logger) : ISeriesStore
{
    private readonly Dictionary<(string,string), Queue<double>> _map = new();

    public void AppendClose(Symbol s, Timeframe tf, double close)
    {
        var key = (s.Value, tf.Value);
        if (!_map.TryGetValue(key, out var q)) { q = new Queue<double>(); _map[key] = q; }
        if (q.Count >= 500) q.Dequeue();
        q.Enqueue(close);
        logger.LogDebug("Append {Symbol}/{TF} Close={Close}", s.Value, tf.Value, close);
    }

    public IReadOnlyList<double> GetCloses(Symbol s, Timeframe tf, int lastN)
    {
        var key = (s.Value, tf.Value);
        if (!_map.TryGetValue(key, out var q)) return Array.Empty<double>();
        return q.TakeLast(lastN).ToArray();
    }
}