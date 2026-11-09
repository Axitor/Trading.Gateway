using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Serialization;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;

/// <summary>Symbol Price Ticker Endpoint Parser.</summary>
internal readonly struct TickerPriceParser : IWsApiResultParser<TickerPriceModel>
{
    /// <inheritdoc />
    public static TickerPriceModel Parse(ReadOnlySpan<byte> result) =>
        JsonSerializer.Deserialize(result, BinanceJsonContext.Default.TickerPriceModel);
}
