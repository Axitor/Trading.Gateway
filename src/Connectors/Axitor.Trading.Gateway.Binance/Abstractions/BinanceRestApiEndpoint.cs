using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Abstraction for a Binance REST API endpoint</summary>
public abstract class BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private protected readonly BinanceRouter<TTelemetry> _router;
    private protected readonly byte[] _host;

    private protected BinanceRestApiEndpoint(BinanceRouter<TTelemetry> router, byte[] host)
    {
        _router = router;
        _host = host;
    }
}
