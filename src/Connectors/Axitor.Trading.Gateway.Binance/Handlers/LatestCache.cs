using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Core.Concurrency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>Latest message of each subscription, read from any thread without locks. Pass Handler to a stream endpoint's CreateSession.</summary>
/// <remarks>
/// <para>For streams where each message replaces the previous one, e.g. book ticker or partial depth. A read never blocks the receive loop and never sees a half-written message.</para>
/// <para>TryRead gives live data only: before the first message, after a break until new data comes, and after unsubscribe it returns false, so a strategy never acts on a stale price.</para>
/// </remarks>
public sealed class LatestCache<TModel> where TModel : struct
{
    private const int StateEmpty = 0;
    private const int StateLive = 1;
    private const int StateStale = 2;

    private readonly SeqLockSlot<TModel>[] _slots;
    private readonly int[] _states;

    /// <summary>Creates a cache. Pass BinanceClient.StreamSessionCapacity.</summary>
    public LatestCache(int capacity)
    {
        _slots = new SeqLockSlot<TModel>[capacity];
        _states = new int[capacity];

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new SeqLockSlot<TModel>();
        }

        Handler = new LatestCacheHandler<TModel>(this);
    }

    /// <summary>The handler to pass to CreateSession.</summary>
    public LatestCacheHandler<TModel> Handler { get; }

    /// <summary>Copies the latest message while the subscription is live. Version grows with every message.</summary>
    public bool TryRead(int subscription, out TModel model, out long version)
    {
        if (Volatile.Read(ref _states[subscription]) != StateLive)
        {
            model = default;
            version = 0;
            return false;
        }

        version = _slots[subscription].Read(out model);
        return true;
    }

    /// <summary>Copies the latest message, also after a break. False only before the first message and after unsubscribe.</summary>
    public bool TryReadLast(int subscription, out TModel model, out long version)
    {
        if (Volatile.Read(ref _states[subscription]) == StateEmpty)
        {
            model = default;
            version = 0;
            return false;
        }

        version = _slots[subscription].Read(out model);
        return true;
    }

    /// <summary>Messages written so far. A cheap check whether anything changed since the last read.</summary>
    public long GetVersion(int subscription) => _slots[subscription].Version;

    internal void Write(int subscription, in TModel model)
    {
        _slots[subscription].Write(in model);

        // Live only once the value is in place, so a reader never sees live with an older value.
        if (_states[subscription] != StateLive)
        {
            Volatile.Write(ref _states[subscription], StateLive);
        }
    }

    internal void MarkStale(int subscription)
    {
        if (_states[subscription] == StateLive)
        {
            Volatile.Write(ref _states[subscription], StateStale);
        }
    }

    internal void Clear(int subscription) => Volatile.Write(ref _states[subscription], StateEmpty);
}

/// <summary>Stream handler of a LatestCache.</summary>
public readonly struct LatestCacheHandler<TModel> : IBinanceStreamHandler<TModel> where TModel : struct
{
    private readonly LatestCache<TModel> _cache;

    internal LatestCacheHandler(LatestCache<TModel> cache) => _cache = cache;

    /// <inheritdoc />
    public void OnData(int subscription, in TModel model) => _cache.Write(subscription, in model);

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _cache.MarkStale(subscription);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap)
    {
        // Live again with the first new message, not before.
    }

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) => _cache.Clear(subscription);
}
