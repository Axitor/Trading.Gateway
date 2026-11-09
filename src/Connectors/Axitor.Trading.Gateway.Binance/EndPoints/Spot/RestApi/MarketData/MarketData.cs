using Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi.MarketData.OrderBook;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi.MarketData.ServerTime;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Spot.RestApi.MarketData;

/// <summary>Binance Spot REST API Market Data endpoints.</summary>
public sealed class MarketData<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Server Time endpoint.</summary>
    public ServerTimeEndpoint<TTelemetry> ServerTime { get; init; }

    /// <summary>Order Book endpoint.</summary>
    public OrderBookEndpoint<TTelemetry> OrderBook { get; init; }

    internal MarketData(BinanceRouter<TTelemetry> router)
    {
        ServerTime = new ServerTimeEndpoint<TTelemetry>(router);
        OrderBook = new OrderBookEndpoint<TTelemetry>(router);
    }
}
