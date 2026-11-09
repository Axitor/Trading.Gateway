using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Parsing.Depth;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.OrderBook;

/// <summary>Order Book. Public endpoint.</summary>
public sealed class OrderBookEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    internal OrderBookEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Order book snapshot of a symbol, RPI orders not included. A local order book starts from it and applies depth stream updates on top.
    /// Cold path: the answer allocates its level arrays.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Public endpoint.
    /// Weight: limit 5, 10, 20, 50 = 2; 100 = 5; 500 = 10; 1000 = 20. Also counts toward BinanceLimits.FuturesDepthSnapshotWeightPercent.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="limit">Levels per side: 5, 10, 20, 50, 100, 500 or 1000. Default is 500. Request weight grows with it.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OrderBookModel>> GetAsync(
        Symbol symbol,
        int limit = 500,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = limit <= 50 ? 2 : (limit <= 100 ? 5 : (limit <= 500 ? 10 : 20)),
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)(BinanceRateLimitBucket.FuturesRequestWeight | BinanceRateLimitBucket.FuturesDepthSnapshotWeight),
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = false,
            NeedsSignature = false,
        };

        OrderBookPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
            Limit = limit,
        };

        return _router.SendRestApiPublicAsync<OrderBookPayload, FuturesOrderBookParser, OrderBookModel>(descriptor, payload, cancellationToken);
    }

    /// <summary>
    /// Order book snapshot of a symbol written into the caller's buffers, best first. RPI orders not included. The answer gives the level counts; nothing is allocated for the levels.
    /// Each buffer must hold at least limit levels, or ArgumentOutOfRangeException is thrown. Reuse the same pair across snapshots, and do not touch them while the request is in flight; a retry rewrites them from the start.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Public endpoint.
    /// Weight: limit 5, 10, 20, 50 = 2; 100 = 5; 500 = 10; 1000 = 20. Also counts toward BinanceLimits.FuturesDepthSnapshotWeightPercent.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="bids">Buffer the bids are written into, best first.</param>
    /// <param name="asks">Buffer the asks are written into, best first.</param>
    /// <param name="limit">Levels per side: 5, 10, 20, 50, 100, 500 or 1000. Default is 500. Request weight grows with it.</param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OrderBookSnapshotModel>> GetAsync(
        Symbol symbol,
        Memory<DepthLevel> bids,
        Memory<DepthLevel> asks,
        int limit = 500,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bids.Length, limit, nameof(bids));
        ArgumentOutOfRangeException.ThrowIfLessThan(asks.Length, limit, nameof(asks));

        RestApiEndpointDescriptor descriptor = new()
        {
            Weight = limit <= 50 ? 2 : (limit <= 100 ? 5 : (limit <= 500 ? 10 : 20)),
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)(BinanceRateLimitBucket.FuturesRequestWeight | BinanceRateLimitBucket.FuturesDepthSnapshotWeight),
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = false,
            NeedsSignature = false,
        };

        OrderBookPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
            Limit = limit,
        };

        return _router.SendRestApiPublicAsync<OrderBookPayload, FuturesOrderBookBufferParser, OrderBookSnapshotModel>(
            descriptor, payload, cancellationToken, new FuturesOrderBookBufferParser(bids, asks));
    }
}
