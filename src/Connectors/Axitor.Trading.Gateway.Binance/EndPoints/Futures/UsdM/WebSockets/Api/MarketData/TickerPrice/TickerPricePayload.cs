using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;

/// <summary>Symbol Price Ticker Endpoint PayLoad Writer.</summary>
internal readonly struct TickerPricePayload : IWsApiRequestWriter
{
    public Symbol Symbol { get; init; }

    /// <inheritdoc />
    public static ReadOnlySpan<byte> Method => "ticker.price"u8;

    /// <inheritdoc />
    public void Write(ref WsApiRequestBuilder builder) => builder.AppendSymbol("symbol"u8, Symbol);
}
