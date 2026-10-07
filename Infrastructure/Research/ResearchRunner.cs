namespace Infrastructure.Research;

/// <summary>Offline computation has its own bounded lane and cannot occupy the execution channel.</summary>
public sealed class ResearchRunner : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("A research run is already in progress");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await Task.Run(() => work(timeout.Token), timeout.Token);
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
