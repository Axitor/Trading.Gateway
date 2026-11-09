using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Receives order book changes of a depth stream. Implement as a readonly struct; keep state in a class it holds.</summary>
/// <remarks>
/// <para>Calls run on the receive loop: return fast and never throw. A thrown exception stops the session for good. Hand heavy work to a channel.</para>
/// <para>Calls for one subscription never overlap. Subscriptions on different connections may be handled on different threads at once.</para>
/// </remarks>
public interface IBinanceDepthHandler
{
    /// <summary>One update. Each level holds the new quantity at its price; zero removes the level. The spans are valid only during the call.</summary>
    void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks);

    /// <summary>Data of the subscription stopped. A fatal stop never resumes; the session Completion faults.</summary>
    void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal);

    /// <summary>Data flows again, but updates inside the gap are lost: a local order book must be rebuilt from a new snapshot. Can come just after the first new data.</summary>
    void OnReconnected(int subscription, in WsStreamGap gap);

    /// <summary>The subscription was unsubscribed: nothing comes for it after this call, and its handle may go to another stream. Runs on the thread that unsubscribed.</summary>
    void OnUnsubscribed(int subscription);
}
