using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Concurrency;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>Local order book of one subscription: synced from snapshots and depth updates, read from any thread without locks.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Two writers, the receive loop and the snapshot worker, take a per-book lock; on the hot path nobody else holds it.
/// Readers go through a seqlock and never hold back the receive loop.
/// </para>
/// <para>
/// DESIGN NOTE: Sync follows the exchange guide. Updates are buffered while a snapshot loads. The boundary is the snapshot's lastUpdateId
/// plus the market offset: 0 on futures, 1 on spot, where the first update after a snapshot starts one past it.
/// Updates with u below the boundary are dropped, the first one applied must have U &lt;= boundary &lt;= u,
/// and every next one must have pu equal to the previous u. Any break means a new snapshot.
/// </para>
/// <para>
/// DESIGN NOTE: The buffer keeps parsed levels in flat arrays, since the handler gets spans, not the raw message.
/// It holds one unbroken chain: a gap or the size cap starts it over from the current update.
/// </para>
/// <para>
/// DESIGN NOTE: A shallow book keeps serving while a fresh snapshot loads. Its updates are buffered too, and the book is rebuilt once the snapshot fits.
/// </para>
/// <para>
/// DESIGN NOTE: While a connection rotates, the new one may deliver an update before the old one delivers the update just before it.
/// The chain check then resyncs the book. It is rare, once a day and after a network repair, and accepted as is.
/// </para>
/// <para>
/// DESIGN NOTE: Snapshot requests carry a ticket. A reset or a newer request takes a new ticket, so a request left in a queue is skipped.
/// </para>
/// </remarks>
internal sealed class OrderBook
{
    private const int StateIdle = 0;
    private const int StateBuffering = 1;
    private const int StateSynced = 2;

    private const int InitialBufferedUpdates = 64;
    private const int InitialBufferedLevels = 1024;

    private readonly Lock _sync = new();
    private readonly OrderBookSide _bids;
    private readonly OrderBookSide _asks;
    private readonly int _refreshThreshold;
    private readonly long _maxBufferBytes;
    private readonly int _snapshotOffset;

    // Read by readers inside the seqlock; changed under the lock, inside a seqlock write.
    private long _sequence;
    private long _lastUpdateId;
    private int _state;

    // Touched under the lock only.
    private BufferedUpdate[] _updates = [];
    private DepthLevel[] _levels = [];
    private int _updateCount;
    private int _levelCount;
    private Symbol _symbol;
    private long _ticket;
    private int _lifetime;
    private int _attempt;
    private SnapshotKind _queued;
    private bool _awaitingFirst;
    private bool _refreshPending;

    public OrderBook(int sideCapacity, int refreshThreshold, int maxBufferBytes, int snapshotOffset)
    {
        _bids = new OrderBookSide(sideCapacity, isAsk: false);
        _asks = new OrderBookSide(sideCapacity, isAsk: true);
        _refreshThreshold = refreshThreshold;
        _maxBufferBytes = maxBufferBytes;
        _snapshotOffset = snapshotOffset;
    }

    /// <summary>Which snapshot a book waits for.</summary>
    public enum SnapshotKind : byte
    {
        /// <summary>None.</summary>
        None,

        /// <summary>The book is out of sync.</summary>
        Resync,

        /// <summary>The book is synced but shallow.</summary>
        Refresh,
    }

    /// <summary>Whether the book can be read.</summary>
    public bool IsSynced => Volatile.Read(ref _state) == StateSynced;

    /// <summary>Changes so far, sync state included.</summary>
    public long Version => SeqLock.ReadVersion(ref _sequence);

    /// <summary>Receive loop. Applies or buffers an update. Returns the snapshot to queue with its ticket, or None when nothing new is needed.</summary>
    public SnapshotKind Update(in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks, out long ticket, out bool lostSync)
    {
        lostSync = false;

        lock (_sync)
        {
            switch (_state)
            {
                case StateSynced:
                    if (!TryApplyLive(in header, bids, asks))
                    {
                        lostSync = true;
                        StartBuffering(in header, bids, asks);
                    }
                    break;

                case StateBuffering:
                    Buffer(in header, bids, asks);
                    break;

                default:
                    _symbol = header.Symbol;
                    StartBuffering(in header, bids, asks);
                    break;
            }

            return Request(out ticket);
        }
    }

    /// <summary>Data stopped: the book is out of sync until new data and a new snapshot.</summary>
    public void Disconnect()
    {
        lock (_sync)
        {
            SeqLock.BeginWrite(ref _sequence);
            SetState(StateIdle);
            SeqLock.EndWrite(ref _sequence);

            _awaitingFirst = false;
            _refreshPending = false;
            ClearBuffer();
        }
    }

    /// <summary>The subscription is gone: the book is emptied for the next stream on its handle.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            SeqLock.BeginWrite(ref _sequence);
            SetState(StateIdle);
            _bids.Clear();
            _asks.Clear();
            _lastUpdateId = 0;
            SeqLock.EndWrite(ref _sequence);

            _symbol = default;
            _lifetime++;
            _ticket++;
            _queued = SnapshotKind.None;
            _attempt = 0;
            _awaitingFirst = false;
            _refreshPending = false;
            ClearBuffer();
        }
    }

    /// <summary>Snapshot worker. False when the request is stale or the book no longer needs a snapshot.</summary>
    public bool TryBeginSnapshot(long ticket, out Symbol symbol, out int lifetime)
    {
        lock (_sync)
        {
            symbol = default;
            lifetime = 0;

            if (ticket != _ticket)
            {
                return false;
            }

            if (Wanted() == SnapshotKind.None)
            {
                _queued = SnapshotKind.None;
                return false;
            }

            symbol = _symbol;
            lifetime = _lifetime;
            return true;
        }
    }

    /// <summary>Snapshot worker. Rebuilds the book from the snapshot levels, best first, and the buffer. False when the snapshot is older than the buffer or no longer needed.</summary>
    public bool ApplySnapshot(int lifetime, long snapshotId, ReadOnlySpan<DepthLevel> snapshotBids, ReadOnlySpan<DepthLevel> snapshotAsks, int limit)
    {
        lock (_sync)
        {
            if (lifetime != _lifetime || Wanted() == SnapshotKind.None)
            {
                return false;
            }

            long boundary = snapshotId + _snapshotOffset;
            int first = 0;
            int levelOffset = 0;

            // Already in the snapshot.
            while (first < _updateCount && _updates[first].FinalUpdateId < boundary)
            {
                levelOffset += _updates[first].BidCount + _updates[first].AskCount;
                first++;
            }

            // Older than the first update kept: what happened between them is unknown.
            if (first < _updateCount && boundary < _updates[first].FirstUpdateId)
            {
                return false;
            }

            SeqLock.BeginWrite(ref _sequence);

            _bids.Load(snapshotBids, limit <= snapshotBids.Length);
            _asks.Load(snapshotAsks, limit <= snapshotAsks.Length);
            _lastUpdateId = snapshotId;
            _awaitingFirst = first == _updateCount;

            for (int i = first; i < _updateCount; i++)
            {
                ref BufferedUpdate update = ref _updates[i];
                ReadOnlySpan<DepthLevel> bids = _levels.AsSpan(levelOffset, update.BidCount);
                levelOffset += update.BidCount;
                ReadOnlySpan<DepthLevel> asks = _levels.AsSpan(levelOffset, update.AskCount);
                levelOffset += update.AskCount;

                ApplyLevels(bids, asks);
                _lastUpdateId = update.FinalUpdateId;
            }

            // A side with no known level left: the next update starts over.
            SetState(_bids.IsBlind || _asks.IsBlind ? StateIdle : StateSynced);

            SeqLock.EndWrite(ref _sequence);

            _refreshPending = false;
            _attempt = 0;
            ClearBuffer();
            return true;
        }
    }

    /// <summary>Snapshot worker. Returns the snapshot to queue next with its ticket and the failed tries so far, or None.</summary>
    public SnapshotKind FinishSnapshot(long ticket, bool applied, out long nextTicket, out int attempt)
    {
        lock (_sync)
        {
            nextTicket = 0;
            attempt = 0;

            // A reset or a newer request took over.
            if (ticket != _ticket)
            {
                return SnapshotKind.None;
            }

            SnapshotKind wanted = Wanted();

            if (wanted == SnapshotKind.None)
            {
                _queued = SnapshotKind.None;
                return SnapshotKind.None;
            }

            if (!applied)
            {
                _attempt++;
            }

            attempt = _attempt;
            _queued = wanted;
            nextTicket = ++_ticket;
            return wanted;
        }
    }

    /// <summary>Copies the best levels while synced, best first.</summary>
    public bool TryCopyTop(Span<DepthLevel> bids, Span<DepthLevel> asks, out int bidCount, out int askCount, out long updateId)
    {
        while (true)
        {
            long start = SeqLock.BeginRead(ref _sequence);
            bool isSynced = _state == StateSynced;

            bidCount = isSynced ? _bids.CopyTop(bids) : 0;
            askCount = isSynced ? _asks.CopyTop(asks) : 0;
            updateId = isSynced ? _lastUpdateId : 0;

            if (SeqLock.EndRead(ref _sequence, start))
            {
                return isSynced;
            }
        }
    }

    /// <summary>Best bid and ask while synced. False when either side is empty.</summary>
    public bool TryGetBest(out DepthLevel bid, out DepthLevel ask, out long updateId)
    {
        while (true)
        {
            long start = SeqLock.BeginRead(ref _sequence);

            // Non-short-circuit: both sides are always read.
            bool found = (_state == StateSynced) & _bids.TryGetBest(out bid) & _asks.TryGetBest(out ask);
            updateId = _lastUpdateId;

            if (SeqLock.EndRead(ref _sequence, start))
            {
                if (!found)
                {
                    bid = default;
                    ask = default;
                    updateId = 0;
                }

                return found;
            }
        }
    }

    /// <summary>HOT: applies an update that continues the chain. False on a gap or a side left blind.</summary>
    private bool TryApplyLive(in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        if (_awaitingFirst)
        {
            long boundary = _lastUpdateId + _snapshotOffset;

            // Already in the snapshot.
            if (header.FinalUpdateId < boundary)
            {
                return true;
            }

            if (boundary < header.FirstUpdateId)
            {
                return false;
            }

            _awaitingFirst = false;
        }
        else if (header.PreviousFinalUpdateId != _lastUpdateId)
        {
            return false;
        }

        SeqLock.BeginWrite(ref _sequence);
        ApplyLevels(bids, asks);
        _lastUpdateId = header.FinalUpdateId;
        SeqLock.EndWrite(ref _sequence);

        if (_bids.IsBlind || _asks.IsBlind)
        {
            return false;
        }

        if (!_refreshPending && (_bids.NeedsRefresh(_refreshThreshold) || _asks.NeedsRefresh(_refreshThreshold)))
        {
            _refreshPending = true;
            ClearBuffer();
        }

        if (_refreshPending)
        {
            Buffer(in header, bids, asks);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyLevels(ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        for (int i = 0; i < bids.Length; i++)
        {
            _bids.Apply(bids[i].Price, bids[i].Quantity);
        }

        for (int i = 0; i < asks.Length; i++)
        {
            _asks.Apply(asks[i].Price, asks[i].Quantity);
        }
    }

    private void StartBuffering(in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        SeqLock.BeginWrite(ref _sequence);
        SetState(StateBuffering);
        SeqLock.EndWrite(ref _sequence);

        _awaitingFirst = false;
        _refreshPending = false;
        ClearBuffer();
        Buffer(in header, bids, asks);
    }

    private void Buffer(in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        int levelCount = bids.Length + asks.Length;

        // A gap: updates before it can never be bridged.
        if (0 < _updateCount && header.PreviousFinalUpdateId != _updates[_updateCount - 1].FinalUpdateId)
        {
            ClearBuffer();
        }

        long bytes = (long)(_updateCount + 1) * Unsafe.SizeOf<BufferedUpdate>() + (long)(_levelCount + levelCount) * Unsafe.SizeOf<DepthLevel>();

        // A snapshot this late is tried again on fresh updates.
        if (_maxBufferBytes < bytes)
        {
            ClearBuffer();
        }

        EnsureBufferCapacity(levelCount);

        _updates[_updateCount++] = new BufferedUpdate()
        {
            FirstUpdateId = header.FirstUpdateId,
            FinalUpdateId = header.FinalUpdateId,
            BidCount = bids.Length,
            AskCount = asks.Length,
        };

        bids.CopyTo(_levels.AsSpan(_levelCount));
        _levelCount += bids.Length;
        asks.CopyTo(_levels.AsSpan(_levelCount));
        _levelCount += asks.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearBuffer()
    {
        _updateCount = 0;
        _levelCount = 0;
    }

    private void EnsureBufferCapacity(int levelCount)
    {
        if (_updateCount == _updates.Length)
        {
            _updates = Grow(_updates, _updateCount + 1, InitialBufferedUpdates);
        }

        if (_levels.Length < _levelCount + levelCount)
        {
            _levels = Grow(_levels, _levelCount + levelCount, InitialBufferedLevels);
        }
    }

    /// <summary>The snapshot the book waits for, by its state.</summary>
    private SnapshotKind Wanted() =>
        _state switch
        {
            StateBuffering => SnapshotKind.Resync,
            StateSynced when _refreshPending => SnapshotKind.Refresh,
            _ => SnapshotKind.None,
        };

    /// <summary>Queues the snapshot the book waits for, unless one already serves. An out of sync book jumps ahead of a shallow one.</summary>
    private SnapshotKind Request(out long ticket)
    {
        SnapshotKind wanted = Wanted();

        if (wanted == SnapshotKind.None || _queued == wanted || _queued == SnapshotKind.Resync)
        {
            ticket = 0;
            return SnapshotKind.None;
        }

        _queued = wanted;
        ticket = ++_ticket;
        return wanted;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetState(int state) => Volatile.Write(ref _state, state);

    /// <summary>Cold path. A larger buffer for more updates than any before; never shrinks.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] items, int required, int initial)
    {
        T[] larger = new T[Math.Max(Math.Max(initial, items.Length * 2), required)];
        items.CopyTo(larger, 0);
        return larger;
    }

    private struct BufferedUpdate
    {
        public long FirstUpdateId;
        public long FinalUpdateId;
        public int BidCount;
        public int AskCount;
    }
}
