using System.Text.Json.Serialization;
using Axitor.Trading.Gateway.Core.Models.Converters;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;

/// <summary>Symbol Price Ticker Response Model.</summary>
public struct TickerPriceModel
{
    /// <summary>Trading symbol, e.g. BTCUSDT.</summary>
    [JsonPropertyName("symbol")]
    [JsonConverter(typeof(SymbolConverter))]
    public Symbol Symbol;

    /// <summary>Latest price.</summary>
    [JsonPropertyName("price")]
    public double Price;

    /// <summary>Price time, milliseconds since the Unix epoch.</summary>
    [JsonPropertyName("time")]
    public long Time;
}
