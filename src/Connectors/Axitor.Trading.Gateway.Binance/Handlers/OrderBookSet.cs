using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>Local order books of a depth session, synced on their own and read from any thread without locks. Pass Handler to the depth stream's CreateSession.</summary>
/// <remarks>
/// <para>
/// One set serves one depth session of its market: subscription handles belong to the session that gave them, and snapshots come from the market's REST API.
/// Creating a session of another market or a second session with the same Handler throws.
/// </para>
/// <para>
/// A book can be read only while synced: not before its first snapshot, not after a connection break, not after a gap in the updates.
/// It then loads a new snapshot by itself.
/// </para>
/// <para>
/// A book keeps only levels it knows exactly: down to the deepest snapshot level, or the whole side when the snapshot had fewer levels than asked.
/// With fewer known levels than the threshold, a fresh snapshot loads in the background while the book keeps serving.
/// </para>
/// <para>
/// Snapshots go one at a time, books out of sync first, and take at most the snapshot share of the market's request weight across the client:
/// BinanceLimits.FuturesDepthSnapshotWeightPercent, shared by USD-M and COIN-M, or BinanceLimits.SpotDepthSnapshotWeightPercent.
/// With the defaults that is 60 snapshots a minute, so after a network break 200 books need a few minutes to come back. A smaller SnapshotLimit costs less weight but gives shallower books.
/// </para>
/// <para>
/// Snapshots are read into one pair of level buffers the set keeps, SnapshotLimit levels each, so loading a snapshot allocates no level arrays.
/// </para>
/// <para>Completion faults and every book stays out of sync once a snapshot fails for good.</para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore holds no unmanaged state without a wait handle; disposing it would make a late Release throw.")]
public sealed class OrderBookSet<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    // Spot chains updates by U = previous u + 1, so the first update after a snapshot starts one past it.
    private const int SpotSnapshotOffset = 1;

    private readonly SnapshotSource _snapshotSource;
    private readonly OrderBook?[] _books;
    private readonly ConcurrentQueue<SnapshotRequest> _resyncQueue = new();
    private readonly ConcurrentQueue<SnapshotRequest> _refreshQueue = new();
    private readonly SemaphoreSlim _wake = new(0);

    // Snapshot levels, best first. One worker loads one snapshot at a time, so one pair serves every book.
    private readonly DepthLevel[] _snapshotBids;
    private readonly DepthLevel[] _snapshotAsks;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BinanceMarkets _market;
    private readonly int _snapshotOffset;
    private readonly int _snapshotLimit;
    private readonly int _sideCapacity;
    private readonly int _refreshThreshold;
    private readonly int _maxBufferBytes;
    private readonly int _retryBaseDelayMillis;
    private readonly int _retryMaxDelayMillis;
    private long _resyncCount;
    private int _isFatal;
    private int _isBound;

    /// <summary>Creates a set for one depth session of the market. Snapshots load from the market's REST API. Throws unless exactly one market is given.</summary>
    public OrderBookSet(BinanceClient<TTelemetry> client, BinanceMarkets market, OrderBookSetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        options ??= new OrderBookSetOptions();

        _snapshotSource = CreateSnapshotSource(client, market);
        _market = market;
        _snapshotOffset = market == BinanceMarkets.Spot ? SpotSnapshotOffset : 0;
        _books = new OrderBook?[client.StreamSessionCapacity];
        _snapshotLimit = options.SnapshotLimit;
        _snapshotBids = new DepthLevel[options.SnapshotLimit];
        _snapshotAsks = new DepthLevel[options.SnapshotLimit];
        _sideCapacity = options.SnapshotLimit * 2;
        _refreshThreshold = 0 < options.RefreshThreshold ? options.RefreshThreshold : options.SnapshotLimit / 4;
        _maxBufferBytes = options.MaxBufferBytes;
        _retryBaseDelayMillis = (int)options.SnapshotRetryBaseDelay.TotalMilliseconds;
        _retryMaxDelayMillis = (int)options.SnapshotRetryMaxDelay.TotalMilliseconds;

        Handler = new OrderBookSetHandler<TTelemetry>(this);

        _ = RunAsync(client.AppToken);
    }

    /// <summary>The handler to pass to CreateSession.</summary>
    public OrderBookSetHandler<TTelemetry> Handler { get; }

    /// <summary>Completes on app shutdown; faults once a snapshot fails for good.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Times a synced book lost its sync on a gap in the updates. Connection breaks are not counted.</summary>
    public long ResyncCount => Volatile.Read(ref _resyncCount);

    /// <summary>Copies the best levels of a synced book, best first. False while it is not synced. The update id is the last one the copy reflects.</summary>
    public bool TryCopyTop(int subscription, Span<DepthLevel> bids, Span<DepthLevel> asks, out int bidCount, out int askCount, out long updateId)
    {
        OrderBook? book = Volatile.Read(ref _books[subscription]);

        if (book is null)
        {
            bidCount = 0;
            askCount = 0;
            updateId = 0;
            return false;
        }

        return book.TryCopyTop(bids, asks, out bidCount, out askCount, out updateId);
    }

    /// <summary>Best bid and ask of a synced book. False while it is not synced or when either side is empty.</summary>
    public bool TryGetBest(int subscription, out DepthLevel bid, out DepthLevel ask, out long updateId)
    {
        OrderBook? book = Volatile.Read(ref _books[subscription]);

        if (book is null)
        {
            bid = default;
            ask = default;
            updateId = 0;
            return false;
        }

        return book.TryGetBest(out bid, out ask, out updateId);
    }

    /// <summary>Whether the book can be read.</summary>
    public bool IsSynced(int subscription) => Volatile.Read(ref _books[subscription])?.IsSynced ?? false;

    /// <summary>Changes of the book so far. A cheap check whether anything changed since the last read.</summary>
    public long GetVersion(int subscription) => Volatile.Read(ref _books[subscription])?.Version ?? 0;

    internal void Update(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        // A fatal set keeps every book out of sync.
        if (Volatile.Read(ref _isFatal) != 0)
        {
            return;
        }

        // Written only on the receive loop of the handle, so no atomics here.
        OrderBook book = _books[subscription] ?? CreateBook(subscription);
        OrderBook.SnapshotKind kind = book.Update(in header, bids, asks, out long ticket, out bool lostSync);

        if (lostSync)
        {
            Interlocked.Increment(ref _resyncCount);
        }

        if (kind != OrderBook.SnapshotKind.None)
        {
            Enqueue(subscription, ticket, kind);
        }
    }

    internal void Disconnect(int subscription) => Volatile.Read(ref _books[subscription])?.Disconnect();

    internal void Reset(int subscription) => Volatile.Read(ref _books[subscription])?.Reset();

    /// <summary>
    /// Cold path. Ties the set to the depth session being created. Throws when the session is of another market or the set already serves a session:
    /// either way a book would sync against foreign update ids and could serve a stale snapshot as live.
    /// </summary>
    internal void Bind(BinanceMarkets market)
    {
        if (market != _market)
        {
            ThrowMarketMismatch();
        }

        if (Interlocked.Exchange(ref _isBound, 1) != 0)
        {
            ThrowAlreadyBound();
        }
    }

    /// <summary>Cold path. One book per handle, kept when the handle is reused.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private OrderBook CreateBook(int subscription)
    {
        OrderBook book = new(_sideCapacity, _refreshThreshold, _maxBufferBytes, _snapshotOffset);
        Volatile.Write(ref _books[subscription], book);
        return book;
    }

    [SuppressMessage("Design", "CA1031", Justification = "Any other exception is a bug: it faults Completion instead of vanishing in the background.")]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Books out of sync go before shallow ones.
                if (_resyncQueue.TryDequeue(out SnapshotRequest request) || _refreshQueue.TryDequeue(out request))
                {
                    await LoadSnapshotAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            // RestApiFatalException included: the app must stop.
            EnterFatal(exception);
        }
    }

    private async ValueTask LoadSnapshotAsync(SnapshotRequest request, CancellationToken cancellationToken)
    {
        OrderBook? book = Volatile.Read(ref _books[request.Subscription]);

        if (book is null || !book.TryBeginSnapshot(request.Ticket, out Symbol symbol, out int lifetime))
        {
            return;
        }

        RestApiRequestResult<OrderBookSnapshotModel> result =
            await _snapshotSource(symbol, _snapshotBids, _snapshotAsks, _snapshotLimit, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // A skipped failure or a snapshot older than the buffer: tried again after a delay.
        bool applied = result.Outcome == RequestOutcome.Success
            && book.ApplySnapshot(
                lifetime,
                result.Value.LastUpdateId,
                _snapshotBids.AsSpan(0, result.Value.BidCount),
                _snapshotAsks.AsSpan(0, result.Value.AskCount),
                _snapshotLimit);

        OrderBook.SnapshotKind kind = book.FinishSnapshot(request.Ticket, applied, out long ticket, out int attempt);

        if (kind == OrderBook.SnapshotKind.None)
        {
            return;
        }

        if (attempt == 0)
        {
            Enqueue(request.Subscription, ticket, kind);
            return;
        }

        long delayMillis = WsReconnectPolicy.GetBackoffDelayMillis(attempt, _retryBaseDelayMillis, _retryMaxDelayMillis);
        _ = EnqueueLaterAsync(request.Subscription, ticket, kind, delayMillis, cancellationToken);
    }

    private async Task EnqueueLaterAsync(int subscription, long ticket, OrderBook.SnapshotKind kind, long delayMillis, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMillis), cancellationToken).ConfigureAwait(false);
            Enqueue(subscription, ticket, kind);
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
    }

    private void Enqueue(int subscription, long ticket, OrderBook.SnapshotKind kind)
    {
        SnapshotRequest request = new() { Ticket = ticket, Subscription = subscription };

        if (kind == OrderBook.SnapshotKind.Resync)
        {
            _resyncQueue.Enqueue(request);
        }
        else
        {
            _refreshQueue.Enqueue(request);
        }

        _wake.Release();
    }

    /// <summary>Cold path. Stops snapshots for good and takes every book out of sync.</summary>
    private void EnterFatal(Exception exception)
    {
        Volatile.Write(ref _isFatal, 1);

        for (int i = 0; i < _books.Length; i++)
        {
            Volatile.Read(ref _books[i])?.Disconnect();
        }

        _completion.TrySetException(exception);
    }

    /// <summary>Cold path. Snapshot request of the market's order book endpoint, built once; a snapshot is rare, so the delegate call costs nothing that matters.</summary>
    private static SnapshotSource CreateSnapshotSource(BinanceClient<TTelemetry> client, BinanceMarkets market) =>
        market switch
        {
            BinanceMarkets.FuturesUsdM => (symbol, bids, asks, limit, cancellationToken) =>
                client.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, limit, cancellationToken: cancellationToken),

            BinanceMarkets.FuturesCoinM => (symbol, bids, asks, limit, cancellationToken) =>
                client.Futures.CoinM.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, limit, cancellationToken: cancellationToken),

            BinanceMarkets.Spot => (symbol, bids, asks, limit, cancellationToken) =>
                client.Spot.RestApi.MarketData.OrderBook.GetAsync(symbol, bids, asks, limit, cancellationToken: cancellationToken),

            _ => ThrowInvalidMarket<SnapshotSource>(market),
        };

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowInvalidMarket<T>(BinanceMarkets market) =>
        throw new ArgumentOutOfRangeException(nameof(market), market, "An order book set serves exactly one market.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMarketMismatch() =>
        throw new InvalidOperationException("The order book set was created for another market than this depth session.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAlreadyBound() =>
        throw new InvalidOperationException("The order book set already serves a depth session. Create one set per session.");

    private delegate ValueTask<RestApiRequestResult<OrderBookSnapshotModel>> SnapshotSource(
        Symbol symbol,
        Memory<DepthLevel> bids,
        Memory<DepthLevel> asks,
        int limit,
        CancellationToken cancellationToken);

    private struct SnapshotRequest
    {
        public long Ticket;
        public int Subscription;
    }
}

/// <summary>Depth handler of an OrderBookSet.</summary>
public readonly struct OrderBookSetHandler<TTelemetry> : IBinanceDepthHandler where TTelemetry : struct, ITelemetryHandler
{
    private readonly OrderBookSet<TTelemetry> _set;

    internal OrderBookSetHandler(OrderBookSet<TTelemetry> set) => _set = set;

    /// <inheritdoc />
    public void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks) =>
        _set.Update(subscription, in header, bids, asks);

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) => _set.Disconnect(subscription);

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap)
    {
        // The book was taken out of sync on the break; the first new update starts the resync.
    }

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription) => _set.Reset(subscription);

    /// <summary>Cold path. Ties the set to the depth session being created; throws on a market mismatch or a second session.</summary>
    internal void Bind(BinanceMarkets market) => _set.Bind(market);
}
