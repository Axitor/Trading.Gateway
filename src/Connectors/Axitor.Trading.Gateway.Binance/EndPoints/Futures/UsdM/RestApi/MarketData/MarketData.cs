using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.KlinesData;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.OrderBook;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ServerTime;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData;

/// <summary>Binance Futures UsdM REST API Market Data endpoints.</summary>
public sealed class MarketData<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>Server Time endpoint.</summary>
    public ServerTimeEndpoint<TTelemetry> ServerTime { get; init; }

    /// <summary>Klines/Candlestick Data endpoint.</summary>
    public KlinesDataEndpoint<TTelemetry> KlinesData { get; init; }

    /// <summary>Exchange Information endpoint.</summary>
    public ExchangeInfoEndpoint<TTelemetry> ExchangeInfo { get; init; }

    /// <summary>Order Book endpoint.</summary>
    public OrderBookEndpoint<TTelemetry> OrderBook { get; init; }

    internal MarketData(BinanceRouter<TTelemetry> router)
    {
        ServerTime = new ServerTimeEndpoint<TTelemetry>(router);
        KlinesData = new KlinesDataEndpoint<TTelemetry>(router);
        ExchangeInfo = new ExchangeInfoEndpoint<TTelemetry>(router);
        OrderBook = new OrderBookEndpoint<TTelemetry>(router);
    }
}
