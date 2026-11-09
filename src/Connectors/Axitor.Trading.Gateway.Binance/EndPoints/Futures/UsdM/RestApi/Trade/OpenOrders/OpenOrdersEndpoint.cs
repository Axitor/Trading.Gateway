using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.OpenOrders;

/// <summary>Current All Open Orders. Signed endpoint.</summary>
public sealed class OpenOrdersEndpoint<TTelemetry> : BinanceRestApiEndpoint<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    private const int SymbolWeight = 1;
    private const int AllSymbolsWeight = 40;

    internal OpenOrdersEndpoint(BinanceRouter<TTelemetry> router) : base(router, router.Options.Uri.FuturesUsdRestApi) { }

    /// <summary>
    /// Open orders of a symbol. Stop and take profit orders placed through NewAlgoOrder are listed by OpenAlgoOrders.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 1. Symbol.Empty asks for every symbol and weighs 40, as GetAllAsync does.
    /// </summary>
    /// <param name="symbol"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='symbol']/node()"/></param>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OpenOrdersModel>> GetAsync(
        Symbol symbol,
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default) =>
        SendAsync(symbol, isRetryable, timeToLiveMs, priority, cancellationToken);

    /// <summary>
    /// Open orders of every symbol. Heavy: prefer GetAsync per symbol.
    /// Note: Params are applied without validation; ensure your custom parameters are strictly correct to prevent runtime failures.
    /// Security: Signed endpoint.
    /// Weight: 40.
    /// </summary>
    /// <param name="isRetryable"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='isRetryable']/node()"/></param>
    /// <param name="timeToLiveMs"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='timeToLiveMs']/node()"/></param>
    /// <param name="priority"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='priority']/node()"/></param>
    /// <param name="cancellationToken"><inheritdoc cref="BinanceDocParams.Definitions" path="/param[@name='cancellationToken']/node()"/></param>
    /// <exception><inheritdoc cref="BinanceDocParams.Definitions" path="/exception[@name='RestApiFatalException']/node()"/></exception>
    public ValueTask<RestApiRequestResult<OpenOrdersModel>> GetAllAsync(
        bool isRetryable = true,
        int timeToLiveMs = 0,
        RequestPriority priority = RequestPriority.Normal,
        CancellationToken cancellationToken = default) =>
        SendAsync(Symbol.Empty, isRetryable, timeToLiveMs, priority, cancellationToken);

    private ValueTask<RestApiRequestResult<OpenOrdersModel>> SendAsync(
        Symbol symbol,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        CancellationToken cancellationToken)
    {
        RestApiEndpointDescriptor descriptor = new()
        {
            // No symbol asks for every symbol, which the exchange weighs 40.
            Weight = symbol.IsEmpty ? AllSymbolsWeight : SymbolWeight,
            HttpMethod = HttpMethod.Get,
            LimitBucket = (int)BinanceRateLimitBucket.FuturesRequestWeight,
            Market = (int)BinanceMarkets.FuturesUsdM,
            Priority = priority,
            TTL = timeToLiveMs,
            IsRetryable = isRetryable,
            NeedsTimestamp = true,
            NeedsSignature = true,
        };

        OpenOrdersPayload payload = new()
        {
            Host = _host,
            Symbol = symbol,
        };

        return _router.SendRestApiSignedAsync<OpenOrdersPayload, OpenOrdersParser, OpenOrdersModel>(descriptor, payload, cancellationToken);
    }
}
