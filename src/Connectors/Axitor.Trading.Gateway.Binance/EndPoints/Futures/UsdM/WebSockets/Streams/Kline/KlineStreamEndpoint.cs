using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Parsing.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Streams.Kline;

/// <summary>Kline/Candlestick Streams. Market route.</summary>
public sealed class KlineStreamEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private readonly BinanceRouter<TTelemetry> _router;

    internal KlineStreamEndpoint(BinanceRouter<TTelemetry> router) => _router = router;

    /// <summary>
    /// Creates a session for the current candle of each symbol at one interval, pushed up to every 250 ms, with a final push once it closes.
    /// Each message holds the whole candle, so LatestCache fits when only the latest state matters. Its connections open on the first subscription.
    /// Note: Throws InvalidOperationException when USD-M futures are left out of BinanceOptions.StreamMarkets,
    /// and ArgumentOutOfRangeException for OneSecond, which streams do not carry.
    /// </summary>
    /// <param name="handler">Receives the data on the receive loop: must return fast and never throw.</param>
    /// <param name="interval">Candle interval of every subscription in the session.</param>
    public BinanceStreamSession CreateSession<THandler>(THandler handler, BinanceInterval interval)
        where THandler : struct, IBinanceStreamHandler<KlineUpdateModel> =>
        _router.CreateStreamSession<FuturesKlineParser, KlineUpdateModel, THandler>(
            BinanceMarkets.FuturesUsdM, _router.Options.Uri.FuturesUsdWebSocketStreamMarket, BuildNameSuffix(interval), handler);

    /// <summary>Cold path. Suffix such as "@kline_1m".</summary>
    private static byte[] BuildNameSuffix(BinanceInterval interval)
    {
        ReadOnlySpan<byte> name = interval.ToUtf8Bytes();

        if (interval == BinanceInterval.OneSecond || name.IsEmpty)
        {
            ThrowIntervalNotStreamed(interval);
        }

        return [.. "@kline_"u8, .. name];
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIntervalNotStreamed(BinanceInterval interval) =>
        throw new ArgumentOutOfRangeException(nameof(interval), interval, "The kline stream does not carry this interval.");
}
