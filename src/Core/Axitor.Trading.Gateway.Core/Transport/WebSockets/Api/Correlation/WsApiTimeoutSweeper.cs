using Axitor.Trading.Gateway.Core.Time;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Correlation;

/// <summary>Background loop that times out late responses in the tables of the transport.</summary>
internal sealed class WsApiTimeoutSweeper : IDisposable
{
    private readonly WsApiPendingTable? _critical;
    private readonly WsApiPendingTable _normal;
    private readonly PeriodicTimer _timer;

    /// <summary>Creates a sweeper over the Critical and Normal tables. Critical is null for a single-lane transport.</summary>
    public WsApiTimeoutSweeper(WsApiPendingTable? critical, WsApiPendingTable normal, TimeSpan interval)
    {
        _critical = critical;
        _normal = normal;
        _timer = new PeriodicTimer(interval);
    }

    /// <summary>Starts the loop. Runs until the app token fires or the sweeper is disposed.</summary>
    public void Start(CancellationToken appCancellationToken) => _ = RunAsync(appCancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                long nowTicks = SysTime.GetTimestamp();
                _critical?.Sweep(nowTicks);
                _normal.Sweep(nowTicks);
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
    }

    /// <summary>Stops the loop.</summary>
    public void Dispose() => _timer.Dispose();
}
