using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Receives the data of one stream type. Implement as a readonly struct; keep state in a class it holds.</summary>
/// <remarks>
/// <para>Calls run on the receive loop: return fast and never throw. A thrown exception stops the session for good. Hand heavy work to a channel.</para>
/// <para>Calls for one subscription never overlap. Subscriptions on different connections may be handled on different threads at once.</para>
/// </remarks>
public interface IBinanceStreamHandler<TModel> where TModel : struct
{
    /// <summary>One message of the subscription. Duplicates from an overlapping reconnect are already dropped.</summary>
    void OnData(int subscription, in TModel model);

    /// <summary>Data of the subscription stopped. A fatal stop never resumes; the session Completion faults.</summary>
    void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal);

    /// <summary>Data flows again, but messages inside the gap are lost: rebuild any state made from them, e.g. a local order book. Can come just after the first new data.</summary>
    void OnReconnected(int subscription, in WsStreamGap gap);

    /// <summary>The subscription was unsubscribed: nothing comes for it after this call, and its handle may go to another stream. Runs on the thread that unsubscribed.</summary>
    void OnUnsubscribed(int subscription);
}
