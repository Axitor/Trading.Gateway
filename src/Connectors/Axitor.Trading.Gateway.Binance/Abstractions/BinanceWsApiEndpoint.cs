using System.Text;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Abstraction for a Binance WebSocket API endpoint.</summary>
public abstract class BinanceWsApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private protected readonly BinanceRouter<TTelemetry> _router;

    /// <summary>Method name for telemetry, built once.</summary>
    private protected readonly string _method;

    private protected BinanceWsApiEndpoint(BinanceRouter<TTelemetry> router, ReadOnlySpan<byte> method)
    {
        _router = router;
        _method = Encoding.UTF8.GetString(method);
    }
}
