using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Handlers;

/// <summary>
/// Your own stream handler. Must be a readonly struct, so its state lives in an object it holds: here an array of mid prices.
/// Calls run on the receive loop: return fast and never throw. Calls for one subscription never overlap.
/// </summary>
internal readonly struct SampleBookTickerHandler : IBinanceStreamHandler<BookTickerModel>
{
    // Last mid price per subscription handle. Zero means no live price.
    private readonly double[] _midPrices;

    /// <summary>Pass BinanceClient.StreamSessionCapacity.</summary>
    public SampleBookTickerHandler(int capacity) => _midPrices = new double[capacity];

    /// <summary>Last mid price of a subscription, read from any thread. Zero when there is no live price.</summary>
    public double GetMidPrice(int subscription) => Volatile.Read(ref _midPrices[subscription]);

    public void OnData(int subscription, in BookTickerModel model) =>
        Volatile.Write(ref _midPrices[subscription], (model.BidPrice + model.AskPrice) / 2);

    // Data stopped: the last price is stale now, so drop it.
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal)
    {
        Volatile.Write(ref _midPrices[subscription], 0);
        Console.WriteLine($"Book ticker {subscription} stopped: {reason}, fatal {isFatal}");
    }

    // Data flows again. Prices inside the gap are lost; the next message brings a fresh one.
    public void OnReconnected(int subscription, in WsStreamGap gap) =>
        Console.WriteLine($"Book ticker {subscription} is back after {gap.EndUtcMillis - gap.StartUtcMillis} ms");

    // Nothing comes for this handle after this call, and it may go to another symbol.
    public void OnUnsubscribed(int subscription) => Volatile.Write(ref _midPrices[subscription], 0);
}
