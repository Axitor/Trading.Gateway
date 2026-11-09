using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api;

/// <summary>Binance Futures UsdM WebSocket API endpoints.</summary>
public sealed class WsApi<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Account endpoints.</summary>
    public Account<TTelemetry> Account { get; init; }

    /// <summary>Market Data endpoints.</summary>
    public MarketData<TTelemetry> MarketData { get; init; }

    /// <summary>Trade endpoints.</summary>
    public Trade<TTelemetry> Trade { get; init; }

    internal WsApi(BinanceRouter<TTelemetry> router)
    {
        Account = new Account<TTelemetry>(router);
        MarketData = new MarketData<TTelemetry>(router);
        Trade = new Trade<TTelemetry>(router);
    }
}
