using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Deduplication;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>State a user data session shares with its dispatcher.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: The handle table, the duplicate filter and the buffers are touched by the dispatcher under the shard lock,
/// and by the session only while it subscribes, from inside the transport call that takes the same lock.
/// </para>
/// <para>
/// DESIGN NOTE: Handler calls come from the receive loop and from the session's key loop, so both take HandlerSync.
/// Locks are taken in one order only: the shard lock, then HandlerSync. The session never calls the transport while holding HandlerSync.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore holds no unmanaged state without a wait handle; disposing it would make a late Release throw.")]
internal sealed class BinanceUserDataState
{
    // A copy from the other connection comes within a rotation overlap, so a few hundred events are plenty.
    private const int DuplicateSlots = 256;
    private const int DuplicateSlotBytes = 2 * 1024;

    // Starting size of the ACCOUNT_UPDATE buffers; they double when an update has more items.
    private const int InitialItems = 16;

    private readonly BinanceUserDataEvents[] _eventByHandle;
    private readonly SemaphoreSlim _wake = new(0);

    private BinanceUserDataEvents _pendingEvent;
    private bool _pendingAnchor;
    private int _anchorHandle = -1;
    private byte[] _listenKey = [];
    private int _expired;
    private int _reconnected;

    // Touched under HandlerSync only.
    private bool _gapOpen;
    private bool _fatalReported;
    private long _gapStartMillis;
    private WsDisconnectReason _gapReason;

    /// <summary>Creates the state for a transport of the given capacity.</summary>
    public BinanceUserDataState(int capacity)
    {
        _eventByHandle = new BinanceUserDataEvents[capacity];
        Duplicates = new WsStreamDuplicateFilter(DuplicateSlots, DuplicateSlotBytes);
        Balances = new AccountUpdateBalance[InitialItems];
        Positions = new AccountUpdatePosition[InitialItems];
        MarginCallPositions = new MarginCallPosition[InitialItems];
    }

    /// <summary>Taken around every handler call.</summary>
    public Lock HandlerSync { get; } = new();

    /// <summary>Copies of events from an overlapping rotation.</summary>
    public WsStreamDuplicateFilter Duplicates { get; }

    /// <summary>Reused balance buffer of ACCOUNT_UPDATE.</summary>
    public AccountUpdateBalance[] Balances { get; set; }

    /// <summary>Reused position buffer of ACCOUNT_UPDATE.</summary>
    public AccountUpdatePosition[] Positions { get; set; }

    /// <summary>Reused position buffer of MARGIN_CALL.</summary>
    public MarginCallPosition[] MarginCallPositions { get; set; }

    /// <summary>The listen key in use. Empty before the first subscription.</summary>
    public byte[] ListenKey
    {
        get => Volatile.Read(ref _listenKey);
        set => Volatile.Write(ref _listenKey, value);
    }

    /// <summary>The next subscription is the expiry anchor.</summary>
    public void ExpectAnchor()
    {
        _pendingAnchor = true;
        _pendingEvent = BinanceUserDataEvents.None;
    }

    /// <summary>The next subscription carries this event.</summary>
    public void ExpectEvent(BinanceUserDataEvents userDataEvent)
    {
        _pendingAnchor = false;
        _pendingEvent = userDataEvent;
    }

    /// <summary>Binds the handle to what the session said it subscribes. Called by the transport before the request goes out.</summary>
    public void OnSubscribing(int handle)
    {
        _eventByHandle[handle] = _pendingEvent;

        if (_pendingAnchor)
        {
            Volatile.Write(ref _anchorHandle, handle);
        }
        else if (Volatile.Read(ref _anchorHandle) == handle)
        {
            Volatile.Write(ref _anchorHandle, -1);
        }
    }

    /// <summary>Event carried by the handle, or None for the anchor and unknown handles.</summary>
    public BinanceUserDataEvents EventOf(int handle) => _eventByHandle[handle];

    /// <summary>Whether the handle is the expiry anchor.</summary>
    public bool IsAnchor(int handle) => Volatile.Read(ref _anchorHandle) == handle;

    /// <summary>The key may have expired: wakes the key loop.</summary>
    public void SignalExpired()
    {
        Volatile.Write(ref _expired, 1);
        _wake.Release();
    }

    /// <summary>The connection is back: wakes the key loop to check the key before the gap closes.</summary>
    public void SignalReconnected()
    {
        Volatile.Write(ref _reconnected, 1);
        _wake.Release();
    }

    /// <summary>Takes the expiry signal.</summary>
    public bool TakeExpired() => Interlocked.Exchange(ref _expired, 0) == 1;

    /// <summary>Takes the reconnect signal.</summary>
    public bool TakeReconnected() => Interlocked.Exchange(ref _reconnected, 0) == 1;

    /// <summary>Waits for a signal or the timeout. True when signaled.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _wake.WaitAsync(timeout, cancellationToken);

    /// <summary>Opens a gap. True only for the call that opened it. Call under HandlerSync.</summary>
    public bool TryOpenGap(WsDisconnectReason reason)
    {
        if (_gapOpen)
        {
            return false;
        }

        _gapOpen = true;
        _gapReason = reason;
        _gapStartMillis = SysTime.UtcMillis();
        return true;
    }

    /// <summary>Closes the open gap and gives its bounds. False when none is open. Call under HandlerSync.</summary>
    public bool TryCloseGap(out WsStreamGap gap)
    {
        if (!_gapOpen)
        {
            gap = default;
            return false;
        }

        _gapOpen = false;
        gap = new WsStreamGap()
        {
            Reason = _gapReason,
            StartUtcMillis = _gapStartMillis,
            EndUtcMillis = SysTime.UtcMillis(),
        };

        return true;
    }

    /// <summary>True only for the first fatal report. Call under HandlerSync.</summary>
    public bool TryReportFatal()
    {
        if (_fatalReported)
        {
            return false;
        }

        _fatalReported = true;
        return true;
    }
}
