using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.WsApi;

/// <summary>Spot user data of one account on its own WebSocket API connection. Create it from the spot WebSocket API and keep it as long as the client.</summary>
public abstract class BinanceSpotUserDataSession
{
    private protected BinanceSpotUserDataSession() { }

    /// <summary>Completes on app shutdown or client Dispose; faults with WsApiFatalException once the session stops for good.</summary>
    public abstract Task Completion { get; }

    /// <summary>
    /// Cold path. Subscribes the user data of the account; every event comes, the exchange has no filter. Take balances and open orders after it, not before.
    /// Returns once the exchange confirmed it, or Rejected: nothing is subscribed. It waits for the session connection and asks again while no answer comes.
    /// Canceling undoes it: no event reaches the handler.
    /// Throws WsApiFatalException once the session is fatal.
    /// </summary>
    public abstract ValueTask<WsStreamSubscribeStatus> SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cold path. Unsubscribes; no event reaches the handler once the call starts. True once the exchange confirmed, or when no connection is open.
    /// Throws WsApiFatalException once the session is fatal.
    /// </summary>
    public abstract ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken = default);
}
