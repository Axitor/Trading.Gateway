using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>Subscriptions of one stream type on their own connections. Create it from the stream endpoint and keep it as long as the client.</summary>
public abstract class BinanceStreamSession
{
    private protected BinanceStreamSession() { }

    /// <summary>Completes on app shutdown or client Dispose; faults with WsStreamFatalException once the session stops for good.</summary>
    public abstract Task Completion { get; }

    /// <summary>
    /// Cold path. Subscribes the stream of a symbol; the handle in the result identifies its data in the handler.
    /// Throws WsStreamFatalException once the session is fatal.
    /// </summary>
    public abstract ValueTask<WsStreamSubscribeResult> SubscribeAsync(Symbol symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cold path. Unsubscribes and frees the handle whatever the reply. True once the exchange confirmed.
    /// Throws WsStreamFatalException once the session is fatal.
    /// </summary>
    public abstract ValueTask<bool> UnsubscribeAsync(int subscription, CancellationToken cancellationToken = default);
}
