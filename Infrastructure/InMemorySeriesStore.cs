using Domain.Services;
using Domain.Trading;

namespace Infrastructure;
public sealed class InMemorySeriesStore : ISeriesStore
{
    private readonly Dictionary<(string,string), Queue<double>> _map = new();

    public void AppendClose(Symbol s, Timeframe tf, double close)
    {
        var key = (s.Value, tf.Value);
        if (!_map.TryGetValue(key, out var q)) { q = new Queue<double>(); _map[key] = q; }
        if (q.Count >= 500) q.Dequeue();
        q.Enqueue(close);
    }

    public IReadOnlyList<double> GetCloses(Symbol s, Timeframe tf, int lastN)
    {
        var key = (s.Value, tf.Value);
        if (!_map.TryGetValue(key, out var q)) return Array.Empty<double>();
        return q.TakeLast(lastN).ToArray();
    }
}