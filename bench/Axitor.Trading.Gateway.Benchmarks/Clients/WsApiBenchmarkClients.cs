using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.NoOp;
using Binance.Net.Clients;

namespace Axitor.Trading.Gateway.Benchmarks.Clients;

/// <summary>Our client with the USD-M WebSocket API on, the Binance.Net socket client and a bare socket, all against the stub.</summary>
public sealed class WsApiBenchmarkClients : IDisposable
{
    public WsApiBenchmarkClients()
    {
        // No streams. The watchdog runs since the WebSocket API is on, but its stream has no recording and never arms.
        Axitor = new BinanceClient<NoOpTelemetryHandler>(
            BenchmarkClients.CreateAxitorOptions(BinanceMarkets.None, BinanceMarkets.FuturesUsdM), new EngineOptions());
        BinanceNet = BenchmarkClients.CreateBinanceNetWsApiClient();
        Raw = new RawWsApiClient();
    }

    public BinanceClient<NoOpTelemetryHandler> Axitor { get; }

    public BinanceSocketClient BinanceNet { get; }

    /// <summary>Not connected until ConnectAsync.</summary>
    public RawWsApiClient Raw { get; }

    public void Dispose()
    {
        Axitor.Dispose();
        BinanceNet.Dispose();
        Raw.Dispose();
    }
}
