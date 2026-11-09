using Axitor.Trading.Gateway.Core.Engine;

namespace Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;

/// <summary>
/// Background worker that pings the exchange when the network is down.
/// Triggered exclusively by the thread that wins the MarkDown() transition.
/// </summary>
internal sealed class NetworkRepairWorker : IDisposable
{
    private readonly NetworkGate _gate;
    private readonly HttpClient _pingClient;
    private readonly Uri _pingUri;
    private readonly TimeSpan _pingInterval;

    public NetworkRepairWorker(EngineOptions options, NetworkGate gate, Uri pingUri, TimeSpan pingInterval, HttpMessageHandler? messageHandler)
    {
        _gate = gate;
        _pingUri = pingUri;
        _pingInterval = pingInterval;

        HttpMessageHandler handler = messageHandler ?? new SocketsHttpHandler();

        // A separate http client for exchange pings.
        _pingClient = new HttpClient(handler, disposeHandler: true) { Timeout = options.RestApi.Network.RequestTimeout };
    }

    /// <summary>Spawns a background task to poll the network. Does not block the caller.</summary>
    /// <remarks>
    /// DESIGN NOTE: No dedicated CancellationTokenSource or graceful teardown state machine is used here.
    /// This is a low-latency fintech library, not an ASP.NET web app. The provided token is the global
    /// host application shutdown token. If it triggers, or if the worker is Disposed, the entire application
    /// is terminating. Intentionally ignore edge cases like ObjectDisposedException or orphaned tasks
    /// during teardown, as the process memory space is being destroyed anyway.
    /// </remarks>
    public void StartRepairLoop(CancellationToken appCancellationToken)
    {
        // Fire-and-forget: run once on network down.
        _ = Task.Run(() => RepairLoopAsync(appCancellationToken), appCancellationToken);
    }

    private async Task RepairLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (await PingExchangeAsync(ct).ConfigureAwait(false))
                {
                    // Repaired.
                    _gate.MarkHealthy();
                    return;
                }

                // Wait to prevent WAF spam.
                await Task.Delay(_pingInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> PingExchangeAsync(CancellationToken ct)
    {
        try
        {
            // Don't need response body, any successful status code means network is up.
            using HttpRequestMessage request = new(HttpMethod.Get, _pingUri);
            using HttpResponseMessage response = await _pingClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Any HTTP or Socket error means network is down.
            return false;
        }
    }

    /// <summary>Dispose IDisposable fields.</summary>
    public void Dispose()
    {
        _pingClient.Dispose();
    }
}
