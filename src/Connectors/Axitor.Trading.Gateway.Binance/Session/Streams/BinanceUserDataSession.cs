using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>User data of one account on its own connection. The listen key is shared by every app on the account: if another app closes it, the session reports a gap and moves to a new key.</summary>
public abstract class BinanceUserDataSession
{
    private protected BinanceUserDataSession() { }

    /// <summary>Completes on app shutdown or client Dispose; faults with WsStreamFatalException once the session stops for good.</summary>
    public abstract Task Completion { get; }

    /// <summary>
    /// Cold path. Gets the listen key and subscribes the events; the exchange sends only these. One subscription per session: to change the events, unsubscribe first.
    /// Returns once the exchange confirmed every event, or Rejected: without an answer it starts over. Canceling undoes it. Take balances and open orders after it.
    /// Throws ArgumentException when events include one the market does not send, WsStreamFatalException once the session is fatal,
    /// and WsApiFatalException when the key request fails for good.
    /// </summary>
    public abstract ValueTask<WsStreamSubscribeStatus> SubscribeAsync(BinanceUserDataEvents events, CancellationToken cancellationToken = default);

    /// <summary>Cold path. Unsubscribes every event and stops the key checks. The key itself stays open. True once the exchange confirmed.</summary>
    public abstract ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken = default);
}
