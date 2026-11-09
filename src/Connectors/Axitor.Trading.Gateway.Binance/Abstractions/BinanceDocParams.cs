using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Dummy class used exclusively for centralized XML documentation strings.</summary>
internal static class BinanceDocParams
{
    /// <summary>Dummy method.</summary>
    /// <param name="symbol">The trading symbol (e.g., "BTCUSDT").</param>
    /// <param name="interval">The time frame as the exchange name it.</param>
    /// <param name="isRetryable">
    /// Cold path for errors. Indicates whether the library should automatically retry the request upon transient failures.
    /// When true, the library retries network/transport faults, rate-limit back-offs, and exchange outages where the request failed or its execution status is unknown (any 5xx, -1000, -1001, -1006, -1007, -1008).
    /// It does not affect business-logic rejections (which are always skipped) or security/ban violations (which are always fatal).
    /// Orders: a retry resends the same clientOrderId, and the exchange matches it only against open orders.
    /// If the first attempt was already filled, fully or partly, the retry places a new order.
    /// When unsure, pass false and resend only after checking on the exchange that the order does not exist.
    /// Any skipped error gracefully returns the complete failure breakdown in the result structure for further analysis.
    /// </param>
    /// <param name="timeToLiveMs">
    /// The absolute maximum lifespan (in milliseconds) for the request across its entire resilience pipeline. Zero means unlimited.
    /// The timer starts exactly when the request enters the pipeline.
    /// Evaluated at the I/O boundary right before EVERY network attempt (both initial and retries) — after awaiting bans, network repairs, rate limits, backoff delays, and acquiring semaphore slots.
    /// If the accumulated time exceeds the TTL, the request is dropped immediately to prevent sending stale data after prolonged waits or multiple failed attempts.
    /// Note: This is not an I/O cancellation timeout. It prevents late retries and delayed requests from starting, but will not abort an already in-flight HTTP call.
    /// </param>
    /// <param name="priority">
    /// The execution priority (Normal or Critical).
    /// Critical requests will consume standard slots if available, but fall back to a dedicated reserve to ensure they are never blocked by routine traffic during heavy load.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation. Leave unset unless the operation is genuinely abandonable.
    /// DESIGN NOTE: This token represents application-wide termination, not granular request cancellation.
    /// In this low-latency engine architecture, cancellation implies the entire host process is shutting down.
    /// In algo trading, cancelling an in-flight request is dangerous. It drops the local network connection but DOES NOT cancel the operation on the exchange,
    /// leaving the bot blind to the execution state.
    /// Do not pass request-scoped tokens here.
    /// </param>
    /// <param name="clientOrderId">
    /// Required for every order. Your unique id for it, up to 36 characters.
    /// The exchange compares it only with open orders: a filled, canceled or expired order with the same id
    /// is not checked, so a fast fill followed by a resend places a second order.
    /// When unsure, pass isRetryable false and resend only after checking on the exchange that the order does not exist.
    /// </param>
    /// <param name="clientAlgoId">
    /// Required for every algo order. Your unique id for it, up to 36 characters.
    /// The exchange compares it only with open algo orders: a triggered, canceled or expired one with the same id
    /// is not checked, so a fast trigger followed by a resend places a second algo order.
    /// When unsure, pass isRetryable false and resend only after checking on the exchange that the algo order does not exist.
    /// </param>
    /// <param name="filters">
    /// Trading rules of the symbol, from exchangeInfo. Fill once per symbol and pass with every order.
    /// Prices are rounded to the nearest tick and the quantity down to the step before sending.
    /// An order that breaks a rule is not sent: it fails with OrderFiltersViolated, and Errors.RuleViolation holds its OrderRuleViolation.
    /// </param>
    /// <exception name="RestApiFatalException">
    /// Thrown when a critical, non-recoverable exchange or infrastructure error occurs.
    /// A long ban (403/418) stays in force on the client even if this exception is caught.
    /// This exception indicates a deterministic failure that cannot be resolved by retrying.
    /// It encapsulates the complete failure breakdown in the result structure for further analysis.
    /// The caller must catch this exception at the highest application level to immediately halt the connector and prevent permanent bans or security violations.
    /// </exception>
    public static void Definitions(
        Symbol symbol,
        BinanceInterval interval,
        bool isRetryable,
        int timeToLiveMs,
        RequestPriority priority,
        ClientOrderId clientOrderId,
        ClientOrderId clientAlgoId,
        SymbolFilters filters,
        CancellationToken cancellationToken
        )
    { }

    /// <summary>Dummy method for the WebSocket API.</summary>
    /// <param name="timeToLiveMs">
    /// The absolute maximum lifespan (in milliseconds) for the request across its entire resilience pipeline. Zero means unlimited.
    /// The timer starts exactly when the request enters the pipeline.
    /// Evaluated right before EVERY send (both initial and retries) — after awaiting bans, network repairs, an open connection, rate limits and backoff delays.
    /// If the accumulated time exceeds the TTL, the request is dropped immediately to prevent sending stale data after prolonged waits or multiple failed attempts.
    /// Note: A request already on the wire is not aborted; its response or the response timeout decides the outcome.
    /// </param>
    /// <param name="priority">
    /// The connection the request goes to (Normal or Critical).
    /// Critical requests use a dedicated connection for orders and fall back to the normal one while it reconnects.
    /// Normal requests never use the critical connection.
    /// </param>
    /// <exception name="WsApiFatalException">
    /// Thrown when a critical, non-recoverable exchange or connection failure occurs.
    /// A long ban (403/418) stays in force on the client even if this exception is caught.
    /// It encapsulates the complete failure breakdown for further analysis; connection-level causes are in Errors.Disconnect.
    /// The caller must catch this exception at the highest application level to immediately halt the connector and prevent permanent bans or security violations.
    /// </exception>
    /// <exception name="InvalidOperationException">
    /// Thrown when the WebSocket API of the endpoint's market is not enabled in BinanceOptions.WsApiMarkets.
    /// </exception>
    public static void WsApiDefinitions(
        int timeToLiveMs,
        RequestPriority priority
        )
    { }
}
