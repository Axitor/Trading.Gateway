using Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData;

/// <summary>Binance Spot WebSocket API Market Data endpoints.</summary>
public sealed class MarketData<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Symbol Price Ticker endpoint.</summary>
    public TickerPriceEndpoint<TTelemetry> TickerPrice { get; init; }

    internal MarketData(BinanceRouter<TTelemetry> router)
    {
        TickerPrice = new TickerPriceEndpoint<TTelemetry>(router);
    }
}
