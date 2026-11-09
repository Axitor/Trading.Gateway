using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.KlinesData;

/// <summary>Klines/Candlestick Data.</summary>
public sealed class KlinesDataEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal KlinesDataEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Writes Kline/candlestick bars of a symbol into the buffer, oldest first, and returns how many were written. Klines are uniquely identified by their open time.
    /// The buffer length is the limit, 1 to 1500: reuse one buffer and slice it for smaller requests. Nothing is allocated for the candles.
    /// Do not touch the buffer while the request is in flight; a retry rewrites it from the start.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Weight: Scales with the limit, below 100 = 1, below 500 = 2, up to 1000 = 5, above = 10.
    /// Security: Public endpoint.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="interval"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='interval']/node()"/></param>
    /// <param name="destination">Buffer the candles are written into. Its length is the number of candles asked for.</param>
    /// <param name="startTimeMs">The timestamp of the oldest candle to include, in milliseconds since the Unix epoch. Leave null to omit and fetch the most recent data.</param>
    /// <param name="endTimeMs">The timestamp of the newest candle to include, in milliseconds since the Unix epoch. Leave null to omit and fetch the most recent data.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<int>> GetAsync(
        Symbol symbol,
        BinanceInterval interval,
        Memory<Kline> destination,
        long? startTimeMs = null,
        long? endTimeMs = null,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default
        )
    {
        int limit = destination.Length;

        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = (limit < 100 ? 1 : (limit < 500 ? 2 : (limit <= 1000 ? 5 : 10))),
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = false,
            NeedsSignature = false,
        };

        KlinesDataPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
            Interval = interval,
            Limit = limit,
            StartTimeMs = startTimeMs,
            EndTimeMs = endTimeMs
        };

        return _router.SendRestApiPublicAsync<KlinesDataPayload, KlinesDataParser, int>(descriptor, payload, cancellationToken, new KlinesDataParser(destination));
    }

}
